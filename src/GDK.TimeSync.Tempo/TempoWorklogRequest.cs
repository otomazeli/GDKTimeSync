namespace GDK.TimeSync.Tempo;

// WorkCategory is optional so a plan written before it existed still posts; blank means "send no
// attribute", which is not the same as sending a blank one.
public sealed record TempoWorklogRequest(string Worker, string OriginTaskId, DateTime Started, int TimeSpentSeconds, string Comment, string WorkCategory = "");
