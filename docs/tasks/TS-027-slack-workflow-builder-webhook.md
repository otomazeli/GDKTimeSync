# TS-027 — Switch Slack delivery to a Workflow Builder webhook payload

## Status

Implemented.

## Objective

The Slack Incoming Webhook URL the user configured (`https://slack.com/shortcuts/...`) failed to connect. Investigation found this is a Slack **Link Trigger** URL (meant to be opened in a browser to launch a workflow interactively), not a machine-callable webhook endpoint — confirmed by POSTing to it directly and getting back `404` with a full HTML page, not an API response. A Workflow Builder **Webhook** trigger (a different trigger type, generating a `hooks.slack.com/triggers/...` URL) is what accepts a programmatic JSON POST with custom fields ("Data Variables").

Since the user's Slack workflow already defines flat Data Variables (`SlackTitle`, `SlackTaskHeading`, `SlackExtraLines`, `TogglProject`, `JiraIssueKey`, `Description`, `Status`, `SlackUser`) rather than the single `text` field a classic Incoming Webhook expects, the app's outgoing payload needed to change shape to match.

## Scope

- `SlackDailyUpdate` (`src/GDK.TimeSync.Slack/SlackDailyUpdate.cs`): replaced the single `Text` field with `SlackTitle`, `SlackTaskHeading`, `SlackExtraLines`, `SlackUser`, matching the Workflow Builder Data Variable names directly. `ContentFingerprint` now hashes all four fields together (idempotency/audit behavior unchanged).
- `SlackDailyUpdateOptions` gained a `JiraUser` field, sourced from the already-stored, non-secret `UserSettings.JiraUser`.
- `SlackDailyUpdateComposer.Compose`: `Title`/`Header` now populate their own fields instead of being prepended as lines; the per-task lines (plus any configured `UserSettings.SlackExtraLines`) are joined into `SlackExtraLines`. One call still covers the whole day's completed tasks — `TogglProject`/`JiraIssueKey`/`Description`/`Status` are sent as empty strings on the wire so a workflow referencing them never sees a missing key, per the user's choice not to switch to one webhook call per task.
- `SlackClient.PostAsync`: sends the new field set, and success is now judged by HTTP status alone — the old check required the literal response body `"ok"`, which is specific to classic Incoming Webhooks and not how a Workflow Builder trigger responds.
- Fixed a real bug found while testing: `HttpClient.PostAsJsonAsync`'s default naming policy is camelCase, which would have sent `slackTitle` etc. instead of the exact-cased `SlackTitle` the user's Data Variables are named — Slack matches variable names to JSON keys case-sensitively. `SlackClient` now serializes with `PropertyNamingPolicy = null` to preserve the exact PascalCase names.
- `ReviewView.xaml`'s Slack preview panel now shows each field (title, heading, extra lines, sender) instead of one combined text block, matching the new shape.

## Safety boundaries

- Same idempotency/audit guarantees as TS-009: `ContentFingerprint` still gates duplicate sends via `IDailySlackDeliveryRepository`.
- `JiraUser` is non-secret (already used elsewhere in the app); no credential values are added to the payload.
- Webhook URL itself is never logged/exposed in exceptions, same as before.

## Verification

- Root-caused live against the user's actual URL (`curl -X POST` returned `404` + HTML) before writing any code, confirming the failure was a wrong-trigger-type problem, not a client bug.
- `SlackClientTests`: asserts the exact PascalCase JSON keys sent on the wire (parsed from the raw request body, not assumed); asserts any 2xx status is treated as success regardless of response body content.
- `SlackDailyUpdateComposerTests`, `ReviewViewModelTests`: updated for the new field shape.
- Full Release build (0 warnings/errors) and full test suite green (316/316).
