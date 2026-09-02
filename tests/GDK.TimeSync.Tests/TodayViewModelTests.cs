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
    public void SettingAnOvernightStartAndEnd_RecalculatesDurationAcrossMidnight()
    {
        var today = new TodayViewModel();
        var item = new PlannedWorkItemViewModel();
        today.Items.Add(item);

        item.Start = new TimeOnly(23, 30);
        item.End = new TimeOnly(0, 15);

        Assert.Equal(TimeSpan.FromMinutes(45), item.Duration);
        Assert.Equal(2700, today.PlannedSeconds);
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
        Assert.Contains("Header=\"Jira key\" Width=\"140\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"Description\" Width=\"300\"", markup, StringComparison.Ordinal);
        Assert.Contains("Header=\"Toggl project\" Width=\"160\"", markup, StringComparison.Ordinal);
    }

    // Issue #3: every input in the quick-edit panel carries its own visible label. The panel used to
    // caption each ROW with one dim line spanning all three columns -- and row one's named only two of
    // its three fields, in the wrong order, so it pointed the reader at the wrong boxes.
    [Fact]
    public void Today_quick_edit_panel_labels_every_field_and_puts_the_jira_key_first()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GDK.TimeSync.Desktop", "Views", "TodayView.xaml"));
        var markup = File.ReadAllText(path);
        var elements = XDocument.Load(path).Descendants().ToArray();
        var labels = elements
            .Where(element => element.Name.LocalName == "TextBlock" && element.Attribute("FontSize")?.Value == "11")
            .Select(element => element.Attribute("Text")?.Value)
            .ToArray();

        foreach (var expected in new[] { "Jira key", "Description", "Toggl project", "Task name", "From", "To" })
            Assert.Contains(expected, labels);

        Assert.DoesNotContain("Jira key · Toggl project", markup, StringComparison.Ordinal);
        Assert.Contains("LostFocus=\"OnJiraKeyLostFocus\"", markup, StringComparison.Ordinal);

        // The key is the first input the user meets, because it is what drives the Jira lookup.
        var inputs = elements
            .Where(element => element.Name.LocalName is "TextBox" or "ComboBox" && element.Attribute("Grid.Row")?.Value == "1")
            .Select(element => element.Attribute("Grid.Column")?.Value)
            .ToArray();
        Assert.Equal("0", inputs[0]);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        Assert.Contains(elements, element => element.Attribute(xaml + "Name")?.Value == "JiraKeyTextBox" && element.Attribute("Grid.Column")?.Value == "0");
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
    public async Task SaveConflict_ReconcilesARemotelyAddedItemInsteadOfDroppingItOrFailing()
    {
        var date = new DateOnly(2026, 8, 24);
        var itemA = PlannedWorkItem.Create(date, "A", "CGM-1", "Original");
        var itemC = PlannedWorkItem.Create(date, "C", "CGM-3", "Added by auto-sync while Today saved");
        var repository = new ConflictingPlanRepository(DailyPlan.Create(date, [itemA]) with { Version = 1 })
        {
            FailSaveTimes = 1,
            OnConflict = current => current with { Items = [.. current.Items, itemC], Version = current.Version + 1 }
        };
        var today = new TodayViewModel(repository, date);
        await today.InitializeAsync();

        var localItem = Assert.Single(today.Items);
        localItem.Name = "A updated";
        await today.FlushAsync();

        Assert.Null(today.PersistenceError);
        var saved = Assert.Single(repository.SavedPlans);
        Assert.Equal(2, saved.Items.Count);
        Assert.Contains(saved.Items, item => item.Id == itemA.Id && item.Name == "A updated");
        Assert.Contains(saved.Items, item => item.Id == itemC.Id);
        Assert.Contains(today.Items, item => item.Id == itemC.Id);
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
    [InlineData(true)]
    [InlineData(false)]
    public void IsAiEnabled_FollowsTheConsentServiceSoTheDraftButtonIsHiddenWhenAiIsOff(bool enabled)
    {
        var today = new TodayViewModel(null, null, new FakeConsentService(enabled, canSubmit: true), new FakeGenerator(new DescriptionSuggestionResult(true, "AI draft", "Ready")));

        Assert.Equal(enabled, today.IsAiEnabled);
    }

    [Fact]
    public void IsAiEnabled_IsFalseWhenNoConsentServiceIsWiredUp() =>
        Assert.False(new TodayViewModel().IsAiEnabled);

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

    private sealed class ConflictingPlanRepository(DailyPlan plan) : IDailyPlanRepository
    {
        public List<DailyPlan> SavedPlans { get; } = [];
        public int FailSaveTimes { get; set; }
        public Func<DailyPlan, DailyPlan>? OnConflict { get; set; }

        public Task<DailyPlan?> GetAsync(DateOnly date, CancellationToken cancellationToken = default) =>
            Task.FromResult<DailyPlan?>(plan);

        public Task SaveAsync(DailyPlan value, CancellationToken cancellationToken = default)
        {
            if (FailSaveTimes > 0)
            {
                FailSaveTimes--;
                if (OnConflict is not null) plan = OnConflict(plan);
                throw new PlanConcurrencyException(value.Date);
            }

            plan = value with { Version = value.Version + 1 };
            SavedPlans.Add(value);
            return Task.CompletedTask;
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

    // ---- Issue #4: Jira lookup on a new row that carries only the key ----

    [Fact]
    public async Task LookUpJiraKeyAsync_FillsNameDescriptionCategoryAndDefaultProjectOnANewRow()
    {
        var today = CreateLookupViewModel(out var jira);
        await today.LoadProjectsAsync();
        var item = new PlannedWorkItemViewModel(jiraIssueKey: "CGMFRAVII-8428");
        today.Items.Add(item);

        await today.LookUpJiraKeyAsync(item);

        Assert.Equal("DMP CPx certificate", item.Name);
        Assert.Equal("DMP CPx certificate", item.Description);
        Assert.Equal("DEVELOPMENT", item.TempoCategory);
        Assert.Equal("CGM", item.TogglProject);
        Assert.Equal(77, item.TogglProjectId);
        Assert.Equal(1, jira.Calls);
    }

    [Fact]
    public async Task LookUpJiraKeyAsync_DoesNothingForARowImportedFromToggl()
    {
        var today = CreateLookupViewModel(out var jira);
        var item = new PlannedWorkItemViewModel(jiraIssueKey: "CGMFRAVII-8428", source: ItemSource.Toggl);
        today.Items.Add(item);

        await today.LookUpJiraKeyAsync(item);

        Assert.Equal("", item.Name);
        Assert.Equal(0, jira.Calls);
    }

    [Theory]
    [InlineData("already named", "", "")]
    [InlineData("", "already described", "")]
    [InlineData("", "", "already projected")]
    public async Task LookUpJiraKeyAsync_NeverTouchesARowThatAlreadyHasContent(string name, string description, string project)
    {
        var today = CreateLookupViewModel(out var jira);
        var item = new PlannedWorkItemViewModel(name, "CGMFRAVII-8428", description, togglProject: project);
        today.Items.Add(item);

        await today.LookUpJiraKeyAsync(item);

        Assert.Equal(name, item.Name);
        Assert.Equal(description, item.Description);
        Assert.Equal(project, item.TogglProject);
        Assert.Equal(0, jira.Calls);
    }

    [Fact]
    public async Task LookUpJiraKeyAsync_MakesNoCallForAKeyThatIsNotAValidIssueKey()
    {
        var today = CreateLookupViewModel(out var jira);
        var item = new PlannedWorkItemViewModel(jiraIssueKey: "not a key");
        today.Items.Add(item);

        await today.LookUpJiraKeyAsync(item);

        Assert.Equal(0, jira.Calls);
        Assert.Equal("", item.Name);
    }

    [Fact]
    public async Task LookUpJiraKeyAsync_LeavesTheRowUntouchedAndStaysSilentWhenJiraIsUnreachable()
    {
        var today = CreateLookupViewModel(out _, jiraFails: true);
        var item = new PlannedWorkItemViewModel(jiraIssueKey: "CGMFRAVII-8428");
        today.Items.Add(item);

        await today.LookUpJiraKeyAsync(item);

        Assert.Equal("", item.Name);
        Assert.Null(today.JiraLookupError);
    }

    [Fact]
    public async Task LookUpJiraKeyAsync_ReportsAKeyJiraDoesNotKnow()
    {
        var today = CreateLookupViewModel(out _, summary: null);
        var item = new PlannedWorkItemViewModel(jiraIssueKey: "CGMFRAVII-8428");
        today.Items.Add(item);

        await today.LookUpJiraKeyAsync(item);

        Assert.Equal("", item.Name);
        Assert.Equal("CGMFRAVII-8428 was not found in Jira.", today.JiraLookupError);
    }

    [Fact]
    public async Task LookUpJiraKeyAsync_DiscardsAResultWhoseKeyTheUserHasSinceChanged()
    {
        var today = CreateLookupViewModel(out var jira);
        var item = new PlannedWorkItemViewModel(jiraIssueKey: "CGMFRAVII-8428");
        today.Items.Add(item);
        jira.Gate = new TaskCompletionSource();

        var inFlight = today.LookUpJiraKeyAsync(item);
        item.JiraIssueKey = "CGMFRAVII-9999";
        jira.Gate.SetResult();
        await inFlight;

        Assert.Equal("", item.Name);
        Assert.Equal("", item.Description);
    }

    [Fact]
    public async Task AddItemCommand_AppliesTheConfiguredDefaultTogglProjectToANewRow()
    {
        var today = CreateLookupViewModel(out _);
        await today.LoadProjectsAsync();

        today.AddItemCommand.Execute(null);

        var added = today.SelectedItem!;
        Assert.Equal("CGM", added.TogglProject);
        Assert.Equal(77, added.TogglProjectId);
    }

    private static TodayViewModel CreateLookupViewModel(out RecordingJiraLookup jira, bool jiraFails = false, string? summary = "DMP CPx certificate")
    {
        jira = new RecordingJiraLookup(jiraFails, summary);
        var settings = new FixedSettingsStore(new UserSettings
        {
            TogglWorkspaceId = 42,
            DefaultTempoWorkCategory = "DEVELOPMENT",
            DefaultTogglProject = "CGM"
        });
        return new TodayViewModel(integrationClients: new ProjectFactory(), settingsStore: settings, jiraLookup: jira);
    }

    private sealed class RecordingJiraLookup(bool fails, string? summary) : IJiraIssueLookup
    {
        public int Calls { get; private set; }
        public TaskCompletionSource? Gate { get; set; }

        public async Task<string?> GetSummaryAsync(string issueKey, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Gate is not null) await Gate.Task;
            if (fails) throw new InvalidOperationException("Jira is not reachable.");
            return summary;
        }
    }

    // ---- Issue #6: the Toggl project picker showed nothing selected on first load ----

    // The ComboBox binds SelectedValue to TogglProjectId over an ItemsSource of TogglProjects. If the
    // rows exist before that list is populated, WPF cannot match the value, drops it, and never
    // re-evaluates. So projects must be loaded first.
    // The defect is in WPF, not in view-model state: the ComboBox drops a SelectedValue it cannot
    // match against an empty ItemsSource and never re-evaluates. The view model's own values are
    // correct either way, so the only thing a test here can pin is the ORDERING that avoids it --
    // TogglProjects must already be populated by the time the rows are built.
    [Fact]
    public async Task InitializeAsync_PopulatesTheProjectListBeforeItBuildsTheRows()
    {
        var date = new DateOnly(2026, 9, 3);
        var stored = PlannedWorkItem.Create(date, "Work", "CGM-1", "Comment", TimeSpan.FromMinutes(30))
            with { TogglProjectId = 77 };
        TodayViewModel? today = null;
        var projectsWhenItemsLoaded = -1;
        var repository = new StubPlanRepository(DailyPlan.Create(date, [stored]),
            onGet: () => projectsWhenItemsLoaded = today!.TogglProjects.Count);
        today = new TodayViewModel(repository, date,
            integrationClients: new ProjectFactory(),
            settingsStore: new FixedSettingsStore(new UserSettings { TogglWorkspaceId = 42 }));

        await today.InitializeAsync();

        Assert.Equal(1, projectsWhenItemsLoaded);
        var row = Assert.Single(today.Items);
        Assert.Equal(77, row.TogglProjectId);
        Assert.Equal("CGM", row.TogglProject);
    }

    // Clear()-then-add reset any SelectedValue that HAD resolved, so refreshing wiped good selections.
    [Fact]
    public async Task LoadProjectsAsync_DoesNotClearTheListWhenTheSameProjectsComeBack()
    {
        var today = new TodayViewModel(
            integrationClients: new ProjectFactory(),
            settingsStore: new FixedSettingsStore(new UserSettings { TogglWorkspaceId = 42 }));
        await today.LoadProjectsAsync();
        var firstInstance = today.TogglProjects[0];

        await today.LoadProjectsAsync();

        Assert.Same(firstInstance, today.TogglProjects[0]);
        Assert.Single(today.TogglProjects);
    }

    private sealed class StubPlanRepository(DailyPlan plan, Action? onGet = null) : IDailyPlanRepository
    {
        public Task<DailyPlan?> GetAsync(DateOnly date, CancellationToken cancellationToken = default)
        {
            onGet?.Invoke();
            return Task.FromResult<DailyPlan?>(date == plan.Date ? plan : null);
        }

        public Task SaveAsync(DailyPlan value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // ---- Issue #7: a Save button, and something that says whether a save has landed ----

    [Fact]
    public async Task EditingARowMarksThePlanUnsavedUntilTheSaveLands()
    {
        var date = new DateOnly(2026, 9, 3);
        var today = new TodayViewModel(new StubPlanRepository(DailyPlan.Create(date, [])), date);
        await today.InitializeAsync();
        Assert.False(today.HasUnsavedChanges);

        today.Items[0].Description = "edited";

        Assert.True(today.HasUnsavedChanges);
        Assert.True(today.SaveCommand.CanExecute(null));

        await today.FlushAsync();

        Assert.False(today.HasUnsavedChanges);
        Assert.NotNull(today.LastSavedAt);
    }

    [Fact]
    public async Task SaveCommandIsUnavailableWithNothingOutstanding()
    {
        var date = new DateOnly(2026, 9, 3);
        var today = new TodayViewModel(new StubPlanRepository(DailyPlan.Create(date, [])), date);
        await today.InitializeAsync();

        Assert.False(today.SaveCommand.CanExecute(null));
    }

    // A failed save must leave the plan marked unsaved: reporting "Saved" over a write that did not
    // happen is worse than showing nothing at all.
    [Fact]
    public async Task AFailedSaveLeavesThePlanMarkedUnsavedAndReportsTheError()
    {
        var date = new DateOnly(2026, 9, 3);
        var today = new TodayViewModel(new ThrowingPlanRepository(DailyPlan.Create(date, [])), date);
        await today.InitializeAsync();

        today.Items[0].Description = "edited";
        await today.FlushAsync();

        Assert.True(today.HasUnsavedChanges);
        Assert.Null(today.LastSavedAt);
        Assert.Equal("Could not save today's plan.", today.PersistenceError);
    }

    private sealed class ThrowingPlanRepository(DailyPlan plan) : IDailyPlanRepository
    {
        public Task<DailyPlan?> GetAsync(DateOnly date, CancellationToken cancellationToken = default) =>
            Task.FromResult<DailyPlan?>(date == plan.Date ? plan : null);
        public Task SaveAsync(DailyPlan value, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("disk is unavailable");
    }

    // ---- Issue #8: Add item applied the project default but not the Tempo category default ----

    [Fact]
    public async Task AddItemCommand_AppliesBothConfiguredDefaults()
    {
        var today = CreateLookupViewModel(out _);
        await today.LoadProjectsAsync();

        today.AddItemCommand.Execute(null);

        var added = today.SelectedItem!;
        Assert.Equal("CGM", added.TogglProject);
        Assert.Equal(77, added.TogglProjectId);
        Assert.Equal("DEVELOPMENT", added.TempoCategory);
    }

    // Matches what the Jira lookup path already does when the setting is blank.
    [Fact]
    public void AddItemCommand_FallsBackToDevelopmentWhenNoTempoCategoryIsConfigured()
    {
        var today = new TodayViewModel(
            settingsStore: new FixedSettingsStore(new UserSettings { DefaultTempoWorkCategory = "" }));

        today.AddItemCommand.Execute(null);

        Assert.Equal("DEVELOPMENT", today.SelectedItem!.TempoCategory);
    }

    [Fact]
    public void Today_quick_edit_panel_lets_the_tempo_category_be_edited_outside_the_grid()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GDK.TimeSync.Desktop", "Views", "TodayView.xaml"));
        var elements = XDocument.Load(path).Descendants().ToArray();

        Assert.Contains(elements, element =>
            element.Name.LocalName == "TextBox" &&
            element.Attribute("Text")?.Value.Contains("SelectedItem.TempoCategory", StringComparison.Ordinal) == true);
        Assert.Contains(elements, element =>
            element.Name.LocalName == "TextBlock" && element.Attribute("Text")?.Value == "Tempo category");
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
