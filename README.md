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
| Build or run from source | .NET 10 SDK and Git — see [§5](#5-set-up-a-developer-machine) for the full setup |
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
file to edit by hand and no environment variable to set for the desktop app.
(The developer-only console tool in §5.5 is the one exception.)

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

## 5. Set up a developer machine

### 5.1 Prerequisites

| Need | Why | Install |
| --- | --- | --- |
| Windows 10/11, x64 | The app is WPF — it does not build or run on Linux or macOS, and there is no cross-platform target | — |
| .NET 10 SDK | Everything targets `net10.0`; the Desktop project targets `net10.0-windows` | `winget install --id Microsoft.DotNet.SDK.10 --exact` |
| Git | The build stamps the short commit into the version shown in the footer and the log | `winget install --id Git.Git --exact` |
| An IDE (optional) | Visual Studio 2026, Rider, or VS Code with the C# Dev Kit | — |

The solution file is `GDK.TimeSync.slnx`, the XML solution format. It needs an SDK that
understands `.slnx` — .NET 10 does. An older SDK on the PATH will fail to open it, so check
`dotnet --version` reports a `10.*` before anything else. Side-by-side 10.x patch versions are
fine; the build does not pin one (there is no `global.json`).

### 5.2 Clone and build

```powershell
git clone git@github.com:otomazeli/GDKTimeSync.git   # or https://github.com/otomazeli/GDKTimeSync.git
cd GDKTimeSync
./scripts/setup.ps1
```

`setup.ps1` is the whole developer setup in one command. It finds `dotnet` (falling back to
`C:\Program Files\dotnet\dotnet.exe` when it is not on the PATH), installs the .NET 10 SDK via
winget if it is missing, verifies the major version is 10, then runs restore, build, and the full
test suite against the solution.

If PowerShell refuses to run it — unsigned scripts are blocked under the default
`Restricted`/`RemoteSigned` policy — run it for that one process instead of changing the
machine-wide policy:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1
```

If winget is unavailable (common on locked-down corporate images), the script tells you to install
the SDK by hand from <https://dotnet.microsoft.com/download/dotnet/10.0> and stops.

### 5.3 The individual commands

`setup.ps1` runs these for you; use them directly for day-to-day work.

```powershell
dotnet restore GDK.TimeSync.slnx
dotnet build   GDK.TimeSync.slnx
dotnet test    tests/GDK.TimeSync.Tests/GDK.TimeSync.Tests.csproj
dotnet run --project src/GDK.TimeSync.Desktop
```

There is one test project covering all eight source projects. To run a single test while working:

```powershell
dotnet test tests/GDK.TimeSync.Tests/GDK.TimeSync.Tests.csproj --filter "FullyQualifiedName~TodayViewModel"
```

The Desktop build output lands in `src/GDK.TimeSync.Desktop/bin/<Debug|Release>/net10.0-windows/`,
with the executable named `GDK.TimeSync.exe` (not `GDK.TimeSync.Desktop.exe` — `AssemblyName` is
overridden).

### 5.4 First run on a fresh machine

Nothing in the repository configures the app. A fresh clone starts with no settings, no database,
and no credentials — all three are created per Windows user on first run, outside the source tree,
at the paths in §4. The app will report itself unconfigured until you enter a Jira base URL, so do
§2 and §3 before expecting anything to connect.

That also means **a clean checkout does not give you a clean app**. To reset the app itself, delete
`%LOCALAPPDATA%\GDK\TimeSync` (settings and logs) and `%LOCALAPPDATA%\GDK TimeSync` (database), and
remove the three `GDK.TimeSync.*` entries from Windows Credential Manager. Rebuilding changes none
of it.

Only one instance runs at a time. Launching a second one surfaces the window already running
instead of starting over, so stop a running copy before starting a debug session — otherwise you
end up debugging a process that immediately exits.

### 5.5 The console project

`src/GDK.TimeSync.Console` is a developer tool, not part of the shipped app. It talks to Tempo
directly and is the one place environment variables apply — it reads standard .NET configuration,
so `Jira__BaseUrl` and `Jira__PersonalAccessToken` (double underscore) are how you give it
credentials. The Desktop app ignores these entirely and uses Credential Manager.

```powershell
$env:Jira__BaseUrl = 'https://jira.cgm.ag'
$env:Jira__PersonalAccessToken = '<your PAT>'

dotnet run --project src/GDK.TimeSync.Console -- tempo-discover
dotnet run --project src/GDK.TimeSync.Console -- tempo-create CGMFRAVII-1234 2026-09-15 09:00 3600 "comment"
```

`tempo-discover` prints your instance's work attributes as JSON — that is how to check the work
attribute id described in §2. `tempo-create` **writes a real worklog to real Tempo**; there is no
dry-run flag on it.

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
