using System.Collections.Concurrent;

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
    IDeliveryAttemptRepository attempts,
    // A new coordinator is built per delivery call (each needs its own live API clients); pass in
    // a store that outlives individual calls so a reconciliation record survives to the next one.
    ConcurrentDictionary<Guid, DeliveryAttempt>? sharedPendingReconciliation = null,
    IAuditLog? auditLog = null) : IPostAllCoordinator
{
    private readonly ConcurrentDictionary<Guid, DeliveryAttempt> pendingReconciliation = sharedPendingReconciliation ?? [];

    // A stack is long, and a truncated one still names the throw site, which is the part that says
    // where delivery actually broke.
    private const int MaxExceptionCharacters = 4000;

    // FailureDetail carries a short reason for the review row; this is the whole exception, for the
    // log only. Types, messages and frames from the integration clients -- no credential reaches
    // here, because the clients' own messages are constants and the token lives in a header this
    // code never reads.
    private void LogException(Guid itemId, string step, Exception exception)
    {
        var text = exception.ToString();
        if (text.Length > MaxExceptionCharacters)
            text = text[..MaxExceptionCharacters] + " …(truncated)";
        auditLog?.Write(AuditLevel.Error, "Delivery", $"{itemId} {step} threw{Environment.NewLine}{text}");
    }

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
        if (pendingReconciliation.TryGetValue(item.Id, out var pending))
            return await RecoverPendingAsync(pending);

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
            return PersistenceFailure(item.Id, null, null, "The stored delivery state could not be read.");
        }

        // A resumable failure falls through to the claim below, which reopens it and carries forward
        // the Toggl entry an earlier run already created. Without this the stored failure was simply
        // returned -- the same outcome replayed in a millisecond, with nothing retried and no way to
        // ever deliver the task.
        if (current is not null && !current.IsResumable())
        {
            if (current.Status != DeliveryAttemptStatus.InProgress)
                return current;

            var reconciliation = RequiresManualReconciliation(current);
            try
            {
                await attempts.SaveAsync(reconciliation, CancellationToken.None);
            }
            catch (Exception)
            {
            }

            return reconciliation;
        }

        DeliveryAttemptClaim claim;
        try
        {
            claim = await attempts.ClaimAsync(item.Id, CancellationToken.None);
        }
        catch (Exception)
        {
            return PersistenceFailure(item.Id, null, null, "The delivery claim could not be taken.");
        }

        if (!claim.IsAcquired)
            return claim.Attempt;

        if (cancellationToken.IsCancellationRequested)
            return await PersistAsync(item.Id, null, null, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled,
                "Cancelled before the Toggl entry was created.");

        long togglEntryId;
        // The claim's own entry id comes first: on a resumed attempt it is the entry an earlier run
        // already created, and the plan item does not carry it. Preferring the item here would post
        // a second Toggl entry for work that is already tracked.
        if ((claim.Attempt.TogglEntryId ?? item.TogglEntryId) is { } knownTogglEntryId)
        {
            togglEntryId = knownTogglEntryId;
        }
        else if (!item.PostToToggl)
        {
            // No HTTP happens on this path, so without a reason the log shows only "Failed
            // TogglFailed" a few milliseconds after Confirmed -- indistinguishable from a
            // configuration problem or an unreachable Toggl.
            return await PersistAsync(item.Id, null, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.TogglFailed,
                "Push to Toggl is off for this task and it has no linked Toggl entry.");
        }
        else
        {
            try
            {
                togglEntryId = await toggl.CreateAsync(item, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return await PersistAsync(item.Id, null, null, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled,
                    "Cancelled while creating the Toggl entry -- one may exist that was not recorded.");
            }
            catch (Exception ex)
            {
                LogException(item.Id, "Toggl", ex);
                return await PersistAsync(item.Id, null, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.TogglFailed, ex.Message);
            }
        }

        current = await PersistAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.InProgress, null);
        if (current.FailureCode == DeliveryFailureCode.PersistenceFailed)
            return current;

        if (cancellationToken.IsCancellationRequested)
            return await PersistAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled,
                "Cancelled after the Toggl entry, before the Jira lookup.");

        string? jiraIssueId;
        try
        {
            jiraIssueId = await jira.GetIssueIdAsync(item.JiraIssueKey, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await PersistAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled,
                "Cancelled during the Jira lookup.");
        }
        catch (Exception ex)
        {
            LogException(item.Id, "Jira", ex);
            return await PersistAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.JiraFailed, ex.Message);
        }

        if (string.IsNullOrWhiteSpace(jiraIssueId))
            return await PersistAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.JiraIssueNotFound,
                $"Jira returned no id for {item.JiraIssueKey}.");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tempoWorklogId = await tempo.CreateAsync(item, jiraIssueId, cancellationToken);
            return await PersistAsync(item.Id, togglEntryId, tempoWorklogId, DeliveryAttemptStatus.Succeeded, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await PersistAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled,
                "Cancelled while writing the Tempo worklog -- one may exist that was not recorded.");
        }
        catch (DeliveryRejectedException ex)
        {
            LogException(item.Id, "Tempo", ex);
            // Tempo answered and refused: nothing was written, so this attempt can be retried.
            return await PersistAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.TempoRejected, ex.Message);
        }
        catch (Exception ex)
        {
            LogException(item.Id, "Tempo", ex);
            return await PersistAsync(item.Id, togglEntryId, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.TempoFailed, ex.Message);
        }
    }

    private async Task<DeliveryAttempt> RecordCancellationBeforeWriteAsync(Guid itemId)
    {
        try
        {
            var claim = await attempts.ClaimAsync(itemId, CancellationToken.None);
            return claim.IsAcquired
                ? await PersistAsync(itemId, null, null, DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled,
                    "Cancelled before delivery began -- nothing was written anywhere.")
                : claim.Attempt;
        }
        catch (Exception)
        {
            return PersistenceFailure(itemId, null, null, "The delivery claim could not be taken while cancelling.");
        }
    }

    private async Task<DeliveryAttempt> PersistAsync(
        Guid itemId,
        long? togglEntryId,
        long? tempoWorklogId,
        DeliveryAttemptStatus status,
        DeliveryFailureCode? failureCode,
        string? failureDetail = null)
    {
        var attempt = new DeliveryAttempt(itemId, togglEntryId, tempoWorklogId, status, failureCode, SlackDeliveryState.NotSupported)
        {
            FailureDetail = failureDetail
        };
        try
        {
            await attempts.SaveAsync(attempt, CancellationToken.None);
            pendingReconciliation.TryRemove(itemId, out _);
            return attempt;
        }
        catch (Exception)
        {
            var persistenceFailure = RequiresManualReconciliation(attempt);
            try
            {
                await attempts.SaveAsync(persistenceFailure, CancellationToken.None);
                pendingReconciliation.TryRemove(itemId, out _);
            }
            catch (Exception)
            {
                pendingReconciliation[itemId] = persistenceFailure;
            }

            return persistenceFailure;
        }
    }

    private async Task<DeliveryAttempt> RecoverPendingAsync(DeliveryAttempt attempt)
    {
        try
        {
            await attempts.SaveAsync(attempt, CancellationToken.None);
            pendingReconciliation.TryRemove(attempt.PlannedWorkItemId, out _);
        }
        catch (Exception)
        {
        }

        return attempt;
    }

    private static DeliveryAttempt RequiresManualReconciliation(DeliveryAttempt attempt) =>
        attempt with { Status = DeliveryAttemptStatus.ReconciliationRequired, FailureCode = DeliveryFailureCode.PersistenceFailed };

    private static DeliveryAttempt PersistenceFailure(Guid itemId, long? togglEntryId, long? tempoWorklogId, string? reason = null) =>
        RequiresManualReconciliation(new DeliveryAttempt(itemId, togglEntryId, tempoWorklogId, DeliveryAttemptStatus.Failed, null, SlackDeliveryState.NotSupported) { FailureDetail = reason });
}
