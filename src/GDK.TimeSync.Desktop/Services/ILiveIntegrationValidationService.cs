using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.Services;

public interface ILiveIntegrationValidationService
{
    Task<LiveValidationResult> CreateTogglAsync(PlannedWorkItem item, CancellationToken cancellationToken = default);
    Task<LiveValidationResult> ValidateJiraAsync(PlannedWorkItem item, CancellationToken cancellationToken = default);
    Task<LiveValidationResult> CreateAndVerifyTempoAsync(PlannedWorkItem item, CancellationToken cancellationToken = default);
}
