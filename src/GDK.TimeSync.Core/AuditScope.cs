namespace GDK.TimeSync.Core;

// A delivery writes some of its own lines ("Confirmed ...", "... -> Failed TempoFailed") while the
// HTTP handler writes the others ("Tempo POST /worklogs -> 400"), and with auto-sync ticking in
// between the only thing joining the two was the timestamp. An AsyncLocal flows through every await
// of one delivery, HttpClient's included, so stamping it in the log tags both sides with one token.
// Null outside a delivery, which is most of the log.
public static class AuditScope
{
    private static readonly AsyncLocal<string?> CurrentToken = new();

    public static string? Current => CurrentToken.Value;

    // Restores the previous value rather than clearing, so a nested scope cannot silently end an
    // outer one.
    public static IDisposable Begin(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var previous = CurrentToken.Value;
        CurrentToken.Value = token;
        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => CurrentToken.Value = previous;
    }
}
