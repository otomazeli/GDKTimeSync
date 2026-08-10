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

    public ShellViewModel(
        IConfigurationStateService configurationState,
        TodayViewModel? today = null,
        TemplatesViewModel? templates = null,
        ReviewViewModel? review = null)
    {
        _ = configurationState;
        this.today = today ?? new TodayViewModel();
        this.templates = templates ?? new TemplatesViewModel(this.today);
        this.review = review ?? new ReviewViewModel();
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
        NavigationPage.Review => review,
        _ => SelectedPage
    };

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await templates.InitializeAsync(cancellationToken);
        await today.InitializeAsync(cancellationToken);
    }

    public Task FlushAsync() => Task.WhenAll(today.FlushAsync(), templates.FlushAsync());

    private void Navigate(object? page)
    {
        if (page is NavigationPage navigationPage)
            SelectedPage = navigationPage;
    }
}
