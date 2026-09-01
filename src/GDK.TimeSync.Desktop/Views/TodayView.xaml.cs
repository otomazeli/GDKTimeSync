using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Desktop.Views;

public partial class TodayView : System.Windows.Controls.UserControl
{
    public TodayView() => InitializeComponent();

    // Lost focus, not PropertyChanged: the key field updates its binding on every keystroke, and a
    // lookup per character would be a request per character. The view model decides whether the row
    // actually qualifies -- this only says "the user finished typing a key".
    private void OnJiraKeyLostFocus(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is TodayViewModel today && today.SelectedItem is { } item)
            _ = today.LookUpJiraKeyAsync(item);
    }
}
