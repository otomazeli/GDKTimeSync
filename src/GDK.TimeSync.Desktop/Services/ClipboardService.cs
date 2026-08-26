namespace GDK.TimeSync.Desktop.Services;

public sealed class ClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        System.Windows.Clipboard.SetText(text);
    }
}
