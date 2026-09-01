# Audit Log Design

## Purpose

Make a failed Jira/Tempo post diagnosable on a machine that has nothing but `GDK.TimeSync.exe` —
no IDE, no debugger, no developer tooling, no ability to attach one.

The immediate trigger: confirmed delivery to Tempo fails on the user's CGM corporate machine, and
the application currently offers no way to discover why.

## Why nothing can be diagnosed today

Two separate gaps, the second worse than the first.

**The application does not log.** There is no `ILogger`, no logging package, and no log file. The
only file under `%LOCALAPPDATA%\GDK\TimeSync\logs` is `setup.log`, written by the PowerShell
installer, not by the application.

**Detail that already exists is discarded on the way up.** `TempoApiException` carries the HTTP
status code from the failing call, but `PostAllCoordinator` reduces it to
`DeliveryFailureCode.TempoFailed` and the status is lost. History then renders that as the fixed
string "Tempo delivery failed.", which is true and useless — it cannot distinguish an expired
credential from a rejected payload.

Worse, `ConfirmedTaskDeliveryService.DeliverConfirmedAsync` wraps client construction for all three
integrations in one `try`, whose catch-all maps **every** failure to
`DeliveryFailureCode.TogglFailed`:

```csharp
using var toggl  = await clients.CreateTogglAsync(cancellationToken);
using var jira   = await clients.CreateJiraAsync(cancellationToken);
using var tempo  = await clients.CreateTempoAsync(cancellationToken);   // a failure here...
...
catch
{
    return await RecordSetupFailureAsync(item.Id, DeliveryFailureCode.TogglFailed);  // ...reports Toggl
}
```

A missing Jira base URL or an unreachable Tempo host is therefore reported to the user as a **Toggl**
failure. Any diagnosis starting from what the application says is starting from the wrong place.

## Product decisions

- The audit log is written to disk always, not behind a verbose/debug switch. A failure that only
  reproduces on a corporate machine must already be recorded when the user goes looking.
- Failure entries include the response body returned by Jira/Tempo/Toggl/Slack. Tempo's 400s carry
  the real reason in the body (`"Worker … could not be found"`); without it the status alone rarely
  identifies the problem.
- Response bodies may contain the user's own issue keys and worklog comments. This is accepted: the
  file stays in the user's own `%LOCALAPPDATA%` and is never transmitted anywhere.
- Credentials are never written, under any configuration. This is not a preference and has no
  opt-out.
- The log is readable from inside the application, because on the target machine there may be no
  convenient way to browse to a file and no tooling to open it with.

## Architecture

### Capturing HTTP: one delegating handler

Every integration client is constructed by `IHttpClientFactory` from a named registration in
`App.ConfigureServices`:

```csharp
services.AddHttpClient(IntegrationClientFactory.TogglHttpClientName);
services.AddHttpClient(IntegrationClientFactory.JiraHttpClientName);
services.AddHttpClient(IntegrationClientFactory.TempoHttpClientName);
services.AddHttpClient(SlackClientFactory.HttpClientName);
```

A single `DelegatingHandler` attached to those four registrations captures method, path, status,
duration and response body for every call any client makes — with no change to `TogglClient`,
`JiraClient`, `TempoClient`, or `SlackClient`. This is the whole reason the HTTP half of the feature
is small: the interception point already exists and every call already routes through it.

### Recording actions: `IAuditLog`

HTTP alone does not say *why* a call was made. A second, narrower seam records the user-visible
action around it, written explicitly by the services that perform them.

```csharp
namespace GDK.TimeSync.Core;

public enum AuditLevel { Info, Warning, Error }

public interface IAuditLog
{
    void Write(AuditLevel level, string category, string message);
}
```

`Write` is deliberately synchronous and non-throwing. A logger that can fail, block, or need
awaiting at ninety call sites is a worse problem than the one being solved; a failure to log must
never alter what the application does.

### Components

| Component | Project | Responsibility |
| --- | --- | --- |
| `IAuditLog`, `AuditLevel` | Core | The seam. No file or HTTP knowledge. |
| `FileAuditLog` | Desktop/Services | Appends to the daily file; owns retention. |
| `AuditLoggingHandler` | Desktop/Services | `DelegatingHandler` on the four named clients. |
| `DiagnosticsViewModel` | Desktop/ViewModels | Reads the tail of today's file. |
| `DiagnosticsView` | Desktop/Views | Sidebar page; copy, open folder, refresh. |

## The log file

```
%LOCALAPPDATA%\GDK\TimeSync\logs\timesync-20260901.log
```

The directory already exists and is already created by `setup-current-user.ps1`, so no installer
change is needed.

One file per local calendar day, UTF-8, append-only. Files older than **14 days** are deleted once at
startup. There is no size cap: a day's volume is bounded by how much the user does, and 14 days of
that is small.

Format is line-oriented with indented continuations, so it is readable in Notepad and greppable:

```text
2026-09-01 14:02:11.884 ERROR Tempo.CreateWorklog
  POST /rest/tempo-timesheets/4/worklogs -> 400 BadRequest (412 ms)
  request: worker=odimar.tomazeli originTaskId=CGMFRAVII-8428
           started=2026-09-01T13:00:00.000 timeSpentSeconds=14400
  response: {"errors":[{"message":"Worker odimar.tomazeli could not be found","field":"worker"}]}
  -> DeliveryFailureCode.TempoFailed
```

Concurrency: auto-sync runs on a thread-pool thread while the UI thread writes its own events, so
appends are serialized under a single process-wide lock.

> `ponytail:` one global lock around the append. Fine at this volume (tens of lines per minute);
> move to a `Channel<T>` with a single writer task if logging ever shows up in a UI stall.

## What is never written

Two specific values, both of which would otherwise be captured by a naive HTTP logger:

**The `Authorization` header.** Toggl's basic-auth token and the Jira/Tempo personal access token
travel here. Only the method, path, status and duration are read from a request. Headers are never
enumerated, so there is no allow-list to get wrong later.

**The Slack webhook URL.** This one is not a header and is easy to leak by accident:
`SlackClient` POSTs to the relative path `""` against an `HttpClient.BaseAddress` that *is* the
secret trigger URL. Logging "the request URI" for Slack would therefore write the credential
verbatim. Slack calls are logged with a fixed literal and no URI at all:

```text
2026-09-01 17:40:03.115 INFO  Slack.PostDailyUpdate
  POST <slack webhook> -> 200 OK (233 ms)
```

Settings changes log which fields were saved, never their values, and never whether a credential
looks valid.

## Actions recorded

| Category | Event |
| --- | --- |
| `App` | Start with version and log path; shutdown |
| `Settings` | Saved, listing changed field names only |
| `Sync` | Start with date and trigger (startup / interval / date selection / tray); finish with imported, updated and reconciliation counts; failure reason |
| `Today` | Date selected |
| `Delivery` | Confirmed for item id + Jira key + date; outcome of each Toggl / Jira / Tempo step; final `DeliveryAttemptStatus` and failure code |
| `Slack` | Digest composed with line count; send claimed; send result |
| `Reconciliation` | Item flagged, with the reason |

## Diagnostics page

A new `NavigationPage.Diagnostics` entry in the existing sidebar, following the pattern the other
pages already use.

- Shows the last 500 entries from today's file, newest first, so an error is visible without
  scrolling. An *entry* is a line beginning with a timestamp in column 1 together with the indented
  continuation lines that follow it — the tail is counted in entries, never in raw lines, so a
  multi-line Tempo error is never shown cut in half.
- **Copy all** — puts the visible entries on the clipboard through the existing `IClipboardService`,
  so the user can paste them into a ticket or a message.
- **Open log folder** — `explorer.exe` at the logs directory.
- **Refresh** — re-reads the file. No file watcher; a button is enough and cannot leak a handle.

Reading uses `FileShare.ReadWrite` so the view never blocks the writer.

## Corrected failure attribution

In scope because it is the specific defect that misdirects diagnosis. The three client constructions
in `DeliverConfirmedAsync` are separated so each failure maps to its own code:

| Failing construction | Was | Becomes |
| --- | --- | --- |
| `CreateTogglAsync` | `TogglFailed` | `TogglFailed` |
| `CreateJiraAsync` | `TogglFailed` | `JiraFailed` |
| `CreateTempoAsync` | `TogglFailed` | `TempoFailed` |

This changes what History displays for setup failures. It corrects a wrong label rather than
altering delivery behaviour, and no external call ordering changes.

## Testing

| Area | Test |
| --- | --- |
| Redaction | Handler given a request carrying an `Authorization` header writes no part of it, at 200 and at 401 |
| Redaction | Slack call through the handler writes neither the base address nor any path segment of it |
| Handler | 2xx logs `Info` without a body; non-2xx logs `Error` with the body; timing recorded |
| Handler | A response body that fails to read still logs the status rather than throwing |
| `FileAuditLog` | Concurrent writes from multiple threads produce complete, uninterleaved entries |
| `FileAuditLog` | Files older than 14 days are removed at startup; current and recent files kept |
| `FileAuditLog` | A write to an unwritable path is swallowed and never propagates to the caller |
| Actions | Confirmed delivery emits the expected category/level sequence via a fake `IAuditLog` |
| Attribution | Each failing client construction yields its own `DeliveryFailureCode` |
| Diagnostics | Tail returns newest-first and caps at 500; copy uses `IClipboardService` |

Existing tests must continue to pass unchanged; `IAuditLog` is optional-nullable at every
construction site so no existing test fixture needs a logger.

## Out of scope

- Log upload, telemetry, or any transmission off the machine.
- A verbosity setting. One level, always on.
- Logging the *request* body for Toggl or Jira. Tempo's request fields are logged because they are
  what Tempo rejects; the others add volume without adding diagnosis.
- Replacing `DeliveryFailureCode` with richer error types. The log carries the detail now; changing
  the persisted enum is a schema change and a separate decision.
