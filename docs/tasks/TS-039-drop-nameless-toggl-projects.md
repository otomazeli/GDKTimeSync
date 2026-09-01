# TS-039 — Drop nameless Toggl projects from the picker

## Status

Implemented.

## Objective

Reported alongside [[TS-038-sync-on-date-selection]]: the Toggl project dropdown opened onto a long
run of empty rows, with the real project names only reachable by scrolling past them.

## Root cause

Not a binding fault — `TodayView.xaml` sets `DisplayMemberPath="Name"` correctly on both project
pickers. Enumerating the live dropdown showed **151 items, 12 of them with a genuinely empty
`Name`**:

```
TogglProject { Id = 211315300, Name =  }
TogglProject { Id = 211315306, Name =  }
TogglProject { Id = 211436331, Name =  }
```

`TogglClient.GetProjectsAsync` calls `workspaces/{id}/projects` and returns whatever comes back. In
a large shared workspace some projects come back without a usable name — the account can see the
project exists but not what it is called. `TodayViewModel.LoadProjectsAsync` then added every one of
them verbatim.

## Scope

Filtered in `TogglClient.GetProjectsAsync` rather than in the view model, because it is the shared
function every caller routes through. That fixes the two pickers *and*
`LiveIntegrationValidationService`, which matches `candidate.Name.Trim()` against an item's Toggl
project name — a blank-named project could never match anything there either.

## Tests

`TogglClientTests.GetProjectsAsync_drops_projects_that_came_back_without_a_usable_name` covers
empty, null, and whitespace-only names, and asserts the surviving order is preserved. Failed before
the change.

## Verified in the running app

Project picker went from **151 items with 12 blank rows** to **139 items, 0 blank**, opening
directly onto `06-GPS apps development (BUD)`.
