# TS-033 — Auto-sync always targets the real current date

## Status

Implemented.

## Objective

With Today's date now switchable ([[TS-031-selectable-today-date]]), the periodic background Toggl sync (TS-020) needed a decision: which date does it sync while the user is viewing a different day? Confirmed with the user: automatic background sync must always target the real current date, never whatever date happens to be selected in the UI — only manual "Sync Now" follows the selected date.

## Root cause / hazard being designed around

`TodayViewModel` is the sole writer for whatever date it currently has loaded — its debounced save does a full replace for that date. If auto-sync fired for real-today while `TodayViewModel` was displaying (and could concurrently be saving) a *different* date, routing it through `MainViewModel.SyncNowAsync()`/`TodayViewModel.ApplyPullResult` would incorrectly inject real-today's rows into the displayed date, or race with `TodayViewModel`'s own writer.

## Scope

- `TogglAutoSyncService` gains three new constructor dependencies: `TodayViewModel`, `ITogglSyncService`, `IDailyPlanRepository` (all already-registered DI singletons).
- `CheckNowAsync` now branches on `today.Date == realToday` (computed from the injected `TimeProvider`, matching the rest of this service's testable-clock pattern):
  - **Same date (the common case):** unchanged — calls `MainViewModel.SyncNowAsync()`, going through `TodayViewModel`'s existing safe save path.
  - **Different date:** a new `SyncDateDirectlyAsync(realToday)` reads real-today's plan directly from `IDailyPlanRepository`, pulls via `ITogglSyncService.PullAsync`, merges adds/updates into a plain list, and saves directly back to the repository — never touching `TodayViewModel.Items`, which represents a different date at that moment.
- The existing try/catch-everything discipline around a sync tick is preserved for both branches — a background loop must never fault or stop future ticks.

## Safety boundaries

- The headless path only ever reads/writes real-today's row via `IDailyPlanRepository`; it has no reference to whatever `TodayViewModel.Items` currently holds, so there is no way for it to leak a different date's rows into the display or vice versa.
- `ITogglSyncService.PullAsync` already owns its own reconciliation-flag bookkeeping via `IDeliveryAttemptRepository` internally — the headless path doesn't duplicate or bypass that.

## Verification

- `TogglAutoSyncServiceTests`: existing same-date tests continue to pass unchanged (new fakes wired into the constructor only). New tests: a different-date tick writes the pulled result directly to a fake `IDailyPlanRepository` for real-today (add + update merge verified), never calls the `MainViewModel`-side sync service, and never touches `TodayViewModel.Items`; a not-yet-existing plan for that date (`GetAsync` returns null) is treated as empty rather than throwing.
- Full Release build (0 warnings/errors) and full test suite green.
