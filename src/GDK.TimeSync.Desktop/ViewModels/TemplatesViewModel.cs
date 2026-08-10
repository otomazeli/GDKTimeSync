using System.Collections.ObjectModel;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class TemplatesViewModel
{
    private readonly ITemplateRepository? repository;
    private bool isInitialized;

    public TemplatesViewModel(TodayViewModel today, ITemplateRepository? repository = null)
    {
        this.repository = repository;
        AddSampleTemplate();
        AddTemplateCommand = today.AddTemplateCommand;
    }

    public ObservableCollection<RecurringTaskTemplateViewModel> Templates { get; } = [];
    public RelayCommand AddTemplateCommand { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (repository is null || isInitialized)
            return;

        var templates = await repository.ListAsync(cancellationToken);
        Templates.Clear();
        if (templates.Count == 0)
            AddSampleTemplate();
        else
            foreach (var template in templates)
                Templates.Add(new RecurringTaskTemplateViewModel(template.Name, template.JiraIssueKey, template.Description, template.Duration, template.TogglProject, template.TempoCategory, template.IsBillable, template.Id));
        isInitialized = true;
    }

    private void AddSampleTemplate() => Templates.Add(new RecurringTaskTemplateViewModel(
        "Knowledge transfer", "CGMFRAVII-2767", "Knowledge transfer", TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT"));
}
