using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class DeliveryHistoryItemViewModel(DeliveryHistoryEntry entry)
{
    private readonly DeliveryAttempt attempt = entry.Attempt;

    public Guid PlannedWorkItemId { get; } = entry.Attempt.PlannedWorkItemId;
    public long? TogglEntryId { get; } = entry.Attempt.TogglEntryId;
    public long? TempoWorklogId { get; } = entry.Attempt.TempoWorklogId;
    public string DateText { get; } = entry.PlanDate?.ToString("yyyy-MM-dd") ?? "Unknown date";
    public string TaskText { get; } = string.Join(" ", new[] { entry.JiraIssueKey, entry.Description }
        .Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } text
        ? text
        : "(task no longer in any plan)";
    public string StatusText { get; } = entry.Attempt.Status switch
    {
        DeliveryAttemptStatus.InProgress => "In progress",
        DeliveryAttemptStatus.Succeeded => "Succeeded",
        DeliveryAttemptStatus.Failed => "Failed",
        DeliveryAttemptStatus.Cancelled => "Cancelled",
        DeliveryAttemptStatus.ReconciliationRequired => "Reconciliation required",
        _ => "Unknown"
    };
    public string? FailureText { get; } = entry.Attempt.FailureCode switch
    {
        DeliveryFailureCode.TogglFailed => "Toggl delivery failed.",
        DeliveryFailureCode.JiraFailed => "Jira delivery failed.",
        DeliveryFailureCode.JiraIssueNotFound => "Jira issue was not found.",
        DeliveryFailureCode.TempoFailed => "Tempo delivery failed.",
        DeliveryFailureCode.PersistenceFailed => "Delivery state could not be saved.",
        DeliveryFailureCode.Cancelled => "Delivery was cancelled.",
        DeliveryFailureCode.RemoteChangedAfterDelivery => "The Toggl entry changed after delivery.",
        _ => null
    };

    public string DestinationText => attempt switch
    {
        { TogglEntryId: not null, TempoWorklogId: not null } => $"Toggl #{attempt.TogglEntryId} · Tempo #{attempt.TempoWorklogId}",
        { TogglEntryId: not null } => $"Toggl #{attempt.TogglEntryId}",
        { TempoWorklogId: not null } => $"Tempo #{attempt.TempoWorklogId}",
        _ => "No external entry recorded"
    };
}
