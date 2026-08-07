namespace GDK.TimeSync.Core;

public sealed record SourceTimeEntry(string SourceEntryId, string Description, DateTimeOffset Started, long DurationSeconds);
