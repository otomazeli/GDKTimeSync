using GDK.TimeSync.Core;
using GDK.TimeSync.Tempo;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Desktop.Services;

public sealed class LiveIntegrationValidationService(
    IIntegrationClientFactory clients,
    IUserSettingsStore settings,
    IDeliveryAttemptRepository attempts) : ILiveIntegrationValidationService
{
    public async Task<LiveValidationResult> CreateTogglAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        var claim = await attempts.ClaimAsync(item.Id, CancellationToken.None);
        if (!claim.IsAcquired)
            return ExistingResult(LiveValidationStep.Toggl, claim.Attempt);

        var afterTogglWrite = claim.Attempt;
        try
        {
            using var toggl = await clients.CreateTogglAsync(cancellationToken);
            var entry = await toggl.CreateTimeEntryAsync(CreateTogglRequest(item), cancellationToken);
            afterTogglWrite = claim.Attempt with { TogglEntryId = entry.Id, Status = DeliveryAttemptStatus.InProgress, FailureCode = null };
            await attempts.SaveAsync(afterTogglWrite, CancellationToken.None);
            return new LiveValidationResult(LiveValidationStep.Toggl, afterTogglWrite, "Toggl entry created.");
        }
        catch (OperationCanceledException)
        {
            return await ReconciliationResultAsync(LiveValidationStep.Toggl, afterTogglWrite, DeliveryFailureCode.Cancelled, "Toggl reconciliation is required.");
        }
        catch
        {
            return await ReconciliationResultAsync(LiveValidationStep.Toggl, afterTogglWrite, DeliveryFailureCode.TogglFailed, "Toggl reconciliation is required.");
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
        var current = await attempts.GetAsync(item.Id, CancellationToken.None);
        if (current is null || current.TogglEntryId is null || current.Status != DeliveryAttemptStatus.InProgress)
            return new LiveValidationResult(LiveValidationStep.Tempo, current ?? NewResultAttempt(item.Id, DeliveryAttemptStatus.InProgress), "Tempo requires a non-terminal Toggl entry.");

        string? jiraIssueId;
        try
        {
            using var jira = await clients.CreateJiraAsync(cancellationToken);
            jiraIssueId = (await jira.GetIssueAsync(item.JiraIssueKey, cancellationToken)).Id;
        }
        catch (OperationCanceledException)
        {
            return new LiveValidationResult(LiveValidationStep.Tempo, current, "Tempo validation cancelled.");
        }
        catch
        {
            return new LiveValidationResult(LiveValidationStep.Tempo, current, "Jira validation failed.");
        }

        if (string.IsNullOrWhiteSpace(jiraIssueId))
            return new LiveValidationResult(LiveValidationStep.Tempo, current, "Jira issue was not found.");

        DeliveryAttempt withTempoId = current;
        try
        {
            using var tempo = await clients.CreateTempoAsync(cancellationToken);
            var created = await tempo.CreateWorklogAsync(CreateTempoRequest(item, jiraIssueId), cancellationToken);
            withTempoId = current with { TempoWorklogId = created.TempoWorklogId, Status = DeliveryAttemptStatus.InProgress, FailureCode = null };
            await attempts.SaveAsync(withTempoId, CancellationToken.None);

            var readback = await tempo.GetWorklogAsync(created.TempoWorklogId, cancellationToken);
            if (readback?.TempoWorklogId == created.TempoWorklogId && readback.TimeSpentSeconds == checked((int)item.Duration.TotalSeconds))
            {
                var succeeded = withTempoId with { Status = DeliveryAttemptStatus.Succeeded };
                await attempts.SaveAsync(succeeded, CancellationToken.None);
                return new LiveValidationResult(LiveValidationStep.Tempo, succeeded, "Tempo worklog verified.");
            }

            return await ReconciliationResultAsync(LiveValidationStep.Tempo, withTempoId, DeliveryFailureCode.TempoFailed, "Tempo reconciliation is required.");
        }
        catch (OperationCanceledException)
        {
            return await ReconciliationResultAsync(LiveValidationStep.Tempo, withTempoId, DeliveryFailureCode.Cancelled, "Tempo reconciliation is required.");
        }
        catch
        {
            return await ReconciliationResultAsync(LiveValidationStep.Tempo, withTempoId, DeliveryFailureCode.TempoFailed, "Tempo reconciliation is required.");
        }
    }

    private TogglCreateTimeEntryRequest CreateTogglRequest(PlannedWorkItem item)
    {
        var workspaceId = settings.Load().TogglWorkspaceId;
        if (workspaceId is not > 0)
            throw new InvalidOperationException();

        var start = item.Day.ToDateTime(item.Start ?? TimeOnly.MinValue);
        var offset = TimeZoneInfo.Local.GetUtcOffset(start);
        return new TogglCreateTimeEntryRequest(workspaceId.Value, item.Comment, new DateTimeOffset(start, offset), new DateTimeOffset(start.Add(item.Duration), offset));
    }

    private GDK.TimeSync.Tempo.TempoWorklogRequest CreateTempoRequest(PlannedWorkItem item, string jiraIssueId)
    {
        var worker = settings.Load().JiraUser;
        if (string.IsNullOrWhiteSpace(worker))
            throw new InvalidOperationException();

        return new GDK.TimeSync.Tempo.TempoWorklogRequest(worker, jiraIssueId, item.Day.ToDateTime(item.Start ?? TimeOnly.MinValue), checked((int)item.Duration.TotalSeconds), item.Comment);
    }

    private async Task<LiveValidationResult> ReconciliationResultAsync(LiveValidationStep step, DeliveryAttempt attempt, DeliveryFailureCode failureCode, string message)
    {
        var reconciliation = attempt with { Status = DeliveryAttemptStatus.ReconciliationRequired, FailureCode = failureCode };
        try
        {
            await attempts.SaveAsync(reconciliation, CancellationToken.None);
        }
        catch
        {
        }

        return new LiveValidationResult(step, reconciliation, message);
    }

    private static LiveValidationResult ExistingResult(LiveValidationStep step, DeliveryAttempt attempt) =>
        new(step, attempt, attempt.Status == DeliveryAttemptStatus.ReconciliationRequired ? "Reconciliation is required before continuing." : "This validation step has already been recorded.");

    private static DeliveryAttempt NewResultAttempt(Guid itemId, DeliveryAttemptStatus status) =>
        new(itemId, null, null, status, null, SlackDeliveryState.NotSupported);
}
