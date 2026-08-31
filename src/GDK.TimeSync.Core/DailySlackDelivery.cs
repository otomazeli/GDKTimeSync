namespace GDK.TimeSync.Core;

public enum DailySlackDeliveryState
{
    InProgress,
    Sent,
    ReconciliationRequired
}

public enum DailySlackFailureCode
{
    UnsuccessfulResponse,
    InvalidResponse,
    Transport,
    Cancelled,
    PersistenceFailed
}

public sealed record DailySlackDelivery(
    DateOnly Date,
    string ContentFingerprint,
    DailySlackDeliveryState State,
    DailySlackFailureCode? FailureCode);

public interface IDailySlackDeliveryRepository
{
    Task<DailySlackDelivery?> GetAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<bool> TryClaimAsync(DateOnly date, string contentFingerprint, CancellationToken cancellationToken = default);
    Task SaveAsync(DailySlackDelivery delivery, CancellationToken cancellationToken = default);
}
