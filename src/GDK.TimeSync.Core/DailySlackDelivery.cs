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
    DailySlackFailureCode? FailureCode)
{
    // The day's claim is taken before the post, so a rejected post leaves the day locked with
    // nothing sent. Reopening it is only safe when we know nothing was delivered: Slack answered,
    // and answered with a failure. Transport, Cancelled and InvalidResponse are all "we do not
    // know", and a second send would double-post into the channel -- those stay locked.
    public bool CanBeRetried =>
        State == DailySlackDeliveryState.ReconciliationRequired &&
        FailureCode == DailySlackFailureCode.UnsuccessfulResponse;
}

public interface IDailySlackDeliveryRepository
{
    Task<DailySlackDelivery?> GetAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<bool> TryClaimAsync(DateOnly date, string contentFingerprint, CancellationToken cancellationToken = default);
    Task SaveAsync(DailySlackDelivery delivery, CancellationToken cancellationToken = default);
}
