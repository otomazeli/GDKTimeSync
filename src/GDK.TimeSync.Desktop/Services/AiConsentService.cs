using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.Services;

public sealed class AiConsentService(IUserSettingsStore settings) : IAiConsentService
{
    public bool IsEnabled => settings.Load().AiEnabled;

    public bool CanSubmit(DescriptionSuggestionRequest request) =>
        IsEnabled &&
        !string.IsNullOrWhiteSpace(request.TaskName) &&
        !string.IsNullOrWhiteSpace(request.JiraIssueKey) &&
        !string.IsNullOrWhiteSpace(request.CurrentDescription);
}
