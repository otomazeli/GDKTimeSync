using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class DeliveryHistoryItemViewModel(DeliveryAttempt attempt)
{
    public Guid PlannedWorkItemId { get; } = attempt.PlannedWorkItemId;
    public long? TogglEntryId { get; } = attempt.TogglEntryId;
    public long? TempoWorklogId { get; } = attempt.TempoWorklogId;
    public string StatusText { get; } = attempt.Status switch
    {
        DeliveryAttemptStatus.InProgress => "In progress",
        DeliveryAttemptStatus.Succeeded => "Succeeded",
        DeliveryAttemptStatus.Failed => "Failed",
        DeliveryAttemptStatus.Cancelled => "Cancelled",
        DeliveryAttemptStatus.ReconciliationRequired => "Reconciliation required",
        _ => "Unknown"
    };
    public string? FailureText { get; } = attempt.FailureCode switch
    {
        DeliveryFailureCode.TogglFailed => "Toggl delivery failed.",
        DeliveryFailureCode.JiraFailed => "Jira delivery failed.",
        DeliveryFailureCode.JiraIssueNotFound => "Jira issue was not found.",
        DeliveryFailureCode.TempoFailed => "Tempo delivery failed.",
        DeliveryFailureCode.PersistenceFailed => "Delivery state could not be saved.",
        DeliveryFailureCode.Cancelled => "Delivery was cancelled.",
        _ => null
    };
}
