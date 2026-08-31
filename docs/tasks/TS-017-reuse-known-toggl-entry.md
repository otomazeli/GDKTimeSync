# TS-017 — Reuse a known Toggl entry instead of duplicating

## Status

Implemented (commit `a3606e3`).

## Objective

Prevent confirmed delivery from creating a duplicate Toggl entry for an item that already has one (imported from Toggl, or delivered before this feature existed), and let such an item still be confirmed for Jira/Tempo even when it isn't marked to post to Toggl.

## Scope

- `PostAllCoordinator.PostItemAsync`: when `PlannedWorkItem.TogglEntryId` is already set, reuse it directly and skip `toggl.CreateAsync`; otherwise behave exactly as before.
- `ReviewViewModel.OpenTaskConfirmation`: the "not marked for Toggl delivery" guard now only blocks when `PostToToggl` is false **and** there is no known `TogglEntryId`, so a linked/imported item remains confirmation-eligible.

## Safety boundaries

- No new write path — this only changes whether `toggl.CreateAsync` is invoked. Jira/Tempo delivery still requires the existing explicit per-item confirmation.
- Items the user explicitly excluded from Toggl (never linked) are still blocked from confirmation exactly as before.

## Verification

- `PostAllCoordinatorTests`: an item with a known `TogglEntryId` and `PostToToggl = false` results in zero calls to the Toggl client, reuses the existing id on the `DeliveryAttempt`, and still calls Jira/Tempo normally.
- `ReviewViewModelTests`: confirmation opens for a linked item with `PostToToggl = false`; a matching regression case confirms an unlinked, unmarked item is still blocked.
- Full Release build (0 warnings/errors) and full test suite green.
