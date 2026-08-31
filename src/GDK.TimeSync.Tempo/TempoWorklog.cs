namespace GDK.TimeSync.Tempo;

public sealed record TempoWorklog(long TempoWorklogId, string Worker, string OriginTaskId, DateTime Started, int TimeSpentSeconds, string Comment);
