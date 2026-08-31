# TS-032 — Date picker on Today, date display on Review

## Status

Implemented.

## Objective

Expose the date-switching capability added in [[TS-031-selectable-today-date]] in the UI: a way to pick a date on Today, and a way to see which date End-of-day review is currently showing.

## Scope

- `TodayView.xaml`: a `DatePicker` bound to `TodayViewModel.SelectedDateTime`, plus a "Today" button bound to `GoToTodayCommand`, added to the page header. This is the single place the selected date is set.
- `ReviewViewModel`: new read-only `PlanDate` property, set from the plan snapshot's date inside the existing `RefreshAsync()` — no new dependency, since Review already reads the current snapshot via `ILocalPlanSnapshotProvider` there.
- `ReviewView.xaml`: shows `PlanDate` ("Reviewing: {date}") near the page header. Review has no independent picker of its own — it always reflects whichever date is currently selected on Today, consistent with all of Review's other data already deriving from Today's snapshot.

## Safety boundaries

- No delivery-path changes. `ConfirmedTaskDeliveryService`, `SlackDailyUpdateComposer`, and `IDailySlackDeliveryRepository` were already fully date-parameterized (keyed off each item's/plan's own date, never `DateTime.Today`), so posting to Jira/Tempo/Slack for a past date already worked correctly once the UI could select one.

## Verification

- `ReviewViewModelTests`: `PlanDate` reflects the snapshot's date after `RefreshAsync`.
- Markup-presence tests (`TodayViewModelTests`, `LiveValidationViewModelTests`) confirm the `DatePicker`/`GoToTodayCommand` binding on Today and the `PlanDate` binding on Review.
- Full Release build (0 warnings/errors) and full test suite green.
