using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.Services;

// The Diagnostics list used to be plain strings, so every line looked the same and a failure had to
// be found by reading. Carrying the level lets the view colour the ones that went wrong.
public sealed record AuditLogEntry(string Text, AuditLevel Level)
{
    public override string ToString() => Text;
}
