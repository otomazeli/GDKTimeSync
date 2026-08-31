# TS-010 — Running-app end-of-day reminder

## Status

Design approved on 2026-08-14. Awaiting written-spec review before implementation planning.

## Objective

Prompt the user to review the day at the configured local time while GDK TimeSync is running, without creating any external delivery effect.

## Scope

- Provide a non-secret Settings choice: `TrayNotificationOnly`, `OpenReviewOnly`, or `Both`.
- Default the choice to `Both`.
- Respect the existing `ReviewReminderTime` setting.
- Emit at most one reminder per configured local date while the app remains running.
- A tray notification uses the existing tray icon; opening Review activates the main window and navigates to Review.

## Safety

- The service is in-process only; it creates no Windows scheduled task and does nothing while the app is not running.
- It must not read credentials, construct integration clients, create delivery attempts, write plans, call HTTP, or post to Toggl, Jira, Tempo, or Slack.
- Opening Review only refreshes its local snapshot. Task delivery and daily Slack delivery remain separately confirmation-gated.

## Traceability

- Implementation and correction commits begin with `TS-010`.
- The task report is `.superpowers/sdd/2026-08-14-ts-010-end-of-day-reminder/task-1-report.md`.
