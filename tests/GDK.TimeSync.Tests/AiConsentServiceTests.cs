using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop;
using GDK.TimeSync.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GDK.TimeSync.Tests;

public sealed class AiConsentServiceTests
{
    [Fact]
    public void Generator_request_discloses_exactly_the_three_selected_task_text_fields()
    {
        var fields = typeof(DescriptionSuggestionRequest)
            .GetProperties()
            .Select(property => property.Name)
            .Order()
            .ToArray();

        Assert.Equal(["CurrentDescription", "JiraIssueKey", "TaskName"], fields);
    }

    [Fact]
    public async Task Unavailable_generator_returns_the_exact_safe_result_without_echoing_request_content()
    {
        const string sentinel = "do-not-echo-this-secret";
        var generator = new UnavailableAssistedTextGenerator();
        var request = new DescriptionSuggestionRequest(sentinel, "TS-012", sentinel);

        var result = await generator.SuggestAsync(request);

        Assert.Equal(new DescriptionSuggestionResult(false, null, "AI provider is not configured."), result);
        Assert.DoesNotContain(sentinel, result.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_preference_allows_a_complete_selected_item()
    {
        var service = new AiConsentService(new FakeSettingsStore(new UserSettings { AiEnabled = true }));

        Assert.True(service.IsEnabled);
        Assert.True(service.CanSubmit(Request()));
    }

    [Fact]
    public void Disabled_or_incomplete_request_is_rejected()
    {
        var enabled = new AiConsentService(new FakeSettingsStore(new UserSettings { AiEnabled = true }));
        var disabled = new AiConsentService(new FakeSettingsStore(new UserSettings { AiEnabled = false }));

        Assert.False(disabled.CanSubmit(Request()));
        Assert.False(enabled.CanSubmit(Request(taskName: " ")));
        Assert.False(enabled.CanSubmit(Request(jiraIssueKey: " ")));
        Assert.False(enabled.CanSubmit(Request(currentDescription: " ")));
    }

    [Fact]
    public void App_services_resolve_ai_contracts_without_provider_credentials()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<AiConsentService>(provider.GetRequiredService<IAiConsentService>());
        Assert.IsType<UnavailableAssistedTextGenerator>(provider.GetRequiredService<IAssistedTextGenerator>());
    }

    private static DescriptionSuggestionRequest Request(
        string taskName = "Task",
        string jiraIssueKey = "TS-012",
        string currentDescription = "Current description") =>
        new(taskName, jiraIssueKey, currentDescription);

    private sealed class FakeSettingsStore(UserSettings settings) : IUserSettingsStore
    {
        public UserSettings Load() => settings;

        public void Save(UserSettings value) { }
    }
}
