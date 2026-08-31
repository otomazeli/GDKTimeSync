using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Desktop.ViewModels;
using GDK.TimeSync.Slack;

namespace GDK.TimeSync.Tests;

public sealed class ConnectionStatusViewModelTests
{
    [Fact]
    public async Task Refresh_maps_read_only_diagnostics_and_slack_configuration()
    {
        var viewModel = new ConnectionStatusViewModel(
            new FakeDiagnostics(
            [
                new(IntegrationDiagnosticTarget.Toggl, true, "Available"),
                new(IntegrationDiagnosticTarget.Jira, false, "Unavailable")
            ]),
            new FakeSlackFactory(true));

        await viewModel.RefreshAsync();

        Assert.Equal(ConnectionStatus.Connected, viewModel.Toggl.Status);
        Assert.Equal(ConnectionStatus.Failed, viewModel.Jira.Status);
        Assert.Equal(ConnectionStatus.Connected, viewModel.Slack.Status);
    }

    [Fact]
    public async Task Refresh_marks_slack_unavailable_without_a_webhook()
    {
        var viewModel = new ConnectionStatusViewModel(new FakeDiagnostics([]), new FakeSlackFactory(false));

        await viewModel.RefreshAsync();

        Assert.Equal(ConnectionStatus.Failed, viewModel.Slack.Status);
        Assert.Equal("Not configured", viewModel.Slack.Message);
    }

    private sealed class FakeDiagnostics(IReadOnlyList<IntegrationDiagnosticResult> results) : IIntegrationDiagnosticsService
    {
        public Task<IReadOnlyList<IntegrationDiagnosticResult>> RunAsync(CancellationToken cancellationToken = default) => Task.FromResult(results);
    }

    private sealed class FakeSlackFactory(bool configured) : ISlackClientFactory
    {
        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) => Task.FromResult(configured);
        public Task<ISlackClient> CreateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
