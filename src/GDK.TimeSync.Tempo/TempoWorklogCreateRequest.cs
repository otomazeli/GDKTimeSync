namespace GDK.TimeSync.Tempo;

public sealed record TempoWorklogCreateRequest(
    string Worker,
    string OriginTaskId,
    DateTime Started,
    int TimeSpentSeconds,
    string Comment,
    string WorkCategory = "");
