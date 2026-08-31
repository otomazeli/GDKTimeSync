using GDK.TimeSync.Jira;
using GDK.TimeSync.Tempo;
using GDK.TimeSync.Toggl;

namespace GDK.TimeSync.Desktop.Services;

public interface IIntegrationClientFactory
{
    Task<ITogglClient> CreateTogglAsync(CancellationToken cancellationToken = default);
    Task<JiraClient> CreateJiraAsync(CancellationToken cancellationToken = default);
    Task<TempoClient> CreateTempoAsync(CancellationToken cancellationToken = default);
}
