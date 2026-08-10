using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class TodayViewModel : INotifyPropertyChanged
{
    public TodayViewModel()
    {
        Items.CollectionChanged += OnItemsChanged;
        AddItemCommand = new RelayCommand(_ => Items.Add(new PlannedWorkItemViewModel()));
        RemoveItemCommand = new RelayCommand(RemoveItem);
        AddTemplateCommand = new RelayCommand(AddTemplate);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PlannedWorkItemViewModel> Items { get; } = [];
    public RelayCommand AddItemCommand { get; }
    public RelayCommand RemoveItemCommand { get; }
    public RelayCommand AddTemplateCommand { get; }
    public double PlannedSeconds => Items.Sum(item => item.Duration.TotalSeconds);

    private void AddTemplate(object? template)
    {
        if (template is not RecurringTaskTemplateViewModel source) return;
        Items.Add(new PlannedWorkItemViewModel(source.Name, source.JiraIssueKey, source.Description, source.Duration, source.TogglProject, source.TempoCategory));
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
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlannedWorkItemViewModel.Duration))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlannedSeconds)));
    }
}
