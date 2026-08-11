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
        var current = await attempts.GetAsync(item.Id, cancellationToken);
        if (current?.Status == DeliveryAttemptStatus.Succeeded)
            return current;

        var togglEntryId = current?.TogglEntryId;
        if (togglEntryId is null)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                togglEntryId = await toggl.CreateAsync(item, cancellationToken);
                current = await SaveAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.InProgress, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return await SaveAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled);
            }
            catch (Exception)
            {
                return await SaveAsync(item.Id, null, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.TogglFailed);
            }
        }

        if (cancellationToken.IsCancellationRequested)
            return await SaveAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled);

        string? jiraIssueId;
        try
        {
            jiraIssueId = await jira.GetIssueIdAsync(item.JiraIssueKey, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await SaveAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled);
        }
        catch (Exception)
        {
            return await SaveAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.JiraFailed);
        }

        if (string.IsNullOrWhiteSpace(jiraIssueId))
            return await SaveAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.JiraIssueNotFound);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tempoWorklogId = await tempo.CreateAsync(item, jiraIssueId, cancellationToken);
            return await SaveAsync(item.Id, togglEntryId, tempoWorklogId, DeliveryAttemptStatus.Succeeded, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await SaveAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled);
        }
        catch (Exception)
        {
            return await SaveAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.TempoFailed);
        }
    }

    private async Task<DeliveryAttempt> SaveAsync(
        Guid itemId,
        long? togglEntryId,
        long? tempoWorklogId,
        DeliveryAttemptStatus status,
        DeliveryFailureCode? failureCode)
    {
        var attempt = new DeliveryAttempt(itemId, togglEntryId, tempoWorklogId, status, failureCode, SlackDeliveryState.NotSupported);
        await attempts.SaveAsync(attempt, CancellationToken.None);
        return attempt;
    }
}
