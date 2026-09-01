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
    private bool isLoadingItems;
    private DateOnly currentDate;
    private int knownVersion;
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
    private readonly IAuditLog? auditLog;
    private string? projectLoadError;

    public TodayViewModel(IDailyPlanRepository? repository = null, DateOnly? date = null, IAiConsentService? aiConsentService = null, IAssistedTextGenerator? assistedTextGenerator = null, IIntegrationClientFactory? integrationClients = null, IUserSettingsStore? settingsStore = null, IAuditLog? auditLog = null)
    {
        this.repository = repository;
        this.aiConsentService = aiConsentService;
        this.assistedTextGenerator = assistedTextGenerator;
        this.integrationClients = integrationClients;
        this.settingsStore = settingsStore;
        this.auditLog = auditLog;
        currentDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        Items.CollectionChanged += OnItemsChanged;
        AddItemCommand = new RelayCommand(_ => AddItem());
        RemoveItemCommand = new RelayCommand(RemoveItem);
        AddTemplateCommand = new RelayCommand(AddTemplate);
        OpenAiConsentCommand = new RelayCommand(OpenAiConsent);
        ConfirmAiConsentCommand = new RelayCommand(async _ => await ConfirmAiConsentAsync());
        CancelAiConsentCommand = new RelayCommand(_ => CancelAiConsent());
        ApplyAiSuggestionCommand = new RelayCommand(_ => ApplyAiSuggestion());
        RefreshProjectsCommand = new RelayCommand(_ => _ = LoadProjectsAsync());
        GoToTodayCommand = new RelayCommand(_ => _ = SelectDateAsync(DateOnly.FromDateTime(DateTime.Today)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PlannedWorkItemViewModel> Items { get; } = [];
    public ObservableCollection<TogglProject> TogglProjects { get; } = [];
    public DateOnly Date
    {
        get => currentDate;
        private set
        {
            if (currentDate == value) return;
            currentDate = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Date)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedDateTime)));
        }
    }

    public DateTime? SelectedDateTime
    {
        get => Date.ToDateTime(TimeOnly.MinValue);
        set
        {
            if (value is { } selected)
                _ = SelectDateAsync(DateOnly.FromDateTime(selected));
        }
    }

    public RelayCommand AddItemCommand { get; }
    public RelayCommand RemoveItemCommand { get; }
    public RelayCommand AddTemplateCommand { get; }
    public RelayCommand OpenAiConsentCommand { get; }
    public RelayCommand ConfirmAiConsentCommand { get; }
    public RelayCommand CancelAiConsentCommand { get; }
    public RelayCommand ApplyAiSuggestionCommand { get; }
    public RelayCommand RefreshProjectsCommand { get; }
    public RelayCommand GoToTodayCommand { get; }
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

    // AI assistance is off by default and has no provider behind it, so the draft button stays
    // hidden until the user opts in from Settings rather than sitting there always failing.
    public bool IsAiEnabled => aiConsentService?.IsEnabled == true;

    // Settings is a separate dialog that does not push changes here; the shell re-asks on every
    // navigation to Today, which is the only way back to this page after saving settings.
    public void RefreshAiAvailability() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAiEnabled)));

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (repository is null || isInitialized)
            return;

        await LoadItemsForCurrentDateAsync(cancellationToken);
        isInitialized = true;
        if (PersistenceError is null)
            await LoadProjectsAsync(cancellationToken);
    }

    // Raised whenever the user picks a date -- including re-picking the one already shown, which is
    // how "Today" is used as a refresh. MainViewModel listens and pulls that date from Toggl:
    // loading the local plan alone left a freshly-picked day showing stale rows until the auto-sync
    // interval happened to elapse, which read as "sync stopped working".
    public event EventHandler? DateSelected;

    public async Task SelectDateAsync(DateOnly newDate, CancellationToken cancellationToken = default)
    {
        if (newDate != Date)
        {
            await FlushAsync();
            Date = newDate;
            await LoadItemsForCurrentDateAsync(cancellationToken);
        }

        DateSelected?.Invoke(this, EventArgs.Empty);
        auditLog?.Write(AuditLevel.Info, "Today", $"Date selected: {Date}");
    }

    private async Task LoadItemsForCurrentDateAsync(CancellationToken cancellationToken)
    {
        isLoadingItems = true;
        try
        {
            if (repository is null)
            {
                if (Items.Count == 0)
                    Items.Add(new PlannedWorkItemViewModel());
                return;
            }

            var plan = await repository.GetAsync(Date, cancellationToken);
            knownVersion = plan?.Version ?? 0;
            Items.Clear();
            if (plan is null || plan.Items.Count == 0)
                Items.Add(new PlannedWorkItemViewModel());
            else
                foreach (var item in plan.Items)
                    Items.Add(new PlannedWorkItemViewModel(item.Name, item.JiraIssueKey, item.Comment, item.Duration, item.TogglProject, item.TempoCategory, item.Id, item.Start, item.End, item.IsBillable, item.Status, item.TogglProjectId, item.PostToToggl, item.TogglEntryId, item.Source));
            PersistenceError = null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (Items.Count == 0)
                Items.Add(new PlannedWorkItemViewModel());
            PersistenceError = "Could not load today's plan.";
        }
        finally
        {
            isLoadingItems = false;
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
        { TogglProjectId = item.TogglProjectId, PostToToggl = item.PostToToggl, TogglEntryId = item.TogglEntryId, Source = item.Source }).ToArray());

    public TodaySyncMergeResult ApplyPullResult(TogglSyncPullResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        foreach (var updated in result.ItemsToUpdate)
        {
            var existing = Items.FirstOrDefault(item => item.Id == updated.Id);
            if (existing is null) continue;
            existing.Start = updated.Start;
            existing.End = updated.End;
            existing.Description = updated.Comment;
            existing.TogglEntryId = updated.TogglEntryId;
            existing.TogglProjectId = updated.TogglProjectId;
            existing.JiraIssueKey = updated.JiraIssueKey;
            existing.TempoCategory = updated.TempoCategory;
        }

        foreach (var added in result.ItemsToAdd)
            Items.Add(new PlannedWorkItemViewModel(
                added.Name, added.JiraIssueKey, added.Comment, added.Duration, added.TogglProject, added.TempoCategory,
                added.Id, added.Start, added.End, added.IsBillable, added.Status,
                added.TogglProjectId, added.PostToToggl, added.TogglEntryId, added.Source));

        // TogglProjectId is set on import/update, but the display name is resolved from the
        // already-loaded TogglProjects list, same as the existing per-property-change path.
        ApplyProjectNames();

        return new TodaySyncMergeResult(result.ItemsToAdd.Count, result.ItemsToUpdate.Count, result.ReconciliationFlaggedCount);
    }

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
        if (repository is not null && isInitialized && !isLoadingItems)
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
                    { TogglProjectId = item.TogglProjectId, PostToToggl = item.PostToToggl, TogglEntryId = item.TogglEntryId, Source = item.Source }).ToArray())
                    with { Version = knownVersion };
                await repository!.SaveAsync(plan);
                knownVersion++;
                PersistenceError = null;
            }
            catch (PlanConcurrencyException)
            {
                // Another writer (e.g. background Toggl auto-sync) saved this date since we last
                // read it. Pull in anything it added that we don't already have locally, then loop
                // around to retry the save with the now-current version -- local edits still win.
                await ReconcileWithLatestPlanAsync();
                lock (persistenceLock)
                {
                    saveRequested = true;
                }
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

    private async Task ReconcileWithLatestPlanAsync()
    {
        var latest = await repository!.GetAsync(Date);
        knownVersion = latest?.Version ?? 0;
        if (latest is null) return;

        var localIds = Items.Select(item => item.Id).ToHashSet();
        foreach (var remoteItem in latest.Items)
        {
            if (localIds.Contains(remoteItem.Id)) continue;
            Items.Add(new PlannedWorkItemViewModel(
                remoteItem.Name, remoteItem.JiraIssueKey, remoteItem.Comment, remoteItem.Duration, remoteItem.TogglProject, remoteItem.TempoCategory,
                remoteItem.Id, remoteItem.Start, remoteItem.End, remoteItem.IsBillable, remoteItem.Status,
                remoteItem.TogglProjectId, remoteItem.PostToToggl, remoteItem.TogglEntryId, remoteItem.Source));
        }
    }

    private void SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record TodaySyncMergeResult(int Imported, int Updated, int ReconciliationFlagged);
