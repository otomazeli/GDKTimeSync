namespace GDK.TimeSync.Tempo;

public sealed record TempoWorklogRequest(string Worker, string OriginTaskId, DateTime Started, int TimeSpentSeconds, string Comment);
