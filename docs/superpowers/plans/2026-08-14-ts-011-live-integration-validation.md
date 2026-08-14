# TS-011 Guided live integration validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a user-initiated, confirmation-gated workflow that validates real Toggl, Jira, Tempo, and Slack integration steps for one selected planned task.

**Architecture:** Read-only diagnostics and each external write are separate operations behind credential-backed factories. A live-validation service persists safe Toggl/Tempo IDs in the existing delivery-attempt model after each successful write, while a dedicated view model controls step-specific confirmation. Existing daily Slack delivery remains a separate TS-009 confirmation path and is never invoked by validation.

**Tech Stack:** .NET 10, C# 14, WPF MVVM, `IHttpClientFactory`, Windows Credential Manager, SQLite, xUnit, mocked `HttpMessageHandler` tests.

## Global Constraints

- All commits begin with `TS-011`.
- A selected existing planned item supplies every issue key, duration, comment, project, start/end, and worker context. Do not hardcode or synthesize live data.
- Opening the page, selecting an item, navigation, diagnostics preview, and confirmation dialogs perform no write and no credential-value read.
- The user explicitly invokes diagnostics and separately confirms Toggl creation, Jira validation, Tempo creation, and the existing final Slack send.
- A write is never retried, compensated, deleted, or resent automatically. Cancellation after a possible write is persisted as reconciliation-required.
- Credentials are factory-only and must never appear in settings, database records, view-model properties, error text, exceptions, logs, tests, or Git.
- Automated verification uses mocks/fakes only. Do not launch the desktop app or issue a live request during development/testing.

---

## File structure and responsibility map

| Area | Files | Responsibility |
| --- | --- | --- |
| Diagnostics | `IntegrationDiagnosticsService.cs`, `IntegrationDiagnosticResult.cs` | Explicit read-only connection checks and safe categories. |
| Live steps | `ILiveIntegrationValidationService.cs`, `LiveIntegrationValidationService.cs`, `LiveValidationResult.cs` | Gated Toggl/Jira/Tempo actions and durable safe state. |
| Presentation | `LiveValidationViewModel.cs`, `ReviewViewModel.cs`, `ReviewView.xaml`, `App.xaml.cs` | Selection, per-step confirmation, safe recovery text, DI wiring. |
| Tests | `IntegrationDiagnosticsServiceTests.cs`, `LiveIntegrationValidationServiceTests.cs`, `LiveValidationViewModelTests.cs` | Confirmation, order, durable-state, readback, cancellation, and secret-boundary coverage. |

## Task 1: Add explicit read-only integration diagnostics

**Files:**
- Create: `src/GDK.TimeSync.Desktop/Services/IntegrationDiagnosticResult.cs`
- Create: `src/GDK.TimeSync.Desktop/Services/IIntegrationDiagnosticsService.cs`
- Create: `src/GDK.TimeSync.Desktop/Services/IntegrationDiagnosticsService.cs`
- Modify: `src/GDK.TimeSync.Desktop/App.xaml.cs`
- Test: `tests/GDK.TimeSync.Tests/IntegrationDiagnosticsServiceTests.cs`

**Interfaces:**

```csharp
public enum IntegrationDiagnosticTarget { Toggl, Jira, Tempo }
public sealed record IntegrationDiagnosticResult(IntegrationDiagnosticTarget Target, bool IsSuccessful, string SafeMessage);
public interface IIntegrationDiagnosticsService
{
    Task<IReadOnlyList<IntegrationDiagnosticResult>> RunAsync(CancellationToken cancellationToken = default);
}
```

`RunAsync` is invoked only by an explicit user command. It uses existing factory-created clients to call Toggl `GetTimeEntriesAsync` for today only, Jira `GetMyselfAsync`, and Tempo `GetWorkAttributesAsync`. It disposes created clients and returns only fixed category messages.

- [ ] **Step 1: Write failing mocked diagnostics tests.**

```csharp
[Fact]
public async Task RunAsync_uses_each_read_only_diagnostic_and_returns_safe_categories()
{
    var service = new IntegrationDiagnosticsService(factory);

    var results = await service.RunAsync();

    Assert.Equal([IntegrationDiagnosticTarget.Toggl, IntegrationDiagnosticTarget.Jira, IntegrationDiagnosticTarget.Tempo], results.Select(x => x.Target));
    Assert.All(results, result => Assert.DoesNotContain(secretSentinel, result.SafeMessage, StringComparison.Ordinal));
    Assert.Equal(0, factory.WriteCalls);
}
```

- [ ] **Step 2: Run the focused test and confirm the contracts are absent.**

Run: `dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter FullyQualifiedName~IntegrationDiagnosticsServiceTests`

Expected: FAIL with missing diagnostics types.

- [ ] **Step 3: Implement safe read-only diagnostics.**

```csharp
public async Task<IReadOnlyList<IntegrationDiagnosticResult>> RunAsync(CancellationToken cancellationToken = default)
{
    var results = new List<IntegrationDiagnosticResult>();
    using var toggl = await clients.CreateTogglAsync(cancellationToken);
    results.Add(await CheckTogglAsync(toggl, cancellationToken));
    // Create/dispose Jira and Tempo independently; reduce all failures to fixed messages.
    return results;
}
```

Use a `try/finally` per target so one unavailable destination does not prevent checking another. Catch cancellation separately, return `"Cancelled"`, and never put an exception message, URL, response body, or credential into a result.

- [ ] **Step 4: Run focused tests and Release build.**

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter FullyQualifiedName~IntegrationDiagnosticsServiceTests
dotnet build GDK.TimeSync.slnx -c Release --no-restore
```

- [ ] **Step 5: Commit Task 1.**

```powershell
git add src/GDK.TimeSync.Desktop/Services/IntegrationDiagnosticResult.cs src/GDK.TimeSync.Desktop/Services/IIntegrationDiagnosticsService.cs src/GDK.TimeSync.Desktop/Services/IntegrationDiagnosticsService.cs src/GDK.TimeSync.Desktop/App.xaml.cs tests/GDK.TimeSync.Tests/IntegrationDiagnosticsServiceTests.cs
git commit -m "TS-011 feat: add integration diagnostics"
```

## Task 2: Add durable, separately confirmed Toggl/Jira/Tempo validation steps

**Files:**
- Create: `src/GDK.TimeSync.Desktop/Services/ILiveIntegrationValidationService.cs`
- Create: `src/GDK.TimeSync.Desktop/Services/LiveIntegrationValidationService.cs`
- Create: `src/GDK.TimeSync.Desktop/Services/LiveValidationResult.cs`
- Test: `tests/GDK.TimeSync.Tests/LiveIntegrationValidationServiceTests.cs`

**Interfaces:**

```csharp
public enum LiveValidationStep { Toggl, Jira, Tempo }
public sealed record LiveValidationResult(LiveValidationStep Step, DeliveryAttempt Attempt, string SafeMessage);
public interface ILiveIntegrationValidationService
{
    Task<LiveValidationResult> CreateTogglAsync(PlannedWorkItem item, CancellationToken cancellationToken = default);
    Task<LiveValidationResult> ValidateJiraAsync(PlannedWorkItem item, CancellationToken cancellationToken = default);
    Task<LiveValidationResult> CreateAndVerifyTempoAsync(PlannedWorkItem item, CancellationToken cancellationToken = default);
}
```

`CreateTogglAsync` claims the existing delivery attempt, creates only Toggl, and immediately persists its safe ID as `InProgress`. `ValidateJiraAsync` is read-only and returns a safe result; it never claims, writes, or creates a client for another target. `CreateAndVerifyTempoAsync` requires an existing attempt with Toggl ID and internally revalidates the Jira key before Tempo creation. It persists Tempo ID then calls `GetWorklogAsync`; matching worklog ID and duration produce `Succeeded`, otherwise reconciliation-required. Cancellation/failure after a possible write persists reconciliation-required and never retries/deletes.

- [ ] **Step 1: Write failing orchestration tests.**

```csharp
[Fact]
public async Task CreateTogglAsync_creates_only_toggl_and_persists_its_id()
{
    var result = await service.CreateTogglAsync(item);

    Assert.Equal(LiveValidationStep.Toggl, result.Step);
    Assert.Equal(DeliveryAttemptStatus.InProgress, result.Attempt.Status);
    Assert.Equal(123L, result.Attempt.TogglEntryId);
    Assert.Equal(0, clients.JiraCalls + clients.TempoCalls + clients.SlackCalls);
}

[Fact]
public async Task CreateAndVerifyTempoAsync_validates_jira_then_reads_back_tempo()
{
    var result = await service.CreateAndVerifyTempoAsync(item);

    Assert.Equal(DeliveryAttemptStatus.Succeeded, result.Attempt.Status);
    Assert.Equal(["Jira", "TempoCreate", "TempoRead"], calls);
}
```

- [ ] **Step 2: Run the focused test and confirm it fails because the live-validation service is absent.**

Run: `dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter FullyQualifiedName~LiveIntegrationValidationServiceTests`

Expected: FAIL with missing service types.

- [ ] **Step 3: Implement the three independently invoked operations.**

```csharp
public async Task<LiveValidationResult> CreateTogglAsync(PlannedWorkItem item, CancellationToken cancellationToken)
{
    var claim = await attempts.ClaimAsync(item.Id, CancellationToken.None);
    if (!claim.IsAcquired) return ExistingResult(LiveValidationStep.Toggl, claim.Attempt);
    using var toggl = await clients.CreateTogglAsync(cancellationToken);
    var entry = await toggl.CreateTimeEntryAsync(CreateRequest(item), cancellationToken);
    var attempt = claim.Attempt with { TogglEntryId = entry.Id, Status = DeliveryAttemptStatus.InProgress };
    await attempts.SaveAsync(attempt, CancellationToken.None);
    return new(LiveValidationStep.Toggl, attempt, "Toggl entry created.");
}
```

Use `try/catch` around every possible write. Do not call Tempo or Slack from the Toggl/Jira operations. Before Tempo, return a fixed blocker if Toggl is absent or the attempt is terminal. Do not store Jira IDs or response payloads; re-read issue metadata inside the confirmed Tempo operation. Persist `ReconciliationRequired` on ambiguous post-write cancellation/failure.

- [ ] **Step 4: Run focused service tests and full integration-client mock tests.**

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter "FullyQualifiedName~LiveIntegrationValidationServiceTests|FullyQualifiedName~TogglClientTests|FullyQualifiedName~JiraClientTests|FullyQualifiedName~TempoClientTests"
dotnet build GDK.TimeSync.slnx -c Release --no-restore
```

- [ ] **Step 5: Commit Task 2.**

```powershell
git add src/GDK.TimeSync.Desktop/Services/ILiveIntegrationValidationService.cs src/GDK.TimeSync.Desktop/Services/LiveIntegrationValidationService.cs src/GDK.TimeSync.Desktop/Services/LiveValidationResult.cs tests/GDK.TimeSync.Tests/LiveIntegrationValidationServiceTests.cs
git commit -m "TS-011 feat: add confirmed live validation steps"
```

## Task 3: Add Review live-validation presentation and recovery states

**Files:**
- Create: `src/GDK.TimeSync.Desktop/ViewModels/LiveValidationViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/ReviewViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/Views/ReviewView.xaml`
- Modify: `src/GDK.TimeSync.Desktop/App.xaml.cs`
- Test: `tests/GDK.TimeSync.Tests/LiveValidationViewModelTests.cs`

**Interfaces:**
- Consumes `IIntegrationDiagnosticsService`, `ILiveIntegrationValidationService`, and the existing read-only `ILocalPlanSnapshotProvider`.
- Produces commands `RunDiagnosticsCommand`, `OpenTogglConfirmationCommand`, `ConfirmTogglCommand`, `ValidateJiraCommand`, `OpenTempoConfirmationCommand`, `ConfirmTempoCommand`, and cancellation commands for visible dialogs.
- Produces safe `Diagnostics`, `SelectedItem`, `StepStatus`, `RecoveryMessage`, and `IsTogglConfirmationVisible`/`IsTempoConfirmationVisible` state.

- [ ] **Step 1: Write failing view-model tests for confirmation boundaries.**

```csharp
[Fact]
public async Task Opening_toggl_confirmation_does_not_read_credentials_or_create_an_entry()
{
    viewModel.OpenTogglConfirmation(item.Id);

    Assert.True(viewModel.IsTogglConfirmationVisible);
    Assert.Equal(0, fakes.CredentialGetCalls + fakes.TogglCreates + fakes.TempoCreates);
}

[Fact]
public async Task Confirming_tempo_after_toggl_shows_readback_status_and_never_sends_slack()
{
    await viewModel.ConfirmTempoAsync();

    Assert.Equal("Tempo worklog verified.", viewModel.StepStatus);
    Assert.Equal(0, fakes.SlackPosts);
}
```

- [ ] **Step 2: Run focused tests and confirm they fail because the view model and commands are absent.**

Run: `dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter FullyQualifiedName~LiveValidationViewModelTests`

Expected: FAIL with missing view-model types.

- [ ] **Step 3: Implement safe presentation and explicit action routing.**

```csharp
public async Task ConfirmTogglAsync(CancellationToken cancellationToken = default)
{
    if (!IsTogglConfirmationVisible || SelectedItem is null || IsInFlight) return;
    IsInFlight = true;
    IsTogglConfirmationVisible = false;
    try { Apply(await validation.CreateTogglAsync(SelectedItem, cancellationToken)); }
    catch { StepStatus = "Toggl validation unavailable."; }
    finally { IsInFlight = false; }
}
```

Bind each confirmation panel to safe selected-item fields only. Render reconciliation messages from status/failure code only. Add one Review entry point that constructs no client or credential; the existing daily Slack compose/send controls remain separate and are enabled only by their existing Tempo-success logic.

- [ ] **Step 4: Run TS-011 focused tests, mocked-safe full suite, build, and diff check.**

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter "FullyQualifiedName~IntegrationDiagnosticsServiceTests|FullyQualifiedName~LiveIntegrationValidationServiceTests|FullyQualifiedName~LiveValidationViewModelTests|FullyQualifiedName~ReviewViewModelTests"
dotnet build GDK.TimeSync.slnx -c Release --no-restore
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter "FullyQualifiedName!~WindowsCredentialStoreTests"
git diff --check
```

- [ ] **Step 5: Commit Task 3.**

```powershell
git add src/GDK.TimeSync.Desktop/ViewModels/LiveValidationViewModel.cs src/GDK.TimeSync.Desktop/ViewModels/ReviewViewModel.cs src/GDK.TimeSync.Desktop/Views/ReviewView.xaml src/GDK.TimeSync.Desktop/App.xaml.cs tests/GDK.TimeSync.Tests/LiveValidationViewModelTests.cs
git commit -m "TS-011 feat: add guided live validation review"
```

## Plan self-review

- Spec coverage: Task 1 implements explicit read-only diagnostics; Task 2 separates durable Toggl/Jira/Tempo operations with readback and reconciliation; Task 3 exposes only individually confirmed presentation steps and leaves Slack separate.
- Safety coverage: every task prohibits startup/navigation/reminder delivery, credentials in views, automatic retries/deletion, and live calls during tests.
- Scope: no synthetic work, AI feature, installer change, scheduled operation, or automatic reconciliation is included.
- Type consistency: the diagnostics and live-validation interfaces defined in Tasks 1–2 are the only service dependencies introduced in Task 3.
- Placeholder scan: no deferred implementation markers remain.
