namespace GDK.TimeSync.Core;

public enum DeliveryAttemptStatus
{
    InProgress,
    Succeeded,
    Failed,
    Cancelled
}

public enum DeliveryFailureCode
{
    TogglFailed,
    JiraFailed,
    JiraIssueNotFound,
    TempoFailed,
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
    SlackDeliveryState SlackState);

public interface IDeliveryAttemptRepository
{
    Task<DeliveryAttempt?> GetAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default);
    Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default);
}
