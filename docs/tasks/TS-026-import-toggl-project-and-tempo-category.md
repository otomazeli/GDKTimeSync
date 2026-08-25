# TS-026 — Populate Toggl project and Tempo category on import

## Status

Implemented.

## Objective

Imported/synced Toggl rows left the Toggl project and Tempo category unset, requiring manual entry even though the project is already known from Toggl and a sensible Tempo category default already existed in Settings.

## Scope

- `TogglSyncService.PullAsync`: reads `UserSettings.DefaultTempoWorkCategory` once per pull (falling back to the hardcoded `"DEVELOPMENT"` when that setting is itself blank, per the requested behavior) and applies it to `TempoCategory` on import, and backfills it on an already-linked item only when currently empty — mirroring the existing Jira-key backfill rule (never overwrites a category the user already set).
- `TodayViewModel.ApplyPullResult`: now also copies `TempoCategory` on the update path (the same class of bug as TS-025 — the service computed it correctly, but the merge into the bound view model dropped it), and calls the existing `ApplyProjectNames()` after every merge so a freshly-imported or updated row's `TogglProjectId` (already set from `entry.ProjectId`) gets its display name resolved from the already-loaded Toggl projects list, the same mechanism used everywhere else in Today.
- Fixed a regression introduced while wiring this up: the Tempo-category/Jira-key "backfill available" checks were briefly factored into whether an *already-succeeded* delivery gets flagged for reconciliation, which would have incorrectly flagged a successful delivery just because a local field happened to be empty. Reconciliation now depends only on whether the remote entry's own start/end/description actually changed, exactly as before this task; backfill checks only apply to items not yet delivered.

## Safety boundaries

Same as TS-022/TS-024/TS-025 — no new write path; only changes what a pull-sync result carries into `TodayViewModel`. An already-set local Tempo category or Jira key is still never overwritten.

## Verification

- `TogglSyncServiceTests`: import uses the configured default Tempo category; falls back to `DEVELOPMENT` when the setting is blank; backfills an empty category on an already-linked item on a later sync; never overwrites an already-set category. A regression test (`PullAsync_DoesNotTouchASuccessfulDeliveryWhenTheRemoteEntryIsUnchanged`) confirms an unrelated local backfill opportunity no longer trips reconciliation on a `Succeeded` delivery.
- `TodayViewModelTests`: `ApplyPullResult` copies `TempoCategory` on update; a new test confirms an imported item's `TogglProjectId` resolves to its display name from an already-loaded `TogglProjects` list.
- Full Release build (0 warnings/errors) and full test suite green (317/317).
