using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class TodayViewModel : INotifyPropertyChanged
{
    private readonly IDailyPlanRepository? repository;
    private readonly object persistenceLock = new();
    private Task? pendingSave;
    private bool saveRequested;
    private bool isInitialized;
    private string? persistenceError;

    public TodayViewModel(IDailyPlanRepository? repository = null, DateOnly? date = null)
    {
        this.repository = repository;
        Date = date ?? DateOnly.FromDateTime(DateTime.Today);
        Items.CollectionChanged += OnItemsChanged;
        AddItemCommand = new RelayCommand(_ => Items.Add(new PlannedWorkItemViewModel()));
        RemoveItemCommand = new RelayCommand(RemoveItem);
        AddTemplateCommand = new RelayCommand(AddTemplate);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PlannedWorkItemViewModel> Items { get; } = [];
    public DateOnly Date { get; }
    public RelayCommand AddItemCommand { get; }
    public RelayCommand RemoveItemCommand { get; }
    public RelayCommand AddTemplateCommand { get; }
    public double PlannedSeconds => Items.Sum(item => item.Duration.TotalSeconds);
    public string? PersistenceError { get => persistenceError; private set => SetField(ref persistenceError, value); }

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
                    Items.Add(new PlannedWorkItemViewModel(item.Name, item.JiraIssueKey, item.Comment, item.Duration, item.TogglProject, item.TempoCategory, item.Id, item.Start, item.End, item.IsBillable));
            isInitialized = true;
            PersistenceError = null;
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

    public DailyPlan CreatePlanSnapshot() => DailyPlan.Create(Date, Items.Select(item => new PlannedWorkItem(
        item.Id, Date, item.Start, item.End, item.Name, item.JiraIssueKey, item.Description,
        item.Duration, item.TogglProject, item.TempoCategory, item.IsBillable)).ToArray());

    private void AddTemplate(object? template)
    {
        if (template is not RecurringTaskTemplateViewModel source) return;
        Items.Add(new PlannedWorkItemViewModel(source.Name, source.JiraIssueKey, source.Description, source.Duration, source.TogglProject, source.TempoCategory, isBillable: source.IsBillable));
    }

    private void RemoveItem(object? item)
    {
        if (item is PlannedWorkItemViewModel plannedItem)
            Items.Remove(plannedItem);
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
        SaveAfterUserAction();
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
                    item.Duration, item.TogglProject, item.TempoCategory, item.IsBillable)).ToArray());
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
