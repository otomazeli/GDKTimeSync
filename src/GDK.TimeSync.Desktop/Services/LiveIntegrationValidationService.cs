using System.Collections.Concurrent;
using GDK.TimeSync.Core;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Desktop.Services;

public sealed class LiveIntegrationValidationService(
    IIntegrationClientFactory clients,
    IUserSettingsStore settings,
    IDeliveryAttemptRepository attempts) : ILiveIntegrationValidationService
{
    private readonly ConcurrentDictionary<Guid, DeliveryAttempt> pendingReconciliation = [];

    public async Task<LiveValidationResult> CreateTogglAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (pendingReconciliation.TryGetValue(item.Id, out var pending))
            return ExistingResult(LiveValidationStep.Toggl, pending);
        if (!TryGetTiming(item, out var timing) || !TryCreateTogglRequest(item, timing, out var request))
            return FailedResult(LiveValidationStep.Toggl, item.Id, DeliveryFailureCode.TogglFailed, "Toggl creation could not start.");
        if (cancellationToken.IsCancellationRequested)
            return CancelledResult(LiveValidationStep.Toggl, item.Id, "Toggl creation cancelled.");

        DeliveryAttemptClaim claim;
        try
        {
            claim = await attempts.ClaimAsync(item.Id, CancellationToken.None);
        }
        catch
        {
            return FailedResult(LiveValidationStep.Toggl, item.Id, DeliveryFailureCode.PersistenceFailed, "Toggl creation could not start.");
        }

        if (!claim.IsAcquired)
            return ExistingResult(LiveValidationStep.Toggl, claim.Attempt);

        ITogglClient toggl;
        try
        {
            toggl = await clients.CreateTogglAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return await PersistPreWriteResultAsync(LiveValidationStep.Toggl, claim.Attempt, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled, "Toggl creation cancelled.");
        }
        catch
        {
            return await PersistPreWriteResultAsync(LiveValidationStep.Toggl, claim.Attempt, DeliveryAttemptStatus.Failed, DeliveryFailureCode.TogglFailed, "Toggl creation failed.");
        }

        var afterWrite = claim.Attempt;
        try
        {
            using (toggl)
            {
                var entry = await toggl.CreateTimeEntryAsync(request, cancellationToken);
                afterWrite = claim.Attempt with { TogglEntryId = entry.Id, Status = DeliveryAttemptStatus.InProgress, FailureCode = null };
                await attempts.SaveAsync(afterWrite, CancellationToken.None);
                return new LiveValidationResult(LiveValidationStep.Toggl, afterWrite, "Toggl entry created.");
            }
        }
        catch (OperationCanceledException)
        {
            return await ReconciliationResultAsync(LiveValidationStep.Toggl, afterWrite, DeliveryFailureCode.Cancelled, "Toggl reconciliation is required.");
        }
        catch
        {
            return await ReconciliationResultAsync(LiveValidationStep.Toggl, afterWrite, DeliveryFailureCode.TogglFailed, "Toggl reconciliation is required.");
        }
    }

    public async Task<LiveValidationResult> ValidateJiraAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        var resultAttempt = NewResultAttempt(item.Id, DeliveryAttemptStatus.Succeeded);
        try
        {
            using var jira = await clients.CreateJiraAsync(cancellationToken);
            var issue = await jira.GetIssueAsync(item.JiraIssueKey, cancellationToken);
            return string.IsNullOrWhiteSpace(issue.Id)
                ? new LiveValidationResult(LiveValidationStep.Jira, resultAttempt with { Status = DeliveryAttemptStatus.Failed, FailureCode = DeliveryFailureCode.JiraIssueNotFound }, "Jira issue was not found.")
                : new LiveValidationResult(LiveValidationStep.Jira, resultAttempt, "Jira issue validated.");
        }
        catch (OperationCanceledException)
        {
            return new LiveValidationResult(LiveValidationStep.Jira, resultAttempt with { Status = DeliveryAttemptStatus.Cancelled, FailureCode = DeliveryFailureCode.Cancelled }, "Jira validation cancelled.");
        }
        catch
        {
            return new LiveValidationResult(LiveValidationStep.Jira, resultAttempt with { Status = DeliveryAttemptStatus.Failed, FailureCode = DeliveryFailureCode.JiraFailed }, "Jira validation failed.");
        }
    }

    public async Task<LiveValidationResult> CreateAndVerifyTempoAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (pendingReconciliation.TryGetValue(item.Id, out var pending))
            return ExistingResult(LiveValidationStep.Tempo, pending);

        DeliveryAttempt? current;
        try
        {
            current = await attempts.GetAsync(item.Id, CancellationToken.None);
        }
        catch
        {
            return FailedResult(LiveValidationStep.Tempo, item.Id, DeliveryFailureCode.PersistenceFailed, "Tempo creation could not start.");
        }

        if (current is null || current.TogglEntryId is null || current.Status != DeliveryAttemptStatus.InProgress)
            return new LiveValidationResult(LiveValidationStep.Tempo, current ?? NewResultAttempt(item.Id, DeliveryAttemptStatus.InProgress), "Tempo requires a non-terminal Toggl entry.");
        if (!TryGetTiming(item, out var timing))
            return FailedResult(LiveValidationStep.Tempo, item.Id, DeliveryFailureCode.TempoFailed, "Tempo creation could not start.", current);
        if (cancellationToken.IsCancellationRequested)
            return CancelledResult(LiveValidationStep.Tempo, item.Id, "Tempo creation cancelled.", current);

        if (current.TempoWorklogId is not null)
            return await ReadExistingTempoAsync(current, timing, cancellationToken);

        if (!TryCreateTempoRequest(item, timing, out var request))
            return FailedResult(LiveValidationStep.Tempo, item.Id, DeliveryFailureCode.TempoFailed, "Tempo creation could not start.", current);

        string? jiraIssueId;
        try
        {
            using var jira = await clients.CreateJiraAsync(cancellationToken);
            jiraIssueId = (await jira.GetIssueAsync(item.JiraIssueKey, cancellationToken)).Id;
        }
        catch (OperationCanceledException)
        {
            return CancelledResult(LiveValidationStep.Tempo, item.Id, "Tempo creation cancelled.", current);
        }
        catch
        {
            return FailedResult(LiveValidationStep.Tempo, item.Id, DeliveryFailureCode.JiraFailed, "Jira validation failed.", current);
        }

        if (string.IsNullOrWhiteSpace(jiraIssueId))
            return FailedResult(LiveValidationStep.Tempo, item.Id, DeliveryFailureCode.JiraIssueNotFound, "Jira issue was not found.", current);

        GDK.TimeSync.Tempo.TempoClient tempo;
        try
        {
            tempo = await clients.CreateTempoAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return CancelledResult(LiveValidationStep.Tempo, item.Id, "Tempo creation cancelled.", current);
        }
        catch
        {
            return FailedResult(LiveValidationStep.Tempo, item.Id, DeliveryFailureCode.TempoFailed, "Tempo creation failed.", current);
        }

        var preWriteMarker = current with { Status = DeliveryAttemptStatus.ReconciliationRequired, FailureCode = DeliveryFailureCode.PersistenceFailed };
        try
        {
            await attempts.SaveAsync(preWriteMarker, CancellationToken.None);
        }
        catch
        {
            return new LiveValidationResult(LiveValidationStep.Tempo, preWriteMarker, "Reconciliation is required.");
        }

        var afterWrite = current;
        try
        {
            using (tempo)
            {
                var created = await tempo.CreateWorklogAsync(request with { OriginTaskId = jiraIssueId }, cancellationToken);
                afterWrite = current with { TempoWorklogId = created.TempoWorklogId, Status = DeliveryAttemptStatus.InProgress, FailureCode = null };
                await attempts.SaveAsync(afterWrite, CancellationToken.None);
                return await VerifyTempoAsync(tempo, afterWrite, timing.DurationSeconds, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            return await ReconciliationResultAsync(LiveValidationStep.Tempo, afterWrite, DeliveryFailureCode.Cancelled, "Tempo reconciliation is required.");
        }
        catch
        {
            return await ReconciliationResultAsync(LiveValidationStep.Tempo, afterWrite, DeliveryFailureCode.TempoFailed, "Tempo reconciliation is required.");
        }
    }

    private async Task<LiveValidationResult> ReadExistingTempoAsync(DeliveryAttempt current, Timing timing, CancellationToken cancellationToken)
    {
        try
        {
            using var tempo = await clients.CreateTempoAsync(cancellationToken);
            return await VerifyTempoAsync(tempo, current, timing.DurationSeconds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return await ReconciliationResultAsync(LiveValidationStep.Tempo, current, DeliveryFailureCode.Cancelled, "Tempo reconciliation is required.");
        }
        catch
        {
            return await ReconciliationResultAsync(LiveValidationStep.Tempo, current, DeliveryFailureCode.TempoFailed, "Tempo reconciliation is required.");
        }
    }

    private async Task<LiveValidationResult> VerifyTempoAsync(GDK.TimeSync.Tempo.TempoClient tempo, DeliveryAttempt attempt, int durationSeconds, CancellationToken cancellationToken)
    {
        var worklogId = attempt.TempoWorklogId!.Value;
        var readback = await tempo.GetWorklogAsync(worklogId, cancellationToken);
        if (readback?.TempoWorklogId != worklogId || readback.TimeSpentSeconds != durationSeconds)
            return await ReconciliationResultAsync(LiveValidationStep.Tempo, attempt, DeliveryFailureCode.TempoFailed, "Tempo reconciliation is required.");

        var succeeded = attempt with { Status = DeliveryAttemptStatus.Succeeded, FailureCode = null };
        try
        {
            await attempts.SaveAsync(succeeded, CancellationToken.None);
            pendingReconciliation.TryRemove(succeeded.PlannedWorkItemId, out _);
            return new LiveValidationResult(LiveValidationStep.Tempo, succeeded, "Tempo worklog verified.");
        }
        catch
        {
            return await ReconciliationResultAsync(LiveValidationStep.Tempo, attempt, DeliveryFailureCode.PersistenceFailed, "Tempo reconciliation is required.");
        }
    }

    private bool TryCreateTogglRequest(PlannedWorkItem item, Timing timing, out TogglCreateTimeEntryRequest request)
    {
        try
        {
            var workspaceId = settings.Load().TogglWorkspaceId;
            if (workspaceId is not > 0) throw new InvalidOperationException();
            request = new TogglCreateTimeEntryRequest(workspaceId.Value, item.Comment, timing.Start, timing.Stop);
            return true;
        }
        catch
        {
            request = null!;
            return false;
        }
    }

    private bool TryCreateTempoRequest(PlannedWorkItem item, Timing timing, out GDK.TimeSync.Tempo.TempoWorklogRequest request)
    {
        try
        {
            var worker = settings.Load().JiraUser;
            if (string.IsNullOrWhiteSpace(worker)) throw new InvalidOperationException();
            request = new GDK.TimeSync.Tempo.TempoWorklogRequest(worker, string.Empty, timing.Start.DateTime, timing.DurationSeconds, item.Comment);
            return true;
        }
        catch
        {
            request = null!;
            return false;
        }
    }

    private async Task<LiveValidationResult> PersistPreWriteResultAsync(LiveValidationStep step, DeliveryAttempt attempt, DeliveryAttemptStatus status, DeliveryFailureCode failureCode, string message)
    {
        var result = attempt with { Status = status, FailureCode = failureCode };
        try
        {
            await attempts.SaveAsync(result, CancellationToken.None);
        }
        catch
        {
        }

        return new LiveValidationResult(step, result, message);
    }

    private async Task<LiveValidationResult> ReconciliationResultAsync(LiveValidationStep step, DeliveryAttempt attempt, DeliveryFailureCode failureCode, string message)
    {
        var reconciliation = attempt with { Status = DeliveryAttemptStatus.ReconciliationRequired, FailureCode = failureCode };
        try
        {
            await attempts.SaveAsync(reconciliation, CancellationToken.None);
            pendingReconciliation.TryRemove(attempt.PlannedWorkItemId, out _);
            return new LiveValidationResult(step, reconciliation, message);
        }
        catch
        {
            var blocked = reconciliation with { FailureCode = DeliveryFailureCode.PersistenceFailed };
            pendingReconciliation[attempt.PlannedWorkItemId] = blocked;
            return new LiveValidationResult(step, blocked, "Reconciliation is required.");
        }
    }

    private static bool TryGetTiming(PlannedWorkItem item, out Timing timing)
    {
        if (item.Start is not { } start || item.End is not { } end || item.Duration <= TimeSpan.Zero || end <= start)
        {
            timing = default;
            return false;
        }

        var localStart = item.Day.ToDateTime(start);
        var localStop = item.Day.ToDateTime(end);
        if (localStop - localStart != item.Duration)
        {
            timing = default;
            return false;
        }

        var offset = TimeZoneInfo.Local.GetUtcOffset(localStart);
        timing = new Timing(new DateTimeOffset(localStart, offset), new DateTimeOffset(localStop, offset), checked((int)item.Duration.TotalSeconds));
        return true;
    }

    private static LiveValidationResult ExistingResult(LiveValidationStep step, DeliveryAttempt attempt) =>
        new(step, attempt, attempt.Status == DeliveryAttemptStatus.ReconciliationRequired ? "Reconciliation is required before continuing." : "This validation step has already been recorded.");

    private static LiveValidationResult FailedResult(LiveValidationStep step, Guid itemId, DeliveryFailureCode failureCode, string message, DeliveryAttempt? attempt = null) =>
        new(step, (attempt ?? NewResultAttempt(itemId, DeliveryAttemptStatus.Failed)) with { Status = DeliveryAttemptStatus.Failed, FailureCode = failureCode }, message);

    private static LiveValidationResult CancelledResult(LiveValidationStep step, Guid itemId, string message, DeliveryAttempt? attempt = null) =>
        new(step, (attempt ?? NewResultAttempt(itemId, DeliveryAttemptStatus.Cancelled)) with { Status = DeliveryAttemptStatus.Cancelled, FailureCode = DeliveryFailureCode.Cancelled }, message);

    private static DeliveryAttempt NewResultAttempt(Guid itemId, DeliveryAttemptStatus status) =>
        new(itemId, null, null, status, null, SlackDeliveryState.NotSupported);

    private readonly record struct Timing(DateTimeOffset Start, DateTimeOffset Stop, int DurationSeconds);
}
