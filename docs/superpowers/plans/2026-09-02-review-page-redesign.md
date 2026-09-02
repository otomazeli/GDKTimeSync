# End-of-day Review Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the End-of-day review from a stack of swapping single-task panels into one worklist grid where a whole day is selected and posted behind a single confirmation, with per-destination delivery state and failure reasons visible on each row.

**Architecture:** The layout is a symptom of the view model: `ReviewViewModel.Items` is a bare `ObservableCollection<PlannedWorkItem>` with one `SelectedTask` and one `LastTaskAttempt`, so the page can only hold one task in mind. A new `ReviewTaskViewModel` per row carries item, delivery attempt, selection and failure text; `ReviewViewModel.Tasks` becomes a collection of those. Guided integration validation moves to Diagnostics unchanged. Delivery still runs one task at a time through the existing `IConfirmedTaskDeliveryService`.

**Tech Stack:** .NET 10, C# 14, WPF, `Microsoft.Extensions.DependencyInjection`, xUnit. No new NuGet packages.

**Spec:** `docs/superpowers/specs/2026-09-02-review-page-redesign-design.md`

## Global Constraints

- No new NuGet dependency.
- Delivery ordering inside a task is unchanged: Toggl → Jira validation → Tempo.
- Every idempotency guarantee is unchanged, including the once-per-day Slack claim.
- Nothing external is written before a second, explicit confirmation click.
- Every user action on Review writes an `IAuditLog` entry — see the spec's "Every action is logged" table for the exact category and message of each. Row selection is deliberately **not** logged.
- No credential, settings value, or Slack URI ever reaches the log.
- `IAuditLog.Write` never throws; a logging failure never changes behaviour.
- Every new constructor parameter is optional and nullable so existing test fixtures need no edits.
- Run `dotnet test GDK.TimeSync.slnx -c Release` and `dotnet build GDK.TimeSync.slnx -c Release` before every commit. Baseline is 402 tests, 0 warnings.

---

## File structure and responsibility map

| Area | Files | Responsibility |
| --- | --- | --- |
| Failure detail | `src/GDK.TimeSync.Core/DeliveryAttempt.cs`, `Core/PostAllCoordinator.cs` | Carry the service's own error text on the attempt, transiently |
| Row | `src/GDK.TimeSync.Desktop/ViewModels/ReviewTaskViewModel.cs` | One task's item, attempt, selection, derived marks |
| Page | `src/GDK.TimeSync.Desktop/ViewModels/ReviewViewModel.cs` | The worklist, batch delivery, audit entries |
| Markup | `src/GDK.TimeSync.Desktop/Views/ReviewView.xaml` | The grid and the single confirmation |
| Relocation | `Views/DiagnosticsView.xaml`, `ViewModels/DiagnosticsViewModel.cs` | Guided validation's new home |

---

## Task 1: Carry the real failure text on the attempt

**Files:**
- Modify: `src/GDK.TimeSync.Core/DeliveryAttempt.cs`
- Modify: `src/GDK.TimeSync.Core/PostAllCoordinator.cs`
- Test: `tests/GDK.TimeSync.Tests/PostAllCoordinatorTests.cs`

**Interfaces:**
- Produces `DeliveryAttempt.FailureDetail` — `public string? FailureDetail { get; init; }`. Tasks 2 and 3 read it.

**Why an `init` property and not a positional parameter:** the persistence layer writes and reads named columns explicitly, so a property it does not know about is simply dropped on save and absent on load — which is exactly the transient behaviour the spec calls for. It also means no existing `new DeliveryAttempt(...)` call site changes.

- [ ] **Step 1: Write the failing test.**

```csharp
[Fact]
public async Task PostAsync_CarriesTheServiceMessageOnATempoFailure()
{
    var item = PlannedWorkItem.Create(new DateOnly(2026, 9, 1), "Work", "CGM-1", "Comment",
        TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
    var coordinator = CreateCoordinator(tempoFailure: new InvalidOperationException("User is invalid"));

    var result = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));

    var attempt = result.Attempts.Single();
    Assert.Equal(DeliveryFailureCode.TempoFailed, attempt.FailureCode);
    Assert.Equal("User is invalid", attempt.FailureDetail);
}

[Fact]
public async Task PostAsync_LeavesFailureDetailNullOnSuccess()
{
    var item = PlannedWorkItem.Create(new DateOnly(2026, 9, 1), "Work", "CGM-1", "Comment",
        TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30));
    var coordinator = CreateCoordinator();

    var result = await coordinator.PostAsync(DailyPlan.Create(item.Day, [item]));

    Assert.Null(result.Attempts.Single().FailureDetail);
}
```

Read `PostAllCoordinatorTests` first and use its existing fake clients and repository; `CreateCoordinator` above stands for whatever that file already does to build one — extend the existing helper with an optional `tempoFailure` rather than adding a second helper.

- [ ] **Step 2: Run the tests and confirm they fail because `FailureDetail` does not exist.**

Run: `dotnet test GDK.TimeSync.slnx --filter "FullyQualifiedName~PostAllCoordinatorTests"`
Expected: build failure — `'DeliveryAttempt' does not contain a definition for 'FailureDetail'`.

- [ ] **Step 3: Add the property.**

In `DeliveryAttempt.cs`, on the record body:

```csharp
    // The service's own explanation of a failure -- "User is invalid", the field Tempo rejected.
    // Deliberately NOT persisted: SqliteDeliveryAttemptRepository reads and writes named columns, so
    // this is dropped on save and absent on load. It answers "why did this just fail?" while the user
    // is still looking at the row; a row rehydrated on a later launch falls back to FailureCode and
    // the audit log, which does keep the detail.
    public string? FailureDetail { get; init; }
```

- [ ] **Step 4: Capture the message in the coordinator.**

Where `PostAllCoordinator` catches a Tempo or Jira failure and builds the failed `DeliveryAttempt`, set `FailureDetail = exception.Message`. Do not add the exception type, stack trace, or inner exception — only the service's message.

- [ ] **Step 5: Run the tests and confirm they pass.**

Run: `dotnet test GDK.TimeSync.slnx --filter "FullyQualifiedName~PostAllCoordinatorTests"`

- [ ] **Step 6: Run the whole suite.**

Run: `dotnet test GDK.TimeSync.slnx -c Release`
Expected: PASS. `SqlitePlanRepositoryTests` in particular must still pass — the new property must not reach SQL.

- [ ] **Step 7: Commit.**

```bash
git add src/GDK.TimeSync.Core tests/GDK.TimeSync.Tests/PostAllCoordinatorTests.cs
git commit -m "feat: carry the service's own failure message on a delivery attempt"
```

---

## Task 2: The row view model

**Files:**
- Create: `src/GDK.TimeSync.Desktop/ViewModels/ReviewTaskViewModel.cs`
- Test: `tests/GDK.TimeSync.Tests/ReviewTaskViewModelTests.cs`

**Interfaces:**
- Consumes `DeliveryAttempt.FailureDetail` (Task 1).
- Produces `ReviewTaskViewModel(PlannedWorkItem item, DeliveryAttempt? attempt = null)` with:
  `PlannedWorkItem Item { get; }`, `Guid Id { get; }`, `string JiraIssueKey { get; }`, `string Description { get; }`, `TimeSpan Duration { get; }`,
  `bool IsSelected { get; set; }`, `bool CanSelect { get; }`,
  `DeliveryMark Toggl { get; }`, `DeliveryMark Jira { get; }`, `DeliveryMark Tempo { get; }`,
  `string? FailureText { get; }`, and `void ApplyAttempt(DeliveryAttempt attempt)`.
- Produces `public enum DeliveryMark { Pending, Delivered, Failed }` in the same file.
- Tasks 3, 4 and 7 consume all of the above.

- [ ] **Step 1: Write the failing tests.**

```csharp
using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Tests;

public sealed class ReviewTaskViewModelTests
{
    private static PlannedWorkItem Item(bool postToToggl = true, long? togglEntryId = null) =>
        PlannedWorkItem.Create(new DateOnly(2026, 9, 1), "Work", "CGM-1", "Comment",
            TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30))
            with { PostToToggl = postToToggl, TogglEntryId = togglEntryId };

    [Fact]
    public void AFreshTaskIsPendingEverywhereAndSelectedByDefault()
    {
        var row = new ReviewTaskViewModel(Item());

        Assert.Equal(DeliveryMark.Pending, row.Toggl);
        Assert.Equal(DeliveryMark.Pending, row.Jira);
        Assert.Equal(DeliveryMark.Pending, row.Tempo);
        Assert.True(row.CanSelect);
        Assert.True(row.IsSelected);
        Assert.Null(row.FailureText);
    }

    [Fact]
    public void ASucceededTaskShowsAllThreeDeliveredAndCannotBeSelected()
    {
        var item = Item();
        var row = new ReviewTaskViewModel(item, new DeliveryAttempt(item.Id, 101, 201,
            DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported));

        Assert.Equal(DeliveryMark.Delivered, row.Toggl);
        Assert.Equal(DeliveryMark.Delivered, row.Jira);
        Assert.Equal(DeliveryMark.Delivered, row.Tempo);
        Assert.False(row.CanSelect);
        Assert.False(row.IsSelected);
    }

    // Delivery is ordered Toggl -> Jira -> Tempo, so a Tempo failure proves Jira validated.
    [Fact]
    public void ATempoFailureMarksTogglAndJiraDeliveredAndTempoFailed()
    {
        var item = Item();
        var row = new ReviewTaskViewModel(item, new DeliveryAttempt(item.Id, 101, null,
            DeliveryAttemptStatus.Failed, DeliveryFailureCode.TempoFailed, SlackDeliveryState.NotSupported)
            { FailureDetail = "User is invalid" });

        Assert.Equal(DeliveryMark.Delivered, row.Toggl);
        Assert.Equal(DeliveryMark.Delivered, row.Jira);
        Assert.Equal(DeliveryMark.Failed, row.Tempo);
        Assert.Equal("Tempo: User is invalid", row.FailureText);
    }

    [Theory]
    [InlineData(DeliveryFailureCode.TogglFailed, "Toggl: Toggl delivery failed.")]
    [InlineData(DeliveryFailureCode.JiraFailed, "Jira: Jira delivery failed.")]
    [InlineData(DeliveryFailureCode.JiraIssueNotFound, "Jira: Jira issue was not found.")]
    [InlineData(DeliveryFailureCode.TempoFailed, "Tempo: Tempo delivery failed.")]
    public void WithoutDetailTheFailureTextFallsBackToTheCodedReason(DeliveryFailureCode code, string expected)
    {
        var item = Item();
        var row = new ReviewTaskViewModel(item, new DeliveryAttempt(item.Id, null, null,
            DeliveryAttemptStatus.Failed, code, SlackDeliveryState.NotSupported));

        Assert.Equal(expected, row.FailureText);
    }

    // The existing per-task guard: an item neither marked for Toggl nor already linked to an entry
    // cannot be delivered at all, so the grid must not offer it.
    [Fact]
    public void ATaskThatCannotBeDeliveredCannotBeSelected()
    {
        var row = new ReviewTaskViewModel(Item(postToToggl: false));

        Assert.False(row.CanSelect);
        Assert.False(row.IsSelected);
    }

    [Fact]
    public void ATaskNotMarkedForTogglButAlreadyLinkedCanStillBeSelected()
    {
        var row = new ReviewTaskViewModel(Item(postToToggl: false, togglEntryId: 555));

        Assert.True(row.CanSelect);
    }

    [Fact]
    public void SettingIsSelectedOnAnUnselectableRowIsIgnored()
    {
        var row = new ReviewTaskViewModel(Item(postToToggl: false));

        row.IsSelected = true;

        Assert.False(row.IsSelected);
    }

    [Fact]
    public void ApplyAttemptUpdatesTheMarksAndDeselectsASucceededRow()
    {
        var item = Item();
        var row = new ReviewTaskViewModel(item);

        row.ApplyAttempt(new DeliveryAttempt(item.Id, 101, 201,
            DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported));

        Assert.Equal(DeliveryMark.Delivered, row.Tempo);
        Assert.False(row.IsSelected);
        Assert.False(row.CanSelect);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail because the type does not exist.**

Run: `dotnet test GDK.TimeSync.slnx --filter "FullyQualifiedName~ReviewTaskViewModelTests"`

- [ ] **Step 3: Implement the row.**

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.ViewModels;

public enum DeliveryMark { Pending, Delivered, Failed }

public sealed class ReviewTaskViewModel : INotifyPropertyChanged
{
    private DeliveryAttempt? attempt;
    private bool isSelected;

    public ReviewTaskViewModel(PlannedWorkItem item, DeliveryAttempt? attempt = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        this.attempt = attempt;
        isSelected = CanSelect;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlannedWorkItem Item { get; }
    public Guid Id => Item.Id;
    public string JiraIssueKey => Item.JiraIssueKey;
    public string Description => Item.Comment;
    public TimeSpan Duration => Item.Duration;

    // Mirrors the guard the old per-task confirmation applied: an item neither marked for Toggl nor
    // already linked to an entry cannot be delivered, and a delivered one must not be posted twice.
    public bool CanSelect =>
        (Item.PostToToggl || Item.TogglEntryId is not null) &&
        attempt?.Status is not DeliveryAttemptStatus.Succeeded;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            var allowed = value && CanSelect;
            if (isSelected == allowed) return;
            isSelected = allowed;
            OnPropertyChanged();
        }
    }

    public DeliveryMark Toggl => Mark(attempt?.TogglEntryId is not null, DeliveryFailureCode.TogglFailed);

    // Reaching Tempo at all proves Jira validated, whether or not Tempo then succeeded.
    public DeliveryMark Jira => Mark(
        attempt?.TempoWorklogId is not null || attempt?.FailureCode == DeliveryFailureCode.TempoFailed,
        DeliveryFailureCode.JiraFailed, DeliveryFailureCode.JiraIssueNotFound);

    public DeliveryMark Tempo => Mark(attempt?.TempoWorklogId is not null, DeliveryFailureCode.TempoFailed);

    public string? FailureText
    {
        get
        {
            if (attempt?.FailureCode is not { } code) return null;
            var where = code switch
            {
                DeliveryFailureCode.TogglFailed => "Toggl",
                DeliveryFailureCode.JiraFailed or DeliveryFailureCode.JiraIssueNotFound => "Jira",
                DeliveryFailureCode.TempoFailed => "Tempo",
                _ => "Delivery"
            };
            return $"{where}: {attempt.FailureDetail ?? CodedReason(code)}";
        }
    }

    public void ApplyAttempt(DeliveryAttempt updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        attempt = updated;
        if (!CanSelect) isSelected = false;
        foreach (var name in new[] { nameof(Toggl), nameof(Jira), nameof(Tempo), nameof(FailureText), nameof(CanSelect), nameof(IsSelected) })
            OnPropertyChanged(name);
    }

    private DeliveryMark Mark(bool delivered, params DeliveryFailureCode[] failedHere)
    {
        if (delivered) return DeliveryMark.Delivered;
        return attempt?.FailureCode is { } code && failedHere.Contains(code) ? DeliveryMark.Failed : DeliveryMark.Pending;
    }

    private static string CodedReason(DeliveryFailureCode code) => code switch
    {
        DeliveryFailureCode.TogglFailed => "Toggl delivery failed.",
        DeliveryFailureCode.JiraFailed => "Jira delivery failed.",
        DeliveryFailureCode.JiraIssueNotFound => "Jira issue was not found.",
        DeliveryFailureCode.TempoFailed => "Tempo delivery failed.",
        DeliveryFailureCode.PersistenceFailed => "Delivery state could not be saved.",
        DeliveryFailureCode.Cancelled => "Delivery was cancelled.",
        DeliveryFailureCode.RemoteChangedAfterDelivery => "The Toggl entry changed after delivery.",
        _ => "Delivery failed."
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
```

- [ ] **Step 4: Run the tests and confirm all pass.**

Run: `dotnet test GDK.TimeSync.slnx --filter "FullyQualifiedName~ReviewTaskViewModelTests"`

- [ ] **Step 5: Run the whole suite, then commit.**

```bash
dotnet test GDK.TimeSync.slnx -c Release
git add src/GDK.TimeSync.Desktop/ViewModels/ReviewTaskViewModel.cs tests/GDK.TimeSync.Tests/ReviewTaskViewModelTests.cs
git commit -m "feat: add a Review row carrying its own delivery state"
```

---

## Task 3: Turn the page into a worklist

**Files:**
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/ReviewViewModel.cs`
- Test: `tests/GDK.TimeSync.Tests/ReviewViewModelTests.cs`

**Interfaces:**
- Consumes `ReviewTaskViewModel` (Task 2).
- Produces `ObservableCollection<ReviewTaskViewModel> Tasks`, `int SelectedCount`, `TimeSpan SelectedDuration`, `string DaySummary`.
- `Items` is removed. `LiveValidation.LoadItems` still needs `IReadOnlyList<PlannedWorkItem>` — pass `Tasks.Select(task => task.Item).ToArray()`.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact]
public async Task RefreshAsync_BuildsOneRowPerTaskWithItsRecordedAttempt()
{
    var delivered = PlannedWorkItem.Create(new DateOnly(2026, 9, 1), "A", "CGM-1", "Delivered", TimeSpan.FromMinutes(30));
    var pending = PlannedWorkItem.Create(new DateOnly(2026, 9, 1), "B", "CGM-2", "Pending", TimeSpan.FromMinutes(45));
    var review = CreateReview(
        items: [delivered, pending],
        attempts: new AttemptRepository(new DeliveryAttempt(delivered.Id, 101, 201,
            DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported)));

    await review.RefreshAsync();

    Assert.Equal(2, review.Tasks.Count);
    var deliveredRow = review.Tasks.Single(task => task.Id == delivered.Id);
    Assert.Equal(DeliveryMark.Delivered, deliveredRow.Tempo);
    Assert.False(deliveredRow.IsSelected);
    var pendingRow = review.Tasks.Single(task => task.Id == pending.Id);
    Assert.Equal(DeliveryMark.Pending, pendingRow.Tempo);
    Assert.True(pendingRow.IsSelected);
}

[Fact]
public async Task SelectedCountAndDurationFollowTheTicks()
{
    var first = PlannedWorkItem.Create(new DateOnly(2026, 9, 1), "A", "CGM-1", "One", TimeSpan.FromMinutes(30));
    var second = PlannedWorkItem.Create(new DateOnly(2026, 9, 1), "B", "CGM-2", "Two", TimeSpan.FromMinutes(45));
    var review = CreateReview(items: [first, second]);
    await review.RefreshAsync();

    Assert.Equal(2, review.SelectedCount);
    Assert.Equal(TimeSpan.FromMinutes(75), review.SelectedDuration);

    review.Tasks[0].IsSelected = false;

    Assert.Equal(1, review.SelectedCount);
    Assert.Equal(TimeSpan.FromMinutes(45), review.SelectedDuration);
}
```

`CreateReview` and `AttemptRepository` stand for the helpers already in `ReviewViewModelTests` — read the file and extend them rather than adding parallel fakes.

- [ ] **Step 2: Run the tests and confirm they fail.**

Run: `dotnet test GDK.TimeSync.slnx --filter "FullyQualifiedName~ReviewViewModelTests"`

- [ ] **Step 3: Replace `Items` with `Tasks` and load attempts in `RefreshAsync`.**

```csharp
    public ObservableCollection<ReviewTaskViewModel> Tasks { get; } = [];

    public int SelectedCount => Tasks.Count(task => task.IsSelected);
    public TimeSpan SelectedDuration => Tasks.Where(task => task.IsSelected).Aggregate(TimeSpan.Zero, (total, task) => total + task.Duration);
    public string DaySummary => $"{Tasks.Count} task(s) · {SelectedDuration:h\\:mm} selected";

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (LiveValidation.IsInFlight) return;

        foreach (var existing in Tasks) existing.PropertyChanged -= OnTaskChanged;
        Tasks.Clear();

        var plan = planProvider?.GetSnapshot();
        PlanDate = plan?.Date;
        if (plan is null) { NotifySelectionChanged(); return; }

        var recorded = new Dictionary<Guid, DeliveryAttempt>();
        if (attempts is not null)
        {
            try
            {
                foreach (var attempt in await attempts.ListAsync(cancellationToken))
                    recorded[attempt.PlannedWorkItemId] = attempt;
            }
            catch
            {
                // A missing delivery history must not stop the day being reviewed; rows simply show
                // as pending, which is what they were before this page knew about attempts at all.
            }
        }

        foreach (var item in plan.Items)
        {
            var row = new ReviewTaskViewModel(item, recorded.GetValueOrDefault(item.Id));
            row.PropertyChanged += OnTaskChanged;
            Tasks.Add(row);
        }

        LiveValidation.LoadItems(Tasks.Select(task => task.Item).ToArray());
        NotifySelectionChanged();
    }

    private void OnTaskChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReviewTaskViewModel.IsSelected)) NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedDuration));
        OnPropertyChanged(nameof(DaySummary));
        // Task 4 adds `PostSelectedCommand.NotifyCanExecuteChanged();` here once that command exists.
        // Do NOT add it in this task -- the command is not declared yet and the file will not compile.
    }
```

`RefreshAsync` becomes `async` — update its callers (`RefreshCommand`, `ShellViewModel.NavigateAsync`) accordingly. Everywhere else that read `Items`, read `Tasks.Select(task => task.Item)`.

- [ ] **Step 4: Run the tests, then the whole suite.**

Run: `dotnet test GDK.TimeSync.slnx -c Release`

- [ ] **Step 5: Commit.**

```bash
git add src/GDK.TimeSync.Desktop/ViewModels/ReviewViewModel.cs tests/GDK.TimeSync.Tests/ReviewViewModelTests.cs
git commit -m "feat: make Review a worklist of rows carrying delivery state"
```

---

## Task 4: Batch delivery behind one confirmation

**Files:**
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/ReviewViewModel.cs`
- Test: `tests/GDK.TimeSync.Tests/ReviewViewModelTests.cs`

**Interfaces:**
- Produces `RelayCommand PostSelectedCommand`, `RelayCommand ConfirmPostSelectedCommand`, `RelayCommand CancelPostSelectedCommand`, `RelayCommand CancelBatchCommand`, `bool IsBatchConfirmationVisible`, `string BatchConfirmationSummary`, `bool IsBatchInFlight`, `string? BatchStatus`.
- The old `OpenTaskConfirmationCommand`, `ConfirmTaskCommand`, `CancelTaskConfirmationCommand`, `SelectedTask`, `LastTaskAttempt`, `IsTaskConfirmationVisible`, `CanConfirmTask`, `TaskDeliveryStatus` are removed. Delete their tests too — they describe a workflow that no longer exists.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact]
public async Task PostSelected_WritesNothingUntilTheSecondConfirmation()
{
    var review = CreateReview(items: [Task30Minutes("CGM-1")], delivery: out var delivery);
    await review.RefreshAsync();

    review.PostSelectedCommand.Execute(null);

    Assert.True(review.IsBatchConfirmationVisible);
    Assert.Equal(0, delivery.Calls);
    Assert.Contains("1 task", review.BatchConfirmationSummary, StringComparison.Ordinal);

    await review.ConfirmPostSelectedAsync();

    Assert.Equal(1, delivery.Calls);
    Assert.False(review.IsBatchConfirmationVisible);
}

[Fact]
public async Task PostSelected_DeliversOnlyTickedRowsInOrder()
{
    var first = Task30Minutes("CGM-1");
    var second = Task30Minutes("CGM-2");
    var third = Task30Minutes("CGM-3");
    var review = CreateReview(items: [first, second, third], delivery: out var delivery);
    await review.RefreshAsync();
    review.Tasks.Single(task => task.JiraIssueKey == "CGM-2").IsSelected = false;

    review.PostSelectedCommand.Execute(null);
    await review.ConfirmPostSelectedAsync();

    Assert.Equal([first.Id, third.Id], delivery.DeliveredIds);
}

// One bad Jira key must not strand the rest of the day.
[Fact]
public async Task PostSelected_ContinuesAfterAFailureAndReportsBothCounts()
{
    var failing = Task30Minutes("CGM-1");
    var succeeding = Task30Minutes("CGM-2");
    var review = CreateReview(items: [failing, succeeding], delivery: out var delivery);
    delivery.FailFor(failing.Id, DeliveryFailureCode.TempoFailed, "User is invalid");
    await review.RefreshAsync();

    review.PostSelectedCommand.Execute(null);
    await review.ConfirmPostSelectedAsync();

    Assert.Equal(2, delivery.Calls);
    Assert.Equal(DeliveryMark.Failed, review.Tasks.Single(task => task.Id == failing.Id).Tempo);
    Assert.Equal("Tempo: User is invalid", review.Tasks.Single(task => task.Id == failing.Id).FailureText);
    Assert.Equal(DeliveryMark.Delivered, review.Tasks.Single(task => task.Id == succeeding.Id).Tempo);
    Assert.Contains("1 succeeded", review.BatchStatus!, StringComparison.Ordinal);
    Assert.Contains("1 failed", review.BatchStatus!, StringComparison.Ordinal);
}

[Fact]
public async Task CancellingTheConfirmationDeliversNothing()
{
    var review = CreateReview(items: [Task30Minutes("CGM-1")], delivery: out var delivery);
    await review.RefreshAsync();

    review.PostSelectedCommand.Execute(null);
    review.CancelPostSelectedCommand.Execute(null);

    Assert.False(review.IsBatchConfirmationVisible);
    Assert.Equal(0, delivery.Calls);
}

// Cancel stops before the NEXT task; it never interrupts one already in flight.
[Fact]
public async Task CancellingMidRunStopsBeforeTheNextTask()
{
    var first = Task30Minutes("CGM-1");
    var second = Task30Minutes("CGM-2");
    var review = CreateReview(items: [first, second], delivery: out var delivery);
    await review.RefreshAsync();
    delivery.OnDelivered = _ => review.CancelBatchCommand.Execute(null);

    review.PostSelectedCommand.Execute(null);
    await review.ConfirmPostSelectedAsync();

    Assert.Equal(1, delivery.Calls);
    Assert.Equal([first.Id], delivery.DeliveredIds);
}

[Fact]
public async Task PostSelectedIsUnavailableWithNothingTicked()
{
    var review = CreateReview(items: [Task30Minutes("CGM-1")]);
    await review.RefreshAsync();

    review.Tasks[0].IsSelected = false;

    Assert.False(review.PostSelectedCommand.CanExecute(null));
}
```

Extend the existing delivery fake in `ReviewViewModelTests` with `Calls`, `DeliveredIds`, `FailFor(...)` and `OnDelivered` rather than writing a second one. Add this helper to the test class:

```csharp
private static PlannedWorkItem Task30Minutes(string jiraIssueKey) =>
    PlannedWorkItem.Create(new DateOnly(2026, 9, 1), jiraIssueKey, jiraIssueKey, $"Work on {jiraIssueKey}",
        TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30))
        with { PostToToggl = true };
```

Read `PlannedWorkItem.Create`'s signature before using it — parameter order is `(day, name, jiraIssueKey, comment, duration, togglProject, tempoCategory, ...)`, and getting it wrong silently produces items whose key and name are swapped.

- [ ] **Step 2: Run the tests and confirm they fail.**

Run: `dotnet test GDK.TimeSync.slnx --filter "FullyQualifiedName~ReviewViewModelTests"`

- [ ] **Step 3: Implement the batch.**

```csharp
    private CancellationTokenSource? batchCancellation;

    private bool isBatchConfirmationVisible;
    private bool isBatchInFlight;
    private string? batchStatus;

    public bool IsBatchConfirmationVisible
    {
        get => isBatchConfirmationVisible;
        private set
        {
            if (isBatchConfirmationVisible == value) return;
            SetField(ref isBatchConfirmationVisible, value);
            ConfirmPostSelectedCommand.NotifyCanExecuteChanged();
            CancelPostSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsBatchInFlight
    {
        get => isBatchInFlight;
        private set
        {
            if (isBatchInFlight == value) return;
            SetField(ref isBatchInFlight, value);
            PostSelectedCommand.NotifyCanExecuteChanged();
            CancelBatchCommand.NotifyCanExecuteChanged();
        }
    }

    public string? BatchStatus { get => batchStatus; private set => SetField(ref batchStatus, value); }
    public string BatchConfirmationSummary =>
        $"{SelectedCount} task(s) → Toggl, Jira, Tempo · {SelectedDuration:h\\:mm} total";

    private void OpenBatchConfirmation()
    {
        if (SelectedCount == 0) return;
        BatchStatus = null;
        IsBatchConfirmationVisible = true;
    }

    public void CancelPostSelected() => IsBatchConfirmationVisible = false;

    public async Task ConfirmPostSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (!IsBatchConfirmationVisible || deliveryService is null || IsBatchInFlight) return;

        IsBatchConfirmationVisible = false;
        IsBatchInFlight = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        batchCancellation = cancellation;
        var succeeded = 0;
        var failed = 0;
        var chosen = Tasks.Where(task => task.IsSelected).ToArray();
        try
        {
            foreach (var row in chosen)
            {
                // Checked before each task, never during one: a cancel must not tear a delivery in half.
                if (cancellation.IsCancellationRequested) break;
                try
                {
                    var attempt = await deliveryService.DeliverConfirmedAsync(row.Item, cancellation.Token);
                    row.ApplyAttempt(attempt);
                    if (attempt.Status == DeliveryAttemptStatus.Succeeded) succeeded++; else failed++;
                }
                catch
                {
                    failed++;
                }
            }
            BatchStatus = $"{succeeded} succeeded, {failed} failed.";
        }
        finally
        {
            batchCancellation = null;
            IsBatchInFlight = false;
            NotifySelectionChanged();
        }
    }

    public void CancelBatch() => batchCancellation?.Cancel();
```

Wire the four commands in the constructor: `PostSelectedCommand` with `CanExecute` of `SelectedCount > 0 && !IsBatchInFlight`; `ConfirmPostSelectedCommand`; `CancelPostSelectedCommand`; `CancelBatchCommand` with `CanExecute` of `IsBatchInFlight`. Delete the old per-task members and their tests.

- [ ] **Step 4: Run the tests, then the whole suite, then commit.**

```bash
dotnet test GDK.TimeSync.slnx -c Release
git add src/GDK.TimeSync.Desktop/ViewModels/ReviewViewModel.cs tests/GDK.TimeSync.Tests/ReviewViewModelTests.cs
git commit -m "feat: post the selected tasks behind one confirmation"
```

---

## Task 5: Audit every action

**Files:**
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/ReviewViewModel.cs`
- Test: `tests/GDK.TimeSync.Tests/ReviewViewModelTests.cs`

**Interfaces:** No new public surface. `ReviewViewModel` already takes `IAuditLog? auditLog`.

Emit exactly these, using the existing `auditLog?.Write(...)`:

| Where | Level | Category | Message |
| --- | --- | --- | --- |
| End of `RefreshAsync` | `Info` | `Review` | `$"Loaded {PlanDate}: {Tasks.Count} task(s), {n} already delivered"` where `n` counts rows whose `CanSelect` is false because they succeeded |
| `RunDryRun` | `Info`, or `Warning` when `DryRunBlockers.Count > 0` | `Review` | `$"Dry run {PlanDate}: {DryRunSummary}"` |
| `OpenBatchConfirmation` | `Info` | `Review` | `$"Post requested for {SelectedCount} task(s): {keys}, {SelectedDuration:h\:mm}"` — `keys` is the selected Jira keys joined with `", "` |
| `ConfirmPostSelectedAsync` entry | `Info` | `Review` | `$"Post confirmed for {chosen.Length} task(s)"` |
| `CancelPostSelected` | `Info` | `Review` | `$"Post cancelled before delivery ({SelectedCount} task(s))"` |
| Loop break on cancel | `Warning` | `Review` | `$"Post cancelled after {succeeded + failed} of {chosen.Length}"` |
| End of `ConfirmPostSelectedAsync` | `Info`, or `Warning` when `failed > 0` | `Review` | `$"Post finished: {succeeded} succeeded, {failed} failed"` |
| `CancelSlackConfirmation` | `Info` | `Slack` | `$"Slack send cancelled for {PlanDate}"` |

Per-task `Delivery` entries already come from `ConfirmedTaskDeliveryService` and are not duplicated here. Row selection is **not** logged.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact]
public async Task AFullPostCycleIsRecordedInOrder()
{
    var log = new RecordingAuditLog();
    var review = CreateReview(items: [Task30Minutes("CGM-1")], auditLog: log);
    await review.RefreshAsync();

    review.PostSelectedCommand.Execute(null);
    await review.ConfirmPostSelectedAsync();

    var review_entries = log.Entries.Where(entry => entry.Category == "Review").Select(entry => entry.Message).ToArray();
    Assert.Contains(review_entries, message => message.StartsWith("Loaded", StringComparison.Ordinal));
    Assert.Contains(review_entries, message => message.StartsWith("Post requested for 1 task(s): CGM-1", StringComparison.Ordinal));
    Assert.Contains(review_entries, message => message.StartsWith("Post confirmed for 1 task(s)", StringComparison.Ordinal));
    Assert.Contains(review_entries, message => message.StartsWith("Post finished: 1 succeeded, 0 failed", StringComparison.Ordinal));
    Assert.True(Array.IndexOf(review_entries, review_entries.First(m => m.StartsWith("Post requested", StringComparison.Ordinal)))
             < Array.IndexOf(review_entries, review_entries.First(m => m.StartsWith("Post confirmed", StringComparison.Ordinal))));
}

[Fact]
public async Task CancellingTheConfirmationIsRecordedAndDeliversNothing()
{
    var log = new RecordingAuditLog();
    var review = CreateReview(items: [Task30Minutes("CGM-1")], auditLog: log, delivery: out var delivery);
    await review.RefreshAsync();

    review.PostSelectedCommand.Execute(null);
    review.CancelPostSelectedCommand.Execute(null);

    Assert.Contains(log.Entries, entry => entry.Category == "Review" && entry.Message.StartsWith("Post cancelled before delivery", StringComparison.Ordinal));
    Assert.DoesNotContain(log.Entries, entry => entry.Category == "Delivery");
    Assert.Equal(0, delivery.Calls);
}

[Fact]
public async Task NoAuditEntryCarriesASettingsValueOrSecret()
{
    var log = new RecordingAuditLog();
    var review = CreateReview(items: [Task30Minutes("CGM-1")], auditLog: log,
        settings: new UserSettings { JiraBaseUrl = "https://jira.example.test", JiraUser = "secret.user@example.test" });
    await review.RefreshAsync();
    review.PostSelectedCommand.Execute(null);
    await review.ConfirmPostSelectedAsync();

    Assert.All(log.Entries, entry =>
    {
        Assert.DoesNotContain("secret.user@example.test", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("jira.example.test", entry.Message, StringComparison.Ordinal);
    });
}
```

Add `RecordingAuditLog` (an `IAuditLog` collecting `(Level, Category, Message)`) as a `private sealed` fake in this test class.

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Add the `auditLog?.Write(...)` calls per the table above.**
- [ ] **Step 4: Run the tests, then the whole suite, then commit.**

```bash
dotnet test GDK.TimeSync.slnx -c Release
git add src/GDK.TimeSync.Desktop/ViewModels/ReviewViewModel.cs tests/GDK.TimeSync.Tests/ReviewViewModelTests.cs
git commit -m "feat: record every Review action in the audit log"
```

---

## Task 6: Move guided validation to Diagnostics

**Files:**
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/DiagnosticsViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/Views/DiagnosticsView.xaml`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/ReviewViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/App.xaml.cs`
- Test: `tests/GDK.TimeSync.Tests/LiveValidationViewModelTests.cs`

**Interfaces:**
- `DiagnosticsViewModel` gains `LiveValidationViewModel LiveValidation { get; }`, constructed from the same `ILocalPlanSnapshotProvider`, `IIntegrationDiagnosticsService` and `ILiveIntegrationValidationService` `ReviewViewModel` used.
- `ReviewViewModel.LiveValidation` is removed, along with its `diagnosticsService` and `validationService` constructor parameters.

- [ ] **Step 1: Point the markup-path tests at the new view.**

`LiveValidationViewModelTests` contains tests that load `ReviewView.xaml` by path and assert bindings (`IsTogglConfirmationVisible`, `SelectedItem.TogglProject`, `CancelOperationCommand`, and others). Change only the filename in those paths to `DiagnosticsView.xaml`. Every behavioural test in that file stays exactly as it is.

- [ ] **Step 2: Run them and confirm the markup tests fail because the bindings are not in `DiagnosticsView.xaml` yet.**

Run: `dotnet test GDK.TimeSync.slnx --filter "FullyQualifiedName~LiveValidationViewModelTests"`

- [ ] **Step 3: Move the markup.** Cut the guided-validation block out of `ReviewView.xaml` and paste it into `DiagnosticsView.xaml` below the log list, unchanged except for being wrapped in `<StackPanel DataContext="{Binding LiveValidation}">` so the existing bindings resolve. Keep its heading text.

- [ ] **Step 4: Move the view model.** Add `LiveValidation` to `DiagnosticsViewModel`, remove it from `ReviewViewModel`, and update `App.ConfigureServices` so the two services are passed to `DiagnosticsViewModel` instead. `ReviewViewModel.RefreshAsync` no longer calls `LiveValidation.LoadItems`; `DiagnosticsViewModel.RefreshAsync` does, from the plan provider.

- [ ] **Step 5: Run the whole suite and a Release build, then commit.**

```bash
dotnet test GDK.TimeSync.slnx -c Release && dotnet build GDK.TimeSync.slnx -c Release
git add src/GDK.TimeSync.Desktop tests/GDK.TimeSync.Tests/LiveValidationViewModelTests.cs
git commit -m "refactor: move guided integration validation to Diagnostics"
```

---

## Task 7: The new Review markup

**Files:**
- Modify: `src/GDK.TimeSync.Desktop/Views/ReviewView.xaml`
- Test: `tests/GDK.TimeSync.Tests/ReviewViewModelTests.cs`

- [ ] **Step 1: Write the failing markup test.**

```csharp
[Fact]
public void Review_view_is_a_grid_with_one_batch_confirmation_and_no_guided_validation()
{
    var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GDK.TimeSync.Desktop", "Views", "ReviewView.xaml"));
    var markup = File.ReadAllText(path);
    var elements = XDocument.Load(path).Descendants().ToArray();

    Assert.Contains(elements, element => element.Name.LocalName == "DataGrid");
    Assert.Contains("{Binding Tasks}", markup, StringComparison.Ordinal);
    Assert.Contains("PostSelectedCommand", markup, StringComparison.Ordinal);
    Assert.Contains("ConfirmPostSelectedCommand", markup, StringComparison.Ordinal);
    Assert.Contains("CancelBatchCommand", markup, StringComparison.Ordinal);
    Assert.Contains("BatchConfirmationSummary", markup, StringComparison.Ordinal);
    Assert.Contains("FailureText", markup, StringComparison.Ordinal);

    // The guided-validation block moved to Diagnostics; none of its bindings may remain here.
    Assert.DoesNotContain("IsTogglConfirmationVisible", markup, StringComparison.Ordinal);
    Assert.DoesNotContain("LiveValidation", markup, StringComparison.Ordinal);

    // Exactly one confirmation panel, where there used to be five.
    Assert.Single(markup.Split("IsBatchConfirmationVisible").Skip(1));
}
```

- [ ] **Step 2: Run it and confirm it fails.**

- [ ] **Step 3: Rewrite `ReviewView.xaml`.**

```xml
<UserControl x:Class="GDK.TimeSync.Desktop.Views.ReviewView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <UserControl.Resources>
        <!-- Same visual language as the connection dots in MainWindow: grey pending, green
             delivered, red failed. Reused deliberately rather than inventing a second vocabulary. -->
        <Style x:Key="MarkDot" TargetType="Ellipse">
            <Setter Property="Width" Value="9" />
            <Setter Property="Height" Value="9" />
            <Setter Property="Margin" Value="0,0,5,0" />
            <Setter Property="Fill" Value="#FF9CA3AF" />
        </Style>
    </UserControl.Resources>
    <DockPanel>
        <StackPanel DockPanel.Dock="Top">
            <TextBlock Text="End-of-day review" FontSize="24" FontWeight="SemiBold" />
            <TextBlock Margin="0,4,0,0" Foreground="DimGray" Text="{Binding PlanDate, StringFormat=Reviewing: {0:D}}" />
            <TextBlock Margin="0,2,0,12" Foreground="DimGray" FontSize="11" Text="{Binding DaySummary}" />
        </StackPanel>

        <StackPanel DockPanel.Dock="Bottom">
            <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                <Button Width="170" Command="{Binding PostSelectedCommand}" Content="{Binding SelectedCount, StringFormat=Post selected ({0})}" />
                <Button Width="100" Margin="8,0,0,0" Command="{Binding DryRunCommand}" Content="Dry Run" />
                <Button Width="100" Margin="8,0,0,0" Command="{Binding RefreshCommand}" Content="Refresh" />
                <Button Width="120" Margin="8,0,0,0" Command="{Binding CancelBatchCommand}" Content="Cancel run">
                    <Button.Style>
                        <Style TargetType="Button">
                            <Setter Property="Visibility" Value="Collapsed" />
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding IsBatchInFlight}" Value="True"><Setter Property="Visibility" Value="Visible" /></DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </Button.Style>
                </Button>
            </StackPanel>

            <Border Margin="0,12,0,0" Padding="12" Background="#FFFFF7ED" BorderBrush="#FFFDBA74" BorderThickness="1">
                <Border.Style>
                    <Style TargetType="Border">
                        <Setter Property="Visibility" Value="Collapsed" />
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding IsBatchConfirmationVisible}" Value="True"><Setter Property="Visibility" Value="Visible" /></DataTrigger>
                        </Style.Triggers>
                    </Style>
                </Border.Style>
                <StackPanel>
                    <TextBlock Text="Confirm delivery" FontWeight="SemiBold" />
                    <TextBlock Margin="0,4,0,0" Text="{Binding BatchConfirmationSummary}" TextWrapping="Wrap" />
                    <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
                        <Button Command="{Binding ConfirmPostSelectedCommand}" Content="{Binding SelectedCount, StringFormat=Post {0} task(s)}" />
                        <Button Margin="8,0,0,0" Command="{Binding CancelPostSelectedCommand}" Content="Cancel" />
                    </StackPanel>
                </StackPanel>
            </Border>

            <TextBlock Margin="0,10,0,0" Text="{Binding BatchStatus}" Foreground="DimGray" TextWrapping="Wrap" />
            <TextBlock Margin="0,4,0,0" Text="{Binding DryRunSummary}" Foreground="DimGray" TextWrapping="Wrap" />

            <Expander Margin="0,16,0,0" Header="Daily Slack update">
                <!-- Move the existing Slack markup in here verbatim: compose button, blockers list,
                     preview box, send / copy / cancel. Its behaviour is out of scope for this task. -->
            </Expander>
        </StackPanel>

        <DataGrid ItemsSource="{Binding Tasks}" AutoGenerateColumns="False" CanUserAddRows="False"
                  HeadersVisibility="Column" GridLinesVisibility="Horizontal" RowDetailsVisibilityMode="Visible">
            <DataGrid.Columns>
                <DataGridCheckBoxColumn Width="34" Binding="{Binding IsSelected, UpdateSourceTrigger=PropertyChanged}">
                    <DataGridCheckBoxColumn.ElementStyle>
                        <Style TargetType="CheckBox">
                            <Setter Property="IsEnabled" Value="{Binding CanSelect}" />
                            <Setter Property="HorizontalAlignment" Value="Center" />
                        </Style>
                    </DataGridCheckBoxColumn.ElementStyle>
                </DataGridCheckBoxColumn>
                <DataGridTextColumn Header="Jira key" Width="140" IsReadOnly="True" Binding="{Binding JiraIssueKey}" />
                <DataGridTextColumn Header="Description" Width="*" IsReadOnly="True" Binding="{Binding Description}" />
                <DataGridTextColumn Header="Duration" Width="90" IsReadOnly="True" Binding="{Binding Duration}" />
                <DataGridTemplateColumn Header="Toggl · Jira · Tempo" Width="150">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                                <Ellipse>
                                    <Ellipse.Style>
                                        <Style TargetType="Ellipse" BasedOn="{StaticResource MarkDot}">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding Toggl}" Value="Delivered"><Setter Property="Fill" Value="#FF22C55E" /></DataTrigger>
                                                <DataTrigger Binding="{Binding Toggl}" Value="Failed"><Setter Property="Fill" Value="#FFEF4444" /></DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Ellipse.Style>
                                </Ellipse>
                                <Ellipse>
                                    <Ellipse.Style>
                                        <Style TargetType="Ellipse" BasedOn="{StaticResource MarkDot}">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding Jira}" Value="Delivered"><Setter Property="Fill" Value="#FF22C55E" /></DataTrigger>
                                                <DataTrigger Binding="{Binding Jira}" Value="Failed"><Setter Property="Fill" Value="#FFEF4444" /></DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Ellipse.Style>
                                </Ellipse>
                                <Ellipse>
                                    <Ellipse.Style>
                                        <Style TargetType="Ellipse" BasedOn="{StaticResource MarkDot}">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding Tempo}" Value="Delivered"><Setter Property="Fill" Value="#FF22C55E" /></DataTrigger>
                                                <DataTrigger Binding="{Binding Tempo}" Value="Failed"><Setter Property="Fill" Value="#FFEF4444" /></DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Ellipse.Style>
                                </Ellipse>
                            </StackPanel>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
            </DataGrid.Columns>
            <DataGrid.RowDetailsTemplate>
                <DataTemplate>
                    <TextBlock Margin="38,0,0,4" Text="{Binding FailureText}" Foreground="Firebrick" FontSize="11" TextWrapping="Wrap">
                        <TextBlock.Style>
                            <Style TargetType="TextBlock">
                                <Setter Property="Visibility" Value="Visible" />
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding FailureText}" Value="{x:Null}"><Setter Property="Visibility" Value="Collapsed" /></DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </TextBlock.Style>
                    </TextBlock>
                </DataTemplate>
            </DataGrid.RowDetailsTemplate>
        </DataGrid>
    </DockPanel>
</UserControl>
```

The `<Expander>` body is the one part left to fill in: move the existing Slack markup across verbatim rather than rewriting it.

- [ ] **Step 4: Run the whole suite and a Release build, then commit.**

```bash
dotnet test GDK.TimeSync.slnx -c Release && dotnet build GDK.TimeSync.slnx -c Release
git add src/GDK.TimeSync.Desktop/Views/ReviewView.xaml tests/GDK.TimeSync.Tests/ReviewViewModelTests.cs
git commit -m "feat: rebuild the Review page as a single worklist grid"
```

---

## Task 8: Documentation and manual verification

**Files:**
- Create: `docs/tasks/TS-041-review-page-redesign.md`
- Modify: `docs/user-guide.md`
- Modify: `docs/operations/recovery-and-reconciliation.md`

- [ ] **Step 1: Write `docs/tasks/TS-041-review-page-redesign.md`** in the house style of the other task docs (Status / Objective / Root cause / Scope / Tests), recording issue #2, that the cause was the single-task view model rather than the layout, the batch-with-one-confirmation decision, the move of guided validation, and the session-only limit on failure detail.

- [ ] **Step 2: Rewrite the "daily workflow" section of `docs/user-guide.md`** for the new flow: tick the tasks, `Post selected`, confirm once. Update the sidebar list, which currently says Review holds "guided per-integration checks" — it does not any more; Diagnostics does.

- [ ] **Step 3: Update `docs/operations/recovery-and-reconciliation.md`** so its recovery walkthrough points at the guided checks on Diagnostics, not Review, and mentions that a failed row now shows its reason inline until the app restarts.

- [ ] **Step 4: Manual verification.** Publish with `scripts/publish-cgm.ps1 -Configuration Release`, run the published exe, and confirm: the grid lists the day with per-destination marks; ticking changes the `Post selected (N)` count; `Post selected` shows one confirmation and cancelling it writes nothing; the audit log contains the request/confirm/finish entries; guided validation appears on Diagnostics and not on Review.

- [ ] **Step 5: Commit.**

```bash
git add docs
git commit -m "docs: document the Review page redesign"
```

---

## Coverage review

- Root cause (single-task view model) and the row model: Tasks 2, 3.
- Batch delivery, one confirmation, continue-on-failure, cancel-between-tasks: Task 4.
- Per-destination marks and the derivation table: Task 2.
- Inline failure reason and its session-only limit: Tasks 1, 2.
- Every action logged, plus the no-secrets rule: Task 5.
- Guided validation relocated: Task 6.
- The grid, one confirmation, Slack collapsed: Task 7.
- Docs and manual verification: Task 8.
