# TS-019 — Wire Sync now to the pull-sync service

## Status

Implemented (commit `5e5c9b3`).

## Objective

Make the existing (previously no-op) "Sync now" tray action actually pull from Toggl and merge the results into Today, with a visible marker distinguishing imported rows from ones typed in the app.

## Scope

- `TodayViewModel.ApplyPullResult(TogglSyncPullResult)`: updates matched rows in place (start/end/description/`TogglEntryId`) and adds imported rows, flowing through the existing debounced-save path — no new persistence code needed.
- `MainViewModel.SyncNowCommand` now calls `ITogglSyncService.PullAsync` and `TodayViewModel.ApplyPullResult`, reporting the outcome through a new `SyncStatusText` property as a plain counts summary (e.g. "Imported 2, updated 1, 1 needs review"), kept separate from the existing `StatusText` (which reflects configuration state).
- `ITogglSyncService` registered in DI (`App.xaml.cs`).
- `TodayView.xaml`: a small "Toggl" badge column on each grid row when `Source == Toggl`, so an accidental duplicate between a locally-typed row and an imported one is easy to spot and delete manually — no automatic matching is attempted (per the TS-018 decision).

## Safety boundaries

- Sync remains manual only, triggered by the existing tray "Sync now" action — no timer or background loop was added. `UserSettings.AutoSyncEnabled`/`SyncIntervalMinutes` remain unused; wiring them up was explicitly out of scope.
- `SyncStatusText` reports counts only; a transport failure shows a fixed generic message, never the underlying error string.

## Verification

- `TodayViewModelTests`: `ApplyPullResult` adds and updates rows correctly.
- `MainViewModelTests`: sync reports counts only (verified to exclude a deliberately sensitive-looking description/name from the status text), reports a generic message on failure (verified to exclude the raw error string), and correctly toggles `IsSynchronizing`/blocks re-entry while a pull is in flight.
- Full Release build (0 warnings/errors) and full test suite green (297/297).
