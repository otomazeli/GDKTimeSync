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
    // allowResend reopens a day this repository would otherwise keep closed -- including one already
    // Sent. It exists for the case the app cannot see: the user has deleted the previous message in
    // Slack and wants the day posted again. Only ever set from an explicit confirmation, never as a
    // default, because the duplicate it can create lands in a channel the app cannot clean up.
    Task<bool> TryClaimAsync(DateOnly date, string contentFingerprint, bool allowResend = false, CancellationToken cancellationToken = default);
    Task SaveAsync(DailySlackDelivery delivery, CancellationToken cancellationToken = default);
}
