using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class HistoryViewModel(IDeliveryHistoryRepository repository) : INotifyPropertyChanged
{
    private string? loadError;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DeliveryHistoryItemViewModel> Items { get; } = [];
    public string? LoadError { get => loadError; private set => SetField(ref loadError, value); }
    public bool IsEmpty => Items.Count == 0;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = await repository.ListHistoryAsync(cancellationToken);
            Items.Clear();
            foreach (var entry in entries)
                Items.Add(new DeliveryHistoryItemViewModel(entry));
            LoadError = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEmpty)));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            Items.Clear();
            LoadError = "Could not load delivery history.";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEmpty)));
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
