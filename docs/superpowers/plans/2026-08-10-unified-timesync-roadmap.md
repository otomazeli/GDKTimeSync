# Unified GDK TimeSync Roadmap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a secure WPF application that plans daily work, records it in Toggl and Tempo/Jira after one confirmation, posts a GDK Slack update, and preserves a reconciled audit trail.

**Architecture:** The existing Core, Toggl, Jira, Tempo, Desktop, and Tests projects remain the application boundaries. The Desktop project is a WPF MVVM shell that orchestrates Core services through dependency injection; API clients remain behind typed interfaces and receive secrets only through a credential-backed factory. Durable templates, plans, delivery attempts, and reconciliation state are added after the UI-shell milestone.

**Tech Stack:** .NET 10, C# 14, WPF, `Microsoft.Extensions.DependencyInjection`, `IHttpClientFactory`, Windows Credential Manager, `System.Text.Json`, SQLite with `Microsoft.Data.Sqlite` from the persistence milestone onward, and xUnit.

## Global Constraints

- Use the existing .NET 10 solution and C# projects; do not introduce a second desktop framework.
- Use WPF bindings, view models, `RelayCommand`, and dependency injection; views do not construct clients or read secrets.
- Use `IHttpClientFactory` for all HTTP clients.
- Secrets are stored only in Windows Credential Manager and are never written to `settings.json`, logs, exceptions, UI bindings, or test output.
- The user must confirm every end-of-day delivery with `Post all`; no scheduled automatic posting.
- AI text assistance is off by default and requires explicit per-request consent before any task or Jira content leaves the application.
- Milestone 1 adds no SQLite and makes no external write calls.
- Run `dotnet test GDK.TimeSync.slnx -c Release --no-restore` and `dotnet build GDK.TimeSync.slnx -c Release --no-restore` before every milestone handoff.

---

## File structure and responsibility map

| Area | Files | Responsibility |
| --- | --- | --- |
| WPF shell | `src/GDK.TimeSync.Desktop/Views/*`, `ViewModels/*`, `MainWindow.xaml` | Navigation and visual workflow only. |
| Configuration | `Desktop/Services/UserSettings*`, `WindowsCredentialStore`, `ConfigurationStateService` | Non-secret preferences, secret presence, and configuration state. |
| Planning domain | `src/GDK.TimeSync.Core/PlannedWorkItem.cs`, `RecurringTaskTemplate.cs`, `DailyPlan.cs` | Validated data shared by UI, storage, and synchronization. |
| Persistence | `src/GDK.TimeSync.Persistence/*` | SQLite schema and repositories for plans, templates, delivery attempts, and reconciliation state. |
| Integrations | existing Toggl/Jira/Tempo projects plus `src/GDK.TimeSync.Slack/*` | Typed API clients, all via `IHttpClientFactory`. |
| Delivery | `Core/PostAllCoordinator.cs`, `Core/ReconciliationEngine.cs` | Ordered, idempotent, observable delivery workflow. |
| Scheduling | `Desktop/Services/EndOfDayReminderService.cs` | Reminder only while the app is running. |
| AI | `Core/IAssistedTextGenerator.cs`, `Desktop/Services/AiConsentService.cs` | Explicitly-consented description suggestions only. |

## Milestone 1 — WPF shell and safe daily-work UI

### Task 1: Add the application shell and page navigation

**Files:**
- Create: `src/GDK.TimeSync.Desktop/ViewModels/ShellViewModel.cs`
- Create: `src/GDK.TimeSync.Desktop/ViewModels/NavigationPage.cs`
- Create: `src/GDK.TimeSync.Desktop/Views/TodayView.xaml`
- Create: `src/GDK.TimeSync.Desktop/Views/TemplatesView.xaml`
- Create: `src/GDK.TimeSync.Desktop/Views/HistoryView.xaml`
- Create: `src/GDK.TimeSync.Desktop/Views/SettingsView.xaml`
- Create: `src/GDK.TimeSync.Desktop/Views/ReviewView.xaml`
- Modify: `src/GDK.TimeSync.Desktop/MainWindow.xaml`
- Modify: `src/GDK.TimeSync.Desktop/App.xaml.cs`
- Test: `tests/GDK.TimeSync.Tests/ShellViewModelTests.cs`

**Interfaces:**
- Produces `ShellViewModel.SelectedPage`, `ShellViewModel.NavigateCommand`, and `NavigationPage` values `Today`, `Templates`, `History`, `Settings`, `Review`.
- Consumes the existing `IConfigurationStateService` without reading credentials.

- [ ] **Step 1: Write failing navigation tests.**

```csharp
[Fact]
public void NavigateCommand_SelectsRequestedPage()
{
    var viewModel = new ShellViewModel(configurationState);
    viewModel.NavigateCommand.Execute(NavigationPage.Review);
    Assert.Equal(NavigationPage.Review, viewModel.SelectedPage);
}
```

- [ ] **Step 2: Run the targeted test and confirm it fails because `ShellViewModel` does not exist.**

Run: `dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter FullyQualifiedName~ShellViewModelTests`

- [ ] **Step 3: Implement the enum, shell view model, DI registration, two-column shell, persistent sidebar, header, and `ContentControl` templates.**

```csharp
public enum NavigationPage { Today, Templates, History, Settings, Review }
public RelayCommand NavigateCommand { get; }
public NavigationPage SelectedPage { get; private set; }
```

- [ ] **Step 4: Run the targeted test and manually inspect every navigation destination.**

- [ ] **Step 5: Commit the shell only.**

```powershell
git add src/GDK.TimeSync.Desktop tests/GDK.TimeSync.Tests/ShellViewModelTests.cs
git commit -m "feat: add unified WPF navigation shell"
```

### Task 2: Add in-memory Today rows, templates, and review guard

**Files:**
- Create: `src/GDK.TimeSync.Desktop/ViewModels/PlannedWorkItemViewModel.cs`
- Create: `src/GDK.TimeSync.Desktop/ViewModels/RecurringTaskTemplateViewModel.cs`
- Create: `src/GDK.TimeSync.Desktop/ViewModels/TodayViewModel.cs`
- Create: `src/GDK.TimeSync.Desktop/ViewModels/TemplatesViewModel.cs`
- Create: `src/GDK.TimeSync.Desktop/ViewModels/ReviewViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/Views/TodayView.xaml`
- Modify: `src/GDK.TimeSync.Desktop/Views/TemplatesView.xaml`
- Modify: `src/GDK.TimeSync.Desktop/Views/ReviewView.xaml`
- Test: `tests/GDK.TimeSync.Tests/TodayViewModelTests.cs`
- Test: `tests/GDK.TimeSync.Tests/ReviewViewModelTests.cs`

**Interfaces:**
- Produces `TodayViewModel.Items`, `AddItemCommand`, `RemoveItemCommand`, `AddTemplateCommand`, and `PlannedSeconds`.
- Produces `ReviewViewModel.CanPostAll == false` and an explanatory message for this milestone.
- `RecurringTaskTemplateViewModel` contains `Name`, `JiraIssueKey`, `Description`, `Duration`, `TogglProject`, and `TempoCategory`.

- [ ] **Step 1: Write failing add/remove/template/guard tests.**

```csharp
[Fact]
public void AddTemplateCommand_AddsEditableItemToToday()
{
    var template = new RecurringTaskTemplateViewModel(
        "Knowledge transfer", "CGMFRAVII-2767", "Knowledge transfer", TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT");
    today.AddTemplateCommand.Execute(template);
    var item = Assert.Single(today.Items);
    Assert.Equal("CGMFRAVII-2767", item.JiraIssueKey);
    Assert.True(item.IsEditable);
}

[Fact]
public void PostAll_IsUnavailableBeforeDeliveryWorkflowExists() =>
    Assert.False(review.PostAllCommand.CanExecute(null));
```

- [ ] **Step 2: Run the targeted tests and confirm they fail.**

Run: `dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter "FullyQualifiedName~TodayViewModelTests|FullyQualifiedName~ReviewViewModelTests"`

- [ ] **Step 3: Implement in-memory rows and the reviewed POC interactions.** Do not call Toggl, Jira, Tempo, Slack, AI, SQLite, or a persistence service.

- [ ] **Step 4: Verify tests pass; launch the desktop app and confirm `Post all` cannot trigger an external call.**

- [ ] **Step 5: Commit.**

```powershell
git add src/GDK.TimeSync.Desktop tests/GDK.TimeSync.Tests
git commit -m "feat: add daily planning UI workflow"
```

## Milestone 2 — Durable planning data and configuration expansion

### Task 3: Introduce the plan/template domain model and SQLite persistence

**Files:**
- Create: `src/GDK.TimeSync.Core/PlannedWorkItem.cs`
- Create: `src/GDK.TimeSync.Core/RecurringTaskTemplate.cs`
- Create: `src/GDK.TimeSync.Core/DailyPlan.cs`
- Create: `src/GDK.TimeSync.Persistence/GDK.TimeSync.Persistence.csproj`
- Create: `src/GDK.TimeSync.Persistence/SqliteDatabase.cs`
- Create: `src/GDK.TimeSync.Persistence/SqliteDailyPlanRepository.cs`
- Create: `src/GDK.TimeSync.Persistence/SqliteTemplateRepository.cs`
- Create: `src/GDK.TimeSync.Persistence/ServiceCollectionExtensions.cs`
- Modify: `GDK.TimeSync.slnx`
- Modify: `src/GDK.TimeSync.Desktop/App.xaml.cs`
- Test: `tests/GDK.TimeSync.Tests/SqlitePlanRepositoryTests.cs`

**Interfaces:**
- Produces `IDailyPlanRepository.GetAsync(DateOnly)`, `SaveAsync(DailyPlan)`, `ITemplateRepository.ListAsync()`, and `SaveAsync(RecurringTaskTemplate)`.
- `PlannedWorkItem` includes one day, local start/end, Jira key, comment, Toggl project, Tempo category, and billable flag.

- [ ] **Step 1: Write failing repository round-trip and uniqueness tests using a temporary SQLite database file.**

```csharp
[Fact]
public async Task SaveAsync_ReplacesThePlanForItsDateWithoutDuplicatingItems()
{
    var plan = DailyPlan.Create(new DateOnly(2026, 8, 10), [item]);
    await repository.SaveAsync(plan);
    await repository.SaveAsync(plan with { Items = [item with { Comment = "Updated" }] });
    var loaded = await repository.GetAsync(new DateOnly(2026, 8, 10));
    Assert.Single(loaded!.Items);
    Assert.Equal("Updated", loaded.Items[0].Comment);
}
```

- [ ] **Step 2: Run the targeted test and confirm it fails because the persistence project is absent.**

- [ ] **Step 3: Add `Microsoft.Data.Sqlite`, create tables with parameterized commands, and implement repositories.** Store no credentials or webhook URLs in this database.

- [ ] **Step 4: Bind Today/Templates to repositories and verify data survives app restart.**

- [ ] **Step 5: Commit.**

```powershell
git add src/GDK.TimeSync.Core src/GDK.TimeSync.Persistence src/GDK.TimeSync.Desktop tests GDK.TimeSync.slnx
git commit -m "feat: persist daily plans and templates"
```

### Task 4: Extend non-secret settings and credential keys

**Files:**
- Modify: `src/GDK.TimeSync.Desktop/Services/UserSettings.cs`
- Modify: `src/GDK.TimeSync.Desktop/Services/CredentialKeys.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/SettingsViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/Views/SettingsView.xaml`
- Test: `tests/GDK.TimeSync.Tests/DesktopConfigurationTests.cs`

**Interfaces:**
- Adds non-secret `ReviewReminderTime`, `DefaultTempoWorkCategory`, `AiEnabled`, and `TogglWorkspaceId` fields.
- Adds secret key `GDK.TimeSync.GDK.SlackWebhook`; it is presence-only in view models.

- [ ] **Step 1: Add tests proving new non-secret settings serialize and all secrets remain absent from JSON.**
- [ ] **Step 2: Run the tests and confirm the new properties are not yet available.**
- [ ] **Step 3: Implement settings bindings and credential presence state; preserve existing partial-save error behavior.**
- [ ] **Step 4: Run tests and manually save/reopen Settings without revealing a secret.**
- [ ] **Step 5: Commit.**

## Milestone 3 — Integration clients and validation

### Task 5: Make credential-backed typed integration clients available through DI

**Files:**
- Create: `src/GDK.TimeSync.Desktop/Services/IIntegrationClientFactory.cs`
- Create: `src/GDK.TimeSync.Desktop/Services/IntegrationClientFactory.cs`
- Modify: `src/GDK.TimeSync.Desktop/App.xaml.cs`
- Modify: `src/GDK.TimeSync.Toggl/ServiceCollectionExtensions.cs`
- Modify: `src/GDK.TimeSync.Jira/ServiceCollectionExtensions.cs`
- Modify: `src/GDK.TimeSync.Tempo/ServiceCollectionExtensions.cs`
- Test: `tests/GDK.TimeSync.Tests/IntegrationClientFactoryTests.cs`

**Interfaces:**
- Produces `Task<ITogglClient> CreateTogglAsync(CancellationToken)`, `Task<JiraClient> CreateJiraAsync(CancellationToken)`, and `Task<TempoClient> CreateTempoAsync(CancellationToken)`.
- Reads secrets only inside the factory and never returns them to a view model.

- [ ] **Step 1: Write a failing fake-store test proving each configured client can be created without exposing secret properties.**
- [ ] **Step 2: Run it and confirm the factory is absent.**
- [ ] **Step 3: Implement the factory with named `HttpClient` registrations and safe configuration errors.**
- [ ] **Step 4: Run client and factory tests with mocked HTTP handlers.**
- [ ] **Step 5: Commit.**

### Task 6: Complete Toggl, Jira, and Tempo read/write contracts

**Files:**
- Modify: `src/GDK.TimeSync.Toggl/ITogglClient.cs`
- Modify: `src/GDK.TimeSync.Toggl/TogglClient.cs`
- Create: `src/GDK.TimeSync.Toggl/TogglCreateTimeEntryRequest.cs`
- Create: `src/GDK.TimeSync.Tempo/ITempoClient.cs`
- Modify: `src/GDK.TimeSync.Tempo/TempoClient.cs`
- Create: `src/GDK.TimeSync.Tempo/TempoWorklog.cs`
- Create: `src/GDK.TimeSync.Tempo/TempoWorklogRequest.cs`
- Test: `tests/GDK.TimeSync.Tests/TogglClientTests.cs`
- Test: `tests/GDK.TimeSync.Tests/TempoClientTests.cs`

**Interfaces:**

```csharp
Task<TogglTimeEntry> CreateTimeEntryAsync(TogglCreateTimeEntryRequest request, CancellationToken cancellationToken = default);
Task<TempoWorklog> CreateWorklogAsync(TempoWorklogRequest request, CancellationToken cancellationToken = default);
Task<TempoWorklog?> GetWorklogAsync(long worklogId, CancellationToken cancellationToken = default);
Task<TempoWorklog> UpdateWorklogAsync(long worklogId, TempoWorklogRequest request, CancellationToken cancellationToken = default);
```

- [ ] **Step 1: Add mocked-response tests for Toggl create, Jira issue metadata, Tempo current worker/attributes/worklog create/read/update, and safe HTTP exceptions.**
- [ ] **Step 2: Run tests and confirm each missing contract fails.**
- [ ] **Step 3: Implement minimal request/response mapping with `IHttpClientFactory` clients; validate Jira keys before Tempo writes.**
- [ ] **Step 4: Run all Toggl/Jira/Tempo tests.**
- [ ] **Step 5: Commit.**

## Milestone 4 — Safe Post all, idempotency, and reconciliation

### Task 7: Persist delivery attempts and make Post all idempotent

**Files:**
- Create: `src/GDK.TimeSync.Core/DeliveryAttempt.cs`
- Create: `src/GDK.TimeSync.Core/IPostAllCoordinator.cs`
- Create: `src/GDK.TimeSync.Core/PostAllCoordinator.cs`
- Create: `src/GDK.TimeSync.Persistence/SqliteDeliveryAttemptRepository.cs`
- Modify: `src/GDK.TimeSync.Core/ServiceCollectionExtensions.cs`
- Test: `tests/GDK.TimeSync.Tests/PostAllCoordinatorTests.cs`

**Interfaces:**
- `Task<PostAllResult> PostAsync(DailyPlan plan, CancellationToken cancellationToken = default)`.
- A delivery attempt stores the plan item identifier, Toggl entry ID, Tempo worklog ID, Slack state, status, and safe failure code.

- [ ] **Step 1: Write failing tests for order, duplicate suppression, cancellation, and a Tempo failure after Toggl creation.**
- [ ] **Step 2: Run the tests and confirm the coordinator is absent.**
- [ ] **Step 3: Implement Toggl -> Jira validation -> Tempo ordering, persist every successful external ID immediately, and do not send Slack when an earlier destination fails.**
- [ ] **Step 4: Run coordinator and persistence tests.**
- [ ] **Step 5: Commit.**

### Task 8: Enable review confirmation and reconciliation UI

**Files:**
- Modify: `src/GDK.TimeSync.Core/ReconciliationEngine.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/ReviewViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/HistoryViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/Views/ReviewView.xaml`
- Modify: `src/GDK.TimeSync.Desktop/Views/HistoryView.xaml`
- Test: `tests/GDK.TimeSync.Tests/ReconciliationEngineTests.cs`
- Test: `tests/GDK.TimeSync.Tests/ReviewViewModelTests.cs`

- [ ] **Step 1: Write tests that a valid configured plan enables Dry Run, while production `Post all` remains disabled until the Slack client exists.**
- [ ] **Step 2: Run tests and confirm the milestone-1 guard fails the new Dry Run behavior.**
- [ ] **Step 3: Implement a confirmation dialog, Dry Run progress/result summary, and history status sourced from delivery attempts.**
- [ ] **Step 4: Run tests and manually verify Dry Run performs no external write.**
- [ ] **Step 5: Commit.**

## Milestone 5 — Slack delivery and tray scheduling

### Task 9: Add the Slack Incoming Webhook client and message composer

**Files:**
- Create: `src/GDK.TimeSync.Slack/GDK.TimeSync.Slack.csproj`
- Create: `src/GDK.TimeSync.Slack/ISlackClient.cs`
- Create: `src/GDK.TimeSync.Slack/SlackClient.cs`
- Create: `src/GDK.TimeSync.Slack/SlackDailyUpdateComposer.cs`
- Create: `src/GDK.TimeSync.Slack/SlackMessage.cs`
- Modify: `GDK.TimeSync.slnx`
- Modify: `src/GDK.TimeSync.Desktop/App.xaml.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/ReviewViewModel.cs`
- Test: `tests/GDK.TimeSync.Tests/SlackClientTests.cs`
- Test: `tests/GDK.TimeSync.Tests/SlackDailyUpdateComposerTests.cs`

**Interfaces:**

```csharp
Task PostAsync(SlackMessage message, CancellationToken cancellationToken = default);
SlackMessage Compose(DateOnly date, IReadOnlyList<PlannedWorkItem> items);
```

- [ ] **Step 1: Write tests for Slack Markdown output, HTTP payload shape, webhook failure handling, and absence of webhook values from thrown messages.**
- [ ] **Step 2: Run tests and confirm the project/client are absent.**
- [ ] **Step 3: Implement a typed webhook client whose base URI comes from the secret factory, call it only from `PostAllCoordinator` after all Tempo worklogs succeed, and enable production `Post all` only when Slack is configured.**
- [ ] **Step 4: Run the Slack and coordinator test suites.**
- [ ] **Step 5: Commit.**

### Task 10: Add the running-app end-of-day reminder

**Files:**
- Create: `src/GDK.TimeSync.Desktop/Services/IEndOfDayReminderService.cs`
- Create: `src/GDK.TimeSync.Desktop/Services/EndOfDayReminderService.cs`
- Modify: `src/GDK.TimeSync.Desktop/Services/TrayIconService.cs`
- Modify: `src/GDK.TimeSync.Desktop/App.xaml.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/ShellViewModel.cs`
- Test: `tests/GDK.TimeSync.Tests/EndOfDayReminderServiceTests.cs`

**Interfaces:**
- `event EventHandler? ReviewDue;`
- `Task StartAsync(CancellationToken cancellationToken = default);`
- `Task StopAsync(CancellationToken cancellationToken = default);`

- [ ] **Step 1: Write tests with an injected clock proving one notification per configured local date and none while disabled.**
- [ ] **Step 2: Run them and confirm the service is absent.**
- [ ] **Step 3: Implement the service as an in-process timer, subscribe in `App`, show/activate the main window, and navigate to Review. Do not create a Windows scheduled task or post automatically.**
- [ ] **Step 4: Run tests and manually verify the tray application prompts only while running.**
- [ ] **Step 5: Commit.**

## Milestone 6 — Explicitly-consented AI descriptions

### Task 11: Add consent-gated text suggestion abstractions

**Files:**
- Create: `src/GDK.TimeSync.Core/IAssistedTextGenerator.cs`
- Create: `src/GDK.TimeSync.Core/DescriptionSuggestionRequest.cs`
- Create: `src/GDK.TimeSync.Desktop/Services/IAiConsentService.cs`
- Create: `src/GDK.TimeSync.Desktop/Services/AiConsentService.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/TodayViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/Views/TodayView.xaml`
- Test: `tests/GDK.TimeSync.Tests/AiConsentServiceTests.cs`
- Test: `tests/GDK.TimeSync.Tests/TodayViewModelTests.cs`

**Interfaces:**

```csharp
Task<bool> RequestConsentAsync(DescriptionSuggestionRequest request, CancellationToken cancellationToken = default);
Task<string> SuggestAsync(DescriptionSuggestionRequest request, CancellationToken cancellationToken = default);
```

- [ ] **Step 1: Write tests proving the generator is never called when consent is declined and that a suggestion never posts any worklog.**
- [ ] **Step 2: Run tests and confirm consent-gated behavior is absent.**
- [ ] **Step 3: Implement the UI prompt and an unavailable-provider message. Do not add an external AI provider until the user selects and approves one; keep all provider credentials in Windows Credential Manager.**
- [ ] **Step 4: Run consent tests and inspect logs/settings for secret and content exclusion.**
- [ ] **Step 5: Commit.**

## Milestone 7 — release hardening

### Task 12: Complete user documentation, packaging, and production verification

**Files:**
- Create: `docs/user-guide.md`
- Modify: `scripts/publish-cgm.ps1`
- Modify: `scripts/setup-current-user.ps1`
- Modify: `scripts/remove-current-user.ps1`
- Create: `docs/operations/recovery-and-reconciliation.md`
- Test: `tests/GDK.TimeSync.Tests/EndToEndDryRunTests.cs`

- [ ] **Step 1: Write a dry-run integration test using fake Toggl/Jira/Tempo/Slack clients that asserts the complete ordered result.**
- [ ] **Step 2: Run it and confirm any missing wiring fails.**
- [ ] **Step 3: Document connection setup, secure credential entry, manual review, partial-failure recovery, and removal. Update packaging scripts to include the latest self-contained desktop output without embedding secrets.**
- [ ] **Step 4: Run the full tests, Release build, self-contained publish script, fresh-user installation check, and manual review confirmation flow.**
- [ ] **Step 5: Commit.**

```powershell
git add docs scripts src tests
git commit -m "docs: prepare TimeSync production workflow"
```

## Coverage review

- WPF Today/Templates/History/Settings/Review and sidebar: Tasks 1-2.
- Secure unified settings session: Task 4, with existing credential tests retained.
- Durable templates, plans, idempotency, and reconciliation: Tasks 3, 7-8.
- Toggl, Jira, and Tempo integration: Tasks 5-6.
- Slack Incoming Webhook and after-worklog behavior: Task 9.
- Running-app reminder and final confirmation: Task 10.
- Explicit AI consent: Task 11.
- Self-contained deployment, recovery, and verification: Task 12.
