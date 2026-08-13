# TS-009 Confirmed Delivery and Daily Slack Design

## Purpose

Build the first production-capable delivery workflow for GDK TimeSync while keeping user control at every external action. A task is delivered only after its own confirmation. Slack is sent only as a separately confirmed daily summary.

## Confirmed task delivery

The Review page shows every task for the selected day. Each task has a review action that displays its Jira key, description, duration, Toggl project, Tempo category, billable state, and delivery status. The user chooses **Post task** only from this task-specific confirmation dialog.

The confirmed task runs through the existing idempotent coordinator as a one-item `DailyPlan`:

```text
confirmed task → Toggl create → Jira lookup → Tempo worklog create → durable attempt state
```

The UI never sends a batch plan. It never advances automatically to another task. A result is shown for the confirmed task only. Existing idempotency and reconciliation states determine whether it is safe to send; pending, failed, cancelled, and reconciliation-required items present a safe status and cannot bypass the coordinator’s guards.

## Work status

`WorkStatus` is a persisted planning field on `PlannedWorkItem` and `RecurringTaskTemplate`. Its allowed values are:

- `Code review`
- `Analyzing`
- `Done`
- `In Progress`
- `Waiting`

New rows and migrated existing rows default to `In Progress`. Today and Templates expose the field. SQLite migration adds a non-secret status column with that default, preserving existing records.

## Daily Slack update

After individual delivery reviews, the Review page composes one daily Slack preview from only tasks whose Tempo worklog is durably recorded as succeeded for the selected date. The daily summary contains a configurable non-secret title, task heading, and optional free-text lines, followed by task lines in this exact format:

```text
{TogglProject} | {JiraIssueKey} {Description} | *{Status}*
```

`TogglProject` is the Organization value. Slack Markdown surrounds only the status with `*`.

The user sees the complete preview and chooses **Send daily Slack update** in a separate final confirmation dialog. It is unavailable when no eligible tasks exist, the Slack webhook is not configured, or a daily Slack attempt requires reconciliation. It cannot invoke task delivery, and task delivery cannot invoke Slack.

## Slack client and persistence

Add a `GDK.TimeSync.Slack` project containing `ISlackClient`, `SlackClient`, `SlackDailyUpdateComposer`, and typed daily-message models. `SlackClient` uses a named `IHttpClientFactory` client. A factory reads the webhook from Windows Credential Manager only when the final confirmation starts. The webhook is never exposed in a model, UI, database, diagnostics, exception, request preview, or test output.

Add a distinct daily Slack-delivery repository, keyed by `DateOnly`, containing only a content fingerprint, a safe state, and a safe failure code. It does not store the message body or webhook. A durable sent record prevents duplicate daily sends. An ambiguous failure becomes `ReconciliationRequired`; it is never automatically resent.

## Error handling

- Local validation errors are shown next to the affected task without raw exception content.
- Integration failure state uses existing safe codes; no raw response/request content is shown.
- Slack HTTP, malformed response, transport, and cancellation errors use typed safe errors without webhook details.
- Database failures preserve safe known IDs/states and require reconciliation rather than creating retries that could duplicate external effects.

## Tests

All HTTP tests use mocked handlers. Production workflow tests use fakes. Tests prove:

- task delivery cannot start before that task’s explicit confirmation;
- confirming one task processes only that item and does not start another;
- Slack composition filters to Tempo-succeeded tasks and formats every line exactly;
- final Slack confirmation is required;
- one sent daily fingerprint cannot post again;
- ambiguous Slack delivery requires reconciliation and is never automatically resent;
- webhook/token/header values are absent from JSON, exceptions, UI models, and test output;
- schema migration applies defaults to existing planning data without storing secrets.

## Non-goals

- No batch approval, automatic per-item sequence, scheduled posting, or background posting.
- No AI, scheduler, external synchronization read, or new credential storage mechanism.
