using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;
using GDK.TimeSync.Jira;
using GDK.TimeSync.Slack;
using GDK.TimeSync.Tempo;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Tests;

public sealed class ShellViewModelTests
{
    [Theory]
    [InlineData(EndOfDayReminderMode.TrayNotificationOnly, true, false)]
    [InlineData(EndOfDayReminderMode.OpenReviewOnly, false, true)]
    [InlineData(EndOfDayReminderMode.Both, true, true)]
    [InlineData((EndOfDayReminderMode)999, true, true)]
    public async Task Review_reminder_mode_executes_only_the_requested_local_presentation_actions(
        EndOfDayReminderMode mode,
        bool showTrayNotification,
        bool openReviewWindow)
    {
        var trayCalls = 0;
        var reviewCalls = 0;

        await ReviewReminderPresenter.PresentAsync(mode, () => trayCalls++, () => { reviewCalls++; return Task.CompletedTask; });

        Assert.Equal(showTrayNotification ? 1 : 0, trayCalls);
        Assert.Equal(openReviewWindow ? 1 : 0, reviewCalls);
    }

    [Fact]
    public void NavigateCommand_SelectsRequestedPage()
    {
        var configurationState = new ConfigurationStateService(new FakeCredentialStore(), new FakeSettingsStore());
        var viewModel = new ShellViewModel(configurationState);

        viewModel.NavigateCommand.Execute(NavigationPage.Review);

        Assert.Equal(NavigationPage.Review, viewModel.SelectedPage);
    }

    [Fact]
    public async Task Navigating_to_review_awaits_one_local_snapshot_refresh_without_external_effects()
    {
        var credentials = new FakeCredentialStore();
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30));
        var snapshot = new TrackingPlanSnapshotProvider(DailyPlan.Create(date, [item]));
        var review = new ReviewViewModel(snapshot);
        var viewModel = new ShellViewModel(new ConfigurationStateService(credentials, new FakeSettingsStore()), review: review);

        await viewModel.NavigateAsync(NavigationPage.Review);

        Assert.Equal(NavigationPage.Review, viewModel.SelectedPage);
        Assert.Equal([item.Id], review.Tasks.Select(task => task.Item.Id));
        Assert.Equal(1, snapshot.Reads);
        Assert.Equal(0, credentials.GetCalls);
    }

    [Fact]
    public async Task Reminder_presentation_opens_review_without_credentials_factories_delivery_or_persistence()
    {
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, "Work", "CGM-1", "Completed", TimeSpan.FromMinutes(30));
        var snapshot = new TrackingPlanSnapshotProvider(DailyPlan.Create(date, [item]));
        var credentials = new TrackingCredentialStore();
        var settings = new TrackingSettingsStore();
        var clients = new TrackingIntegrationClientFactory();
        var attempts = new TrackingAttemptRepository();
        var deliveries = new TrackingDailyDeliveryRepository();
        var slack = new TrackingSlackClientFactory();
        var delivery = new TrackingDeliveryService(new ConfirmedTaskDeliveryService(clients, settings, attempts));
        var review = new ReviewViewModel(snapshot, delivery, attempts, deliveries, slack, settings);
        var shell = new ShellViewModel(new ConfigurationStateService(credentials, settings), review: review);
        var trayCalls = 0;

        await ReviewReminderPresenter.PresentAsync(
            EndOfDayReminderMode.Both,
            () => trayCalls++,
            () => shell.NavigateAsync(NavigationPage.Review));

        Assert.Equal(1, trayCalls);
        Assert.Equal(NavigationPage.Review, shell.SelectedPage);
        Assert.Equal([item.Id], review.Tasks.Select(task => task.Item.Id));
        Assert.Equal(1, snapshot.Reads);
        Assert.Equal(0, credentials.Calls);
        Assert.Equal(0, settings.LoadCalls + settings.SaveCalls);
        Assert.Equal(0, clients.Calls);
        Assert.Equal(0, delivery.Calls);
        Assert.Equal(1, attempts.Calls); // RefreshAsync now loads recorded attempts to build each row's delivery marks.
        Assert.Equal(0, deliveries.Calls);
        Assert.Equal(0, slack.Calls);
    }

    private sealed class FakeCredentialStore : ICredentialStore
    {
        public int GetCalls { get; private set; }
        public Task SaveAsync(string key, string secret, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) { GetCalls++; return Task.FromResult<string?>(null); }
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TrackingPlanSnapshotProvider(DailyPlan plan) : ILocalPlanSnapshotProvider
    {
        public int Reads { get; private set; }
        public DailyPlan GetSnapshot() { Reads++; return plan; }
    }

    private sealed class FakeSettingsStore : IUserSettingsStore
    {
        public UserSettings Load() => new();
        public void Save(UserSettings settings) { }
    }

    private sealed class TrackingCredentialStore : ICredentialStore
    {
        public int Calls { get; private set; }
        public Task SaveAsync(string key, string secret, CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; }
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult<string?>(null); }
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(false); }
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; }
    }

    private sealed class TrackingSettingsStore : IUserSettingsStore
    {
        public int LoadCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public UserSettings Load() { LoadCalls++; return new UserSettings(); }
        public void Save(UserSettings settings) => SaveCalls++;
    }

    private sealed class TrackingDeliveryService(IConfirmedTaskDeliveryService inner) : IConfirmedTaskDeliveryService
    {
        public int Calls { get; private set; }
        public Task<DeliveryAttempt> DeliverConfirmedAsync(PlannedWorkItem item, CancellationToken cancellationToken = default)
        {
            Calls++;
            return inner.DeliverConfirmedAsync(item, cancellationToken);
        }
    }

    private sealed class TrackingIntegrationClientFactory : IIntegrationClientFactory
    {
        public int Calls { get; private set; }
        public Task<ITogglClient> CreateTogglAsync(CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
        public Task<JiraClient> CreateJiraAsync(CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
        public Task<TempoClient> CreateTempoAsync(CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
    }

    private sealed class TrackingAttemptRepository : IDeliveryAttemptRepository
    {
        public int Calls { get; private set; }
        public Task<DeliveryAttempt?> GetAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult<DeliveryAttempt?>(null); }
        public Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default) { Calls++; return Task.FromResult<IReadOnlyList<DeliveryAttempt>>([]); }
        public Task<DeliveryAttemptClaim> ClaimAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
        public Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; }
    }

    private sealed class TrackingDailyDeliveryRepository : IDailySlackDeliveryRepository
    {
        public int Calls { get; private set; }
        public Task<DailySlackDelivery?> GetAsync(DateOnly date, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult<DailySlackDelivery?>(null); }
        public Task<bool> TryClaimAsync(DateOnly date, string contentFingerprint, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(false); }
        public Task SaveAsync(DailySlackDelivery delivery, CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; }
    }

    private sealed class TrackingSlackClientFactory : ISlackClientFactory
    {
        public int Calls { get; private set; }
        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(false); }
        public Task<ISlackClient> CreateAsync(CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
    }
}
