using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Jira;
using GDK.TimeSync.Tempo;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Tests;

public sealed class TogglSyncServiceTests
{
    private static readonly DateOnly Date = new(2026, 8, 24);

    [Fact]
    public async Task PullAsync_ImportsAnUnmatchedEntryAsANewLocalItemNotMarkedForTogglPosting()
    {
        var entry = CreateEntry(id: 555, projectId: 9, description: "Investigate bug", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
        var toggl = new FakeTogglClient([entry]);
        var service = CreateService(toggl, new InMemoryAttemptRepository());

        var result = await service.PullAsync(Date, []);

        var added = Assert.Single(result.ItemsToAdd);
        Assert.Empty(result.ItemsToUpdate);
        Assert.Equal(0, result.ReconciliationFlaggedCount);
        Assert.Equal(555, added.TogglEntryId);
        Assert.Equal(9, added.TogglProjectId);
        Assert.Equal(ItemSource.Toggl, added.Source);
        Assert.False(added.PostToToggl);
        Assert.Equal("Investigate bug", added.Comment);
        Assert.Equal("", added.JiraIssueKey);
        Assert.Equal("DEVELOPMENT", added.TempoCategory);
        Assert.Equal(0, toggl.CreateCount);
    }

    [Fact]
    public async Task PullAsync_ExtractsALeadingJiraKeyFromTheDescriptionOnImport()
    {
        var entry = CreateEntry(id: 555, projectId: 9, description: "CGMFRAVII-2763 - AxiSanté Agile Meetings and Activities 2026 - Daily Squad Ségur 1", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
        var toggl = new FakeTogglClient([entry]);
        var service = CreateService(toggl, new InMemoryAttemptRepository());

        var result = await service.PullAsync(Date, []);

        var added = Assert.Single(result.ItemsToAdd);
        Assert.Equal("CGMFRAVII-2763", added.JiraIssueKey);
        Assert.Equal("AxiSanté Agile Meetings and Activities 2026 - Daily Squad Ségur 1", added.Comment);
        Assert.Equal(added.Comment, added.Name);
    }

    [Fact]
    public async Task PullAsync_ExtractsALeadingJiraKeyWhenOnlySpaceSeparatedFromTheDescription()
    {
        var entry = CreateEntry(id: 555, projectId: 9, description: "CGMFRAVII-8139 Proxy DMP : Impact et endpoints (Clean Architecture) - Planning", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
        var toggl = new FakeTogglClient([entry]);
        var service = CreateService(toggl, new InMemoryAttemptRepository());

        var result = await service.PullAsync(Date, []);

        var added = Assert.Single(result.ItemsToAdd);
        Assert.Equal("CGMFRAVII-8139", added.JiraIssueKey);
        Assert.Equal("Proxy DMP : Impact et endpoints (Clean Architecture) - Planning", added.Comment);
    }

    [Fact]
    public async Task PullAsync_ExtractsALeadingJiraKeyWhenPipeSeparatedFromTheDescription()
    {
        var entry = CreateEntry(id: 555, projectId: 9, description: "CGMFRAVII-8424 | DMP — Infrastructure : DTOs JSON et mapper (TDD)", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
        var toggl = new FakeTogglClient([entry]);
        var service = CreateService(toggl, new InMemoryAttemptRepository());

        var result = await service.PullAsync(Date, []);

        var added = Assert.Single(result.ItemsToAdd);
        Assert.Equal("CGMFRAVII-8424", added.JiraIssueKey);
        Assert.Equal("DMP — Infrastructure : DTOs JSON et mapper (TDD)", added.Comment);
    }

    [Fact]
    public async Task PullAsync_LeavesJiraKeyEmptyWhenTheLeadingTokenDoesNotLookLikeAnIssueKey()
    {
        var entry = CreateEntry(id: 555, projectId: 9, description: "Team sync - weekly planning", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
        var toggl = new FakeTogglClient([entry]);
        var service = CreateService(toggl, new InMemoryAttemptRepository());

        var result = await service.PullAsync(Date, []);

        var added = Assert.Single(result.ItemsToAdd);
        Assert.Equal("", added.JiraIssueKey);
        Assert.Equal("Team sync - weekly planning", added.Comment);
    }

    [Fact]
    public async Task PullAsync_BackfillsAnEmptyJiraKeyOnAnAlreadyLinkedItemOnASubsequentSync()
    {
        var localItem = PlannedWorkItem.Create(Date, "Work", jiraIssueKey: "", comment: "Investigate bug") with { TogglEntryId = 555 };
        var entry = CreateEntry(id: 555, projectId: null, description: "CGMFRAVII-2763 - Investigate bug", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
        var toggl = new FakeTogglClient([entry]);
        var service = CreateService(toggl, new InMemoryAttemptRepository());

        var result = await service.PullAsync(Date, [localItem]);

        var updated = Assert.Single(result.ItemsToUpdate);
        Assert.Equal("CGMFRAVII-2763", updated.JiraIssueKey);
        Assert.Equal("Investigate bug", updated.Comment);
    }

    [Fact]
    public async Task PullAsync_NeverOverwritesAnAlreadySetLocalJiraKey()
    {
        var localItem = PlannedWorkItem.Create(Date, "Work", jiraIssueKey: "CGM-999", comment: "Old description") with { TogglEntryId = 555 };
        var entry = CreateEntry(id: 555, projectId: null, description: "CGMFRAVII-2763 - New description", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
        var toggl = new FakeTogglClient([entry]);
        var service = CreateService(toggl, new InMemoryAttemptRepository());

        var result = await service.PullAsync(Date, [localItem]);

        var updated = Assert.Single(result.ItemsToUpdate);
        Assert.Equal("CGM-999", updated.JiraIssueKey);
        Assert.Equal("New description", updated.Comment);
    }

    [Fact]
    public async Task PullAsync_UsesTheConfiguredDefaultTempoCategoryOnImport()
    {
        var entry = CreateEntry(id: 555, projectId: 9, description: "Investigate bug", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
        var toggl = new FakeTogglClient([entry]);
        var service = CreateService(toggl, new InMemoryAttemptRepository(), new UserSettings { TogglWorkspaceId = 42, DefaultTempoWorkCategory = "SUPPORT" });

        var result = await service.PullAsync(Date, []);

        var added = Assert.Single(result.ItemsToAdd);
        Assert.Equal("SUPPORT", added.TempoCategory);
    }

    [Fact]
    public async Task PullAsync_FallsBackToDevelopmentWhenNoDefaultTempoCategoryIsConfigured()
    {
        var entry = CreateEntry(id: 555, projectId: 9, description: "Investigate bug", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
        var toggl = new FakeTogglClient([entry]);
        var service = CreateService(toggl, new InMemoryAttemptRepository(), new UserSettings { TogglWorkspaceId = 42, DefaultTempoWorkCategory = "" });

        var result = await service.PullAsync(Date, []);

        var added = Assert.Single(result.ItemsToAdd);
        Assert.Equal("DEVELOPMENT", added.TempoCategory);
    }

    [Fact]
    public async Task PullAsync_BackfillsAnEmptyTempoCategoryOnAnAlreadyLinkedItemOnASubsequentSync()
    {
        var localItem = PlannedWorkItem.Create(Date, "Work", jiraIssueKey: "CGM-1", comment: "Investigate bug", tempoCategory: "") with { TogglEntryId = 555 };
        var entry = CreateEntry(id: 555, projectId: null, description: "Investigate bug", start: new TimeOnly(9, 0), end: new TimeOnly(9, 45));
        var toggl = new FakeTogglClient([entry]);
        var service = CreateService(toggl, new InMemoryAttemptRepository());

        var result = await service.PullAsync(Date, [localItem]);

        var updated = Assert.Single(result.ItemsToUpdate);
        Assert.Equal("DEVELOPMENT", updated.TempoCategory);
    }

    [Fact]
    public async Task PullAsync_NeverOverwritesAnAlreadySetLocalTempoCategory()
    {
        var localItem = PlannedWorkItem.Create(Date, "Work", jiraIssueKey: "CGM-1", comment: "Investigate bug", tempoCategory: "SUPPORT") with { TogglEntryId = 555 };
        var entry = CreateEntry(id: 555, projectId: null, description: "Investigate bug changed", start: new TimeOnly(9, 0), end: new TimeOnly(9, 45));
        var toggl = new FakeTogglClient([entry]);
        var service = CreateService(toggl, new InMemoryAttemptRepository());

        var result = await service.PullAsync(Date, [localItem]);

        var updated = Assert.Single(result.ItemsToUpdate);
        Assert.Equal("SUPPORT", updated.TempoCategory);
    }

    [Fact]
    public async Task PullAsync_RefreshesAMatchedNotYetDeliveredItemInPlace()
    {
        var localItem = PlannedWorkItem.Create(Date, "Work", "CGM-1", "Old description") with { TogglEntryId = 555 };
        var entry = CreateEntry(id: 555, projectId: null, description: "New description", start: new TimeOnly(9, 0), end: new TimeOnly(9, 45));
        var toggl = new FakeTogglClient([entry]);
        var service = CreateService(toggl, new InMemoryAttemptRepository());

        var result = await service.PullAsync(Date, [localItem]);

        Assert.Empty(result.ItemsToAdd);
        var updated = Assert.Single(result.ItemsToUpdate);
        Assert.Equal(localItem.Id, updated.Id);
        Assert.Equal("New description", updated.Comment);
        Assert.Equal(new TimeOnly(9, 0), updated.Start);
        Assert.Equal(new TimeOnly(9, 45), updated.End);
        Assert.Equal(0, result.ReconciliationFlaggedCount);
    }

    [Fact]
    public async Task PullAsync_FlagsReconciliationWhenARemoteChangeFollowsASuccessfulDelivery()
    {
        var localItem = PlannedWorkItem.Create(Date, "Work", "CGM-1", "Old description", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
        var attempts = new InMemoryAttemptRepository();
        var succeeded = new DeliveryAttempt(localItem.Id, 555, 999, DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported);
        await attempts.SaveAsync(succeeded);
        var entry = CreateEntry(id: 555, projectId: null, description: "Changed after delivery", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
        var toggl = new FakeTogglClient([entry]);
        var service = CreateService(toggl, attempts);

        var result = await service.PullAsync(Date, [localItem]);

        Assert.Empty(result.ItemsToAdd);
        Assert.Empty(result.ItemsToUpdate);
        Assert.Equal(1, result.ReconciliationFlaggedCount);
        var saved = await attempts.GetAsync(localItem.Id);
        Assert.Equal(DeliveryAttemptStatus.ReconciliationRequired, saved!.Status);
        Assert.Equal(DeliveryFailureCode.RemoteChangedAfterDelivery, saved.FailureCode);
        Assert.Equal(555, saved.TogglEntryId);
        Assert.Equal(999, saved.TempoWorklogId);
        Assert.Equal(0, toggl.CreateCount);
    }

    [Fact]
    public async Task PullAsync_DoesNotTouchASuccessfulDeliveryWhenTheRemoteEntryIsUnchanged()
    {
        var localItem = PlannedWorkItem.Create(Date, "Work", "CGM-1", "Same description", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
        var attempts = new InMemoryAttemptRepository();
        await attempts.SaveAsync(new DeliveryAttempt(localItem.Id, 555, 999, DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported));
        attempts.ResetSaveCalls();
        var entry = CreateEntry(id: 555, projectId: null, description: "Same description", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
        var toggl = new FakeTogglClient([entry]);
        var service = CreateService(toggl, attempts);

        var result = await service.PullAsync(Date, [localItem]);

        Assert.Empty(result.ItemsToAdd);
        Assert.Empty(result.ItemsToUpdate);
        Assert.Equal(0, result.ReconciliationFlaggedCount);
        Assert.Equal(0, attempts.SaveCalls);
    }

    [Fact]
    public async Task PullAsync_ExcludesAStillRunningEntry()
    {
        var running = new TogglTimeEntry { Id = 555, Description = "Running", Start = ToOffset(Date, new TimeOnly(9, 0)), Stop = null, WorkspaceId = 42 };
        var toggl = new FakeTogglClient([running]);
        var service = CreateService(toggl, new InMemoryAttemptRepository());

        var result = await service.PullAsync(Date, []);

        Assert.Empty(result.ItemsToAdd);
        Assert.Empty(result.ItemsToUpdate);
    }

    [Fact]
    public async Task PullAsync_ExcludesEntriesFromAnotherWorkspace()
    {
        var entry = new TogglTimeEntry { Id = 555, Description = "Other workspace", Start = ToOffset(Date, new TimeOnly(9, 0)), Stop = ToOffset(Date, new TimeOnly(9, 30)), WorkspaceId = 7 };
        var toggl = new FakeTogglClient([entry]);
        var service = CreateService(toggl, new InMemoryAttemptRepository());

        var result = await service.PullAsync(Date, []);

        Assert.Empty(result.ItemsToAdd);
    }

    [Fact]
    public async Task PullAsync_ReturnsErrorAndMakesNoCallsWhenWorkspaceIsNotConfigured()
    {
        var toggl = new FakeTogglClient([]);
        var factory = new FakeIntegrationClientFactory(toggl);
        var service = new TogglSyncService(factory, new FixedSettingsStore(new UserSettings()), new InMemoryAttemptRepository(), CreateIssueKeyValidator());

        var result = await service.PullAsync(Date, []);

        Assert.NotNull(result.Error);
        Assert.Empty(result.ItemsToAdd);
        Assert.Equal(0, factory.TogglCreateCalls);
    }

    private static TogglSyncService CreateService(ITogglClient toggl, IDeliveryAttemptRepository attempts) =>
        CreateService(toggl, attempts, new UserSettings { TogglWorkspaceId = 42 });

    private static TogglSyncService CreateService(ITogglClient toggl, IDeliveryAttemptRepository attempts, UserSettings settings) =>
        new(new FakeIntegrationClientFactory(toggl), new FixedSettingsStore(settings), attempts, CreateIssueKeyValidator());

    private static IssueKeyValidator CreateIssueKeyValidator() => new(new IssueKeyValidationOptions());

    private static TogglTimeEntry CreateEntry(long id, long? projectId, string description, TimeOnly start, TimeOnly end) => new()
    {
        Id = id,
        Description = description,
        Start = ToOffset(Date, start),
        Stop = ToOffset(Date, end),
        WorkspaceId = 42,
        ProjectId = projectId
    };

    private static DateTimeOffset ToOffset(DateOnly date, TimeOnly time) =>
        new DateTimeOffset(date.ToDateTime(time), TimeZoneInfo.Local.GetUtcOffset(date.ToDateTime(time)));

    private sealed class FixedSettingsStore(UserSettings settings) : IUserSettingsStore
    {
        public UserSettings Load() => settings;
        public void Save(UserSettings value) { }
    }

    private sealed class FakeIntegrationClientFactory(ITogglClient toggl) : IIntegrationClientFactory
    {
        public int TogglCreateCalls { get; private set; }

        public Task<ITogglClient> CreateTogglAsync(CancellationToken cancellationToken = default)
        {
            TogglCreateCalls++;
            return Task.FromResult(toggl);
        }

        public Task<JiraClient> CreateJiraAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TempoClient> CreateTempoAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeTogglClient(IReadOnlyList<TogglTimeEntry> entries) : ITogglClient
    {
        public int CreateCount { get; private set; }

        public void Dispose() { }

        public Task<IReadOnlyList<TogglTimeEntry>> GetTimeEntriesAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(entries);

        public Task<IReadOnlyList<TogglProject>> GetProjectsAsync(long workspaceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TogglTimeEntry> CreateTimeEntryAsync(TogglCreateTimeEntryRequest request, CancellationToken cancellationToken = default)
        {
            CreateCount++;
            return Task.FromResult(new TogglTimeEntry { Id = -1 });
        }
    }

    private sealed class InMemoryAttemptRepository : IDeliveryAttemptRepository
    {
        private readonly Dictionary<Guid, DeliveryAttempt> attempts = [];
        public int SaveCalls { get; private set; }

        public void ResetSaveCalls() => SaveCalls = 0;

        public Task<DeliveryAttempt?> GetAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(attempts.GetValueOrDefault(plannedWorkItemId));

        public Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeliveryAttempt>>(attempts.Values.ToArray());

        public Task<DeliveryAttemptClaim> ClaimAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default)
        {
            if (attempts.TryGetValue(plannedWorkItemId, out var existing))
                return Task.FromResult(new DeliveryAttemptClaim(existing, false));
            var created = new DeliveryAttempt(plannedWorkItemId, null, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported);
            attempts[plannedWorkItemId] = created;
            return Task.FromResult(new DeliveryAttemptClaim(created, true));
        }

        public Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            attempts[attempt.PlannedWorkItemId] = attempt;
            return Task.CompletedTask;
        }
    }
}
