// Fully qualified below: WinForms is also referenced here, and both assemblies have an Application
// and a MessageBox.
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace GDK.TimeSync.Desktop.Services;

public sealed class MessageBoxUserConfirmation : IUserConfirmation
{
    // OKCancel rather than a purpose-built window: WPF's MessageBox cannot relabel its buttons, and a
    // custom dialog is a Window, a XAML file and an owner-tracking problem for one word. The caller
    // names the action in the message text instead.
    //
    // Cancel is the default button, so Enter or a stray double-click declines rather than reposts.
    public bool Confirm(string message, string title = "GDK TimeSync")
    {
        // Owned only when there is a window on screen to own it: closing the app to the tray hides
        // the main window, and owning a dialog to a hidden window leaves it with nothing to sit in
        // front of.
        var owner = Application.Current?.MainWindow;
        return owner is { IsLoaded: true, IsVisible: true }
            ? MessageBox.Show(owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK
            : MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;
    }
}
