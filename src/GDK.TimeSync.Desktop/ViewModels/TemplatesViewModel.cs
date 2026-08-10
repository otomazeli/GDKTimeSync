using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class TemplatesViewModel : INotifyPropertyChanged
{
    private readonly ITemplateRepository? repository;
    private readonly object persistenceLock = new();
    private readonly HashSet<RecurringTaskTemplateViewModel> dirtyTemplates = [];
    private Task? pendingSave;
    private bool isInitialized;
    private string? statusMessage;

    public TemplatesViewModel(TodayViewModel today, ITemplateRepository? repository = null)
    {
        this.repository = repository;
        Templates.CollectionChanged += OnTemplatesChanged;
        if (repository is null)
            Templates.Add(CreateSampleTemplate());
        AddTemplateCommand = today.AddTemplateCommand;
        NewTemplateCommand = new RelayCommand(_ => Templates.Add(new RecurringTaskTemplateViewModel()));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RecurringTaskTemplateViewModel> Templates { get; } = [];
    public RelayCommand AddTemplateCommand { get; }
    public RelayCommand NewTemplateCommand { get; }
    public string? StatusMessage { get => statusMessage; private set => SetField(ref statusMessage, value); }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (repository is null || isInitialized)
            return;

        try
        {
            var templates = await repository.ListAsync(cancellationToken);
            Templates.Clear();
            if (templates.Count == 0)
            {
                var sample = CreateSampleTemplate();
                Templates.Add(sample);
                await repository.SaveAsync(ToTemplate(sample), cancellationToken);
            }
            else
            {
                foreach (var template in templates)
                    Templates.Add(ToViewModel(template));
            }

            isInitialized = true;
            StatusMessage = null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (Templates.Count == 0)
                Templates.Add(CreateSampleTemplate());
            isInitialized = true;
            StatusMessage = "Could not load templates. You can continue using the local sample.";
        }
    }

    public Task FlushAsync()
    {
        lock (persistenceLock)
            return pendingSave ?? Task.CompletedTask;
    }

    private void OnTemplatesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (RecurringTaskTemplateViewModel template in e.OldItems)
                template.PropertyChanged -= OnTemplatePropertyChanged;
        if (e.NewItems is not null)
            foreach (RecurringTaskTemplateViewModel template in e.NewItems)
            {
                template.PropertyChanged += OnTemplatePropertyChanged;
                QueueSave(template);
            }
    }

    private void OnTemplatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is RecurringTaskTemplateViewModel template)
            QueueSave(template);
    }

    private void QueueSave(RecurringTaskTemplateViewModel template)
    {
        if (repository is null || !isInitialized)
            return;

        lock (persistenceLock)
        {
            dirtyTemplates.Add(template);
            pendingSave ??= PersistDirtyTemplatesAsync();
        }
    }

    private async Task PersistDirtyTemplatesAsync()
    {
        await Task.Yield();
        while (true)
        {
            RecurringTaskTemplateViewModel[] templates;
            lock (persistenceLock)
            {
                if (dirtyTemplates.Count == 0)
                {
                    pendingSave = null;
                    return;
                }

                templates = dirtyTemplates.ToArray();
                dirtyTemplates.Clear();
            }

            try
            {
                foreach (var template in templates)
                    await repository!.SaveAsync(ToTemplate(template));
                StatusMessage = null;
            }
            catch
            {
                StatusMessage = "Could not save templates.";
                lock (persistenceLock)
                {
                    dirtyTemplates.UnionWith(templates);
                    pendingSave = null;
                }
                return;
            }
        }
    }

    private static RecurringTaskTemplateViewModel CreateSampleTemplate() => new(
        "Knowledge transfer", "CGMFRAVII-2767", "Knowledge transfer", TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT");

    private static RecurringTaskTemplateViewModel ToViewModel(RecurringTaskTemplate template) => new(
        template.Name, template.JiraIssueKey, template.Description, template.Duration, template.TogglProject, template.TempoCategory, template.IsBillable, template.Id);

    private static RecurringTaskTemplate ToTemplate(RecurringTaskTemplateViewModel template) => new(
        template.Id, template.Name, template.JiraIssueKey, template.Description, template.Duration, template.TogglProject, template.TempoCategory, template.IsBillable);

    private void SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
