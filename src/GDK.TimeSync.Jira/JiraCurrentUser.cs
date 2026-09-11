namespace GDK.TimeSync.Jira;

// Key is what Tempo wants as a worklog `worker`, and it is not always the same as Name -- the Delphi
// reference client (uTempoClient.pas) reads `key` first and falls back to `name`.
public sealed record JiraCurrentUser(string? Name, string? DisplayName, string? EmailAddress, string? Key = null)
{
    // The rule lives here, on the only type that can answer it, because it was previously spelled out
    // in two services and a typed setting overrode both -- which is how an email address reached
    // Tempo and came back "User is invalid". EmailAddress is deliberately never a candidate.
    public string TempoWorker => string.IsNullOrWhiteSpace(Key) ? Name ?? "" : Key;
}
