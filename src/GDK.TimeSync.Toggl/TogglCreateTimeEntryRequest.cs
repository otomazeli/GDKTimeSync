namespace GDK.TimeSync.Toggl;

public sealed record TogglCreateTimeEntryRequest(long WorkspaceId, string Description, DateTimeOffset Start, DateTimeOffset Stop);
