namespace GDK.TimeSync.Core;

public enum DeliveryAttemptStatus
{
    InProgress,
    Succeeded,
    Failed,
    Cancelled,
    ReconciliationRequired
}

public enum DeliveryFailureCode
{
    TogglFailed,
    JiraFailed,
    JiraIssueNotFound,
    TempoFailed,
    PersistenceFailed,
    Cancelled
}

public enum SlackDeliveryState
{
    NotSupported
}

public sealed record DeliveryAttempt(
    Guid PlannedWorkItemId,
    long? TogglEntryId,
    long? TempoWorklogId,
    DeliveryAttemptStatus Status,
    DeliveryFailureCode? FailureCode,
    SlackDeliveryState SlackState,
    DateTimeOffset? TogglWriteRecordedAtUtc = null,
    DateTimeOffset? TempoWriteRecordedAtUtc = null,
    DateTimeOffset? ReconciliationRecordedAtUtc = null);

public sealed record DeliveryAttemptClaim(DeliveryAttempt Attempt, bool IsAcquired);

public interface IDeliveryAttemptRepository
{
    Task<DeliveryAttempt?> GetAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default);
    Task<DeliveryAttemptClaim> ClaimAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default);
    Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default);
}
