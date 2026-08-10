# Unified WPF UI Shell Design

## Purpose

Replace the current minimal GDK.TimeSync desktop window with a usable, WPF-native daily work-planning shell. The design combines the reviewed TogglHelper, TempoVclTest, and Slack Daily Update workflows while preserving the existing secure configuration services.

This document defines UI milestone 1 only. It establishes the approved navigation and user flow without adding new external posting behavior, SQLite, or AI delivery.

## Product decisions

- The desktop/tray app remains a .NET 10 WPF application.
- The primary screen is **Today**, not an integration-specific page.
- The end-of-day reminder runs only while the application is running.
- The reminder opens a review page; the user must choose **Post all**. No automatic posting is permitted.
- Slack uses a single Incoming Webhook URL, stored as a secret.
- AI assistance is opt-in for every request. It must never send task/Jira content without an explicit user action.
- Credentials are never displayed after being saved.

## WPF application shell

`MainWindow` becomes a two-column `Grid`:

```text
Sidebar                         Content
-----------------------------   ------------------------------------------
GDK TimeSync                    Page title, date, and review reminder
  Today                         Active page content
  Templates
  History
  Settings
  End-of-day review
```

The sidebar is a persistent navigation pane. The content area uses a `ContentControl` bound to the currently selected page view model. Each navigation item is a command; navigation does not require a new window.

## Pages

### Today

The daily working surface contains:

- date, planned-duration, and validation-summary header;
- task rows with start/stop, Jira key, description, Toggl project, Tempo work category, and validation status;
- add/remove task controls;
- a shortcut to add a recurring template;
- a review action that opens End-of-day review.

For milestone 1, rows are in memory only. The page must clearly state that no external posts occur. Persistent task records and synchronization arrive in later milestones.

### Templates

Displays example recurring templates and the navigation/selection flow. Milestone 1 may add a selected template to the in-memory Today list. Persistent template authoring is deferred until a data model is approved.

### History

Provides the future audit/retry layout and an explicit empty state. No fabricated delivery history is persisted. Reconciliation and durable history are deferred with idempotency storage.

### Settings

Replaces the separate Settings dialog with a page while reusing:

- `IUserSettingsStore` / `UserSettingsService` for non-secret preferences;
- `ICredentialStore` / `WindowsCredentialStore` for secrets;
- `IConfigurationStateService` for a single configuration-completeness result.

Settings groups:

- Toggl connection and workspace;
- CGM Jira/Tempo connection and Jira base URL;
- GDK Slack Incoming Webhook;
- review reminder time and default Tempo work category;
- optional, disabled-by-default AI configuration.

Secret fields are empty `PasswordBox` replacement inputs. The page may show only "configured" or "not configured". `settings.json` stores no tokens, PATs, webhook URLs, authorization headers, or AI keys.

### End-of-day review

Displays the planned Toggl entries, Jira/Tempo validation, and Slack preview in this order:

```text
Toggl -> Jira/Tempo -> Slack
```

Milestone 1 shows the review layout but disables `Post all` with clear explanatory text. It must never call Toggl, Jira, Tempo, Slack, or AI APIs. A later workflow milestone enables the command only after validation, idempotency, and reconciliation exist.

## View model boundaries

- `ShellViewModel`: selected page and navigation commands.
- `TodayViewModel`: in-memory rows, add/remove commands, summary, and review navigation.
- `TemplatesViewModel`: example templates and add-to-Today command.
- `HistoryViewModel`: empty-state/audit presentation.
- `SettingsViewModel`: existing secure saving behavior, extended with non-secret workflow preferences.
- `ReviewViewModel`: summary and disabled posting guard.

Views bind to these view models. No view may read credentials, construct HTTP clients, or write settings directly. Existing dependency injection remains the creation boundary.

## Error handling and safety

- A configuration error appears near the affected setting or task, without revealing secret values or raw authorization data.
- Settings close only after non-secret persistence and any requested credential saves complete.
- A partial settings-save failure reports that credentials may have been saved while non-secret settings did not; it does not delete credentials automatically.
- Diagnostics contain booleans and state transitions only, never token values, webhook URLs, authorization headers, or secret strings.
- The existing `ConfigurationStateService` remains the source of truth for connection completeness and command enablement.

## Tests and verification

Add automated tests for:

- sidebar navigation and selected-page state;
- Today add/remove row commands and summary changes;
- template-to-Today addition;
- Settings availability/configuration status refresh;
- End-of-day review `Post all` guard being unavailable in milestone 1;
- existing credential existence and secret-exclusion tests.

Run the complete test suite and a Release build. Perform a manual desktop check for navigation, settings save/reopen, and confirmation that the Post all control cannot make external calls.

## Deferred milestones

1. Persistent templates and daily plans.
2. Toggl entry retrieval/posting and Jira issue validation.
3. Tempo worklog creation, idempotency, reconciliation, and durable history.
4. Slack webhook posting after successful worklog delivery.
5. Tray schedule/reminder behavior.
6. Per-request AI assistance.
