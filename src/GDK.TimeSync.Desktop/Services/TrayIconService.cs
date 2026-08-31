using System.Drawing;
using System.Windows.Forms;
using System.Windows.Input;

namespace GDK.TimeSync.Desktop.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon notifyIcon;
    private readonly ToolStripMenuItem syncItem;
    private readonly ICommand syncCommand;

    public TrayIconService(Action open, Action settings, Action exit, ICommand syncCommand)
    {
        this.syncCommand = syncCommand;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open GDK TimeSync", null, (_, _) => open());
        menu.Items.Add(new ToolStripSeparator());
        syncItem = new ToolStripMenuItem("Sync Now", null, (_, _) => syncCommand.Execute(null));
        menu.Items.Add(syncItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => settings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        notifyIcon = new NotifyIcon { Icon = LoadApplicationIcon(), Text = "GDK TimeSync", ContextMenuStrip = menu, Visible = true };
        notifyIcon.DoubleClick += (_, _) => open();
        syncCommand.CanExecuteChanged += OnSyncCanExecuteChanged;
        UpdateSyncEnabled();
    }

    public void Dispose()
    {
        syncCommand.CanExecuteChanged -= OnSyncCanExecuteChanged;
        notifyIcon.Dispose();
    }

    public void ShowReviewReminder() =>
        notifyIcon.ShowBalloonTip(5000, "GDK TimeSync", "Your end-of-day review is ready.", ToolTipIcon.Info);

    private void OnSyncCanExecuteChanged(object? sender, EventArgs e) => UpdateSyncEnabled();

    private void UpdateSyncEnabled() => syncItem.Enabled = syncCommand.CanExecute(null);

    // <ApplicationIcon> embeds GDK.TimeSync.ico into the exe itself (the taskbar/Explorer icon
    // already used it correctly), but a NotifyIcon needs an explicit Icon instance -- it never
    // picks up the embedded one on its own. Extracting it from the running exe works for both
    // the self-contained single-file publish and a plain framework-dependent build, without
    // depending on a loose Assets file being present at runtime.
    private static Icon LoadApplicationIcon()
    {
        try
        {
            var path = System.Windows.Forms.Application.ExecutablePath;
            return !string.IsNullOrEmpty(path) ? Icon.ExtractAssociatedIcon(path) ?? SystemIcons.Application : SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }
}
