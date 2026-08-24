using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class TodayViewModel : INotifyPropertyChanged, ILocalPlanSnapshotProvider
{
    private readonly IDailyPlanRepository? repository;
    private readonly IAiConsentService? aiConsentService;
    private readonly IAssistedTextGenerator? assistedTextGenerator;
    private readonly object persistenceLock = new();
    private Task? pendingSave;
    private bool saveRequested;
    private bool isInitialized;
    private string? persistenceError;
    private PlannedWorkItemViewModel? selectedItem;
    private DescriptionSuggestionRequest? pendingAiRequest;
    private string? suggestedDescription;
    private string? aiStatus;
    private bool isAiConsentVisible;
    private Guid? pendingAiItemId;
    private Guid? suggestedItemId;
    private readonly IIntegrationClientFactory? integrationClients;
    private readonly IUserSettingsStore? settingsStore;
    private string? projectLoadError;

    public TodayViewModel(IDailyPlanRepository? repository = null, DateOnly? date = null, IAiConsentService? aiConsentService = null, IAssistedTextGenerator? assistedTextGenerator = null, IIntegrationClientFactory? integrationClients = null, IUserSettingsStore? settingsStore = null)
    {
        this.repository = repository;
        this.aiConsentService = aiConsentService;
        this.assistedTextGenerator = assistedTextGenerator;
        this.integrationClients = integrationClients;
        this.settingsStore = settingsStore;
        Date = date ?? DateOnly.FromDateTime(DateTime.Today);
        Items.CollectionChanged += OnItemsChanged;
        AddItemCommand = new RelayCommand(_ => AddItem());
        RemoveItemCommand = new RelayCommand(RemoveItem);
        AddTemplateCommand = new RelayCommand(AddTemplate);
        OpenAiConsentCommand = new RelayCommand(OpenAiConsent);
        ConfirmAiConsentCommand = new RelayCommand(async _ => await ConfirmAiConsentAsync());
        CancelAiConsentCommand = new RelayCommand(_ => CancelAiConsent());
        ApplyAiSuggestionCommand = new RelayCommand(_ => ApplyAiSuggestion());
        RefreshProjectsCommand = new RelayCommand(_ => _ = LoadProjectsAsync());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PlannedWorkItemViewModel> Items { get; } = [];
    public ObservableCollection<TogglProject> TogglProjects { get; } = [];
    public DateOnly Date { get; }
    public RelayCommand AddItemCommand { get; }
    public RelayCommand RemoveItemCommand { get; }
    public RelayCommand AddTemplateCommand { get; }
    public RelayCommand OpenAiConsentCommand { get; }
    public RelayCommand ConfirmAiConsentCommand { get; }
    public RelayCommand CancelAiConsentCommand { get; }
    public RelayCommand ApplyAiSuggestionCommand { get; }
    public RelayCommand RefreshProjectsCommand { get; }
    public double PlannedSeconds => Items.Sum(item => item.Duration.TotalSeconds);
    public IReadOnlyList<WorkStatusOption> WorkStatuses => WorkStatusOption.All;
    public string? PersistenceError { get => persistenceError; private set => SetField(ref persistenceError, value); }
    public string? ProjectLoadError { get => projectLoadError; private set => SetField(ref projectLoadError, value); }
    public PlannedWorkItemViewModel? SelectedItem { get => selectedItem; set => SetField(ref selectedItem, value); }
    public DescriptionSuggestionRequest? PendingAiRequest { get => pendingAiRequest; private set => SetField(ref pendingAiRequest, value); }
    public string? SuggestedDescription { get => suggestedDescription; private set => SetSuggestedDescription(value); }
    public bool HasSuggestedDescription => !string.IsNullOrWhiteSpace(SuggestedDescription);
    public string? AiStatus { get => aiStatus; private set => SetField(ref aiStatus, value); }
    public bool IsAiConsentVisible { get => isAiConsentVisible; private set => SetField(ref isAiConsentVisible, value); }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (repository is null || isInitialized)
            return;

        try
        {
            var plan = await repository.GetAsync(Date, cancellationToken);
            Items.Clear();
            if (plan is null || plan.Items.Count == 0)
                Items.Add(new PlannedWorkItemViewModel());
            else
                foreach (var item in plan.Items)
                    Items.Add(new PlannedWorkItemViewModel(item.Name, item.JiraIssueKey, item.Comment, item.Duration, item.TogglProject, item.TempoCategory, item.Id, item.Start, item.End, item.IsBillable, item.Status));
            isInitialized = true;
            PersistenceError = null;
            await LoadProjectsAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (Items.Count == 0)
                Items.Add(new PlannedWorkItemViewModel());
            isInitialized = true;
            PersistenceError = "Could not load today's plan.";
        }
    }

    public Task FlushAsync()
    {
        lock (persistenceLock)
            return pendingSave ?? Task.CompletedTask;
    }

    public async Task LoadProjectsAsync(CancellationToken cancellationToken = default)
    {
        TogglProjects.Clear();
        ProjectLoadError = null;
        if (integrationClients is null || settingsStore is null) return;

        try
        {
            var workspaceId = settingsStore.Load().TogglWorkspaceId;
            if (workspaceId is not > 0) return;
            using var toggl = await integrationClients.CreateTogglAsync(cancellationToken);
            foreach (var project in await toggl.GetProjectsAsync(workspaceId.Value, cancellationToken))
                TogglProjects.Add(project);
            ApplyProjectNames();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            ProjectLoadError = "Could not load Toggl projects.";
        }
    }

    public DailyPlan GetSnapshot() => DailyPlan.Create(Date, Items.Select(item => new PlannedWorkItem(
        item.Id, Date, item.Start, item.End, item.Name, item.JiraIssueKey, item.Description,
        item.Duration, item.TogglProject, item.TempoCategory, item.IsBillable, item.Status)
        { TogglProjectId = item.TogglProjectId, PostToToggl = item.PostToToggl }).ToArray());

    private void AddTemplate(object? template)
    {
        if (template is not RecurringTaskTemplateViewModel source) return;
        Items.Add(new PlannedWorkItemViewModel(source.Name, source.JiraIssueKey, source.Description, source.Duration, source.TogglProject, source.TempoCategory, isBillable: source.IsBillable, status: source.Status, togglProjectId: source.TogglProjectId));
    }

    private void AddItem()
    {
        var item = new PlannedWorkItemViewModel();
        Items.Add(item);
        SelectedItem = item;
    }

    private void RemoveItem(object? item)
    {
        if (item is PlannedWorkItemViewModel plannedItem)
            Items.Remove(plannedItem);
    }

    private void OpenAiConsent(object? _)
    {
        if (SelectedItem is null)
        {
            AiStatus = "Select an item before requesting an AI suggestion.";
            return;
        }

        PendingAiRequest = new DescriptionSuggestionRequest(
            SelectedItem.Name,
            SelectedItem.JiraIssueKey,
            SelectedItem.Description);
        pendingAiItemId = SelectedItem.Id;
        SuggestedDescription = null;
        suggestedItemId = null;
        AiStatus = null;
        IsAiConsentVisible = true;
    }

    private void CancelAiConsent()
    {
        PendingAiRequest = null;
        pendingAiItemId = null;
        IsAiConsentVisible = false;
        AiStatus = null;
    }

    private async Task ConfirmAiConsentAsync()
    {
        var request = PendingAiRequest;
        var requestItemId = pendingAiItemId;
        PendingAiRequest = null;
        pendingAiItemId = null;
        IsAiConsentVisible = false;
        SuggestedDescription = null;
        suggestedItemId = null;

        if (request is null)
        {
            AiStatus = "Open the consent preview before continuing.";
            return;
        }

        if (aiConsentService is null || assistedTextGenerator is null)
        {
            AiStatus = "AI provider is not configured.";
            return;
        }

        try
        {
            if (!aiConsentService.CanSubmit(request) ||
                !aiConsentService.IsEnabled ||
                string.IsNullOrWhiteSpace(request.TaskName) ||
                string.IsNullOrWhiteSpace(request.JiraIssueKey) ||
                string.IsNullOrWhiteSpace(request.CurrentDescription))
            {
                AiStatus = "AI suggestions are unavailable for this item.";
                return;
            }

            var result = await assistedTextGenerator.SuggestAsync(request);
            if (!result.IsAvailable || string.IsNullOrWhiteSpace(result.SuggestedDescription))
            {
                AiStatus = "AI provider is not configured.";
                return;
            }

            SuggestedDescription = result.SuggestedDescription;
            suggestedItemId = requestItemId;
            AiStatus = "AI suggestion is ready to review.";
        }
        catch
        {
            AiStatus = "AI suggestion could not be generated.";
        }
    }

    private void ApplyAiSuggestion()
    {
        if (SuggestedDescription is null || suggestedItemId is null || SelectedItem?.Id != suggestedItemId)
        {
            AiStatus = "Select the item that received the suggestion before applying it.";
            return;
        }

        SelectedItem.Description = SuggestedDescription;
        SuggestedDescription = null;
        suggestedItemId = null;
        AiStatus = "AI suggestion applied locally.";
    }

    private void SetSuggestedDescription(string? value)
    {
        if (string.Equals(suggestedDescription, value, StringComparison.Ordinal)) return;
        suggestedDescription = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SuggestedDescription)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSuggestedDescription)));
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (PlannedWorkItemViewModel item in e.OldItems)
                item.PropertyChanged -= OnItemPropertyChanged;
        if (e.NewItems is not null)
            foreach (PlannedWorkItemViewModel item in e.NewItems)
                item.PropertyChanged += OnItemPropertyChanged;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlannedSeconds)));
        SaveAfterUserAction();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlannedWorkItemViewModel.Duration))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlannedSeconds)));
        if (e.PropertyName == nameof(PlannedWorkItemViewModel.TogglProjectId))
            ApplyProjectName((PlannedWorkItemViewModel)sender!);
        SaveAfterUserAction();
    }

    private void ApplyProjectNames()
    {
        foreach (var item in Items) ApplyProjectName(item);
    }

    private void ApplyProjectName(PlannedWorkItemViewModel item)
    {
        if (item.TogglProjectId is not { } id) return;
        var project = TogglProjects.FirstOrDefault(value => value.Id == id);
        if (project is not null && !string.Equals(item.TogglProject, project.Name, StringComparison.Ordinal))
            item.TogglProject = project.Name;
    }

    private void SaveAfterUserAction()
    {
        if (repository is not null && isInitialized)
            QueueSave();
    }

    private void QueueSave()
    {
        lock (persistenceLock)
        {
            saveRequested = true;
            pendingSave ??= PersistRequestedPlansAsync();
        }
    }

    private async Task PersistRequestedPlansAsync()
    {
        await Task.Yield();
        while (true)
        {
            lock (persistenceLock)
            {
                if (!saveRequested)
                {
                    pendingSave = null;
                    return;
                }

                saveRequested = false;
            }

            try
            {
                var plan = DailyPlan.Create(Date, Items.Select(item => new PlannedWorkItem(
                    item.Id, Date, item.Start, item.End, item.Name, item.JiraIssueKey, item.Description,
                    item.Duration, item.TogglProject, item.TempoCategory, item.IsBillable, item.Status)
                    { TogglProjectId = item.TogglProjectId, PostToToggl = item.PostToToggl }).ToArray());
                await repository!.SaveAsync(plan);
                PersistenceError = null;
            }
            catch
            {
                PersistenceError = "Could not save today's plan.";
                lock (persistenceLock)
                {
                    if (!saveRequested)
                    {
                        pendingSave = null;
                        return;
                    }
                }
            }
        }
    }

    private void SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
