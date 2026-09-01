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
    public ConnectionStatusViewModel? ConnectionStatus { get; }

    // Every sync already produces a result line; until this was surfaced it was computed and
    // discarded, so a sync that imported nothing looked exactly like one that never ran.
    public MainViewModel? Main { get; }

    public ShellViewModel(
        IConfigurationStateService configurationState,
        TodayViewModel? today = null,
        TemplatesViewModel? templates = null,
        ReviewViewModel? review = null,
        SettingsViewModel? settings = null,
        HistoryViewModel? history = null,
        ConnectionStatusViewModel? connectionStatus = null,
        MainViewModel? main = null)
    {
        Main = main;
        _ = configurationState;
        this.today = today ?? new TodayViewModel();
        this.templates = templates ?? new TemplatesViewModel(this.today);
        this.review = review ?? new ReviewViewModel();
        this.settings = settings;
        this.history = history;
        ConnectionStatus = connectionStatus;
        NavigateCommand = new RelayCommand(value => _ = NavigateAsync(value));
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
        if (ConnectionStatus is not null) await ConnectionStatus.RefreshAsync(cancellationToken);
    }

    public Task FlushAsync() => Task.WhenAll(today.FlushAsync(), templates.FlushAsync());

    public async Task NavigateAsync(object? page, CancellationToken cancellationToken = default)
    {
        if (page is NavigationPage navigationPage)
        {
            SelectedPage = navigationPage;
            if (navigationPage == NavigationPage.Today)
                today.RefreshAiAvailability();
            if (navigationPage == NavigationPage.Review)
                await review.RefreshAsync(cancellationToken);
            if (navigationPage == NavigationPage.History)
                _ = history?.LoadAsync();
        }
    }
}
