namespace GDK.TimeSync.Core;

public interface IPlannedItemTogglClient
{
    Task<long> CreateAsync(PlannedWorkItem item, CancellationToken cancellationToken = default);
}

public interface IPlannedItemJiraClient
{
    Task<string?> GetIssueIdAsync(string issueKey, CancellationToken cancellationToken = default);
}

public interface IPlannedItemTempoClient
{
    Task<long> CreateAsync(PlannedWorkItem item, string jiraIssueId, CancellationToken cancellationToken = default);
}

public interface IPostAllCoordinator
{
    Task<PostAllResult> PostAsync(DailyPlan plan, CancellationToken cancellationToken = default);
}

public sealed record PostAllResult(IReadOnlyList<DeliveryAttempt> Attempts);

public sealed class PostAllCoordinator(
    IPlannedItemTogglClient toggl,
    IPlannedItemJiraClient jira,
    IPlannedItemTempoClient tempo,
    IDeliveryAttemptRepository attempts) : IPostAllCoordinator
{
    public async Task<PostAllResult> PostAsync(DailyPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var results = new List<DeliveryAttempt>();

        foreach (var item in plan.Items)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var attempt = await PostItemAsync(item, cancellationToken);
            results.Add(attempt);
            if (attempt.Status == DeliveryAttemptStatus.Cancelled)
                break;
        }

        return new PostAllResult(results);
    }

    private async Task<DeliveryAttempt> PostItemAsync(PlannedWorkItem item, CancellationToken cancellationToken)
    {
        DeliveryAttempt? current;
        try
        {
            current = await attempts.GetAsync(item.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await RecordCancellationBeforeWriteAsync(item.Id);
        }
        catch (Exception)
        {
            return PersistenceFailure(item.Id, null, null);
        }

        if (current is not null)
            return current;

        DeliveryAttemptClaim claim;
        try
        {
            claim = await attempts.ClaimAsync(item.Id, CancellationToken.None);
        }
        catch (Exception)
        {
            return PersistenceFailure(item.Id, null, null);
        }

        if (!claim.IsAcquired)
            return claim.Attempt;

        if (cancellationToken.IsCancellationRequested)
            return await PersistAsync(item.Id, null, null, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled);

        long togglEntryId;
        try
        {
            togglEntryId = await toggl.CreateAsync(item, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await PersistAsync(item.Id, null, null, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled);
        }
        catch (Exception)
        {
            return await PersistAsync(item.Id, null, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.TogglFailed);
        }

        current = await PersistAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.InProgress, null);
        if (current.FailureCode == DeliveryFailureCode.PersistenceFailed)
            return current;

        if (cancellationToken.IsCancellationRequested)
            return await PersistAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled);

        string? jiraIssueId;
        try
        {
            jiraIssueId = await jira.GetIssueIdAsync(item.JiraIssueKey, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await PersistAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled);
        }
        catch (Exception)
        {
            return await PersistAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.JiraFailed);
        }

        if (string.IsNullOrWhiteSpace(jiraIssueId))
            return await PersistAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.JiraIssueNotFound);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tempoWorklogId = await tempo.CreateAsync(item, jiraIssueId, cancellationToken);
            return await PersistAsync(item.Id, togglEntryId, tempoWorklogId, DeliveryAttemptStatus.Succeeded, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await PersistAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled);
        }
        catch (Exception)
        {
            return await PersistAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.TempoFailed);
        }
    }

    private async Task<DeliveryAttempt> RecordCancellationBeforeWriteAsync(Guid itemId)
    {
        try
        {
            var claim = await attempts.ClaimAsync(itemId, CancellationToken.None);
            return claim.IsAcquired
                ? await PersistAsync(itemId, null, null, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled)
                : claim.Attempt;
        }
        catch (Exception)
        {
            return PersistenceFailure(itemId, null, null);
        }
    }

    private async Task<DeliveryAttempt> PersistAsync(
        Guid itemId,
        long? togglEntryId,
        long? tempoWorklogId,
        DeliveryAttemptStatus status,
        DeliveryFailureCode? failureCode)
    {
        var attempt = new DeliveryAttempt(itemId, togglEntryId, tempoWorklogId, status, failureCode, SlackDeliveryState.NotSupported);
        try
        {
            await attempts.SaveAsync(attempt, CancellationToken.None);
            return attempt;
        }
        catch (Exception)
        {
            var persistenceFailure = PersistenceFailure(itemId, togglEntryId, tempoWorklogId);
            try
            {
                await attempts.SaveAsync(persistenceFailure, CancellationToken.None);
            }
            catch (Exception)
            {
            }

            return persistenceFailure;
        }
    }

    private static DeliveryAttempt PersistenceFailure(Guid itemId, long? togglEntryId, long? tempoWorklogId) =>
        new(itemId, togglEntryId, tempoWorklogId, DeliveryAttemptStatus.Failed, DeliveryFailureCode.PersistenceFailed, SlackDeliveryState.NotSupported);
}
