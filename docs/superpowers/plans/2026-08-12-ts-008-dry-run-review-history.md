# TS-008 Dry Run Review and History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a safe WPF Dry Run review and delivery-history display while keeping production delivery unavailable.

**Architecture:** `ReviewViewModel` receives a read-only local-plan projection and produces an in-memory Dry Run result; it never receives the integration factory or post coordinator. `HistoryViewModel` queries `IDeliveryAttemptRepository` only through a read-only listing method and maps safe attempt fields to UI rows. WPF views bind to those view models; confirmation is UI state, not an integration command.

**Tech Stack:** .NET 10, C# 14, WPF, MVVM, existing Core repositories and xUnit.

## Global Constraints

- All implementation commits begin with `TS-008`.
- Do not call Toggl, Jira, Tempo, Slack, `IIntegrationClientFactory`, or `IPostAllCoordinator`.
- `PostAllCommand.CanExecute(null)` remains `false`.
- Dry Run makes zero external and zero persistence writes.
- Display/persist no credentials, authorization headers, webhook URLs, request/response bodies, or raw exceptions.
- Do not change SQLite schema or add dependencies.

---

### Task 1: Implement safe review and history presentation

**Files:**

- Create: `src/GDK.TimeSync.Desktop/ViewModels/HistoryViewModel.cs`
- Create: `src/GDK.TimeSync.Desktop/ViewModels/DeliveryHistoryItemViewModel.cs`
- Modify: `src/GDK.TimeSync.Core/DeliveryAttempt.cs`
- Modify: `src/GDK.TimeSync.Persistence/SqliteDeliveryAttemptRepository.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/ReviewViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/ShellViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/App.xaml.cs`
- Modify: `src/GDK.TimeSync.Desktop/Views/ReviewView.xaml`
- Modify: `src/GDK.TimeSync.Desktop/Views/HistoryView.xaml`
- Modify: `src/GDK.TimeSync.Desktop/MainWindow.xaml`
- Test: `tests/GDK.TimeSync.Tests/ReviewViewModelTests.cs`
- Test: `tests/GDK.TimeSync.Tests/HistoryViewModelTests.cs`

**Interfaces:**

- Add `Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default)` to `IDeliveryAttemptRepository`; implementation returns rows ordered by safe stable item ID and does not write.
- `ReviewViewModel` consumes a `Func<DailyPlan>` or focused read-only plan provider and exposes `DryRunCommand`, `ConfirmReviewCommand`, `CanPostAll`, `DryRunSummary`, `DryRunBlockers`, and `IsConfirmationVisible`.
- `HistoryViewModel` consumes `IDeliveryAttemptRepository`, exposes `ObservableCollection<DeliveryHistoryItemViewModel> Items`, `LoadAsync`, and safe `LoadError` text.

- [ ] **Step 1: Write failing focused tests.**

```csharp
[Fact]
public void DryRun_ForAValidLocalPlan_ReportsSequenceWithoutUsingDelivery()
{
    var review = new ReviewViewModel(() => plan);

    review.DryRunCommand.Execute(null);

    Assert.Contains("Toggl", review.DryRunSummary);
    Assert.Empty(review.DryRunBlockers);
    Assert.False(review.PostAllCommand.CanExecute(null));
}

[Fact]
public async Task LoadAsync_MapsReconciliationStatusWithoutExposingRawFailure()
{
    var history = new HistoryViewModel(repositoryWithReconciliationAttempt);

    await history.LoadAsync();

    var item = Assert.Single(history.Items);
    Assert.Equal("Reconciliation required", item.StatusText);
    Assert.DoesNotContain("token", item.FailureText ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run focused tests and confirm RED.**

Run:

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter "FullyQualifiedName~ReviewViewModelTests|FullyQualifiedName~HistoryViewModelTests"
```

Expected: the new Dry Run/history APIs do not exist.

- [ ] **Step 3: Implement the minimal read-only contracts and view models.**

```csharp
public Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default);

public bool CanPostAll => false;
public RelayCommand DryRunCommand { get; }
public RelayCommand ConfirmReviewCommand { get; }
```

The Dry Run validates only local `DailyPlan` fields: non-empty Jira key, positive duration, and start/end consistency when both times exist. It sets a safe summary and blockers in memory. It does not call any client/repository method. `ConfirmReviewCommand` only toggles preview confirmation state; `PostAllCommand` remains disabled and inert.

`HistoryViewModel.LoadAsync` invokes only `ListAsync`, maps IDs/status/failure-code names to display-safe strings, and shows `"Could not load delivery history."` on a repository error.

- [ ] **Step 4: Bind the WPF views and register shared view models.**

Update the shell so `NavigationPage.History` uses the injected `HistoryViewModel`. Use `TodayViewModel` to supply the current local plan through a read-only conversion method; never flush or save during Dry Run. Add a `Loaded`/navigation-safe async initialization path for history that does not block window construction. Review view contains Dry Run and preview-confirm controls, plus disabled Post all. History view shows an empty state or a safe list.

- [ ] **Step 5: Run GREEN verification.**

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter "FullyQualifiedName~ReviewViewModelTests|FullyQualifiedName~HistoryViewModelTests"
dotnet build GDK.TimeSync.slnx -c Release --no-restore
git diff --check
dotnet test GDK.TimeSync.slnx -c Release --no-restore
```

Document the known environment-only Credential Manager test separately if it still fails; do not weaken it.

- [ ] **Step 6: Commit.**

```powershell
git add src/GDK.TimeSync.Core src/GDK.TimeSync.Persistence src/GDK.TimeSync.Desktop tests/GDK.TimeSync.Tests
git commit -m "TS-008 feat: add dry-run review and history"
```

## Plan self-review

- Spec coverage: review summary/blockers, non-writing Dry Run, confirmation preview, permanently disabled production post, and safe history mapping each have a concrete implementation/test step.
- Scope: the only repository addition is a read-only list query; no schema or integration-client changes are permitted.
- Type consistency: Core exposes `ListAsync`; Desktop consumes it through `HistoryViewModel`; Review receives only a `DailyPlan` provider and cannot reach integration services.
