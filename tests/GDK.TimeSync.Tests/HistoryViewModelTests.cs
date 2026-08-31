using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Tests;

public sealed class HistoryViewModelTests
{
    [Fact]
    public async Task LoadAsync_MapsReconciliationStatusWithoutExposingRawFailure()
    {
        var history = new HistoryViewModel(new FakeRepository([
            new DeliveryHistoryEntry(
                new DeliveryAttempt(Guid.NewGuid(), 101, 201, DeliveryAttemptStatus.ReconciliationRequired, DeliveryFailureCode.TempoFailed, SlackDeliveryState.NotSupported),
                new DateOnly(2026, 8, 13), "CGM-1", "Knowledge transfer")
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

    [Fact]
    public async Task LoadAsync_ShowsTheDateAndTaskInsteadOfTheRawItemIdentifier()
    {
        var history = new HistoryViewModel(new FakeRepository([
            new DeliveryHistoryEntry(
                new DeliveryAttempt(Guid.NewGuid(), 101, 201, DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported),
                new DateOnly(2026, 8, 13), "CGM-1", "Knowledge transfer"),
            new DeliveryHistoryEntry(
                new DeliveryAttempt(Guid.NewGuid(), null, null, DeliveryAttemptStatus.Failed, DeliveryFailureCode.TogglFailed, SlackDeliveryState.NotSupported),
                null, "", "")
        ]));

        await history.LoadAsync();

        Assert.Equal("2026-08-13", history.Items[0].DateText);
        Assert.Equal("CGM-1 Knowledge transfer", history.Items[0].TaskText);
        Assert.Equal("Toggl #101 · Tempo #201", history.Items[0].DestinationText);
        Assert.Equal("Unknown date", history.Items[1].DateText);
        Assert.Equal("(task no longer in any plan)", history.Items[1].TaskText);
        Assert.Equal("No external entry recorded", history.Items[1].DestinationText);
    }

    private sealed class FakeRepository(IReadOnlyList<DeliveryHistoryEntry>? entries) : IDeliveryHistoryRepository
    {
        public Task<IReadOnlyList<DeliveryHistoryEntry>> ListHistoryAsync(CancellationToken cancellationToken = default) =>
            entries is null ? throw new InvalidOperationException("sensitive-token") : Task.FromResult(entries);
    }
}
