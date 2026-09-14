# GDK TimeSync

A Windows desktop app (WPF, .NET 10) that plans a day's work and — only on an explicit click —
records it in Toggl, Jira/Tempo, and posts a daily update to Slack. Nothing is ever posted
automatically. The one thing it does on its own is *pull* Toggl entries to keep Today current, which
is a read-only call.

This file covers **everything you must configure to get it running**. For how to use the app day to
day, see [docs/user-guide.md](docs/user-guide.md); for cleaning up a partial delivery, see
[docs/operations/recovery-and-reconciliation.md](docs/operations/recovery-and-reconciliation.md).

## 1. Prerequisites

| To | You need |
| --- | --- |
| Build or run from source | .NET 10 SDK (`scripts/setup.ps1` installs it via winget if missing) |
| Run a published release | Nothing — the published exe is self-contained and bundles the runtime |

The app is Windows-only (WPF), x64.

## 2. External accounts to set up first

Three of the four integrations need something created outside the app. Do these before opening
Settings, because the app only stores what you paste into it.

### Toggl

1. Get your **API token** from Toggl Track → Profile settings → bottom of the page.
2. Get your **workspace ID** — it is the number in the Toggl web URL
   (`track.toggl.com/…/workspaces/<id>/…`).

### Jira (CGM)

Create a **personal access token** in Jira → Profile → Personal Access Tokens. The same token is
used for Tempo, because Tempo runs against the same Jira instance — there is no separate Tempo
credential and no separate Tempo URL.

### Tempo

Nothing to create, but one thing to verify. The app sends the work category as work attribute
**id 4**, with key `_WorkCategory_`, matching the reference client this was built against. If your
Tempo instance numbers that attribute differently, delivery will fail. Check it in the app:
**Diagnostics → Run diagnostics** prints the id being sent next to the ids your instance reports.

The Tempo worklog author (`worker`) is **not** configured anywhere — the app asks Jira
`/rest/api/2/myself` and uses `key`, falling back to `name`. An email address is never a valid Tempo
worker, which is why there is no field for it.

### Slack (optional — only for the daily update)

Create a **Workflow Builder** workflow that starts **"From a webhook"**, and give the webhook
trigger these eight Data Variables, spelled exactly like this (they are matched case-sensitively):

| Variable | Carries |
| --- | --- |
| `SlackTitle` | The message title |
| `SlackTaskHeading` | The completed-tasks heading |
| `SlackExtraLines` | The task lines, newline-separated |
| `SlackUser` | Your email, if you set one |
| `TogglProject`, `JiraIssueKey`, `Description`, `Status` | Sent empty — present so a workflow referencing them never sees a missing key |

Then copy the trigger's URL. **It must be the webhook trigger URL** (`https://hooks.slack.com/triggers/…`),
which appears inside the webhook trigger's own setup panel. The workflow's **"Copy link"** button
gives you a *shortcut* link (`https://slack.com/shortcuts/…`) instead — that is a page, not an
endpoint, and posting to it answers `404`. The app now rejects a non-`hooks.slack.com` URL when you
save it.

**The webhook URL is itself a credential**: anyone holding it can post to your channel with no
authentication. Treat it like a password — and if it leaks, delete the trigger and create a new one,
which issues a new URL.

## 3. Configure the app

Everything below is entered in **Settings → Edit settings and credentials**. There is no config
file to edit by hand and no environment variable to set.

### Credentials

Stored **only** in Windows Credential Manager, never in `settings.json`, the logs, or the UI. Once
saved, a field shows "Configured" and a Replace button; the value is never displayed again.

| Field | Required | Credential Manager key |
| --- | --- | --- |
| Toggl API token | Yes | `GDK.TimeSync.Toggl.ApiToken` |
| CGM Jira personal access token | Yes (also used for Tempo) | `GDK.TimeSync.CGM.JiraPAT` |
| GDK Slack Workflow Builder webhook URL | Only for the Slack update | `GDK.TimeSync.GDK.SlackWebhook` |

### Settings

Non-secret, stored in `settings.json` (see paths below).

| Setting | Default | Required | Notes |
| --- | --- | --- | --- |
| Jira base URL | — | **Yes** | e.g. `https://jira.cgm.ag`. Must be absolute; this alone decides whether the app counts as configured. Tempo uses it too. |
| Your email for the Slack update | empty | No | Sent as the Slack `SlackUser` variable. **Not** the Tempo worker — that comes from Jira. Must be a valid email if set. |
| Toggl workspace ID | — | **Yes, to deliver** | Delivery fails with "No Toggl workspace is configured" without it. |
| End-of-day review reminder | `16:00` | No | `HH:mm`. |
| End-of-day reminder presentation | Both | No | Tray notification, open Review, or both. |
| Default Tempo work category | `DEVELOPMENT` | No | Applied to new rows; editable per row. |
| Default Toggl project | none | No | Picked from a list loaded from Toggl; used for new rows. |
| Enable optional AI assistance | off | No | Off by default; nothing is sent anywhere without a per-use consent prompt. |
| Automatically pull new Toggl entries | on | No | Read-only pull. Never posts or writes. |
| Auto-sync interval (minutes) | `5` | No | Minimum 1. |
| Slack daily update title | `Daily update` | No | Non-secret presentation text. |
| Slack completed-tasks heading | `Completed tasks` | No | |
| Slack optional extra lines | empty | No | One per line. |

## 4. Where things are stored

Per-user, no admin rights, nothing in Program Files:

| What | Path |
| --- | --- |
| Settings | `%LOCALAPPDATA%\GDK\TimeSync\settings.json` |
| Logs (14-day retention) | `%LOCALAPPDATA%\GDK\TimeSync\logs\timesync-<yyyyMMdd>.log` |
| Database | `%LOCALAPPDATA%\GDK TimeSync\timesync.db` |
| Credentials | Windows Credential Manager (keys above) |

Note the database folder is `GDK TimeSync` (with a space), while settings and logs are under
`GDK\TimeSync`. That is not a typo — they are genuinely different folders.

Credentials are per Windows user, so a new machine or a new user profile needs all three entered
again.

## 5. Build, test, run

```powershell
dotnet build GDK.TimeSync.slnx
dotnet test tests/GDK.TimeSync.Tests/GDK.TimeSync.Tests.csproj
dotnet run --project src/GDK.TimeSync.Desktop
```

`scripts/setup.ps1` installs the .NET 10 SDK via winget if you don't have it.

## 6. Publish and install

```powershell
./scripts/publish-cgm.ps1      # self-contained exe + setup scripts, zipped, for the CGM machine
./scripts/publish-desktop.ps1  # just the self-contained exe
```

`publish-cgm.ps1` produces `artifacts/GDK.TimeSync-CGM-Windows-x64.zip`. On the target machine,
extract it and run:

```powershell
.\setup-current-user.ps1 -CreateDesktopShortcut -Launch
```

It runs for the current user only, needs no administrator rights, and never asks for or stores a
credential. `-EnableAutoStart` adds it to startup; `.\remove-current-user.ps1` uninstalls
(add `-RemoveUserData -RemoveCredentials` to also delete the local database and stored credentials).

**Check the version after installing.** The window footer and the log's first line both show
`v<version>+<commit>` — the commit comes from `git rev-parse --short HEAD` at build time. If it
doesn't match what you expect, you are running an older build.

## 7. Check it works

**Diagnostics → Run diagnostics** calls all three read-only endpoints and reports each as available
or, if not, with the status code or reason. It also prints the Jira identity that will be used as
the Tempo worker, and the Tempo work attributes. Nothing is written anywhere.

**Review → Dry Run** validates a day's plan locally — every item needs a Jira issue key, a positive
duration, and a sane start/end range — and contacts nothing.

When a delivery does fail, the log names the cause: every Jira/Tempo/Toggl/Slack call is recorded
with its status and timing, failures include the request and response bodies, and every line caused
by one task carries the same `[token]` so a delivery can be read end to end. **Diagnostics** shows
today's log inside the app, with failures in red.
