# TS-016 — Toggl entry link and item source

## Status

Implemented (commit `913f6b7`).

## Objective

Give each planned item a durable link to its Toggl time entry and a flag for where it originated, as the foundation for a later pull-sync feature that imports entries created directly in Toggl.

## Scope

- Add `PlannedWorkItem.TogglEntryId` (nullable) and `PlannedWorkItem.Source` (`ItemSource.Local` | `ItemSource.Toggl`, default `Local`).
- Add both as additive SQLite columns (`toggl_entry_id INTEGER NULL`, `source INTEGER NOT NULL DEFAULT 0`) via the existing `EnsureColumnAsync` migration pattern; wire them into `SqliteDailyPlanRepository`'s read/write paths.
- Fix a pre-existing bug in `TodayViewModel.InitializeAsync`: it only passed 11 of `PlannedWorkItemViewModel`'s constructor args on reload, so `TogglProjectId` and `PostToToggl` silently reset to their defaults on every app restart. Fixed alongside the new fields so all four survive a reload.

## Safety boundaries

- Additive-only schema change; no effect on existing rows or the legacy-file/positional-constructor compatibility already required of `PlannedWorkItem`.
- No secrets involved — both fields are public Toggl identifiers, never credential data.

## Verification

- `SqlitePlanRepositoryTests`: round-trips `TogglEntryId`/`Source`, and confirms both default correctly when unset.
- `TodayViewModelTests`: regression test proving `TogglProjectId`, `PostToToggl`, `TogglEntryId`, and `Source` all survive `InitializeAsync`.
- Full Release build (0 warnings/errors) and full test suite green.
