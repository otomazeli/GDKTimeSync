# TS-025 — Fix JiraIssueKey never reaching the Today grid on sync update

## Status

Implemented.

## Objective

TS-022/TS-024 correctly computed a backfilled `JiraIssueKey` inside `TogglSyncService.PullAsync`'s `ItemsToUpdate`, but the extracted key never actually appeared in the Today grid or persisted to disk for already-linked rows.

## Root cause

`TodayViewModel.ApplyPullResult`'s update loop copied `Start`, `End`, `Description`, and `TogglEntryId` from each `TogglSyncPullResult.ItemsToUpdate` entry onto the matching UI-bound `PlannedWorkItemViewModel`, but never copied `JiraIssueKey`. The correctly-computed value from the service layer was silently dropped before it ever reached the grid or the debounced-save path (which only persists what actually changed on the view model), so the field stayed permanently empty for any row that was already linked before this fix landed.

Confirmed against real data by reading the local SQLite database directly (bypassing the UI): `TogglSyncService.PullAsync`'s replicated logic produced the right key, but the persisted `PlannedWorkItem.JiraIssueKey` for already-imported rows remained empty across multiple sync cycles — isolating the bug to the merge step rather than the parsing logic added in TS-022/TS-024.

## Scope

- `TodayViewModel.ApplyPullResult`: the update loop now also sets `existing.JiraIssueKey = updated.JiraIssueKey`.

## Safety boundaries

No new write path; this only fixes which fields a sync update actually applies to the bound view model (and therefore what gets persisted).

## Verification

- `TodayViewModelTests.ApplyPullResult_UpdatesAMatchedItemInPlaceWithoutChangingItemCount` extended to assert the Jira key is carried through on update, not just Start/End/Comment/TogglEntryId.
- Full Release build (0 warnings/errors) and full test suite green (312/312; one unrelated cross-process-lock test flaked under system load during a background suite run and passed cleanly in isolation).
- Re-verified against the real local database after republishing and relaunching: confirmed the Jira key round-trips onto already-linked rows following this fix.
