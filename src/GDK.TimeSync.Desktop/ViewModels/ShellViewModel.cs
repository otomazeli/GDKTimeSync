using System.ComponentModel;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private NavigationPage selectedPage = NavigationPage.Today;
    private readonly TodayViewModel today;
    private readonly TemplatesViewModel templates;
    private readonly ReviewViewModel review;
    private readonly SettingsViewModel? settings;
    private readonly HistoryViewModel? history;

    public ShellViewModel(
        IConfigurationStateService configurationState,
        TodayViewModel? today = null,
        TemplatesViewModel? templates = null,
        ReviewViewModel? review = null,
        SettingsViewModel? settings = null,
        HistoryViewModel? history = null)
    {
        _ = configurationState;
        this.today = today ?? new TodayViewModel();
        this.templates = templates ?? new TemplatesViewModel(this.today);
        this.review = review ?? new ReviewViewModel();
        this.settings = settings;
        this.history = history;
        NavigateCommand = new RelayCommand(Navigate);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand NavigateCommand { get; }

    public NavigationPage SelectedPage
    {
        get => selectedPage;
        private set
        {
            if (selectedPage == value) return;
            selectedPage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedPage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentPageViewModel)));
        }
    }

    public object CurrentPageViewModel => SelectedPage switch
    {
        NavigationPage.Today => today,
        NavigationPage.Templates => templates,
        NavigationPage.History when history is not null => history,
        NavigationPage.Review => review,
        NavigationPage.Settings when settings is not null => settings,
        _ => SelectedPage
    };

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await templates.InitializeAsync(cancellationToken);
        await today.InitializeAsync(cancellationToken);
        if (settings is not null) await settings.LoadAsync(cancellationToken);
    }

    public Task FlushAsync() => Task.WhenAll(today.FlushAsync(), templates.FlushAsync());

    private void Navigate(object? page)
    {
        if (page is NavigationPage navigationPage)
        {
            SelectedPage = navigationPage;
            if (navigationPage == NavigationPage.History)
                _ = history?.LoadAsync();
        }
    }
}
