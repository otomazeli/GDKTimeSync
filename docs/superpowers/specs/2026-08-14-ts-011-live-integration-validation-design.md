# TS-011: Guided live integration validation design

## Purpose

TS-011 closes the gap between the existing mocked integration tests and a carefully controlled real-system validation. It supplies a guided workflow that proves each configured destination with user-provided, real planned work—not synthetic entries or hardcoded values.

## User workflow

The user opens Live Validation from Review and selects one existing planned item. The screen presents safe task metadata and the next required action:

1. **Run diagnostics** — an explicitly invoked, read-only operation checks configured Toggl access, Jira current user, and Tempo worker/configuration. It displays only safe result categories.
2. **Confirm Toggl creation** — a confirmation screen shows the selected task’s project, local start/end, duration, and comment. A positive confirmation creates one Toggl entry and records its safe ID.
3. **Confirm Jira validation** — a separate user action reads and validates the selected issue key before Tempo creation. Its safe issue ID is not treated as a secret.
4. **Confirm Tempo creation** — a confirmation screen displays the selected Jira key, start, duration, comment, worker identity, and safe Tempo configuration metadata. A positive confirmation creates one worklog, records its safe ID, and reads it back for verification.
5. **Confirm Slack send** — only after Tempo verification, the user may compose and explicitly confirm a Slack update. This retains the TS-009 daily-update safeguards and never sends automatically.

The actions intentionally preserve partial results. If Toggl creation succeeds and Tempo does not, the durable state says so and points to reconciliation; no code deletes the Toggl entry or silently retries Tempo.

## Architecture

The Desktop layer receives a narrow `ILiveIntegrationValidationService` that is invoked only from a dedicated Review command after its applicable confirmation state is visible. It constructs typed clients through existing credential-backed factories only inside explicit operations. The service delegates creation and durable state handling to the existing delivery-attempt persistence model where possible, and it exposes safe result records to the view model.

A `LiveValidationViewModel` owns selected-item state, step state, safe result/status text, confirmation visibility, and in-flight protection. It never accepts credential strings or client objects. Its commands have no effect until the corresponding user confirmation is positive.

The existing `IHttpClientFactory` registrations and typed Toggl/Jira/Tempo/Slack clients remain the HTTP boundary. TS-011 adds no new HTTP stack and no scheduled/background caller.

## Data and recovery

Validation state is linked to the planned work item and records only safe external IDs, delivery status, timestamp, and safe failure/reconciliation code. It records a completed external step immediately before the user can proceed. A new validation cannot reuse a terminal existing delivery attempt without an explicit reconciliation decision; it does not overwrite or duplicate it.

The UI shows a short recovery action for each partial state: Toggl-created/Tempo-missing, Tempo-readback mismatch, Slack unknown/reconciliation-required, and cancelled operation. Recovery is informational in TS-011; no automatic repair or deletion occurs.

## Security and error handling

Configuration preflight uses credential presence only when possible. A credential value is read only inside the factory that performs the user-confirmed network operation. Errors are category-only (for example, “Tempo validation unavailable”); they never include response bodies, request URLs, tokens, webhook values, or exception details.

Cancellation is offered while an operation is in flight. Because a remote service may have accepted a request before cancellation, cancellation after a write becomes a durable reconciliation-required state rather than a retryable failure.

## Test strategy

Automated tests use mocked `HttpMessageHandler` responses and fake credential stores only. They prove:

1. Opening, navigation, selection, and confirmation preview perform no external write and no credential value read.
2. Each confirmation invokes only its selected integration step; a Toggl confirmation cannot create Tempo or Slack work.
3. Jira validation occurs before Tempo creation and uses the mapped Jira issue ID.
4. Success writes safe IDs/state immediately and Tempo is read back before the step succeeds.
5. Failure/cancellation after a possible write produces reconciliation-required state, with no automatic retry/delete/compensation.
6. Slack remains unavailable until Tempo success and requires a separate final confirmation.
7. Factory/client failures and malformed responses are shown as safe categories with no secrets.

No automated test, build, package, or reminder action makes a live network call. A manual production validation occurs only when the user explicitly presses the individual confirmed action in the installed application.

## Non-goals

- No automatic live validation, batch approval, scheduled delivery, or background retry.
- No synthetic test issue, test time entry, or embedded configuration value.
- No automatic reconciliation, deletion, or compensation of real data.
- No changes to AI assistance or release packaging.
