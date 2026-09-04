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
    Cancelled,
    RemoteChangedAfterDelivery,

    // Appended, never reordered: these are persisted as ints in delivery_attempts.failure_code.
    // Tempo answered and refused the worklog, so nothing was written -- distinct from TempoFailed,
    // which also covers a timeout that may have written one we never recorded.
    TempoRejected
}

// Thrown by a delivery client when the remote answered with a failure status. That answer is proof
// the write did not happen, which is what makes the attempt safe to repeat; an exception without one
// leaves the outcome unknown.
public sealed class DeliveryRejectedException(string message, Exception? innerException = null)
    : Exception(message, innerException);

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
    DateTimeOffset? ReconciliationRecordedAtUtc = null)
{
    // The service's own explanation of a failure -- "User is invalid", the field Tempo rejected.
    // Deliberately NOT persisted: SqliteDeliveryAttemptRepository reads and writes named columns, so
    // this is dropped on save and absent on load. It answers "why did this just fail?" while the user
    // is still looking at the row; a row rehydrated on a later launch falls back to FailureCode and
    // the audit log, which does keep the detail.
    public string? FailureDetail { get; init; }
}

public static class DeliveryRetry
{
    // A failure is resumable only when we know the write did not happen. Delivery is ordered
    // Toggl -> Jira -> Tempo and the attempt records the Toggl entry id, so a retry resumes from
    // the recorded point rather than starting over.
    //
    // The Jira step is a lookup, so it writes nothing and always repeats safely. TempoRejected means
    // Tempo answered and refused. Everything else is excluded because the outcome is unknown, and
    // repeating an unknown write is how you get a duplicate worklog or a duplicate Toggl entry:
    // TogglFailed and TempoFailed both cover timeouts that may have succeeded, Cancelled can land
    // between a write and its persistence, and PersistenceFailed and RemoteChangedAfterDelivery mean
    // the stored and remote states may already disagree -- which is what reconciliation is for.
    public static bool IsResumable(this DeliveryAttempt attempt) =>
        attempt is { Status: DeliveryAttemptStatus.Failed, FailureCode:
            DeliveryFailureCode.JiraFailed or
            DeliveryFailureCode.JiraIssueNotFound or
            DeliveryFailureCode.TempoRejected };
}

public sealed record DeliveryAttemptClaim(DeliveryAttempt Attempt, bool IsAcquired);

public interface IDeliveryAttemptRepository
{
    Task<DeliveryAttempt?> GetAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default);
    Task<DeliveryAttemptClaim> ClaimAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default);
    Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default);
}

// History needs the human-readable task an attempt belongs to; a raw attempt only carries its
// item's GUID. PlanDate/JiraIssueKey/Description are null/empty when the planned item behind an
// old attempt no longer exists.
public sealed record DeliveryHistoryEntry(DeliveryAttempt Attempt, DateOnly? PlanDate, string JiraIssueKey, string Description);

public interface IDeliveryHistoryRepository
{
    Task<IReadOnlyList<DeliveryHistoryEntry>> ListHistoryAsync(CancellationToken cancellationToken = default);
}
