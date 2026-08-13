# TS-009 Confirmed Delivery and Daily Slack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver each task only after its own confirmation, then send one separately confirmed Slack daily update for the Tempo-succeeded tasks.

**Architecture:** First extend the planning model with a durable `WorkStatus` migration. Next build a typed Slack client, pure composer, and daily-Slack state repository with mocked tests. Finally connect task-specific confirmation and final Slack confirmation to narrow Desktop services that use the existing idempotent coordinator and credential-backed factories only after explicit UI confirmation. No batch approval or automatic sequence exists.

**Tech Stack:** .NET 10, C# 14, WPF, `IHttpClientFactory`, Windows Credential Manager, SQLite with existing `Microsoft.Data.Sqlite`, xUnit.

## Global Constraints

- Every implementation and fix commit begins with `TS-009`.
- Each task must have genuine RED/GREEN test evidence before its commit.
- A task may create external effects only after that specific task’s UI confirmation; no batch, scheduler, tray action, background service, or automatic sequence may post.
- Daily Slack send requires a separate final confirmation; it never invokes task delivery.
- Webhook URLs, Jira/Toggl/Tempo credentials, headers, request bodies, response bodies, raw exceptions, and AI content must never reach JSON, SQLite, views, logs, diagnostics, thrown messages, test output, or Git.
- All HTTP construction uses `IHttpClientFactory`; tests use mocked handlers/fakes and must make no live request.
- Ambiguous/persistence-failed external operations require reconciliation and are never automatically retried.

---

### Task 1: Persist work status on plans and templates

**Files:**

- Create: `src/GDK.TimeSync.Core/WorkStatus.cs`
- Modify: `src/GDK.TimeSync.Core/PlannedWorkItem.cs`
- Modify: `src/GDK.TimeSync.Core/RecurringTaskTemplate.cs`
- Modify: `src/GDK.TimeSync.Persistence/SqliteDatabase.cs`
- Modify: `src/GDK.TimeSync.Persistence/SqliteDailyPlanRepository.cs`
- Modify: `src/GDK.TimeSync.Persistence/SqliteTemplateRepository.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/PlannedWorkItemViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/RecurringTaskTemplateViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/TodayViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/TemplatesViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/Views/TodayView.xaml`
- Modify: `src/GDK.TimeSync.Desktop/Views/TemplatesView.xaml`
- Test: `tests/GDK.TimeSync.Tests/SqlitePlanRepositoryTests.cs`
- Test: `tests/GDK.TimeSync.Tests/TodayViewModelTests.cs`

**Interfaces:**

- Produces `WorkStatus` with values `CodeReview`, `Analyzing`, `Done`, `InProgress`, and `Waiting`.
- `PlannedWorkItem` and `RecurringTaskTemplate` include `WorkStatus Status`; all `Create` factories default it to `WorkStatus.InProgress`.
- Existing SQLite records acquire a non-null `work_status` default representing `InProgress`; migration is safe when the column already exists.

- [ ] **Step 1: Write failing status/migration tests.**

```csharp
[Fact]
public async Task Existing_plan_rows_migrate_to_in_progress_status()
{
    await CreateLegacyPlanDatabaseAsync(databasePath);
    var loaded = await repository.GetAsync(new DateOnly(2026, 8, 13));
    Assert.Equal(WorkStatus.InProgress, Assert.Single(loaded!.Items).Status);
}

[Fact]
public void Template_added_to_today_preserves_its_status()
{
    var template = new RecurringTaskTemplateViewModel(status: WorkStatus.Done);
    today.AddTemplateCommand.Execute(template);
    Assert.Equal(WorkStatus.Done, Assert.Single(today.Items).Status);
}
```

- [ ] **Step 2: Run the focused tests and record RED.**

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter "FullyQualifiedName~SqlitePlanRepositoryTests|FullyQualifiedName~TodayViewModelTests"
```

Expected: `WorkStatus` and its persisted mappings are absent.

- [ ] **Step 3: Implement the model, explicit migration, repository mappings, and editable WPF status selection.**

Use an integer database column and a one-time `PRAGMA table_info` check before `ALTER TABLE`; then update null/legacy values to `InProgress`. Never store status as arbitrary user text. Bind a ComboBox to the enum values and render display text through a dedicated mapping, not enum `ToString()`.

- [ ] **Step 4: Run focused GREEN verification.**

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter "FullyQualifiedName~SqlitePlanRepositoryTests|FullyQualifiedName~TodayViewModelTests"
dotnet build GDK.TimeSync.slnx -c Release --no-restore
git diff --check
```

- [ ] **Step 5: Commit.**

```powershell
git add src/GDK.TimeSync.Core src/GDK.TimeSync.Persistence src/GDK.TimeSync.Desktop tests/GDK.TimeSync.Tests
git commit -m "TS-009 feat: persist task work status"
```

### Task 2: Add safe daily Slack composition and delivery state

**Files:**

- Create: `src/GDK.TimeSync.Slack/GDK.TimeSync.Slack.csproj`
- Create: `src/GDK.TimeSync.Slack/ISlackClient.cs`
- Create: `src/GDK.TimeSync.Slack/SlackClient.cs`
- Create: `src/GDK.TimeSync.Slack/SlackDailyUpdateComposer.cs`
- Create: `src/GDK.TimeSync.Slack/SlackDailyUpdate.cs`
- Create: `src/GDK.TimeSync.Slack/SlackApiException.cs`
- Create: `src/GDK.TimeSync.Core/DailySlackDelivery.cs`
- Create: `src/GDK.TimeSync.Persistence/SqliteDailySlackDeliveryRepository.cs`
- Modify: `src/GDK.TimeSync.Core/DeliveryAttempt.cs`
- Modify: `src/GDK.TimeSync.Persistence/SqliteDatabase.cs`
- Modify: `src/GDK.TimeSync.Persistence/ServiceCollectionExtensions.cs`
- Modify: `GDK.TimeSync.slnx`
- Modify: `tests/GDK.TimeSync.Tests/GDK.TimeSync.Tests.csproj`
- Test: `tests/GDK.TimeSync.Tests/SlackDailyUpdateComposerTests.cs`
- Test: `tests/GDK.TimeSync.Tests/SlackClientTests.cs`
- Test: `tests/GDK.TimeSync.Tests/SqliteDailySlackDeliveryRepositoryTests.cs`

**Interfaces:**

```csharp
public interface ISlackClient : IDisposable
{
    Task PostAsync(SlackDailyUpdate update, CancellationToken cancellationToken = default);
}

public interface IDailySlackDeliveryRepository
{
    Task<DailySlackDelivery?> GetAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<bool> TryClaimAsync(DateOnly date, string contentFingerprint, CancellationToken cancellationToken = default);
    Task SaveAsync(DailySlackDelivery delivery, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1: Write failing composer, client, and repository tests.**

```csharp
[Fact]
public void Compose_UsesProjectIssueDescriptionAndBoldStatus()
{
    var update = composer.Compose(date, [Succeeded("CGM", "CGMFRAVII-2767", "Knowledge transfer", WorkStatus.Done)]);
    Assert.Contains("CGM | CGMFRAVII-2767 Knowledge transfer | *Done*", update.Text);
}

[Fact]
public async Task PostAsync_WebhookFailure_DoesNotExposeWebhook()
{
    var exception = await Assert.ThrowsAsync<SlackApiException>(() => client.PostAsync(update));
    Assert.DoesNotContain("hooks.slack.com", exception.ToString(), StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task TryClaimAsync_RejectsSecondSendForSameDateAndFingerprint()
{
    Assert.True(await repository.TryClaimAsync(date, fingerprint));
    Assert.False(await repository.TryClaimAsync(date, fingerprint));
}
```

- [ ] **Step 2: Run focused tests and record RED.**

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter "FullyQualifiedName~SlackDailyUpdateComposerTests|FullyQualifiedName~SlackClientTests|FullyQualifiedName~SqliteDailySlackDeliveryRepositoryTests"
```

Expected: Slack project/contracts are absent.

- [ ] **Step 3: Implement pure composition, typed webhook post, and idempotent daily state.**

`SlackDailyUpdateComposer` accepts only safe completed-task display data and configured non-secret title/header/extra lines. Its exact task line is `{TogglProject} | {JiraIssueKey} {Description} | *{Status}*`. Compute a SHA-256 fingerprint of message content in memory and persist only the fingerprint/state/failure code. The repository stores no message body or URL. Use a named `HttpClient`; `SlackClient` accepts a configured `HttpClient` from a later factory and sends `{"text": "..."}`. Translate errors to `SlackApiException` without inner exception/raw content. Mark cancellation/ambiguous request completion `ReconciliationRequired`, never a retryable send.

- [ ] **Step 4: Run focused GREEN verification.**

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter "FullyQualifiedName~SlackDailyUpdateComposerTests|FullyQualifiedName~SlackClientTests|FullyQualifiedName~SqliteDailySlackDeliveryRepositoryTests"
dotnet build GDK.TimeSync.slnx -c Release --no-restore
git diff --check
```

- [ ] **Step 5: Commit.**

```powershell
git add GDK.TimeSync.slnx src/GDK.TimeSync.Core src/GDK.TimeSync.Persistence src/GDK.TimeSync.Slack tests/GDK.TimeSync.Tests
git commit -m "TS-009 feat: add daily Slack delivery contracts"
```

### Task 3: Add per-task confirmation and final Slack confirmation

**Files:**

- Create: `src/GDK.TimeSync.Desktop/Services/IConfirmedTaskDeliveryService.cs`
- Create: `src/GDK.TimeSync.Desktop/Services/ConfirmedTaskDeliveryService.cs`
- Create: `src/GDK.TimeSync.Desktop/Services/ISlackClientFactory.cs`
- Create: `src/GDK.TimeSync.Desktop/Services/SlackClientFactory.cs`
- Modify: `src/GDK.TimeSync.Desktop/Services/IntegrationClientFactory.cs`
- Modify: `src/GDK.TimeSync.Desktop/App.xaml.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/ReviewViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/HistoryViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/Views/ReviewView.xaml`
- Modify: `src/GDK.TimeSync.Desktop/Views/HistoryView.xaml`
- Modify: `src/GDK.TimeSync.Desktop/Services/UserSettings.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/SettingsViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/Views/SettingsView.xaml`
- Test: `tests/GDK.TimeSync.Tests/ConfirmedTaskDeliveryServiceTests.cs`
- Test: `tests/GDK.TimeSync.Tests/ReviewViewModelTests.cs`
- Test: `tests/GDK.TimeSync.Tests/SlackClientFactoryTests.cs`

**Interfaces:**

```csharp
public interface IConfirmedTaskDeliveryService
{
    Task<DeliveryAttempt> DeliverConfirmedAsync(PlannedWorkItem item, CancellationToken cancellationToken = default);
}

public interface ISlackClientFactory
{
    Task<ISlackClient> CreateAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1: Write failing confirmation and no-automatic-delivery tests.**

```csharp
[Fact]
public async Task ConfirmedTask_DeliversOnlyTheSelectedItem()
{
    review.OpenTaskConfirmation(first.Id);
    await review.ConfirmTaskAsync();
    Assert.Equal([first.Id], deliveryService.DeliveredItemIds);
}

[Fact]
public async Task SendSlack_RequiresSeparateFinalConfirmation()
{
    await review.ComposeSlackPreviewAsync();
    Assert.Empty(slackClient.PostedUpdates);
    await review.ConfirmSlackAsync();
    Assert.Single(slackClient.PostedUpdates);
}

[Fact]
public async Task NoConfirmation_ProducesNoDeliveryOrSlackCall()
{
    await review.RefreshAsync();
    Assert.Empty(deliveryService.DeliveredItemIds);
    Assert.Empty(slackClient.PostedUpdates);
}
```

- [ ] **Step 2: Run focused tests and record RED.**

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter "FullyQualifiedName~ConfirmedTaskDeliveryServiceTests|FullyQualifiedName~ReviewViewModelTests|FullyQualifiedName~SlackClientFactoryTests"
```

Expected: confirmation-only services and view-model state are absent.

- [ ] **Step 3: Implement narrow factories, confirmed delivery, and WPF confirmation dialogs.**

`ConfirmedTaskDeliveryService` creates Toggl/Jira/Tempo clients only inside `DeliverConfirmedAsync`, invokes the existing coordinator for a one-item plan, disposes all created clients, and returns only safe `DeliveryAttempt` state. It is never called by page load, Dry Run, navigation, History, tray, or scheduler.

`SlackClientFactory` reads `CredentialKeys.SlackWebhook` only inside `CreateAsync`, validates an absolute HTTPS URI, creates the named Slack `HttpClient`, and returns an `ISlackClient` that owns/disposes that client. It uses safe category-only configuration errors.

`ReviewViewModel` contains separate selected-item and Slack-preview confirmation state. Opening/closing dialogs performs no delivery. Confirming a selected task calls only `IConfirmedTaskDeliveryService.DeliverConfirmedAsync(selectedItem)`. It has no batch command. Slack preview filters to Tempo-succeeded items, excludes unsafe states with safe warnings, and its final confirm calls only the Slack path. If a daily Slack record is already sent or reconciliation-required, send stays unavailable. Production UI shows one action per task plus one final Slack confirmation.

- [ ] **Step 4: Run GREEN and safety verification.**

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter "FullyQualifiedName~ConfirmedTaskDeliveryServiceTests|FullyQualifiedName~ReviewViewModelTests|FullyQualifiedName~SlackClientFactoryTests"
dotnet build GDK.TimeSync.slnx -c Release --no-restore
git diff --check
dotnet test GDK.TimeSync.slnx -c Release --no-restore
```

Test with fakes/mocked handlers only. Do not run the live application to post or use entered credentials for verification.

- [ ] **Step 5: Commit.**

```powershell
git add src/GDK.TimeSync.Desktop src/GDK.TimeSync.Slack tests/GDK.TimeSync.Tests
git commit -m "TS-009 feat: add confirmed task and Slack delivery"
```

## Plan self-review

- Spec coverage: individual task confirmation is Task 3; status/migration is Task 1; pure Slack composition/state is Task 2; separate daily Slack confirmation/idempotency is Task 3.
- Safety: Task 1 has no network code, Task 2 is mocked typed client/state only, Task 3 confines external-capable factories to explicit confirmation services.
- Traceability: every implementation and correction commit uses `TS-009`.
- Type consistency: Task 1 provides `WorkStatus`; Task 2 consumes it in the composer and provides Slack contracts/state; Task 3 consumes all contracts for the confirmed UI workflow.
