# TS-022 — Extract the Jira key from Toggl-authored descriptions

## Status

Implemented.

## Objective

Entries typed directly in Toggl commonly lead with the Jira issue key, e.g. `CGMFRAVII-2763 - AxiSanté Agile Meetings and Activities 2026 - Daily Squad Ségur 1`. Pull-sync (TS-018) previously left `JiraIssueKey` empty for every imported row, requiring the user to retype information already present in the description.

## Scope

- `TogglSyncService.ParseDescription`: splits a Toggl description on the first ` - `, and if the leading segment matches the same Jira-key pattern already enforced elsewhere (`IssueKeyValidator`/`IssueKeyValidationOptions`, `src/GDK.TimeSync.Core`), treats it as the Jira key and the remainder as the comment; otherwise leaves the key empty and the description unchanged.
- Applied on **import**: a newly-added row gets `JiraIssueKey`/`Name`/`Comment` populated from the parsed result instead of always dumping the raw description into `Comment` with an empty key.
- Applied on **update** (a matched, not-yet-delivered row): the key is only backfilled when the local `JiraIssueKey` is currently empty — an already-set key (typed by the user, or previously delivered) is never overwritten by a later sync, preserving the existing "Jira key is locally owned" rule.

## Safety boundaries

- No new write path; this only changes what values a pull-sync result carries into `TodayViewModel`.
- Only affects rows whose Toggl-sourced description happens to match the exact `{KEY} - {text}` shape with a key already valid by this app's existing Jira-key pattern — entries created by the app itself don't carry that prefix (its own Toggl-entry descriptions are the plain worklog comment), so this cannot misfire on round-tripped, app-created entries.

## Verification

- `TogglSyncServiceTests`: a leading valid-looking key is extracted and stripped from the comment on import; a leading token that isn't a valid key format leaves the whole description untouched; an already-linked item with an empty key gets backfilled on a later sync without needing an unrelated change; an already-set local key is never overwritten even when the remote description would parse to a different one.
- Full Release build (0 warnings/errors) and full test suite green (309/309).
