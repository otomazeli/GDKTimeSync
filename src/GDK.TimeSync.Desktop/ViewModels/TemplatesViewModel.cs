using System.Collections.ObjectModel;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class TemplatesViewModel
{
    public TemplatesViewModel(TodayViewModel today)
    {
        Templates.Add(new RecurringTaskTemplateViewModel(
            "Knowledge transfer", "CGMFRAVII-2767", "Knowledge transfer", TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT"));
        AddTemplateCommand = today.AddTemplateCommand;
    }

    public ObservableCollection<RecurringTaskTemplateViewModel> Templates { get; } = [];
    public RelayCommand AddTemplateCommand { get; }
}
