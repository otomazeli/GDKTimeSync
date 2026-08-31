# TS-034 — End-of-day reminder snaps back to real-today before showing Review

## Status

Implemented.

## Objective

The end-of-day reminder is a time-based trigger meaning "it's the end of the real day, come review it." With Today's date now switchable ([[TS-031-selectable-today-date]]), if the user had navigated Today to a past date and forgotten about it, the reminder firing could silently show a stale day's review instead of today's.

## Scope

- `App.xaml.cs`'s `HandleReviewReminderAsync` now calls `TodayViewModel.SelectDateAsync(DateOnly.FromDateTime(DateTime.Today))` before navigating to the Review page, only on this automatic reminder-triggered path.
- A deliberate manual click on the "End-of-day review" nav item does **not** force this reset — that is exactly the user's stated workflow for deliberately reviewing/posting a past date, and `ShellViewModel.NavigateAsync` is unchanged.

## Safety boundaries

- Only affects which date is selected when the reminder opens Review; no delivery behavior changes.

## Verification

- Not unit tested — consistent with the rest of `App.xaml.cs`'s startup/reminder wiring, none of which has unit coverage elsewhere in this repo either.
- Manual: trigger the reminder while Today is showing a past date; confirm Review opens showing today's plan, not the past date's.
- Full Release build (0 warnings/errors) and full test suite green.
