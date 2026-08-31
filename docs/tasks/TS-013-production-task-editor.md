# TS-013 Task 1 — Production-ready Today task editor

## Objective

Expose the fields required to create a reliable real delivery from the Today page while preserving the existing WPF, SQLite, credential, and confirmation boundaries.

## Scope

- Recalculate duration when a valid start/end pair is edited.
- Load Toggl projects for the configured workspace through the credential-backed integration factory.
- Persist the selected Toggl project ID and per-task Toggl posting intent.
- Expose start, end, duration, project, billable, and Toggl posting controls in Today.
- Use the selected Toggl project ID and explicit end time when creating a Toggl entry.
- Preserve compatibility with existing positional domain constructors and legacy SQLite files.

## Safety boundaries

- No credentials are added to domain models, settings JSON, logs, or tests.
- Project loading is read-only.
- No live integration call is made by tests.
- Posting still requires the existing explicit confirmation workflow.

## Verification

- Focused Today, delivery, planning persistence, and SQLite tests.
- Full Release test suite.
- Release build with zero warnings/errors.
- `git diff --check` and commit review.
