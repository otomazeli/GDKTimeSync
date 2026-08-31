# TS-008 — Dry Run review and delivery history

## Status

Approved for implementation on 2026-08-12.

## Goal

Give the WPF desktop user a safe end-of-day review of today’s plan and delivery state. The feature must support a local Dry Run and a confirmation preview while production `Post all` remains disabled until the later Slack milestone.

## Scope

- Review shows planned-item count, planned duration, and the intended sequence: Toggl → Jira → Tempo → Slack.
- Dry Run validates only local plan data and reports readiness/blockers. It does not construct integration clients, call HTTP, or write to Toggl, Jira, Tempo, Slack, or delivery-attempt storage.
- Confirmation previews the reviewed work and requires an explicit user action. It cannot enable or execute production delivery.
- History reads the existing safe delivery-attempt records and shows status, safe IDs, and safe failure code. `ReconciliationRequired` is clearly actionable but cannot trigger a retry or reconciliation write in this task.
- All displayed errors remain safe: no credentials, headers, webhook URL, request body, response body, or raw exception details.

## Non-goals

- No real `Post all`, no `IPostAllCoordinator.PostAsync` wiring, and no factory/integration-client usage.
- No Slack client, scheduling, AI, database schema change, or migration.
- No automatic retry/reconciliation action.

## Acceptance criteria

- `ReviewViewModel.DryRunCommand` is available for a valid locally planned item, produces an in-memory result, and makes zero external or persistence writes.
- `ReviewViewModel.PostAllCommand.CanExecute(null)` remains `false`.
- Review confirmation is a preview-state interaction only and cannot invoke production delivery.
- `HistoryViewModel` loads safe statuses from `IDeliveryAttemptRepository` and represents `ReconciliationRequired` without raw data.
- Tests prove the Dry Run no-write guarantee, confirmation guard, disabled production post, and status mapping.

## Traceability

- Implementation commits must begin with `TS-008`.
- Review/fix commits must retain `TS-008`.
- Task report: `.superpowers/sdd/2026-08-10-unified-timesync-roadmap/task-8-report.md`.
