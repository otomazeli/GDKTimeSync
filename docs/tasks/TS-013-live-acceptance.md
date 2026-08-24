# TS-013 Task 2 — Controlled live integration acceptance

## Objective

Validate one user-selected task against Toggl, Jira, and Tempo through the existing Review live-validation workflow.

## Safety

- Diagnostics are read-only and run first.
- No live call is made by automated tests or packaging.
- Credential values remain in Windows Credential Manager.
- Each Toggl/Tempo write requires the existing explicit confirmation.
- Stop on any mismatch, reconciliation-required state, or unknown error.

## Acceptance sequence

1. Install/run the self-contained desktop build.
2. Configure non-secret settings and credential-manager entries.
3. Open Today and select one real task with Jira key, start/end, duration, Toggl project, and `Post to Toggl` enabled.
4. Open Review and run Diagnostics.
5. Select the task and verify the safe preview metadata.
6. Confirm Toggl; verify the created entry and duration in Toggl.
7. Validate Jira; verify the issue key and current user.
8. Confirm Tempo create-and-verify; verify the returned worklog and duration.
9. Record the result in History. Do not run a second write for the same task unless reconciliation explicitly instructs manual recovery.

## Automated verification

- Mocked tests and Release build must pass before any manual acceptance.
- Live acceptance is manual and opt-in; this task does not execute it automatically.
