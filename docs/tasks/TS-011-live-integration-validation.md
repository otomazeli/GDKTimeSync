# TS-011 — Guided live integration validation

## Status

Design approved on 2026-08-14. Awaiting written-spec review before implementation planning.

## Objective

Provide a user-initiated, auditable way to validate the configured Toggl, Jira, Tempo, and Slack integrations against their real services without automatic writes or hidden credentials.

## Confirmation policy

- A user explicitly starts every read-only diagnostic.
- Every external write has its own final confirmation.
- Toggl creation, Jira issue validation/readback, Tempo creation/readback, and Slack send are separate user-visible steps.
- The workflow never runs from startup, reminder, navigation, or background scheduling.

## Safety

- Operate only on a user-selected existing planned task; never hardcode an issue key, duration, description, project, account, worker, or webhook.
- The user sees safe metadata only: issue key, comment, duration, local start/end, destination, safe IDs, and status.
- Credentials remain in Windows Credential Manager and are factory-only; they must not appear in UI, logs, exceptions, persisted validation records, tests, or Git.
- Each successful external ID/status is durable. A failure or cancellation stops the current run and exposes recovery guidance; no auto retry, delete, compensation, or resend occurs.
- Slack is always a separate final action and uses only Tempo-confirmed work.

## Traceability

- Implementation and correction commits begin with `TS-011`.
- The task report directory is `.superpowers/sdd/2026-08-14-ts-011-live-integration-validation/`.
