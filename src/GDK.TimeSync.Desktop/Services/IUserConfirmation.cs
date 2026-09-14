namespace GDK.TimeSync.Desktop.Services;

// A blocking yes/no the view model can ask without knowing about WPF -- same shape and reason as
// IClipboardService. Used where the consequence lands outside the app and cannot be undone by it:
// a second message in a Slack channel is not something this app can take back.
public interface IUserConfirmation
{
    bool Confirm(string message, string title = "GDK TimeSync");
}
