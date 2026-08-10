using System.ComponentModel;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Desktop.Services;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private NavigationPage selectedPage = NavigationPage.Today;

    public ShellViewModel(IConfigurationStateService configurationState)
    {
        _ = configurationState;
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
        }
    }

    private void Navigate(object? page)
    {
        if (page is NavigationPage navigationPage)
            SelectedPage = navigationPage;
    }
}
