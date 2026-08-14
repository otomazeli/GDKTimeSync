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
        menu.Items.Add("Reconcile Today").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => settings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        notifyIcon = new NotifyIcon { Icon = SystemIcons.Application, Text = "GDK TimeSync", ContextMenuStrip = menu, Visible = true };
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
}
