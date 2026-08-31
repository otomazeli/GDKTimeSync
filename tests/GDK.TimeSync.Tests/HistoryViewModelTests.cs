using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Tests;

public sealed class HistoryViewModelTests
{
    [Fact]
    public async Task LoadAsync_MapsReconciliationStatusWithoutExposingRawFailure()
    {
        var history = new HistoryViewModel(new FakeRepository([
            new DeliveryAttempt(Guid.NewGuid(), 101, 201, DeliveryAttemptStatus.ReconciliationRequired, DeliveryFailureCode.TempoFailed, SlackDeliveryState.NotSupported)
        ]));

        await history.LoadAsync();

        var item = Assert.Single(history.Items);
        Assert.Equal("Reconciliation required", item.StatusText);
        Assert.DoesNotContain("token", item.FailureText ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_UsesSafeErrorTextWhenReadingHistoryFails()
    {
        var history = new HistoryViewModel(new FakeRepository(null));

        await history.LoadAsync();

        Assert.Equal("Could not load delivery history.", history.LoadError);
    }

    private sealed class FakeRepository(IReadOnlyList<DeliveryAttempt>? attempts) : IDeliveryAttemptRepository
    {
        public Task<DeliveryAttempt?> GetAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default) => Task.FromResult<DeliveryAttempt?>(null);
        public Task<DeliveryAttemptClaim> ClaimAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default) =>
            attempts is null ? throw new InvalidOperationException("sensitive-token") : Task.FromResult(attempts);
    }
}
