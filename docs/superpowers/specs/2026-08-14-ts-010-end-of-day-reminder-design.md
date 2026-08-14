# TS-010: Running-app end-of-day reminder design

## Purpose

TS-010 adds a local reminder for the end-of-day review workflow. It prompts the user only while the desktop application is already running. It never performs delivery work.

## User-facing behavior

Settings continues to use `ReviewReminderTime` for the local reminder time and adds `EndOfDayReminderMode` with these choices:

| Choice | Result at the configured time |
| --- | --- |
| Tray notification only | Show a tray notification. |
| Open Review window only | Activate the main window and navigate to Review. |
| Both | Do both actions. This is the default. |

The reminder is emitted at most once for each local calendar date while the application is running. Changing Settings affects later checks without requiring application restart. No Windows scheduled task, background process, or startup launch is introduced.

## Architecture

`IEndOfDayReminderService` exposes `StartAsync`, `StopAsync`, and a `ReviewDue` event. `EndOfDayReminderService` accepts a clock abstraction and a read-only settings source so timing can be deterministic in tests. It tracks only the last local date for which it raised an event.

`App` starts and stops the singleton service. Its event handler obtains no integration service or credential. It calls a small UI action that applies the configured behavior: the tray service displays a notification and/or the existing main-window action activates the window and routes `ShellViewModel` to `NavigationPage.Review`.

Settings stores only the enum value in `settings.json`; it adds no credential field. The default is `Both`, so current users receive the most visible reminder until they choose another mode.

## Safety boundaries

- The reminder service is local and read-only with respect to plans and delivery state.
- Reminder handling never calls `IIntegrationClientFactory`, `ISlackClientFactory`, `ICredentialStore`, `IConfirmedTaskDeliveryService`, or any HTTP client.
- Navigation to Review refreshes a local snapshot only. It cannot confirm or send a task or Slack update.
- Invalid reminder time/mode values fall back to safe settings defaults and do not produce repeated notifications.

## Error handling

An invalid persisted mode is normalized to `Both`. An invalid reminder time is treated as the existing default time (`16:00`). Notification or window-activation failure is contained at the UI boundary and does not retry delivery or affect user data. The service stays alive for a later day.

## Test strategy

Tests use a fake clock/settings source and capture event callbacks. They prove:

1. No reminder before the configured local time.
2. One event at/after the time and no duplicate for the same date.
3. A new local date permits one new event.
4. The selected mode routes only to the appropriate tray/window action.
5. The reminder and Review navigation make zero credential, persistence-write, factory, and delivery calls.

## Non-goals

- No automatic delivery, batch approval, or scheduled external posting.
- No Windows Task Scheduler integration.
- No AI integration.
- No changes to Toggl, Jira, Tempo, or Slack client contracts.
