# TS-040 — Audit log and Diagnostics page

## Status

Implemented.

## Objective

A confirmed delivery to Tempo fails on the user's CGM corporate machine. That machine has nothing
but `GDK.TimeSync.exe` -- no IDE, no debugger, no developer tooling, and no way to attach one. The
app needed a way to make a failure like this diagnosable from the machine it happens on, without
requiring a developer to reproduce it elsewhere.

## Root cause

Two gaps, the second worse than the first.

The application did not log anything. There was no `ILogger`, no logging package, and no log file
-- the only file under `%LOCALAPPDATA%\GDK\TimeSync\logs` was `setup.log`, written by the
PowerShell installer, not by the app.

Detail that already existed was discarded on the way up. `TempoApiException` carries the HTTP
status code from the failing call, but `PostAllCoordinator` reduced it to
`DeliveryFailureCode.TempoFailed`, and History rendered that as the fixed string "Tempo delivery
failed." -- true and useless, since it cannot distinguish an expired credential from a rejected
payload.

Worse, `ConfirmedTaskDeliveryService.DeliverConfirmedAsync` wrapped construction of all three
integration clients in one `try`, whose catch-all mapped **every** construction failure to
`DeliveryFailureCode.TogglFailed`. A missing Jira base URL or an unreachable Tempo host was
therefore reported to the user as a **Toggl** failure -- any diagnosis starting from what the app
said was starting from the wrong place.

## Scope

**`IAuditLog` / `AuditLevel`** (`GDK.TimeSync.Core`) -- the seam: `void Write(AuditLevel level,
string category, string message)`. Deliberately synchronous and non-throwing; a logger that can
fail, block, or need awaiting at every call site would be a worse problem than the one it solves.

**`FileAuditLog`** (Desktop/Services) writes to
`%LOCALAPPDATA%\GDK\TimeSync\logs\timesync-yyyyMMdd.log`, one file per local calendar day, UTF-8,
append-only. Files older than 14 days are deleted once at startup; there is no size cap. Each entry
is a line beginning with `yyyy-MM-dd HH:mm:ss.fff`, a level, and a category, with any continuation
lines (a Tempo request body, a response body) indented two spaces -- readable in Notepad and
greppable. Concurrent writers (auto-sync on a thread-pool thread, the UI thread writing its own
events) are serialized under a single process-wide lock.

**`AuditLoggingHandler`** (Desktop/Services) is a `DelegatingHandler` registered on all four named
`HttpClient`s (`IntegrationClientFactory`'s Toggl/Jira/Tempo registrations and
`SlackClientFactory`'s), so every Toggl, Jira, Tempo, and Slack call is captured with method, path,
status, and duration, with no change to any of the four clients themselves. A failing call (non-2xx
status, or a transport exception) also logs the response body, truncated at 4000 characters, and a
body that fails to read still logs the status rather than throwing.

**What is never written.** The `Authorization` header -- Toggl's token and the Jira/Tempo personal
access token travel here. The handler never enumerates request headers at all, so there is no
allow-list of "safe" headers to get wrong later. The Slack webhook URL needed separate handling
because it isn't a header: `SlackClient` posts to the relative path `""` against an
`HttpClient.BaseAddress` that *is* the secret trigger URL, so logging "the request URI" for Slack
would write the credential verbatim. Slack calls are logged with the fixed literal `<slack
webhook>` and no URI at all. Settings changes log which field names changed, never their values,
and never whether a credential looks valid.

Response bodies from Jira and Tempo may still contain the user's own issue keys and worklog
comments -- accepted deliberately, because Tempo's 400s carry the real reason in the body (for
example `"Worker … could not be found"`) and the status code alone rarely identifies the problem.
The file never leaves the machine, so this trades a small amount of exposure to the user's own
`%LOCALAPPDATA%` for the ability to diagnose the actual failure without a debugger.

**`DiagnosticsViewModel` / `DiagnosticsView`** add a `NavigationPage.Diagnostics` entry to the
existing sidebar. It shows the last 500 entries from today's file, newest first, counted in
entries (a timestamped line plus its indented continuations) rather than raw lines, so a multi-line
Tempo error is never shown cut in half. **Copy all** puts the visible entries on the clipboard
through the existing `IClipboardService`; **Open log folder** opens the logs directory in
`explorer.exe`; **Refresh** re-reads the file (no file watcher -- a button is enough and cannot
leak a handle). Reading uses `FileShare.ReadWrite` so the view never blocks the writer.

**Corrected failure attribution.** `ConfirmedTaskDeliveryService.DeliverConfirmedAsync`'s three
client constructions are now attributed independently instead of sharing one catch-all:

| Failing construction | Was | Becomes |
| --- | --- | --- |
| `CreateTogglAsync` | `TogglFailed` | `TogglFailed` |
| `CreateJiraAsync` | `TogglFailed` | `JiraFailed` |
| `CreateTempoAsync` | `TogglFailed` | `TempoFailed` |

This changes what History displays for a client-setup failure. It corrects a wrong label rather
than changing delivery behaviour -- no external call ordering changes, and this is the specific
defect that had been misdirecting diagnosis before the log existed. See
[[TS-036-readable-history-and-dead-code-removal]] for the History row rendering this reads through,
and `docs/operations/recovery-and-reconciliation.md` for the reader-facing recovery path, which now
starts at the Diagnostics page.

## Tests

- `FileAuditLogTests`: appends a timestamped entry to today's file; keeps concurrent entries whole
  and uninterleaved; never throws when the directory can't be written; formats the timestamp
  invariantly under a hostile culture; removes files older than 14 days and keeps recent ones;
  doesn't throw when the log directory doesn't exist yet.
- `AuditLoggingHandlerTests`: logs `Info` without a body for a 2xx call; logs `Error` with the
  response body for a failing call; never writes the `Authorization` header at 200 or 401; never
  writes any part of the Slack webhook URL; logs the status even when the response body can't be
  read; logs a transport failure as an error and rethrows.
- `AuditLogWiringTests`: `ConfigureServices` registers a single `IAuditLog`; the log directory sits
  beside `settings.json` under `GDK\TimeSync`; `SyncNowAsync` records the outcome counts.
- `DiagnosticsViewModelTests`: refresh shows complete entries newest-first; caps the number of
  entries shown; `CopyAllCommand` puts every shown entry on the clipboard; refresh reports an empty
  log without failing.
- `ConfirmedTaskDeliveryServiceTests.DeliverConfirmedAsync_AttributesAClientSetupFailureToTheClientThatFailed`
  (`[Theory]` over toggl/jira/tempo): each failing client construction yields its own
  `DeliveryFailureCode`.

380/380 pass; existing tests were unaffected because `IAuditLog` is optional-nullable at every
construction site, so no pre-existing test fixture needed a logger.
