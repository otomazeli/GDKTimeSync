# TS-020 — Automatic Toggl sync on launch and on an interval

## Status

Implemented.

## Objective

Sync with Toggl automatically once the app launches, and again roughly every `SyncIntervalMinutes` while it keeps running, instead of requiring a manual "Sync now" click every time.

## Scope

- New `ITogglAutoSyncService` / `TogglAutoSyncService`: an in-process `PeriodicTimer` (built on the injectable `TimeProvider`, mirroring `EndOfDayReminderService`'s existing lifecycle pattern) that calls the already-existing `MainViewModel.SyncNowAsync()` — the same method the manual tray "Sync now" action uses — once immediately on `StartAsync`, then again whenever `SyncIntervalMinutes` has elapsed since the last check.
- Wires up `UserSettings.AutoSyncEnabled` and `UserSettings.SyncIntervalMinutes`, which existed on the settings record but were never read anywhere before this task. `SyncIntervalMinutes`'s default changed from 15 to 5 minutes.
- `App.xaml.cs`: registers and starts the service alongside `IEndOfDayReminderService` on startup, and stops it on both exit paths (forced exit and the graceful tray "Exit" action).

## Safety boundaries

- No new write path: this only adds a scheduler around the existing read-only `TogglSyncService.PullAsync` pull (TS-018) and the existing merge-and-report path in `MainViewModel.SyncNowAsync` (TS-019). Zero calls to Toggl/Jira/Tempo/Slack write endpoints.
- Manual and automatic sync share `MainViewModel.IsSynchronizing`, so they can never run concurrently or double-post.
- A failed or throwing sync attempt is caught and never stops future timer ticks or crashes the app; `AutoSyncEnabled = false` makes every tick a no-op without disabling the timer itself.
- No new Settings UI was added for these two fields in this task — they remain code-level configuration, same as before.

## Verification

- `TogglAutoSyncServiceTests`: an immediate sync on `StartAsync`; no second sync before the interval elapses; a sync after the interval elapses; `AutoSyncEnabled = false` prevents any call; `StopAsync` prevents further syncs from later ticks; a transient failure on one call does not prevent the next.
- Full Release build (0 warnings/errors) and full test suite green (303/303).
- Manual: run the desktop app with Toggl configured, confirm Today's grid picks up entries shortly after launch without clicking "Sync now," and confirm the app exits cleanly (both tray "Exit" and forced close).
