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

    public DiagnosticsViewModel(
        AuditLogReader reader,
        IClipboardService? clipboard = null,
        ILocalPlanSnapshotProvider? planProvider = null,
        IIntegrationDiagnosticsService? diagnosticsService = null,
        ILiveIntegrationValidationService? validationService = null)
    {
        this.reader = reader;
        this.clipboard = clipboard;
        LiveValidation = new LiveValidationViewModel(planProvider, diagnosticsService, validationService);
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());
        CopyAllCommand = new RelayCommand(_ => clipboard?.SetText(string.Join(Environment.NewLine, Entries.Select(entry => entry.Text))));
        OpenFolderCommand = new RelayCommand(_ => OpenFolder());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AuditLogEntry> Entries { get; } = [];
    public RelayCommand RefreshCommand { get; }
    public RelayCommand CopyAllCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public LiveValidationViewModel LiveValidation { get; }
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

    public async Task RefreshAsync()
    {
        Entries.Clear();
        foreach (var entry in reader.ReadRecentEntries(MaxEntries))
            Entries.Add(entry);
        StatusText = Entries.Count == 0
            ? "No entries recorded today."
            : $"Showing the most recent {Entries.Count} entries from {reader.CurrentFilePath}";
        await LiveValidation.RefreshAsync();
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
