using GDK.TimeSync.Desktop.ViewModels;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Tests;

public sealed class TodayViewModelTests
{
    [Fact]
    public void AddItemCommand_AddsEditableItemAndUpdatesPlannedSeconds()
    {
        var today = new TodayViewModel();

        today.AddItemCommand.Execute(null);

        var item = Assert.Single(today.Items);
        Assert.True(item.IsEditable);
        Assert.Equal(0, today.PlannedSeconds);
    }

    [Fact]
    public void RemoveItemCommand_RemovesItemAndUpdatesPlannedSeconds()
    {
        var today = new TodayViewModel();
        var item = new PlannedWorkItemViewModel("Work", "CGMFRAVII-1", "Description", TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT");
        today.Items.Add(item);

        today.RemoveItemCommand.Execute(item);

        Assert.Empty(today.Items);
        Assert.Equal(0, today.PlannedSeconds);
    }

    [Fact]
    public void AddTemplateCommand_AddsEditableItemToToday()
    {
        var today = new TodayViewModel();
        var template = new RecurringTaskTemplateViewModel(
            "Knowledge transfer", "CGMFRAVII-2767", "Knowledge transfer", TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT");

        today.AddTemplateCommand.Execute(template);

        var item = Assert.Single(today.Items);
        Assert.Equal("CGMFRAVII-2767", item.JiraIssueKey);
        Assert.True(item.IsEditable);
        Assert.Equal(1800, today.PlannedSeconds);
    }

    [Fact]
    public void AddTemplateCommand_PreservesTemplateWorkStatus()
    {
        var today = new TodayViewModel();
        var template = new RecurringTaskTemplateViewModel(status: WorkStatus.Done);

        today.AddTemplateCommand.Execute(template);

        Assert.Equal(WorkStatus.Done, Assert.Single(today.Items).Status);
    }

    [Fact]
    public void ChangingDuration_UpdatesPlannedSeconds()
    {
        var today = new TodayViewModel();
        var item = new PlannedWorkItemViewModel("Work", "CGMFRAVII-1", "Description", TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT");
        today.Items.Add(item);

        item.Duration = TimeSpan.FromMinutes(45);

        Assert.Equal(2700, today.PlannedSeconds);
    }

    [Fact]
    public void OpeningAndCancellingAiConsent_DoesNotCallServicesOrEditTheSelectedDescription()
    {
        var repository = new CountingPlanRepository();
        var integration = new IntegrationCallCounter();
        var policy = new FakeConsentService(isEnabled: true, canSubmit: true);
        var generator = new FakeGenerator(new DescriptionSuggestionResult(true, "Suggested", "Ready"));
        var today = new TodayViewModel(repository, null, policy, generator);
        var selected = AddSelectedItem(today, "Current description");

        today.OpenAiConsentCommand.Execute(null);
        Assert.Equal(new DescriptionSuggestionRequest(selected.Id, "Work", "CGMFRAVII-1", "Current description"), today.PendingAiRequest);
        today.CancelAiConsentCommand.Execute(null);

        Assert.Null(today.PendingAiRequest);
        Assert.False(today.IsAiConsentVisible);
        Assert.Equal("Current description", selected.Description);
        Assert.Equal(0, policy.CanSubmitCalls);
        Assert.Equal(0, generator.SuggestCalls);
        Assert.Equal(0, repository.GetCalls);
        Assert.Equal(0, repository.SaveCalls);
        Assert.Equal(0, integration.Calls);
    }

    [Fact]
    public void ConfirmedUnavailableAiRequest_UsesTheSelectedPayloadAndDoesNotEditTheDescription()
    {
        var policy = new FakeConsentService(isEnabled: true, canSubmit: true);
        var generator = new FakeGenerator(new DescriptionSuggestionResult(false, null, "AI provider is not configured."));
        var today = new TodayViewModel(null, null, policy, generator);
        var selected = AddSelectedItem(today, "Current description");

        today.OpenAiConsentCommand.Execute(null);
        today.ConfirmAiConsentCommand.Execute(null);

        Assert.Equal(new DescriptionSuggestionRequest(selected.Id, "Work", "CGMFRAVII-1", "Current description"), generator.Request);
        Assert.Equal("AI provider is not configured.", today.AiStatus);
        Assert.Null(today.SuggestedDescription);
        Assert.False(today.IsAiConsentVisible);
        Assert.Equal("Current description", selected.Description);
        Assert.Equal(1, policy.CanSubmitCalls);
        Assert.Equal(1, generator.SuggestCalls);
    }

    [Fact]
    public void SuggestedDescription_RequiresExplicitApplyAndUpdatesOnlyTheStillSelectedItem()
    {
        var policy = new FakeConsentService(isEnabled: true, canSubmit: true);
        var generator = new FakeGenerator(new DescriptionSuggestionResult(true, "AI draft", "Ready"));
        var today = new TodayViewModel(null, null, policy, generator);
        var selected = AddSelectedItem(today, "Current description");
        var other = new PlannedWorkItemViewModel("Other", "CGMFRAVII-2", "Other description");
        today.Items.Add(other);

        today.OpenAiConsentCommand.Execute(null);
        today.ConfirmAiConsentCommand.Execute(null);

        Assert.Equal("AI draft", today.SuggestedDescription);
        Assert.Equal("Current description", selected.Description);
        Assert.Equal("Other description", other.Description);

        today.SelectedItem = other;
        today.ApplyAiSuggestionCommand.Execute(null);

        Assert.Equal("Current description", selected.Description);
        Assert.Equal("Other description", other.Description);
        Assert.Equal("AI draft", today.SuggestedDescription);

        today.SelectedItem = selected;
        today.ApplyAiSuggestionCommand.Execute(null);

        Assert.Equal("AI draft", selected.Description);
        Assert.Equal("Other description", other.Description);
        Assert.Null(today.SuggestedDescription);
        Assert.Equal(1, policy.CanSubmitCalls);
        Assert.Equal(1, generator.SuggestCalls);
    }

    [Fact]
    public void DeniedAiRequest_RecordsOnlyTheSelectedPayloadAndDoesNotCallTheGenerator()
    {
        var policy = new FakeConsentService(isEnabled: true, canSubmit: false);
        var generator = new FakeGenerator(new DescriptionSuggestionResult(true, "AI draft", "Ready"));
        var integration = new IntegrationCallCounter();
        var today = new TodayViewModel(null, null, policy, generator);
        var selected = AddSelectedItem(today, "Current description");

        today.OpenAiConsentCommand.Execute(null);
        today.ConfirmAiConsentCommand.Execute(null);
        today.ApplyAiSuggestionCommand.Execute(null);

        Assert.Equal(new DescriptionSuggestionRequest(selected.Id, "Work", "CGMFRAVII-1", "Current description"), policy.Request);
        Assert.Equal(1, policy.CanSubmitCalls);
        Assert.Equal(0, generator.SuggestCalls);
        Assert.Equal(0, integration.Calls);
        Assert.Null(today.SuggestedDescription);
        Assert.Equal("Current description", selected.Description);
    }

    [Theory]
    [InlineData(false, true, "Work", "CGMFRAVII-1", "Current description")]
    [InlineData(true, true, "", "CGMFRAVII-1", "Current description")]
    public void DisabledOrIncompleteAiRequest_BlocksBeforeCallingTheGenerator(bool enabled, bool canSubmit, string name, string jiraKey, string description)
    {
        var policy = new FakeConsentService(enabled, canSubmit);
        var generator = new FakeGenerator(new DescriptionSuggestionResult(true, "AI draft", "Ready"));
        var today = new TodayViewModel(null, null, policy, generator);
        var selected = new PlannedWorkItemViewModel(name, jiraKey, description);
        today.Items.Add(selected);
        today.SelectedItem = selected;

        today.OpenAiConsentCommand.Execute(null);
        today.ConfirmAiConsentCommand.Execute(null);

        Assert.Equal(1, policy.CanSubmitCalls);
        Assert.Equal(0, generator.SuggestCalls);
        Assert.Null(today.SuggestedDescription);
        Assert.Equal(description, selected.Description);
    }

    [Fact]
    public void GeneratorException_UsesAFixedSafeStatusWithoutEchoingExceptionText()
    {
        const string sentinel = "do-not-echo-this-provider-exception";
        var policy = new FakeConsentService(isEnabled: true, canSubmit: true);
        var generator = new FakeGenerator(Task.FromException<DescriptionSuggestionResult>(new InvalidOperationException(sentinel)));
        var today = new TodayViewModel(null, null, policy, generator);
        var selected = AddSelectedItem(today, "Current description");

        today.OpenAiConsentCommand.Execute(null);
        today.ConfirmAiConsentCommand.Execute(null);

        Assert.DoesNotContain(sentinel, today.AiStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, today.SuggestedDescription ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("Current description", selected.Description);
        Assert.Equal(1, generator.SuggestCalls);
    }

    [Fact]
    public void PolicyException_UsesAFixedSafeStatusWithoutEchoingExceptionText()
    {
        const string sentinel = "do-not-echo-this-policy-exception";
        var policy = new FakeConsentService(isEnabled: true, canSubmit: true, canSubmitException: new InvalidOperationException(sentinel));
        var generator = new FakeGenerator(new DescriptionSuggestionResult(true, "AI draft", "Ready"));
        var today = new TodayViewModel(null, null, policy, generator);
        var selected = AddSelectedItem(today, "Current description");

        today.OpenAiConsentCommand.Execute(null);
        today.ConfirmAiConsentCommand.Execute(null);

        Assert.Equal("AI suggestion could not be generated.", today.AiStatus);
        Assert.DoesNotContain(sentinel, today.AiStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, today.SuggestedDescription ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("Current description", selected.Description);
        Assert.Equal(1, policy.CanSubmitCalls);
        Assert.Equal(0, generator.SuggestCalls);
    }

    private static PlannedWorkItemViewModel AddSelectedItem(TodayViewModel today, string description)
    {
        var item = new PlannedWorkItemViewModel("Work", "CGMFRAVII-1", description);
        today.Items.Add(item);
        today.SelectedItem = item;
        return item;
    }

    private sealed class FakeConsentService(bool isEnabled, bool canSubmit, Exception? canSubmitException = null) : IAiConsentService
    {
        public int CanSubmitCalls { get; private set; }
        public DescriptionSuggestionRequest? Request { get; private set; }
        public bool IsEnabled { get; } = isEnabled;

        public bool CanSubmit(DescriptionSuggestionRequest request)
        {
            CanSubmitCalls++;
            Request = request;
            if (canSubmitException is not null) throw canSubmitException;
            return canSubmit;
        }
    }

    private sealed class FakeGenerator : IAssistedTextGenerator
    {
        private readonly Task<DescriptionSuggestionResult> result;

        public FakeGenerator(DescriptionSuggestionResult result) : this(Task.FromResult(result)) { }

        public FakeGenerator(Task<DescriptionSuggestionResult> result) => this.result = result;

        public int SuggestCalls { get; private set; }
        public DescriptionSuggestionRequest? Request { get; private set; }

        public Task<DescriptionSuggestionResult> SuggestAsync(DescriptionSuggestionRequest request, CancellationToken cancellationToken = default)
        {
            SuggestCalls++;
            Request = request;
            return result;
        }
    }

    private sealed class CountingPlanRepository : IDailyPlanRepository
    {
        public int GetCalls { get; private set; }
        public int SaveCalls { get; private set; }

        public Task<DailyPlan?> GetAsync(DateOnly date, CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return Task.FromResult<DailyPlan?>(null);
        }

        public Task SaveAsync(DailyPlan plan, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class IntegrationCallCounter
    {
        public int Calls { get; private set; }

        public void Record() => Calls++;
    }
}
