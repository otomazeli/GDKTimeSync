using System.Net;
using System.Net.Http.Json;
using GDK.TimeSync.Desktop.ViewModels;
using GDK.TimeSync.Desktop.Services;
using GDK.TimeSync.Core;
using GDK.TimeSync.Jira;
using GDK.TimeSync.Tempo;
using GDK.TimeSync.Toggl;
using System.Xml.Linq;

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
    public void AddItemCommand_selects_the_new_item_for_editor_form()
    {
        var today = new TodayViewModel();

        today.AddItemCommand.Execute(null);

        Assert.Same(Assert.Single(today.Items), today.SelectedItem);
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
    public void SettingStartAndEnd_RecalculatesDuration()
    {
        var today = new TodayViewModel();
        var item = new PlannedWorkItemViewModel();
        today.Items.Add(item);

        item.Start = new TimeOnly(8, 15);
        item.End = new TimeOnly(8, 45);

        Assert.Equal(TimeSpan.FromMinutes(30), item.Duration);
        Assert.Equal(1800, today.PlannedSeconds);
    }

    [Fact]
    public void Snapshot_PreservesProjectIdentityTimingBillableAndTogglIntent()
    {
        var today = new TodayViewModel(null, new DateOnly(2026, 8, 20));
        var item = new PlannedWorkItemViewModel(
            jiraIssueKey: "CGMFRAVII-2767",
            description: "Knowledge transfer",
            togglProject: "CGM",
            togglProjectId: 123,
            start: new TimeOnly(8, 15),
            end: new TimeOnly(8, 45),
            isBillable: false,
            postToToggl: true);
        today.Items.Add(item);

        var snapshot = Assert.Single(today.GetSnapshot().Items);

        Assert.Equal(123, snapshot.TogglProjectId);
        Assert.Equal(new TimeOnly(8, 15), snapshot.Start);
        Assert.Equal(new TimeOnly(8, 45), snapshot.End);
        Assert.False(snapshot.IsBillable);
        Assert.True(snapshot.PostToToggl);
    }

    [Fact]
    public async Task LoadProjectsAsync_PopulatesTheConfiguredWorkspaceProjects()
    {
        var today = new TodayViewModel(
            date: new DateOnly(2026, 8, 20),
            integrationClients: new ProjectFactory(),
            settingsStore: new FixedSettingsStore(new UserSettings { TogglWorkspaceId = 42 }));

        await today.LoadProjectsAsync();

        var project = Assert.Single(today.TogglProjects);
        Assert.Equal(77, project.Id);
        Assert.Equal("CGM", project.Name);
        Assert.Null(today.ProjectLoadError);
    }

    [Fact]
    public void Ai_consent_markup_names_the_ai_actions_and_discloses_exactly_three_fields()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GDK.TimeSync.Desktop", "Views", "TodayView.xaml"));
        var elements = XDocument.Load(path).Descendants().ToArray();
        var disclosedBindings = elements
            .Attributes("Text")
            .Select(attribute => attribute.Value)
            .Where(value => value.Contains("PendingAiRequest.", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal([
            "{Binding PendingAiRequest.TaskName, StringFormat=Task name: {0}}",
            "{Binding PendingAiRequest.JiraIssueKey, StringFormat=Jira issue key: {0}}",
            "{Binding PendingAiRequest.CurrentDescription, StringFormat=Current description: {0}}"
        ], disclosedBindings);
        Assert.Contains(elements, element => element.Attribute("Text")?.Value == "AI description consent");
        Assert.Contains(elements, element => element.Attribute("Text")?.Value == "If you continue, AI will receive only these fields from the selected task:");
        Assert.Equal("Draft AI description", ButtonContent(elements, "{Binding OpenAiConsentCommand}"));
        Assert.Equal("Continue with AI", ButtonContent(elements, "{Binding ConfirmAiConsentCommand}"));
        Assert.Equal("Apply to description", ButtonContent(elements, "{Binding ApplyAiSuggestionCommand}"));
    }

    [Fact]
    public void Today_editor_uses_non_overlapping_rows_and_readable_grid_columns()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GDK.TimeSync.Desktop", "Views", "TodayView.xaml"));
        var markup = File.ReadAllText(path);

        Assert.DoesNotContain("<RowDefinition Height=\"8\" />", markup, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"20\" />", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"Jira key\" Width=\"140\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"Description\" Width=\"300\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"Toggl project\" Width=\"160\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Today_view_offers_a_date_picker_and_a_quick_jump_back_to_today()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GDK.TimeSync.Desktop", "Views", "TodayView.xaml"));
        var elements = XDocument.Load(path).Descendants().ToArray();

        Assert.Contains(elements, element =>
            element.Name.LocalName == "DatePicker" &&
            element.Attribute("SelectedDate")?.Value == "{Binding SelectedDateTime}");
        Assert.Equal("Today", ButtonContent(elements, "{Binding GoToTodayCommand}"));
    }

    [Fact]
    public async Task InitializeAsync_PreservesTogglProjectPostingIntentAndSyncLinkAcrossReload()
    {
        var date = new DateOnly(2026, 8, 24);
        var item = PlannedWorkItem.Create(date, "Work", "CGMFRAVII-1", "Description") with
        {
            TogglProjectId = 9,
            PostToToggl = false,
            TogglEntryId = 555,
            Source = ItemSource.Toggl
        };
        var repository = new CountingPlanRepository(DailyPlan.Create(date, [item]));
        var today = new TodayViewModel(repository, date);

        await today.InitializeAsync();

        var loaded = Assert.Single(today.Items);
        Assert.Equal(9, loaded.TogglProjectId);
        Assert.False(loaded.PostToToggl);
        Assert.Equal(555, loaded.TogglEntryId);
        Assert.Equal(ItemSource.Toggl, loaded.Source);
    }

    [Fact]
    public void ApplyPullResult_AddsImportedItemsNotMarkedForTogglPosting()
    {
        var today = new TodayViewModel();
        var date = today.Date;
        var added = PlannedWorkItem.Create(date, "Investigate bug", comment: "Investigate bug") with
        {
            TogglEntryId = 555,
            Source = ItemSource.Toggl,
            PostToToggl = false
        };

        var merge = today.ApplyPullResult(new TogglSyncPullResult([added], [], 0, null));

        var item = Assert.Single(today.Items);
        Assert.Equal(555, item.TogglEntryId);
        Assert.Equal(ItemSource.Toggl, item.Source);
        Assert.False(item.PostToToggl);
        Assert.Equal(1, merge.Imported);
        Assert.Equal(0, merge.Updated);
        Assert.Equal(0, merge.ReconciliationFlagged);
    }

    [Fact]
    public void ApplyPullResult_UpdatesAMatchedItemInPlaceWithoutChangingItemCount()
    {
        var today = new TodayViewModel();
        today.AddItemCommand.Execute(null);
        var existing = Assert.Single(today.Items);
        existing.Description = "Old description";
        var updated = PlannedWorkItem.Create(today.Date, comment: "New description", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30), tempoCategory: "DEVELOPMENT") with
        {
            Id = existing.Id,
            TogglEntryId = 555,
            TogglProjectId = 200556941,
            JiraIssueKey = "CGMFRAVII-2763"
        };

        var merge = today.ApplyPullResult(new TogglSyncPullResult([], [updated], 1, null));

        Assert.Single(today.Items);
        Assert.Equal("New description", existing.Description);
        Assert.Equal(new TimeOnly(9, 0), existing.Start);
        Assert.Equal(new TimeOnly(9, 30), existing.End);
        Assert.Equal(555, existing.TogglEntryId);
        Assert.Equal(200556941, existing.TogglProjectId);
        Assert.Equal("CGMFRAVII-2763", existing.JiraIssueKey);
        Assert.Equal("DEVELOPMENT", existing.TempoCategory);
        Assert.Equal(0, merge.Imported);
        Assert.Equal(1, merge.Updated);
        Assert.Equal(1, merge.ReconciliationFlagged);
    }

    [Fact]
    public void ApplyPullResult_ResolvesTheTogglProjectNameForAnUpdatedItem()
    {
        var today = new TodayViewModel();
        today.TogglProjects.Add(new TogglProject(200556941, "CompuGroup Delphi developments (BR) (FIX)"));
        today.AddItemCommand.Execute(null);
        var existing = Assert.Single(today.Items);
        var updated = PlannedWorkItem.Create(today.Date, comment: "Description") with
        {
            Id = existing.Id,
            TogglEntryId = 555,
            TogglProjectId = 200556941
        };

        today.ApplyPullResult(new TogglSyncPullResult([], [updated], 0, null));

        Assert.Equal("CompuGroup Delphi developments (BR) (FIX)", existing.TogglProject);
    }

    [Fact]
    public void ApplyPullResult_ResolvesTheTogglProjectNameForAnImportedItem()
    {
        var today = new TodayViewModel();
        today.TogglProjects.Add(new TogglProject(314, "GDK"));
        var added = PlannedWorkItem.Create(today.Date, "Investigate bug", comment: "Investigate bug") with
        {
            TogglEntryId = 555,
            TogglProjectId = 314,
            Source = ItemSource.Toggl
        };

        today.ApplyPullResult(new TogglSyncPullResult([added], [], 0, null));

        var item = Assert.Single(today.Items);
        Assert.Equal("GDK", item.TogglProject);
    }

    [Fact]
    public async Task OpeningAndCancellingAiConsent_DoesNotCallServicesOrPersistTheSelectedDescription()
    {
        var date = new DateOnly(2026, 8, 15);
        var repository = new CountingPlanRepository(DailyPlan.Create(date,
        [PlannedWorkItem.Create(date, "Work", "CGMFRAVII-1", "Current description")]));
        var policy = new FakeConsentService(isEnabled: true, canSubmit: true);
        var generator = new FakeGenerator(new DescriptionSuggestionResult(true, "Suggested", "Ready"));
        var today = new TodayViewModel(repository, date, policy, generator);
        await today.InitializeAsync();
        repository.Reset();
        var selected = Assert.Single(today.Items);
        today.SelectedItem = selected;

        today.OpenAiConsentCommand.Execute(null);
        Assert.Equal(new DescriptionSuggestionRequest("Work", "CGMFRAVII-1", "Current description"), today.PendingAiRequest);
        today.CancelAiConsentCommand.Execute(null);
        await today.FlushAsync();

        Assert.Null(today.PendingAiRequest);
        Assert.False(today.IsAiConsentVisible);
        Assert.Equal("Current description", selected.Description);
        Assert.Equal(0, policy.CanSubmitCalls);
        Assert.Equal(0, generator.SuggestCalls);
        Assert.Equal(0, repository.GetCalls);
        Assert.Equal(0, repository.SaveCalls);
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

        Assert.Equal(new DescriptionSuggestionRequest("Work", "CGMFRAVII-1", "Current description"), generator.Request);
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
    public async Task DeniedAiRequest_RecordsOnlyTheSelectedPayloadAndDoesNotCallTheGeneratorOrPersist()
    {
        var date = new DateOnly(2026, 8, 15);
        var repository = new CountingPlanRepository(DailyPlan.Create(date,
        [PlannedWorkItem.Create(date, "Work", "CGMFRAVII-1", "Current description")]));
        var policy = new FakeConsentService(isEnabled: true, canSubmit: false);
        var generator = new FakeGenerator(new DescriptionSuggestionResult(true, "AI draft", "Ready"));
        var today = new TodayViewModel(repository, date, policy, generator);
        await today.InitializeAsync();
        repository.Reset();
        var selected = Assert.Single(today.Items);
        today.SelectedItem = selected;

        today.OpenAiConsentCommand.Execute(null);
        today.ConfirmAiConsentCommand.Execute(null);
        today.ApplyAiSuggestionCommand.Execute(null);
        await today.FlushAsync();

        Assert.Equal(new DescriptionSuggestionRequest("Work", "CGMFRAVII-1", "Current description"), policy.Request);
        Assert.Equal(1, policy.CanSubmitCalls);
        Assert.Equal(0, generator.SuggestCalls);
        Assert.Equal(0, repository.GetCalls);
        Assert.Equal(0, repository.SaveCalls);
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

    [Fact]
    public async Task SelectDateAsync_FlushesPendingSaveForOldDateBeforeLoadingTheNewDate()
    {
        var oldDate = new DateOnly(2026, 8, 24);
        var newDate = new DateOnly(2026, 8, 20);
        var plans = new Dictionary<DateOnly, DailyPlan>
        {
            [newDate] = DailyPlan.Create(newDate, [PlannedWorkItem.Create(newDate, "Yesterday work", comment: "Yesterday work")])
        };
        var repository = new RecordingPlanRepository(plans);
        var today = new TodayViewModel(repository, oldDate);
        await today.InitializeAsync();
        Assert.Single(today.Items).Description = "Edited before switching date";

        await today.SelectDateAsync(newDate);

        Assert.Contains(repository.SavedPlans, plan => plan.Date == oldDate);
        Assert.Equal(newDate, today.Date);
        Assert.Equal("Yesterday work", Assert.Single(today.Items).Description);
    }

    [Fact]
    public async Task SelectDateAsync_ClearsExistingItemsAndLoadsTheNewDatesPlanFresh()
    {
        var oldDate = new DateOnly(2026, 8, 24);
        var newDate = new DateOnly(2026, 8, 20);
        var plans = new Dictionary<DateOnly, DailyPlan>
        {
            [newDate] = DailyPlan.Create(newDate, [PlannedWorkItem.Create(newDate, "Yesterday work", comment: "Yesterday work")])
        };
        var repository = new RecordingPlanRepository(plans);
        var today = new TodayViewModel(repository, oldDate);
        await today.InitializeAsync();
        today.AddItemCommand.Execute(null);
        today.AddItemCommand.Execute(null);
        Assert.Equal(3, today.Items.Count);

        await today.SelectDateAsync(newDate);

        var item = Assert.Single(today.Items);
        Assert.Equal("Yesterday work", item.Description);
    }

    [Fact]
    public async Task SelectDateAsync_ReloadingTheNewDateDoesNotQueueASpuriousSave()
    {
        var oldDate = new DateOnly(2026, 8, 24);
        var newDate = new DateOnly(2026, 8, 20);
        var plans = new Dictionary<DateOnly, DailyPlan>
        {
            [newDate] = DailyPlan.Create(newDate, [PlannedWorkItem.Create(newDate, "Yesterday work", comment: "Yesterday work")])
        };
        var repository = new RecordingPlanRepository(plans);
        var today = new TodayViewModel(repository, oldDate);
        await today.InitializeAsync();

        await today.SelectDateAsync(newDate);
        await today.FlushAsync();

        Assert.Empty(repository.SavedPlans);
    }

    [Fact]
    public async Task SelectDateAsync_ToTheSameDate_IsANoOp()
    {
        var date = new DateOnly(2026, 8, 24);
        var repository = new CountingPlanRepository(DailyPlan.Create(date, []));
        var today = new TodayViewModel(repository, date);
        await today.InitializeAsync();
        var getCallsBefore = repository.GetCalls;

        await today.SelectDateAsync(date);

        Assert.Equal(getCallsBefore, repository.GetCalls);
    }

    [Fact]
    public void GoToTodayCommand_ReturnsToTodaysDateAndReloads()
    {
        var past = DateOnly.FromDateTime(DateTime.Today).AddDays(-3);
        var realToday = DateOnly.FromDateTime(DateTime.Today);
        var plans = new Dictionary<DateOnly, DailyPlan>
        {
            [realToday] = DailyPlan.Create(realToday, [PlannedWorkItem.Create(realToday, "Today work", comment: "Today work")])
        };
        var repository = new RecordingPlanRepository(plans);
        var today = new TodayViewModel(repository, past);

        today.GoToTodayCommand.Execute(null);

        Assert.Equal(realToday, today.Date);
        Assert.Equal("Today work", Assert.Single(today.Items).Description);
    }

    private static PlannedWorkItemViewModel AddSelectedItem(TodayViewModel today, string description)
    {
        var item = new PlannedWorkItemViewModel("Work", "CGMFRAVII-1", description);
        today.Items.Add(item);
        today.SelectedItem = item;
        return item;
    }

    private static string? ButtonContent(IEnumerable<XElement> elements, string command) =>
        elements.Single(element =>
            element.Name.LocalName == "Button" &&
            element.Attribute("Command")?.Value == command)
        .Attribute("Content")?.Value;

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

    private sealed class CountingPlanRepository(DailyPlan? plan = null) : IDailyPlanRepository
    {
        public int GetCalls { get; private set; }
        public int SaveCalls { get; private set; }

        public Task<DailyPlan?> GetAsync(DateOnly date, CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return Task.FromResult(plan);
        }

        public Task SaveAsync(DailyPlan plan, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }

        public void Reset()
        {
            GetCalls = 0;
            SaveCalls = 0;
        }
    }

    private sealed class RecordingPlanRepository(IReadOnlyDictionary<DateOnly, DailyPlan> plans) : IDailyPlanRepository
    {
        public List<DailyPlan> SavedPlans { get; } = [];

        public Task<DailyPlan?> GetAsync(DateOnly date, CancellationToken cancellationToken = default)
        {
            plans.TryGetValue(date, out var plan);
            return Task.FromResult(plan);
        }

        public Task SaveAsync(DailyPlan plan, CancellationToken cancellationToken = default)
        {
            SavedPlans.Add(plan);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedSettingsStore(UserSettings settings) : IUserSettingsStore
    {
        public UserSettings Load() => settings;
        public void Save(UserSettings value) { }
    }

    private sealed class ProjectFactory : IIntegrationClientFactory
    {
        public Task<ITogglClient> CreateTogglAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ITogglClient>(new TogglClient(new HttpClient(new ProjectHandler()) { BaseAddress = new Uri("https://toggl.example.test/") }, new TogglOptions { ApiToken = "unit-token" }));

        public Task<JiraClient> CreateJiraAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TempoClient> CreateTempoAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ProjectHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[] { new TogglProject(77, "CGM") })
            });
    }
}
