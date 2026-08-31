using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.Services;

public enum LiveValidationStep { Toggl, Jira, Tempo }

public enum LiveValidationOutcome
{
    Created,
    Validated,
    Verified,
    Blocked,
    Failed,
    Cancelled,
    ReconciliationRequired
}

public sealed record LiveValidationResult(
    LiveValidationStep Step,
    DeliveryAttempt Attempt,
    string SafeMessage,
    LiveValidationOutcome Outcome);

public sealed record LiveValidationPreview(
    DeliveryAttempt? Attempt,
    string TempoWorker,
    string TempoBaseUrl,
    string TempoCategory);
