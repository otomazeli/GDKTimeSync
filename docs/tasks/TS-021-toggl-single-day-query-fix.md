# TS-021 — Fix Toggl single-day query returning zero entries

## Status

Implemented.

## Objective

Fix Toggl sync (manual and automatic) never picking up entries logged for the current day.

## Root cause

Confirmed via a live, read-only diagnostic against a real Toggl account: `GET /me/time_entries?start_date=X&end_date=X` (same date for both bounds — exactly what `TogglSyncService.PullAsync` and `IntegrationDiagnosticsService`'s Toggl check both request for "today") returns **zero** entries from Toggl's API, even when entries exist for that date. Widening the query so `end_date` is strictly after `start_date` (e.g. `start_date + 1 day`) correctly returns them. This was reproduced and isolated with three comparison queries against the live API before any code changed:
- `start_date=2026-08-25&end_date=2026-08-25` → 0 entries
- `start_date=2026-08-25&end_date=2026-08-26` → 3 entries (all real, matching the configured workspace)
- `start_date=2026-08-24&end_date=2026-08-26` → 10 entries (confirms nothing else, e.g. workspace or credentials, was wrong)

## Scope

- `TogglClient.GetTimeEntriesAsync` (`src/GDK.TimeSync.Toggl/TogglClient.cs`): requests `end_date = endDate.AddDays(1)` instead of `endDate`, then filters the response back down to entries whose local start date falls within the originally-requested `[startDate, endDate]` range. This is a single fix point — every caller (`TogglSyncService`, `IntegrationDiagnosticsService`, `ConfirmedTaskDeliveryService`'s live-validation path) benefits without any call-site changes, and the method's documented inclusive-both-ends contract is preserved.

## Safety boundaries

- Read-only change; no new write path.
- Filtering is client-side only, based on each entry's own `start` timestamp — no additional data is requested or retained.

## Verification

- Diagnosed live against a real Toggl workspace using a throwaway, read-only diagnostic script (never printed the API token; single `GET` calls only) before writing the fix, to confirm the exact root cause rather than guessing.
- `TogglClientTests`: existing test updated for the new query string; new tests assert a same-day query now returns entries a real Toggl account would otherwise hide, and that entries just outside the requested range (pulled in by the one-day widening) are correctly excluded from the result.
- `IntegrationDiagnosticsServiceTests`: updated the four Toggl request-URL assertions to expect the widened `end_date`.
- Re-ran the live diagnostic after the fix: the exact single-day query used in production now returns the same entries as the widened query (3/3), confirming the fix against real data, not just the mocked test suite.
- Full Release build (0 warnings/errors) and full test suite green (305/305).
