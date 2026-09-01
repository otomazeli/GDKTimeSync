using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class DiagnosticsViewModel : INotifyPropertyChanged
{
    private const int MaxEntries = 500;
    private readonly AuditLogReader reader;
    private readonly IClipboardService? clipboard;
    private string statusText = "";

    public DiagnosticsViewModel(AuditLogReader reader, IClipboardService? clipboard = null)
    {
        this.reader = reader;
        this.clipboard = clipboard;
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());
        CopyAllCommand = new RelayCommand(_ => clipboard?.SetText(string.Join(Environment.NewLine, Entries)));
        OpenFolderCommand = new RelayCommand(_ => OpenFolder());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> Entries { get; } = [];
    public RelayCommand RefreshCommand { get; }
    public RelayCommand CopyAllCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public string LogFilePath => reader.CurrentFilePath;

    public string StatusText
    {
        get => statusText;
        private set
        {
            if (statusText == value) return;
            statusText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        }
    }

    public Task RefreshAsync()
    {
        Entries.Clear();
        foreach (var entry in reader.ReadRecentEntries(MaxEntries))
            Entries.Add(entry);
        StatusText = Entries.Count == 0
            ? "No entries recorded today."
            : $"Showing the most recent {Entries.Count} entries from {reader.CurrentFilePath}";
        return Task.CompletedTask;
    }

    private void OpenFolder()
    {
        try
        {
            var directory = Path.GetDirectoryName(reader.CurrentFilePath);
            if (directory is not null) Process.Start(new ProcessStartInfo("explorer.exe", directory));
        }
        catch
        {
        }
    }
}
