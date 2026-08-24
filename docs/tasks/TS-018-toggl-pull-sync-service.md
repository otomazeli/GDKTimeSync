# TS-018 — Read-only Toggl pull-sync service

## Status

Implemented (commit `d73ca10`).

## Objective

Let the user bring time entries logged directly in Toggl into GDK.TimeSync's local plan, so they can be filled in and pushed onward to Jira/Tempo/Slack through the existing confirmed-delivery workflow.

## Scope

- New `ITogglSyncService.PullAsync(date, localItems)` / `TogglSyncService`: fetches the day's Toggl entries, filters to the configured workspace and to entries with a defined end time (a still-running timer is excluded), and reconciles them against the local plan.
- Matching key: `PlannedWorkItem.TogglEntryId`, falling back to the linked `DeliveryAttempt.TogglEntryId` for items delivered before this feature existed.
- Unmatched entries are returned as new items: `Source = Toggl`, `PostToToggl = false` (it already exists in Toggl), empty Jira key/Tempo category for the user to fill in.
- Matched items not yet successfully delivered have their Toggl-owned fields (start/end/description) refreshed.
- Matched items with a `Succeeded` delivery are never overwritten — if the remote entry changed since delivery, the existing `DeliveryAttempt` is flipped to `ReconciliationRequired` / `DeliveryFailureCode.RemoteChangedAfterDelivery` instead, per the no-silent-overwrite decision.
- No heuristic/fuzzy matching for locally-created, never-linked items — an unmatched Toggl entry is always imported as a new row rather than guessed onto an existing one.

## Safety boundaries

- Zero write calls to Toggl, Jira, Tempo, or Slack. The only write is the narrow `IDeliveryAttemptRepository.SaveAsync` reconciliation flip described above.
- Never calls `IDailyPlanRepository.SaveAsync` directly (that repository does a full delete-and-reinsert per day; only `TodayViewModel`, the existing single writer, may apply changes, in TS-019).
- Never resets a terminal `Succeeded`/`Failed` status back to something re-postable.
- Any transport failure returns a generic error string; no raw exception detail is surfaced.

## Verification

- `TogglSyncServiceTests`: unmatched entry imported correctly; matched-undelivered entry refreshed in place; matched-and-succeeded-but-changed entry flagged for reconciliation with the original Toggl/Tempo ids preserved and no write to Toggl; matched-and-unchanged makes no write at all; a running (unfinished) entry and an entry from another workspace are both excluded; an unconfigured workspace short-circuits with zero calls.
- Full Release build (0 warnings/errors) and full test suite green.
