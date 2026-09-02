# TS-035 — Drop the Toggl project from the Slack task line

## Status

Implemented.

## Objective

The Toggl project prefix on every Slack digest line was not wanted in the posted text.

## Scope

- `SlackDailyCompletedItem`: `TogglProject` removed from the record — nothing else read it.
- `SlackDailyUpdateComposer.Compose`: line is now `{JiraIssueKey} {Description} | *{Status}*` (see [[TS-009-confirmed-task-delivery-and-daily-slack]]).
- `ReviewViewModel.ComposeSlackPreviewAsync`: no longer passes `item.TogglProject`.
- `SlackClient` still sends a blank `TogglProject` Data Variable on the wire, unchanged from [[TS-027-slack-workflow-builder-webhook]], so the user's Workflow Builder trigger never sees a missing key.

## Tests

`SlackDailyUpdateComposerTests`, `ReviewViewModelTests`, `EndToEndDryRunTests` updated to the shortened line. 350/350 pass.
