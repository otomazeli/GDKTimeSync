# TS-010 Running-app end-of-day reminder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a configurable, in-process end-of-day reminder that opens Review, shows a tray notification, or does both without ever posting work.

**Architecture:** A non-secret `EndOfDayReminderMode` setting controls presentation. An injected-time-provider service detects the configured local time once per local date and emits a local event. `App` maps that event to the existing tray icon and Review navigation; integration, credential, persistence-write, and delivery services remain outside the reminder path.

**Tech Stack:** .NET 10, C# 14, WPF, `TimeProvider`, `PeriodicTimer`, Windows Forms `NotifyIcon`, MVVM, xUnit.

## Global Constraints

- All commits for this work start with `TS-010`.
- The reminder runs only while the WPF application process is already running; do not add Windows Task Scheduler, startup launch, or a background process.
- It must never read a credential, create an integration/Slack client, write delivery state, call HTTP, or post to Toggl, Jira, Tempo, or Slack.
- The existing per-task confirmation and separate final Slack confirmation remain the only paths that can perform delivery.
- `EndOfDayReminderMode.Both` is the default and is stored only as a non-secret `settings.json` value.
- Run Release tests and build before each task handoff. Do not launch the desktop UI or make a live integration call for automated verification.

---

## File structure and responsibility map

| Area | Files | Responsibility |
| --- | --- | --- |
| Non-secret setting | `Desktop/Services/EndOfDayReminderMode.cs`, `UserSettings.cs`, `UserSettingsService.cs` | Durable reminder presentation preference and safe default/normalization. |
| Settings UI | `SettingsViewModel.cs`, `SettingsWindow.xaml`, `SettingsWindow.xaml.cs` | Edit and persist the reminder choice without credentials. |
| Local scheduler | `Desktop/Services/IEndOfDayReminderService.cs`, `EndOfDayReminderService.cs` | Raise one local `ReviewDue` event per date when the configured time is reached. |
| Presentation | `ReviewReminderActions.cs`, `TrayIconService.cs`, `App.xaml.cs` | Select a tray notification and/or Review navigation in response to the event. |
| Tests | `DesktopConfigurationTests.cs`, `EndOfDayReminderServiceTests.cs`, `ShellViewModelTests.cs` | Validate setting persistence, deterministic timing, presentation choice, and no-delivery boundaries. |

## Task 1: Persist and edit reminder presentation mode

**Files:**
- Create: `src/GDK.TimeSync.Desktop/Services/EndOfDayReminderMode.cs`
- Modify: `src/GDK.TimeSync.Desktop/Services/UserSettings.cs`
- Modify: `src/GDK.TimeSync.Desktop/Services/UserSettingsService.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/SettingsViewModel.cs`
- Modify: `src/GDK.TimeSync.Desktop/SettingsWindow.xaml`
- Modify: `src/GDK.TimeSync.Desktop/SettingsWindow.xaml.cs`
- Test: `tests/GDK.TimeSync.Tests/DesktopConfigurationTests.cs`

**Interfaces:**
- Produces `public enum EndOfDayReminderMode { TrayNotificationOnly, OpenReviewOnly, Both }` and `EndOfDayReminderModes.Normalize(EndOfDayReminderMode)`.
- Produces `UserSettings.EndOfDayReminderMode`, defaulting to `EndOfDayReminderMode.Both`.
- Produces `SettingsViewModel.EndOfDayReminderMode` and preserves it in every `SaveAsync` overload.

- [ ] **Step 1: Write failing configuration tests.**

```csharp
[Fact]
public async Task Saving_reminder_mode_persists_a_non_secret_preference()
{
    var settings = new FakeSettingsStore(new UserSettings { JiraBaseUrl = "https://jira.cgm.ag" });
    var viewModel = CreateSettingsViewModel(settings);
    viewModel.EndOfDayReminderMode = EndOfDayReminderMode.TrayNotificationOnly;

    await viewModel.SaveAsync("https://jira.cgm.ag", null, null);

    Assert.Equal(EndOfDayReminderMode.TrayNotificationOnly, settings.Current.EndOfDayReminderMode);
    Assert.DoesNotContain("token", JsonSerializer.Serialize(settings.Current), StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void Invalid_persisted_reminder_mode_loads_as_both()
{
    Assert.Equal(EndOfDayReminderMode.Both, EndOfDayReminderModes.Normalize((EndOfDayReminderMode)999));
}
```

- [ ] **Step 2: Run the focused configuration test and confirm it fails because the mode type/property and normalization method are absent.**

Run: `dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter FullyQualifiedName~DesktopConfigurationTests`

Expected: FAIL with missing `EndOfDayReminderMode` members.

- [ ] **Step 3: Implement the minimal non-secret setting and editor.**

```csharp
public enum EndOfDayReminderMode
{
    TrayNotificationOnly,
    OpenReviewOnly,
    Both
}

public static class EndOfDayReminderModes
{
    public static EndOfDayReminderMode Normalize(EndOfDayReminderMode mode) =>
        Enum.IsDefined(mode) ? mode : EndOfDayReminderMode.Both;
}

public sealed record UserSettings
{
    public EndOfDayReminderMode EndOfDayReminderMode { get; init; } = EndOfDayReminderMode.Both;
}
```

Add a `ComboBox` to `SettingsWindow` with the three enum choices, populate it from `SettingsViewModel`, and include its selected value in the draft `UserSettings`. In `UserSettingsService.Load`, normalize an out-of-range enum to `Both` before returning and saving the corrected non-secret value. Add the property to all Settings view-model save/load mappings; do not add a credential field or binding.

- [ ] **Step 4: Run the focused configuration suite and confirm it passes.**

Run: `dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter FullyQualifiedName~DesktopConfigurationTests`

Expected: PASS with mode persistence/default/normalization and existing secret-exclusion tests.

- [ ] **Step 5: Commit Task 1.**

```powershell
git add src/GDK.TimeSync.Desktop/Services/EndOfDayReminderMode.cs src/GDK.TimeSync.Desktop/Services/UserSettings.cs src/GDK.TimeSync.Desktop/Services/UserSettingsService.cs src/GDK.TimeSync.Desktop/ViewModels/SettingsViewModel.cs src/GDK.TimeSync.Desktop/SettingsWindow.xaml src/GDK.TimeSync.Desktop/SettingsWindow.xaml.cs tests/GDK.TimeSync.Tests/DesktopConfigurationTests.cs
git commit -m "TS-010 feat: configure reminder presentation"
```

## Task 2: Implement the deterministic local reminder service

**Files:**
- Create: `src/GDK.TimeSync.Desktop/Services/IEndOfDayReminderService.cs`
- Create: `src/GDK.TimeSync.Desktop/Services/EndOfDayReminderService.cs`
- Create: `src/GDK.TimeSync.Desktop/Services/ReviewDueEventArgs.cs`
- Test: `tests/GDK.TimeSync.Tests/EndOfDayReminderServiceTests.cs`

**Interfaces:**
- Consumes `IUserSettingsStore` and `TimeProvider`; it does not consume credentials, integration factories, clients, repositories, or coordinators.
- Produces `event EventHandler<ReviewDueEventArgs>? ReviewDue`, `Task StartAsync(CancellationToken)`, and `Task StopAsync(CancellationToken)`.
- Produces `ReviewDueEventArgs(EndOfDayReminderMode Mode)`.
- Provides `internal bool CheckNow()` for deterministic unit tests; it returns `true` only when it raises the event.

- [ ] **Step 1: Write failing timing and safety tests.**

```csharp
[Fact]
public void CheckNow_Raises_once_after_the_configured_time_for_each_local_date()
{
    var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 14, 15, 59, 0, TimeSpan.Zero));
    var settings = new FakeSettingsStore(new UserSettings { ReviewReminderTime = "16:00" });
    var service = new EndOfDayReminderService(settings, clock);
    var raised = 0;
    service.ReviewDue += (_, _) => raised++;

    Assert.False(service.CheckNow());
    clock.Advance(TimeSpan.FromMinutes(1));
    Assert.True(service.CheckNow());
    Assert.False(service.CheckNow());
    Assert.Equal(1, raised);
}

[Fact]
public void CheckNow_uses_both_for_an_invalid_persisted_mode()
{
    var service = CreateService("16:00", (EndOfDayReminderMode)999, new DateTimeOffset(2026, 8, 14, 16, 0, 0, TimeSpan.Zero));
    EndOfDayReminderMode? mode = null;
    service.ReviewDue += (_, args) => mode = args.Mode;

    service.CheckNow();

    Assert.Equal(EndOfDayReminderMode.Both, mode);
}
```

- [ ] **Step 2: Run the targeted service test and confirm it fails because the reminder service contracts are absent.**

Run: `dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter FullyQualifiedName~EndOfDayReminderServiceTests`

Expected: FAIL with missing reminder service types.

- [ ] **Step 3: Implement one-date-only timing with an in-process timer.**

```csharp
public interface IEndOfDayReminderService
{
    event EventHandler<ReviewDueEventArgs>? ReviewDue;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

internal bool CheckNow()
{
    var now = timeProvider.GetLocalNow();
    var loadedSettings = settingsStore.Load();
    var settings = loadedSettings with
    {
        EndOfDayReminderMode = EndOfDayReminderModes.Normalize(loadedSettings.EndOfDayReminderMode)
    };
    var due = ParseTimeOrDefault(settings.ReviewReminderTime);
    if (now.TimeOfDay < due.ToTimeSpan() || lastRaisedDate == DateOnly.FromDateTime(now.DateTime)) return false;
    lastRaisedDate = DateOnly.FromDateTime(now.DateTime);
    ReviewDue?.Invoke(this, new ReviewDueEventArgs(settings.EndOfDayReminderMode));
    return true;
}
```

`StartAsync` creates one `PeriodicTimer` using the injected `TimeProvider`, checks immediately, and checks each minute. `StopAsync` cancels and awaits the timer loop. Use a local cancellation token source and make repeated starts/stops safe. Parse invalid raw times as `16:00`. Keep `CheckNow` free of I/O writes and external dependencies.

- [ ] **Step 4: Run focused reminder tests and the Release build.**

Run:

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter FullyQualifiedName~EndOfDayReminderServiceTests
dotnet build GDK.TimeSync.slnx -c Release --no-restore
```

Expected: PASS, 0 build warnings/errors.

- [ ] **Step 5: Commit Task 2.**

```powershell
git add src/GDK.TimeSync.Desktop/Services/IEndOfDayReminderService.cs src/GDK.TimeSync.Desktop/Services/EndOfDayReminderService.cs src/GDK.TimeSync.Desktop/Services/ReviewDueEventArgs.cs tests/GDK.TimeSync.Tests/EndOfDayReminderServiceTests.cs
git commit -m "TS-010 feat: add local end-of-day reminder"
```

## Task 3: Wire reminder presentation into tray and Review navigation

**Files:**
- Create: `src/GDK.TimeSync.Desktop/Services/ReviewReminderActions.cs`
- Modify: `src/GDK.TimeSync.Desktop/Services/TrayIconService.cs`
- Modify: `src/GDK.TimeSync.Desktop/App.xaml.cs`
- Modify: `src/GDK.TimeSync.Desktop/ViewModels/ShellViewModel.cs`
- Test: `tests/GDK.TimeSync.Tests/ShellViewModelTests.cs`

**Interfaces:**
- Consumes `IEndOfDayReminderService.ReviewDue` and `ReviewDueEventArgs.Mode`.
- Produces `TrayIconService.ShowReviewReminder()` with no client/factory/credential dependency.
- Produces `ShellViewModel.NavigateAsync(NavigationPage.Review)` as the only opening action; it refreshes the local Review snapshot.

- [ ] **Step 1: Write failing presentation and navigation-boundary tests.**

```csharp
[Theory]
[InlineData(EndOfDayReminderMode.TrayNotificationOnly, true, false)]
[InlineData(EndOfDayReminderMode.OpenReviewOnly, false, true)]
[InlineData(EndOfDayReminderMode.Both, true, true)]
public void Reminder_mode_selects_only_its_local_presentation_actions(
    EndOfDayReminderMode mode, bool expectedTray, bool expectedReview)
{
    var actions = ReviewReminderActions.From(mode);

    Assert.Equal(expectedTray, actions.ShowTrayNotification);
    Assert.Equal(expectedReview, actions.OpenReviewWindow);
}

[Fact]
public async Task Navigating_to_review_from_a_reminder_reads_only_the_local_snapshot()
{
    var shell = CreateShellWithTrackingReview(out var snapshot, out var credentials);

    await shell.NavigateAsync(NavigationPage.Review);

    Assert.Equal(1, snapshot.Reads);
    Assert.Equal(0, credentials.GetCalls);
}
```

- [ ] **Step 2: Run the targeted shell test and confirm it fails because reminder action selection/tray presentation is absent.**

Run: `dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter FullyQualifiedName~ShellViewModelTests`

Expected: FAIL with missing `ReviewReminderActions` and tray reminder surface.

- [ ] **Step 3: Implement UI-boundary wiring without delivery behavior.**

```csharp
public readonly record struct ReviewReminderActions(bool ShowTrayNotification, bool OpenReviewWindow)
{
    public static ReviewReminderActions From(EndOfDayReminderMode mode) => mode switch
    {
        EndOfDayReminderMode.TrayNotificationOnly => new(true, false),
        EndOfDayReminderMode.OpenReviewOnly => new(false, true),
        _ => new(true, true)
    };
}
```

Add `TrayIconService.ShowReviewReminder()` using `NotifyIcon.ShowBalloonTip` with fixed non-secret text. Register `TimeProvider.System` and the singleton reminder service in `App`. Start it after app composition, stop it before `Shutdown`, and unsubscribe/dispose it on exit. On `ReviewDue`, marshal to the WPF dispatcher, compute `ReviewReminderActions`, show the tray notification when selected, and otherwise/also show the main window then await `ShellViewModel.NavigateAsync(NavigationPage.Review)`. Do not call a coordinator, confirmation command, integration factory, credential store, repository save, or HTTP client.

- [ ] **Step 4: Run all TS-010 focused tests, full mocked-safe tests, and the Release build.**

Run:

```powershell
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter "FullyQualifiedName~DesktopConfigurationTests|FullyQualifiedName~EndOfDayReminderServiceTests|FullyQualifiedName~ShellViewModelTests"
dotnet build GDK.TimeSync.slnx -c Release --no-restore
dotnet test GDK.TimeSync.slnx -c Release --no-restore --filter "FullyQualifiedName!~WindowsCredentialStoreTests"
git diff --check
```

Expected: all selected and mocked-safe tests pass; build has 0 warnings/errors; diff check is clean. The known sandbox-only Credential Manager write test remains unchanged and is not weakened.

- [ ] **Step 5: Commit Task 3.**

```powershell
git add src/GDK.TimeSync.Desktop/App.xaml.cs src/GDK.TimeSync.Desktop/Services/ReviewReminderActions.cs src/GDK.TimeSync.Desktop/Services/TrayIconService.cs src/GDK.TimeSync.Desktop/ViewModels/ShellViewModel.cs tests/GDK.TimeSync.Tests/ShellViewModelTests.cs
git commit -m "TS-010 feat: route reminders to review"
```

## Plan self-review

- Spec coverage: Task 1 provides the three user-selectable persisted modes and default; Task 2 provides once-per-date in-process timing; Task 3 maps the due event to tray and/or Review navigation without delivery.
- Safety coverage: every task excludes credentials, client factories, repository writes, HTTP, and delivery. Task 3 regression coverage preserves local-only Review navigation.
- Scope: the plan does not add task scheduler integration, AI, external calls, or changes to confirmation behavior.
- Type consistency: `EndOfDayReminderMode`, `ReviewDueEventArgs`, `IEndOfDayReminderService`, `CheckNow`, and `ReviewReminderActions` are defined before their later use.
- Placeholder scan: no `TBD`, `TODO`, or deferred implementation steps remain.
