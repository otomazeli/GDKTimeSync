# TS-028 — Fix Toggl project ID never refreshing on sync update

## Status

Implemented.

## Objective

The "Toggl project" column stayed empty for already-imported/synced rows even though Toggl genuinely has a project assigned to those entries.

## Root cause

Confirmed via a raw, unparsed fetch of Toggl's API (bypassing this app's deserialization entirely) that `project_id` was present and non-null for every entry — ruling out both a Toggl-side data problem and a `TogglTimeEntry` deserialization bug. The actual defect was in `TogglSyncService.PullAsync`'s **update** branch (used for any entry already linked from a prior sync): unlike `BuildImportedItem` (the import path, which correctly sets `TogglProjectId = entry.ProjectId`), the update path's `with { ... }` never included `TogglProjectId` at all, so it silently kept whatever the field already was (`null`, from the very first import before TS-026 started populating it). Once any entry gets linked, every subsequent sync only goes through this update path, so the value could never self-correct.

The same gap existed in `TodayViewModel.ApplyPullResult`'s update loop — it didn't copy `TogglProjectId` from the service's result onto the bound view model either, the same class of bug as TS-025's `JiraIssueKey` miss.

## Scope

- `TogglSyncService.PullAsync`: the update path now sets `TogglProjectId = entry.ProjectId` unconditionally (Toggl is authoritative for its own project assignment, unlike the locally-owned Jira key/Tempo category — so this always mirrors Toggl, it isn't a backfill-only-if-empty rule). `remoteChanged` now also considers a project-id difference, so a project reassignment on an already-delivered item is still caught by the existing reconciliation-required path.
- `TodayViewModel.ApplyPullResult`: the update loop now also copies `TogglProjectId`, which (via the existing `TogglProjectId` property-changed handler) also triggers the existing Toggl-project-name resolution for updated rows, not just newly-added ones.

## Safety boundaries

Same as prior Toggl-sync fixes — no new write path; only changes what a pull-sync result carries into `TodayViewModel`.

## Verification

- Diagnosed with a raw, unparsed HTTP GET against the real Toggl API (no app code involved) before writing any fix, to conclusively rule out a Toggl-side or deserialization cause.
- `TogglSyncServiceTests`: an already-linked item imported without a project id gets it filled in on the next sync; a project id that changes in Toggl is reflected, not left stale.
- `TodayViewModelTests`: `ApplyPullResult` copies `TogglProjectId` on update; a new test confirms the Toggl project display name resolves for an *updated* row (previously only proven for newly-added rows).
- Full Release build (0 warnings/errors) and full test suite green (319/319).
