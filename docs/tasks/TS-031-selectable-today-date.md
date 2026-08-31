# TS-031 — Make Today's date switchable

## Status

Implemented.

## Objective

Today and End-of-day review always operated on whatever date the app happened to launch with. The user needs to go back and finish or post a previous day's work (forgotten item, forgotten Jira/Slack post) without changing what the app defaults to on the next launch.

## Scope

- `TodayViewModel.Date` changes from a constructor-only `{ get; }` to a `{ get; private set; }` that raises `PropertyChanged` (and a paired `SelectedDateTime` change) when it moves.
- `InitializeAsync`'s load body is extracted into a shared `LoadItemsForCurrentDateAsync`, reused by a new `SelectDateAsync(DateOnly, CancellationToken)`: no-op if unchanged; otherwise flushes the outgoing date's pending debounced save (`FlushAsync`) before switching `Date` and reloading `Items` for the new date.
- A transient `isLoadingItems` guard (distinct from the existing startup-only `isInitialized` guard) is folded into `SaveAfterUserAction`'s condition, so repopulating `Items` for a newly selected date never queues a spurious save of that same data back to itself.
- New `SelectedDateTime` (`DateTime?`) wrapper property for WPF `DatePicker` binding, and a `GoToTodayCommand` that jumps back to `DateTime.Today`.

## Safety boundaries

- The outgoing date's save is always flushed to completion before `Date` changes, so a switch never races with `TodayViewModel`'s own debounced writer — no partial or lost writes for the date being left.
- `LoadProjectsAsync` (Toggl workspace projects, not date-scoped) is only called from `InitializeAsync` on startup, not on every date switch — switching dates never re-hits the Toggl API.

## Verification

- `TodayViewModelTests`: switching flushes the outgoing date's pending save first, the new date's items load fresh, reloading itself queues no extra save, switching to the same date is a no-op, `GoToTodayCommand` returns to today and reloads.
- Full Release build (0 warnings/errors) and full test suite green.
