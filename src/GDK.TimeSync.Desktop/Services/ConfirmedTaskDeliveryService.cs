using System.Collections.Concurrent;
using GDK.TimeSync.Core;
using GDK.TimeSync.Jira;
using GDK.TimeSync.Tempo;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Desktop.Services;

public sealed class ConfirmedTaskDeliveryService(
    IIntegrationClientFactory clients,
    IUserSettingsStore settings,
    IDeliveryAttemptRepository attempts,
    IAuditLog? auditLog = null) : IConfirmedTaskDeliveryService
{
    // Shared across calls (this service is a DI singleton) so a reconciliation record raised by one
    // delivery -- built with its own short-lived PostAllCoordinator and API clients -- survives to
    // be recovered by the next, instead of vanishing with the coordinator that created it.
    private readonly ConcurrentDictionary<Guid, DeliveryAttempt> pendingReconciliation = new();

    // Resolved once per process, not per item: a Review batch delivers item by item and the identity
    // does not change between them.
    private string? resolvedWorker;

    public async Task<DeliveryAttempt> DeliverConfirmedAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        auditLog?.Write(AuditLevel.Info, "Delivery", $"Confirmed {item.Id} {item.JiraIssueKey} {item.Day}");
        var attempt = await DeliverAsync(item, cancellationToken);
        var detail = string.IsNullOrWhiteSpace(attempt.FailureDetail) ? "" : $": {attempt.FailureDetail}";
        auditLog?.Write(attempt.FailureCode is not null ? AuditLevel.Error : AuditLevel.Info, "Delivery", $"{item.Id} -> {attempt.Status} {attempt.FailureCode}{detail}");
        return attempt;
    }

    private async Task<DeliveryAttempt> DeliverAsync(PlannedWorkItem item, CancellationToken cancellationToken)
    {
        UserSettings configuration;
        try
        {
            configuration = settings.Load();
        }
        catch (Exception exception)
        {
            LogException(item.Id, "Settings", exception);
            return await RecordSetupFailureAsync(item.Id, DeliveryFailureCode.SetupFailed, "Settings could not be read.");
        }
        if (configuration.TogglWorkspaceId is not > 0)
            return await RecordSetupFailureAsync(item.Id, DeliveryFailureCode.SetupFailed, "No Toggl workspace is configured.");

        ITogglClient toggl;
        JiraClient jira;
        TempoClient tempo;
        try { toggl = await clients.CreateTogglAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return await RecordSetupFailureAsync(item.Id, DeliveryFailureCode.Cancelled, "Cancelled before any delivery."); }
        catch (Exception exception) { LogException(item.Id, "Toggl client", exception); return await RecordSetupFailureAsync(item.Id, DeliveryFailureCode.SetupFailed, "The Toggl client could not be created -- check the API token."); }

        try { jira = await clients.CreateJiraAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { toggl.Dispose(); return await RecordSetupFailureAsync(item.Id, DeliveryFailureCode.Cancelled, "Cancelled before any delivery."); }
        catch (Exception exception) { LogException(item.Id, "Jira client", exception); toggl.Dispose(); return await RecordSetupFailureAsync(item.Id, DeliveryFailureCode.SetupFailed, "The Jira client could not be created -- check the base URL and PAT."); }

        try { tempo = await clients.CreateTempoAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { jira.Dispose(); toggl.Dispose(); return await RecordSetupFailureAsync(item.Id, DeliveryFailureCode.Cancelled, "Cancelled before any delivery."); }
        catch (Exception exception) { LogException(item.Id, "Tempo client", exception); jira.Dispose(); toggl.Dispose(); return await RecordSetupFailureAsync(item.Id, DeliveryFailureCode.SetupFailed, "The Tempo client could not be created -- check the base URL and PAT."); }

        try
        {
            using (toggl)
            using (jira)
            using (tempo)
            {
                // Tempo rejects a worker it does not recognise ("User is invalid"), and the typed
                // setting was the single most common way to get it wrong on a machine nobody can
                // debug. Ask Jira who we are instead, and keep the setting only as an override for
                // an instance where /myself does not return what Tempo wants.
                var worker = await ResolveWorkerAsync(jira, configuration.JiraUser, cancellationToken);
                if (string.IsNullOrWhiteSpace(worker))
                    return await RecordSetupFailureAsync(item.Id, DeliveryFailureCode.SetupFailed,
                        "No Tempo worker: nothing configured, and Jira did not report an identity.");

                var coordinator = new PostAllCoordinator(
                    new TogglDeliveryClient(toggl, configuration.TogglWorkspaceId.Value),
                    new JiraDeliveryClient(jira),
                    new TempoDeliveryClient(tempo, worker),
                    attempts,
                    pendingReconciliation,
                    auditLog);
                return (await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]), cancellationToken)).Attempts.Single();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await RecordSetupFailureAsync(item.Id, DeliveryFailureCode.Cancelled, "Cancelled during delivery.");
        }
    }

    // Prefers `key`, then `name`, matching the reference client. A configured value wins over both:
    // it is an explicit override, and someone who typed it did so because resolution was not enough.
    private async Task<string> ResolveWorkerAsync(JiraClient jira, string configured, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        if (resolvedWorker is not null) return resolvedWorker;

        try
        {
            var me = await jira.GetMyselfAsync(cancellationToken);
            var worker = !string.IsNullOrWhiteSpace(me.Key) ? me.Key : me.Name;
            if (string.IsNullOrWhiteSpace(worker)) return "";

            auditLog?.Write(AuditLevel.Info, "Delivery", $"Tempo worker resolved from Jira: {worker}");
            resolvedWorker = worker;
            return worker;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return "";
        }
    }

    // The whole exception, for the log only -- the review row shows the short reason instead. The
    // integration clients' own messages are constants and the token lives in a header this code
    // never reads, so no credential reaches here.
    private void LogException(Guid itemId, string step, Exception exception)
    {
        var text = exception.ToString();
        if (text.Length > MaxExceptionCharacters)
            text = text[..MaxExceptionCharacters] + " …(truncated)";
        auditLog?.Write(AuditLevel.Error, "Delivery", $"{itemId} {step} threw{Environment.NewLine}{text}");
    }

    private const int MaxExceptionCharacters = 4000;

    // `reason` is what the audit log shows. Every path here returns before a single HTTP call, so
    // without it the log reads "Failed TogglFailed" milliseconds after "Confirmed" and says nothing
    // about which of half a dozen causes it was.
    private async Task<DeliveryAttempt> RecordSetupFailureAsync(Guid itemId, DeliveryFailureCode failureCode, string reason)
    {
        try
        {
            var claim = await attempts.ClaimAsync(itemId, CancellationToken.None);
            if (!claim.IsAcquired)
                return claim.Attempt.Status == DeliveryAttemptStatus.InProgress
                    ? claim.Attempt with { Status = DeliveryAttemptStatus.ReconciliationRequired, FailureCode = DeliveryFailureCode.PersistenceFailed }
                    : claim.Attempt;

            var failed = new DeliveryAttempt(itemId, null, null,
                failureCode == DeliveryFailureCode.Cancelled ? DeliveryAttemptStatus.Cancelled : DeliveryAttemptStatus.Failed,
                failureCode,
                SlackDeliveryState.NotSupported) with { FailureDetail = reason };
            await attempts.SaveAsync(failed, CancellationToken.None);
            return failed;
        }
        catch
        {
            return new DeliveryAttempt(itemId, null, null, DeliveryAttemptStatus.ReconciliationRequired,
                DeliveryFailureCode.PersistenceFailed, SlackDeliveryState.NotSupported);
        }
    }

    private sealed class TogglDeliveryClient(ITogglClient client, long workspaceId) : IPlannedItemTogglClient
    {
        public async Task<long> CreateAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
        {
            var startTime = item.Start ?? TimeOnly.MinValue;
            var start = item.Day.ToDateTime(startTime);
            var offset = TimeZoneInfo.Local.GetUtcOffset(start);
            var stopDay = item.End is { } end && PlannedWorkItem.EndWrapsToNextDay(startTime, end) ? item.Day.AddDays(1) : item.Day;
            var entry = await client.CreateTimeEntryAsync(new TogglCreateTimeEntryRequest(
                workspaceId,
                item.TogglDescription,
                new DateTimeOffset(start, offset),
                new DateTimeOffset(item.End is { } stop ? stopDay.ToDateTime(stop) : start.Add(item.Duration), offset),
                item.TogglProjectId), cancellationToken);
            return entry.Id;
        }
    }

    private sealed class JiraDeliveryClient(JiraClient client) : IPlannedItemJiraClient
    {
        public async Task<string?> GetIssueIdAsync(string issueKey, CancellationToken cancellationToken = default) =>
            (await client.GetIssueAsync(issueKey, cancellationToken)).Id;
    }

    private sealed class TempoDeliveryClient(TempoClient client, string worker) : IPlannedItemTempoClient
    {
        public async Task<long> CreateAsync(PlannedWorkItem item, string jiraIssueId, CancellationToken cancellationToken = default)
        {
            try
            {
                return await CreateCoreAsync(item, jiraIssueId, cancellationToken);
            }
            // A status code means Tempo answered and refused, so the worklog does not exist and the
            // task can be posted again. Without one the outcome is unknown and must not be repeated.
            catch (TempoApiException exception) when (exception.StatusCode is not null)
            {
                throw new DeliveryRejectedException(exception.Message, exception);
            }
        }

        private async Task<long> CreateCoreAsync(PlannedWorkItem item, string jiraIssueId, CancellationToken cancellationToken)
        {
            var worklog = await client.CreateWorklogAsync(new GDK.TimeSync.Tempo.TempoWorklogRequest(
                worker,
                jiraIssueId,
                item.Day.ToDateTime(item.Start ?? TimeOnly.MinValue),
                checked((int)item.Duration.TotalSeconds),
                item.Comment,
                // Issue #13: the category the user picks per row had no route to the payload, so every
                // worklog reached Tempo uncategorised.
                item.TempoCategory), cancellationToken);
            return worklog.TempoWorklogId;
        }
    }
}
