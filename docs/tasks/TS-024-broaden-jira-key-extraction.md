# TS-024 — Broaden Jira key extraction to plain space-separated descriptions

## Status

Implemented.

## Objective

TS-022's parser only extracted a Jira key when it was followed by ` - ` (dash-separated). Real data showed two more common formats: a plain space with no dash — e.g. `CGMFRAVII-8139 Proxy DMP : Impact et endpoints (Clean Architecture) - Planning`, where the key is followed by a plain space and the *next* ` - ` belongs to the description text itself, not the key separator — and a pipe separator — e.g. `CGMFRAVII-8424 | DMP — Infrastructure : DTOs JSON et mapper (TDD)`. The old parser split on the first ` - ` anywhere in the string, which for the first example grabbed everything up through `...Architecture)` as the "key" candidate, failed validation, and left the row with an empty Jira key and the full raw text.

## Scope

- `TogglSyncService.ParseDescription`: now extracts the **first whitespace-delimited token** and checks that against the Jira-key pattern, instead of splitting on the first ` - `. If the token is a valid key, an optional leading `-` or `|` on the remainder is also stripped (so `KEY text`, `KEY - text`, and `KEY | text` all produce a clean comment). This is a strict superset of the previous behavior — every case the old parser handled still works, plus the plain-space and pipe-separated cases.

## Safety boundaries

Same as TS-022 — no new write path, only changes what a pull-sync result carries into `TodayViewModel`; an already-set local Jira key is still never overwritten.

## Verification

- `TogglSyncServiceTests`: added cases for the plain-space form (`CGMFRAVII-8139 Proxy DMP : Impact et endpoints (Clean Architecture) - Planning` → key `CGMFRAVII-8139`) and the pipe-separated form (`CGMFRAVII-8424 | DMP — Infrastructure : DTOs JSON et mapper (TDD)` → key `CGMFRAVII-8424`); existing dash-separated and non-matching cases still pass unchanged.
- Full Release build (0 warnings/errors) and full test suite green (312/312).
- Re-verified against the real local database after republishing and relaunching (see follow-up verification in this task's commit/conversation) rather than trusting the unit tests alone, since the previous fix (TS-022) had not visibly applied to real data yet.
