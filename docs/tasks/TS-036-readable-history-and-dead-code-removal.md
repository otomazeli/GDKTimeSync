# TS-036 — Readable History rows, and removal of the unused sync/reconciliation engines

## Status

Implemented.

## Objective

Two findings from a review of the completed TS-008..TS-035 work against the source:

1. The History page rendered a raw `PlannedWorkItemId` GUID as a row's only identifier — the page
   loaded and displayed, but told the user nothing about which task an attempt belonged to.
2. `SyncEngine`, `InMemorySyncStateStore`, and `ReconciliationEngine` were referenced only by their
   own tests: never registered in `App.xaml.cs`, never called from any live code path.

## Task 1 — History shows the task, not its GUID

`DeliveryAttempt` carries no date or description, and `IDailyPlanRepository` can only fetch one
date at a time, so History had nothing to render. Both tables live in the same SQLite file, so the
join happens in SQL:

- `Core/DeliveryAttempt.cs`: new `DeliveryHistoryEntry(Attempt, PlanDate, JiraIssueKey, Description)`
  and a separate one-method `IDeliveryHistoryRepository`. Kept off `IDeliveryAttemptRepository`
  deliberately — eight test fakes implement that interface and none of them care about history.
- `SqliteDeliveryAttemptRepository`: also implements `IDeliveryHistoryRepository`. `ListHistoryAsync`
  LEFT JOINs `planned_work_items`, ordered newest plan date first. LEFT, not INNER: an attempt
  outlives its planned item if the plan row was replaced, and such a row must still be listed
  rather than silently vanish from history.
- `HistoryViewModel` / `DeliveryHistoryItemViewModel` / `HistoryView.xaml`: rows now read
  `2026-08-13  CGM-1 Knowledge transfer` / `Succeeded — Toggl #101 · Tempo #201`, with
  `Unknown date` + `(task no longer in any plan)` for an orphaned attempt.

## Task 2 — Delete the unused engines

Removed, with the contracts and DTOs that existed only to serve them:

`SyncEngine.cs`, `InMemorySyncStateStore.cs`, `ReconciliationEngine.cs`, `SyncContracts.cs`
(`ISyncStateStore`, `IJiraIssueValidator`, `ITempoWorklogWriter`), `SourceTimeEntry.cs`,
`Core/TempoWorklogRequest.cs` (distinct from the live `Tempo/TempoWorklogRequest.cs`), plus
`SyncEngineTests.cs` and `ReconciliationEngineTests.cs`.

Also removed the permanently-disabled "Reconcile Today" tray menu item (`TrayIconService.cs`) — the
UI hook the reconciliation engine was meant for, greyed out since it was added.

`TimeEntry.cs` / `TimeEntryParser.cs` were left alone: the parser is still registered by
`AddTimeSyncCore` and reachable from the Console project.

The `ReconciliationRequired` **status** is unaffected — it is produced by
`ConfirmedTaskDeliveryService`, not by the deleted engine, so
`docs/operations/recovery-and-reconciliation.md` remains accurate. It was updated for the new
History row layout and for the removed tray item.

## Tests

- `SqlitePlanRepositoryTests.ListHistoryAsync_JoinsEachAttemptToItsPlannedTaskNewestDayFirst`:
  proves the join, the ordering, and the orphaned-attempt case against a real SQLite file.
- `HistoryViewModelTests.LoadAsync_ShowsTheDateAndTaskInsteadOfTheRawItemIdentifier`.
- 348/348 pass; Release build clean.
