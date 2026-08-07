using System.Drawing;
using System.Windows.Forms;

namespace GDK.TimeSync.Desktop.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon notifyIcon;

    public TrayIconService(Action open, Action settings, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open GDK TimeSync", null, (_, _) => open());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sync Now").Enabled = false;
        menu.Items.Add("Reconcile Today").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => settings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "GDK TimeSync - Not configured",
            ContextMenuStrip = menu,
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => open();
    }

    public void Dispose() => notifyIcon.Dispose();
}
