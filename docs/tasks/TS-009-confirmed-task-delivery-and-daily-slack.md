# TS-009 — Confirmed task delivery and daily Slack update

## Status

Design approved on 2026-08-13. Awaiting written-spec review before implementation planning.

## Objective

Enable explicitly confirmed delivery of one planned task at a time through Toggl, Jira, and Tempo; then compose and explicitly confirm one safe daily Slack update for completed tasks.

## User-confirmation policy

- Each planned task requires its own confirmation before it can create external Toggl/Jira/Tempo effects.
- There is no approve-all action and no automatic sequence.
- The daily Slack update requires a separate final confirmation.
- No scheduled/background operation may post anything.

## Slack task-line format

```text
{TogglProject} | {JiraIssueKey} {Description} | *{Status}*
```

`TogglProject` supplies Organization. Status is one of `Code review`, `Analyzing`, `Done`, `In Progress`, or `Waiting`.

## Safety

- Slack daily composition includes only Tempo-succeeded tasks for the selected day.
- Pending, failed, cancelled, and reconciliation-required tasks are excluded and shown as blockers.
- All external IDs/states are durable and idempotent; a duplicate or ambiguous Slack send must require reconciliation rather than automatic resend.
- Webhook URLs and all credentials remain only in Windows Credential Manager, are factory-only, and must never reach views, local database, logs, diagnostics, messages, exceptions, tests, or Git.

## Traceability

- Implementation and fix commits begin with `TS-009`.
- The future task report is `.superpowers/sdd/2026-08-13-ts-009-confirmed-delivery-slack/task-1-report.md`.
