using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GDK.TimeSync.Desktop;

// Two instances against one database each import the same Toggl entry under their own new item id:
// the plan's version check only forces a reconcile, and that merges the other writer's rows by id
// without noticing they carry a toggl_entry_id the local list already has. Three instances running
// at once on 2026-09-08 tripled every imported row. Keeping it to one instance removes the second
// writer, which is the only place the duplication comes from.
internal static class SingleInstance
{
    // Closing the window hides it rather than destroying it, so a second launch cannot find the
    // first instance through Process.MainWindowHandle -- that is zero for a hidden window. A
    // registered broadcast message still reaches it, so that is what carries "show yourself".
    private const int HwndBroadcast = 0xffff;
    private static readonly int ActivationMessage = RegisterWindowMessage("GDK.TimeSync.Activate");

    // Local\ rather than Global\: one instance per logged-on user, not one per machine.
    // Held for the lifetime of the process; Windows releases it when the process ends.
    private static Mutex? held;

    internal static bool TryAcquire(string name = @"Local\GDK.TimeSync.SingleInstance")
    {
        var mutex = new Mutex(true, name, out var isFirst);
        if (isFirst)
            held = mutex;
        else
            mutex.Dispose();
        return isFirst;
    }

    internal static void ActivateExistingInstance() =>
        PostMessage(HwndBroadcast, ActivationMessage, IntPtr.Zero, IntPtr.Zero);

    internal static void ListenForActivation(Window window, Action activate)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(activate);
        if (PresentationSource.FromVisual(window) is not HwndSource source) return;

        source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message == ActivationMessage) activate();
            return IntPtr.Zero;
        });
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(int hwnd, int message, IntPtr wParam, IntPtr lParam);
}
