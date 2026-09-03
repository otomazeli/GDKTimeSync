namespace GDK.TimeSync.Jira;

// Key is what Tempo wants as a worklog `worker`, and it is not always the same as Name -- the Delphi
// reference client (uTempoClient.pas) reads `key` first and falls back to `name`.
public sealed record JiraCurrentUser(string? Name, string? DisplayName, string? EmailAddress, string? Key = null);
