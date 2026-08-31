# GDK TimeSync user guide

GDK TimeSync is a Windows desktop app that helps you plan a day's work and, only when you
explicitly confirm, record it in Toggl, Jira/Tempo, and post a daily update to the GDK Slack
channel. It never *posts or writes* anything automatically -- every delivery to Toggl, Tempo,
Jira, or Slack requires an explicit confirmation click in the app. It does, by default, *pull*
your Toggl entries automatically in the background every few minutes (a read-only, authenticated
API call, purely to keep Today up to date) -- see [background sync](#connection-setup-and-secure-credential-entry)
below if you'd rather turn that off.

## Installing

See the package `README.txt` (produced by `scripts/publish-cgm.ps1`) or run
`scripts\setup-current-user.ps1 -CreateDesktopShortcut -Launch` from an extracted release ZIP.
Setup runs entirely for the current Windows user, needs no administrator rights, and never
requests, stores, or transmits any credential itself.

To remove GDK TimeSync later, see [Removal](#removal) below.

## The sidebar

The app has five pages, reachable from the sidebar:

- **Today** -- build the plan for a day: add planned work items (Jira issue key, comment,
  duration, Toggl project, Tempo category, billable flag), reuse recurring templates, and pick a
  different date with the date picker or "Today" button.
- **Templates** -- manage recurring task templates you can drop into Today with one click.
- **History** -- shows every delivery attempt ever recorded (Toggl entry ID, Tempo worklog ID,
  status, and failure reason if any). This is the source of truth for what has and hasn't been
  delivered; see [Recovery and reconciliation](operations/recovery-and-reconciliation.md).
- **Settings** -- shows non-secret configuration and whether each credential is configured (see
  below); "Edit settings and credentials" opens the settings window.
- **Review** -- the end-of-day screen: Dry Run, guided per-integration checks, per-task
  confirmation, and the daily Slack update. This is where everything gets delivered.

## Connection setup and secure credential entry

Open **Settings > Edit settings and credentials** to configure:

- **Jira base URL** (e.g. `https://jira.cgm.ag`) and, optionally, your **Jira user email** (used
  as the Tempo worklog author) and default **Tempo work category**.
- **Toggl workspace ID**.
- **End-of-day review reminder** time and how it's presented (tray notification, opening Review,
  or both).
- **Slack daily update title / completed-tasks heading / extra lines** -- the non-secret text
  used to compose the daily Slack message.
- **Enable optional AI assistance** -- off by default; see [AI assistance](#ai-assistance).

Three credentials are entered as passwords and stored **only** in Windows Credential Manager,
never in `settings.json`, logs, or anywhere visible in the UI:

- **Toggl API token**
- **CGM Jira personal access token** -- also used for Tempo, since Tempo runs against the same
  Jira Cloud/Server instance.
- **GDK Slack Incoming Webhook** (optional -- only needed for the daily Slack update)

Once a credential is saved, the field shows "Configured" and a "Replace Token"/"Replace Webhook"
button; the value itself is never displayed again, before or after saving. The Settings page
shows Toggl/Jira/Slack as "Configured" or "Not configured" for a quick glance without opening the
settings window.

**Background Toggl sync**: by default the app pulls your Toggl entries automatically every 5
minutes while it's running (`AutoSyncEnabled: true`, `SyncIntervalMinutes: 5`) so Today stays
current without a manual "Sync now" click -- this is a read-only pull, never a write. There is no
Settings-window toggle for this yet; to disable it or change the interval, close the app and edit
`AutoSyncEnabled`/`SyncIntervalMinutes` in your `settings.json` by hand.

## The daily workflow

1. **Plan** -- on Today, add or edit the day's planned work items (or drop in a template from
   Templates).
2. **Review** -- open Review for the day. Click **Dry Run** to validate the plan locally (every
   item has a Jira issue key, a positive duration, and a valid start/end range) and see a summary
   of planned minutes. Dry Run never contacts Toggl, Jira, Tempo, or Slack and never records
   anything.
3. **Optional: guided integration validation** -- select a planned item under "Guided integration
   validation" and explicitly run "Create Toggl entry", "Validate Jira", and "Create and verify
   Tempo" to confirm each integration is working before delivering real work.
4. **Confirm each task** -- click **Post task** on a planned item, review the confirmation panel
   (Jira key, comment, duration, Toggl project, Tempo category, billable flag), then **Post task**
   again to confirm, or **Cancel**. Confirming delivers that one item through Toggl, then Jira
   validation, then Tempo, in that order, and records the result. Nothing is delivered until you
   click the second, explicit confirmation.
5. **Send the daily Slack update** -- once tasks are posted, click **Compose daily Slack update**
   to build a preview from everything delivered that day (any task not yet posted to Jira/Tempo is
   still included, marked "not posted in Jira"). Review the preview, then click **Send daily Slack
   update** to post it, **Copy message** to copy the text instead, or **Cancel**. A daily Slack
   update can be sent at most once per day; sending again is blocked once one exists for that
   date.

There is no "post everything at once" button by design -- each task is confirmed individually, and
the daily Slack update is a separate, final confirmation. This keeps every delivery to an external
system an explicit, reviewable action.

## Reminders

If a review reminder time is configured (Settings > "End-of-day review reminder"), GDK TimeSync
shows a tray balloon ("Your end-of-day review is ready."), opens the Review page, or both,
depending on the configured presentation mode. The system tray icon also offers "Open GDK
TimeSync", "Sync Now" (pulls recent Toggl entries), and "Settings" without needing the main window
open.

## AI assistance

AI-assisted description suggestions are **off by default** and fully opt-in. Enabling "Enable
optional AI assistance" in Settings only makes the feature available; using it still requires an
explicit per-suggestion consent step in the Today page before any suggestion is requested or
applied.

## Removal

Run `scripts\remove-current-user.ps1` from the installed application folder or the release
package:

- With no switches, it removes the application folder and any Desktop/Start Menu/Startup
  shortcuts, but preserves your local data and credentials.
- Add `-RemoveUserData` to also delete the local settings and database (planned items, templates,
  delivery history, daily Slack delivery state).
- Add `-RemoveCredentials` to also delete the Toggl, Jira, and Slack credentials from Windows
  Credential Manager.
- Add `-DryRun` to preview what would be removed without changing anything.

## Learn more

- [Recovery and reconciliation](operations/recovery-and-reconciliation.md) -- what to do when a
  delivery attempt or daily Slack update needs manual attention.
- [Desktop publishing](desktop-publish.md) and `scripts/publish-cgm.ps1` -- how the self-contained
  release package is built.
