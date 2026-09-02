# TS-038 — Sync when a date is selected, and show the sync result

## Status

Implemented.

## Objective

Reported as "it seems it is not synching anymore the toggl entries", narrowed by the user to:
*"if I close and reopen the app the sync happens, but if I change the date for today or click on
Today button the sync doesn't happen."*

## Root cause

`TodayViewModel.SelectDateAsync` did three things — flush pending saves, set `Date`, and
`LoadItemsForCurrentDateAsync` — and that last one is a **purely local repository read**. Nothing
on the date-selection path ever pulled from Toggl.

Relaunching worked because `App.OnStartup` calls `TogglAutoSyncService.StartAsync`, whose
`RunTimerAsync` invokes `CheckNowAsync()` immediately. That was the only prompt sync in the app.

Two things made it worse:

- `CheckNowAsync` gates on `lastSyncedAt` against `SyncIntervalMinutes` (15 by default), so after
  switching dates the user waited out the remainder of the previous window — not a fresh interval.
- `SelectDateAsync` opened with `if (newDate == Date) return;`, so pressing **Today** while already
  on today was a complete no-op. The button looks like a refresh and did nothing at all.

A third defect made all of this invisible: `MainViewModel.SyncStatusText` is set on every sync —
`"Imported 3, updated 0, 0 needs review."` or `"Sync failed: Toggl is not reachable…"` — and was
**bound to no view anywhere in the app**. A sync that imported nothing looked identical to one that
never ran, which is why the failure presented as "sync stopped working" rather than "sync is late".

## Scope

- `TodayViewModel`: new `DateSelected` event, raised by `SelectDateAsync` on every pick —
  *including* re-picking the date already shown, which is how **Today** is used as a refresh.
- `MainViewModel`: subscribes and calls `SyncNowAsync()`. That path bypasses the auto-sync interval
  gate entirely and reports through `SyncStatusText`. It deliberately follows the *selected* date;
  only the automatic background sync stays pinned to the real current date, per
  [[TS-033-autosync-always-real-today]].
- `ShellViewModel.Main` + `MainWindow.xaml`: a "Last sync" line in the sidebar, falling back to
  "Not synced yet".

## Duplicate safety

Selecting a date now syncs far more often than the old 15-minute interval, so the import path was
re-checked. `TogglSyncService.PullAsync` keys every remote entry against a `linkIndex` built from
each local item's `TogglEntryId` (or its stored delivery-attempt link); a match takes the update
path, never the add path. `MainViewModel.SyncNowAsync` additionally refuses re-entry while a sync is
in flight. A regression test now pulls the same entry three times in a row and asserts nothing is
added after the first.

## Tests

- `MainViewModelTests.SelectingAnotherDate_PullsThatDateFromTogglWithoutWaitingForTheAutoSyncInterval`
- `MainViewModelTests.GoToToday_PullsAgainEvenWhenTodayIsAlreadyTheSelectedDate`
- `MainViewModelTests.ShellExposesTheSyncResultSoTheWindowCanShowIt`
- `TogglSyncServiceTests.PullAsync_AddsNothingOnASecondSyncOfAnEntryItAlreadyImported`

Both date tests failed before the change (0 pulls, expected 1). 356/356 pass; Release build clean.

## Verification limits

The two date tests are the behavioural proof. In the live app the fix was confirmed to build, run,
and render the new status line, but every sync during the check legitimately found nothing new, so
the live status text could not distinguish a re-run from a stale value.
