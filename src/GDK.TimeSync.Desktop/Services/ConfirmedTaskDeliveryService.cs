using GDK.TimeSync.Core;
using GDK.TimeSync.Jira;
using GDK.TimeSync.Tempo;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Desktop.Services;

public sealed class ConfirmedTaskDeliveryService(
    IIntegrationClientFactory clients,
    IUserSettingsStore settings,
    IDeliveryAttemptRepository attempts) : IConfirmedTaskDeliveryService
{
    public async Task<DeliveryAttempt> DeliverConfirmedAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        UserSettings configuration;
        try
        {
            configuration = settings.Load();
        }
        catch
        {
            return await RecordSetupFailureAsync(item.Id, DeliveryFailureCode.TogglFailed);
        }
        if (configuration.TogglWorkspaceId is not > 0 || string.IsNullOrWhiteSpace(configuration.JiraUser))
            return await RecordSetupFailureAsync(item.Id, DeliveryFailureCode.TogglFailed);

        try
        {
            using var toggl = await clients.CreateTogglAsync(cancellationToken);
            using var jira = await clients.CreateJiraAsync(cancellationToken);
            using var tempo = await clients.CreateTempoAsync(cancellationToken);
            var coordinator = new PostAllCoordinator(
                new TogglDeliveryClient(toggl, configuration.TogglWorkspaceId.Value),
                new JiraDeliveryClient(jira),
                new TempoDeliveryClient(tempo, configuration.JiraUser),
                attempts);
            return (await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]), cancellationToken)).Attempts.Single();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await RecordSetupFailureAsync(item.Id, DeliveryFailureCode.Cancelled);
        }
        catch
        {
            return await RecordSetupFailureAsync(item.Id, DeliveryFailureCode.TogglFailed);
        }
    }

    private async Task<DeliveryAttempt> RecordSetupFailureAsync(Guid itemId, DeliveryFailureCode failureCode)
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
                SlackDeliveryState.NotSupported);
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
            var start = item.Day.ToDateTime(item.Start ?? TimeOnly.MinValue);
            var offset = TimeZoneInfo.Local.GetUtcOffset(start);
            var entry = await client.CreateTimeEntryAsync(new TogglCreateTimeEntryRequest(
                workspaceId,
                item.Comment,
                new DateTimeOffset(start, offset),
                new DateTimeOffset(start.Add(item.Duration), offset)), cancellationToken);
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
            var worklog = await client.CreateWorklogAsync(new GDK.TimeSync.Tempo.TempoWorklogRequest(
                worker,
                jiraIssueId,
                item.Day.ToDateTime(item.Start ?? TimeOnly.MinValue),
                checked((int)item.Duration.TotalSeconds),
                item.Comment), cancellationToken);
            return worklog.TempoWorklogId;
        }
    }
}
