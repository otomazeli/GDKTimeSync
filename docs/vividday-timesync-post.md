# VividDay: three bugs in GDK.TimeSync that no test could catch

**Author:** Odimar Tomazeli
**Categories:** primarily *Emerging technologies, especially AI* (4) and *Best practices & core concepts* (3); the tooling notes touch *New tools & methods* (1).

I was entering the same day's work three times: Toggl, then a Jira/Tempo worklog, then a Slack update. For VividDay I built GDK.TimeSync, which plans the day once and delivers it to all four on one confirmation click. I wrote it almost entirely with an AI pair (Claude Code): spec, plan, task by task, review after each task. The speed was not the interesting part. The three worst bugs were invisible to every test I had.

## The stack, briefly

.NET 10 / C# 14, WPF with MVVM, `Microsoft.Extensions.DependencyInjection`, `IHttpClientFactory`, SQLite through `Microsoft.Data.Sqlite`, xUnit. Eight projects under `src/`, one per integration plus Core, Persistence and the Desktop shell. 43 test files, 389 `[Fact]` and 60 `[InlineData]` cases — 449 test cases. Credentials live only in Windows Credential Manager, never in `settings.json`, never in a log, never in an exception message.

I picked C# mainly for its async/await model. This app is almost entirely I/O against four external HTTP APIs, and a first-class async story makes that easy to write, easy to keep off the UI thread, and easy to test.

## Finding 1: design for diagnosis on a machine you cannot debug

Delivery to Tempo failed on my CGM corporate machine. That machine has `GDK.TimeSync.exe` and nothing else: no IDE, no debugger. The app said "Tempo delivery failed." True and useless — it cannot tell an expired credential from a rejected payload. It was often wrong, too: `ConfirmedTaskDeliveryService` built all three integration clients in one `try`, and the catch-all mapped every failure to `DeliveryFailureCode.TogglFailed`. A missing Jira base URL was reported as Toggl's fault.

The fix was one `DelegatingHandler` — `src/GDK.TimeSync.Desktop/Services/AuditLoggingHandler.cs` — registered on the four named HttpClients:

```csharp
services.AddHttpClient(IntegrationClientFactory.TempoHttpClientName)
    .AddHttpMessageHandler(provider => new AuditLoggingHandler(
        provider.GetRequiredService<IAuditLog>(), IntegrationClientFactory.TempoHttpClientName));
services.AddHttpClient(SlackClientFactory.HttpClientName)
    .AddHttpMessageHandler(provider => new AuditLoggingHandler(
        provider.GetRequiredService<IAuditLog>(), SlackClientFactory.HttpClientName, redactUri: true));
```

Nothing changed inside the Toggl, Jira, Tempo or Slack clients. Every outbound call — method, path, status, duration, and the response body on a failure — now lands in `%LOCALAPPDATA%\GDK\TimeSync\logs\timesync-<date>.log`. The Tempo failure became one line naming the rejected field: `"field":"worker"`, the identity we send as the worklog author.

The log is always on: a failure that only reproduces on a corporate machine has to be recorded before the user goes looking. We ship executables to machines we cannot attach to all the time.

Redaction works by not reading. The `Authorization` header carries the Toggl token and the Jira PAT, so the handler never enumerates request headers at all — there is no allow-list to get wrong later. The Slack webhook URL is not a header either: `SlackClient` POSTs to the relative path `""` against a `BaseAddress` that *is* the secret.

```csharp
private string DescribeUri(Uri? uri) =>
    redactUri ? "<slack webhook>" : uri?.AbsolutePath ?? "(no uri)";
```

Slack failure bodies are dropped entirely, because a corporate proxy block page echoes the requested URL back inside the body. A credential can reach your log through the response.

## Finding 2: two-way binding is a write path

A WPF `ComboBox` bound with `SelectedValue`/`SelectedValuePath` writes `null` back into the source property when it cannot resolve the value against its `ItemsSource`. `DataGrid` cells are realised as you scroll, and the Toggl project list loads asynchronously. Any row realised before that list arrived had its project id set to `null`, and autosave persisted it.

The row kept its project *name*, because nothing was bound to the name. It lost its project *id*, and the id is what delivery posts with. The grid looked correct; the entries reached Toggl with no project.

I did not guess this. I queried the SQLite file: `toggl_project` still held the name, `toggl_project_id` was `NULL`. That is evidence, and it pointed at a write path, not a read path.

The fix binds `ItemsSource`/`SelectedItem` to per-row properties, so a null can be rejected when there is nothing to choose from:

```csharp
public TogglProject? SelectedTogglProject
{
    get => TogglProjectOptions.FirstOrDefault(project => project.Id == TogglProjectId);
    set
    {
        // A null with nothing to choose from is the control failing to resolve,
        // not the user clearing the field.
        if (value is null && TogglProjectOptions.Count == 0) return;
        TogglProjectId = value?.Id;
        TogglProject = value?.Name ?? "";
        OnPropertyChanged();
    }
}
```

A repair pass matches the id back when the real options arrive.

The general rule: a control that cannot resolve a value will happily tell your model the value is gone. Two-way binding writes, and it writes on the control's schedule, not yours.

## Finding 3: two small traps worth memorising

`:` in a custom .NET format string is the **time separator specifier**, not a literal — it is substituted from the current culture. Audit timestamps are written and parsed back with `CultureInfo.InvariantCulture` on both sides. Any format string you parse back needs a fixed culture.

`Button.Content` is `object`, so a binding's `StringFormat` is silently ignored on a `ContentControl`; `ContentStringFormat` is what WPF actually applies. Two Review buttons shipped showing a bare number instead of "Post selected (3)". Running the app caught it.

## What didn't work

The AI pair produced real bugs, and being vague about that would waste the finding.

- A `with { ... }` expression in the Toggl sync update path silently omitted `TogglProjectId`. Records make an omission invisible: the field keeps its old value. Import worked, update did not, so once an entry was linked the value could never self-correct.
- The catch-all that blamed Toggl for Jira and Tempo failures survived review until the audit log made it obvious.
- The `SelectedValue` null-writeback survived about a week of daily use.

The 449 tests caught none of the three. Running the app and reading the real database did. The AI pair is a fast, tireless implementer with no instinct for "this looked right on screen and was wrong in the store".

## Takeaways (Guru Card shape)

1. Route every outbound call through one seam. Cross-cutting capture is then one class, not N edits.
2. Log always, never behind a debug switch. The failure you need is on a machine you cannot attach to.
3. Redact by not reading: never enumerate headers, never log a URL that *is* a credential, and check the response body too.
4. Treat two-way binding as a write path. Reject a "cleared" value that arrives when there was nothing to choose from.
5. Diagnose from stored state. Query the database before forming a theory.
6. Force an invariant culture on any format string you parse back; use `ContentStringFormat`, not `StringFormat`, on a `ContentControl`.
7. With an AI pair: tests for logic, review for structure, the real app for anything binding-shaped. All three, or you ship the binding bug.
