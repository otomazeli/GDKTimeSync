# TS-014 — Task editor quick-edit panel and connection status

## Status

Implemented. Documented retroactively from commits `79d3ede` (Task 1) and `f10384e` (Task 2). No task brief, plan, or SDD review trail exists for this task, unlike TS-008–TS-012.

## Objective

Make the Today page usable for entering a real task end-to-end without leaving the row grid, and give the user an at-a-glance, read-only view of whether Toggl, Jira, and Slack are currently reachable.

## Task 1 — Task editor and connection status (commit `79d3ede`)

### Scope

- Add a "What are you working on?" quick-edit panel above the Today grid, bound to `TodayViewModel.SelectedItem`: description, Jira key, Toggl project (combo box), task name, start/end time, billable, push-to-Toggl, and a read-only computed duration.
- `AddItemCommand` now selects the newly added row (`TodayViewModel.AddItem`) so the quick-edit panel targets it immediately.
- Add `ConnectionStatusViewModel` (Toggl/Jira/Slack). Each item exposes `Checking`/`Connected`/`Failed` plus a status message. `RefreshCommand` runs `IIntegrationDiagnosticsService.RunAsync` for Toggl/Jira and `ISlackClientFactory.IsConfiguredAsync` for Slack.
- Wire `ConnectionStatusViewModel` into `ShellViewModel` (refreshed as part of `InitializeAsync`) and render it as a sidebar "Connections" panel in `MainWindow.xaml` with a manual "Refresh connections" button.
- Enlarge the main window (420x760 → 650x1080 minimum) to fit the new panel and grid.

### Safety boundaries

- Connection status is read-only: it calls only the existing diagnostics/Slack-configuration-check services, never a write path.
- No credential values are read or displayed — only boolean connected/failed state and a safe status string.
- Errors during refresh are caught and reported as "Unavailable" rather than surfacing raw exception detail.

## Task 2 — Today editor layout fix (commit `f10384e`)

### Scope

- Fix overlapping label rows in the quick-edit panel (row height 8 → 20).
- Give the Today `DataGrid` explicit per-column widths, enable column resizing, and enable horizontal scrolling so all columns stay readable at the enlarged window size.

### Safety boundaries

- Presentation-only change; no view-model, service, or data-flow behavior changed.

## Verification

- `ConnectionStatusViewModelTests`: maps diagnostics/Slack results to per-service status, and reports Slack "Not configured" when no webhook exists.
- `TodayViewModelTests`: `AddItemCommand` selects the new row; a layout-guard test asserts the fixed row height and explicit grid column widths are present in `TodayView.xaml`.
- No live integration or credential test coverage needed — no new external call paths were introduced.

## Known gaps

- No task brief, plan, or `.superpowers/sdd` review history exists for TS-014 (unlike TS-008–TS-012). This document was reconstructed from the commit diffs after the fact and has not been through the project's usual review/re-review cycle.
