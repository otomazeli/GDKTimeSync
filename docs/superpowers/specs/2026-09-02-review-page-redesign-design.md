# End-of-day Review Redesign

## Purpose

Make the end-of-day review fast enough that a developer chooses to use it. It is the screen the whole
application builds toward — every write to Toggl, Jira, Tempo and Slack happens here — and it is
currently the least pleasant page in the app.

Reported in [issue #2](https://github.com/otomazeli/GDKTimeSync/issues/2):

> I have to tell you that I don't like and I don't find the End-of-day review layout userfriendly at
> all, we need to re-design this an keep compact and user friendly and operational that a developer
> would love to use it because it have him time.

## Why the page is shaped the way it is

The layout is a symptom; the cause is in the view model.

`ReviewViewModel.Items` is an `ObservableCollection<PlannedWorkItem>` — the bare domain record,
carrying no delivery state. Alongside it sit a single `SelectedTask` and a single `LastTaskAttempt`.
**The page can only think about one task at a time.** With that model, a list is impossible; the UI
has to be a sequence of panels that swap one task in and out, and per-task delivery state has to live
somewhere else, which is why History exists as a separate page.

The visible result is `ReviewView.xaml`: 160 lines, 20 buttons, one vertically-scrolling `StackPanel`
holding four stacked concerns at equal weight — Dry Run, guided integration validation, per-task
delivery, and the daily Slack update. Five of those blocks are confirmation panels that appear and
disappear through visibility bindings, so the page changes height while you work and the button you
want moves. A six-task day is six rounds of scroll, click, scroll, confirm.

The individual interactions are correct. The double confirmation before any external write is
deliberate and stays. What is wrong is the shape.

## Product decisions

Settled with the reporter before this document:

- **Batch delivery with one confirmation.** Tick the tasks, press once, confirm once. Delivery still
  runs task by task underneath, with per-row results.
- **Guided integration validation moves to Diagnostics.** It is a "is this working?" tool and belongs
  beside the audit log, not in the middle of the daily worklist. Nothing is deleted.
- **Each row shows delivery state per destination and, when it failed, the reason inline.**
- Rejected for now: a running total against an expected working day, and showing the billable flag
  and Toggl project per row. Both were offered and not chosen; neither is precluded later.

## The page

```text
End-of-day review · Tuesday, 1 September 2026                 6 tasks · 7:15
────────────────────────────────────────────────────────────────────────────
 ☑  CGMFRAVII-8428  DMP — CPx : sélection du certificat…   4:00   ○ ○ ○
 ☑  CGMFRAVII-2763  Sprint Planning Squad                  1:00   ○ ○ ○
 ☐  CGMFRAVII-2763  Townhall France                        0:30   ● ● ●
 ☐  CGMFRAVII-8428  Proxy DMP endpoints                    1:45   ● ● ✗
        ⚠ Tempo: User is invalid (worker)
────────────────────────────────────────────────────────────────────────────
[ Post selected (2) ]   [ Dry Run ]   [ Refresh ]

▸ Daily Slack update
```

One grid. The three marks per row are Toggl, Jira, Tempo. A row that has already been delivered is
unticked and cannot be selected — the existing idempotency already refuses a second delivery, so the
UI stops offering one rather than letting the user discover the refusal.

The Slack section keeps its current behaviour and moves below the grid as a collapsible block.

## Batch delivery

`Post selected` opens **one** confirmation naming the count, the total duration, and the destinations,
then `Post N tasks` or `Cancel`. Nothing external is written before that second click.

Delivery iterates the selected rows in order through the existing
`IConfirmedTaskDeliveryService.DeliverConfirmedAsync`, one task at a time, updating each row as its
result arrives. Ordering within a task (Toggl → Jira validation → Tempo) is untouched, as is every
idempotency guarantee. A failure part-way through leaves earlier successes recorded and visible, and
the run continues to the remaining tasks — one bad Jira key must not strand the rest of the day.
Cancelling stops before the next task begins; it never interrupts a task mid-delivery.

## Per-row delivery state

`ReviewTaskViewModel` wraps one `PlannedWorkItem` with its `DeliveryAttempt`, its selection state, and
its failure text. `ReviewViewModel.Tasks` becomes a collection of these.

State is derived, not stored twice:

| Mark | Condition |
| --- | --- |
| Toggl delivered | `DeliveryAttempt.TogglEntryId` is set |
| Tempo delivered | `DeliveryAttempt.TempoWorklogId` is set |
| Jira validated | `TempoWorklogId` is set, **or** `FailureCode` is `TempoFailed` — delivery is ordered Toggl → Jira → Tempo, so reaching Tempo at all proves Jira validated, whether or not Tempo then succeeded |
| Failed | `Status` is `Failed`; the ✗ sits on the step `FailureCode` names (`TogglFailed` → Toggl, `JiraFailed`/`JiraIssueNotFound` → Jira, `TempoFailed` → Tempo) |

The alternatives were rejected deliberately: keeping `PlannedWorkItem` with a parallel attempt
dictionary makes every binding an indirection, and reusing Today's `PlannedWorkItemViewModel` would
merge two unrelated jobs — editing a plan and reporting a delivery — into one type.

## The inline failure reason, and its limit

Showing `Tempo: User is invalid (worker)` on the row is the single non-trivial piece of this design,
because **that text does not currently exist anywhere the UI can reach.**

`PostAllCoordinator` reduces every failure to a `DeliveryFailureCode`, and the response body that
carries the real explanation is read only by `AuditLoggingHandler` on its way to the log file. Reading
the log back into a view model would couple the UI to a text file format for no good reason.

Instead, the message travels with the failure: `TempoApiException` (and the Jira equivalent) carry the
service's own message, and `PostAllCoordinator` records it in a **transient, non-persisted** field on
the `DeliveryAttempt` it returns.

The limit that follows, stated plainly rather than discovered later: **the detail survives the session,
not a restart.** A row delivered and failed in this session shows the real reason. A row loaded from
SQLite on a later launch shows the coded reason — "Tempo delivery failed" — plus a pointer to
Diagnostics, where the full entry is still in the log. Persisting the detail would mean a schema change
and storing service error text in the database, which is a larger decision than this redesign should
make on its own.

## What moves to Diagnostics

`LiveValidationViewModel` (395 lines) and its markup move to the Diagnostics page unchanged — same
code, same behaviour, same tests, new home. Diagnostics becomes the one place for "is this working?":
the audit log, the guided Toggl/Jira/Tempo checks, and the existing diagnostics run.

Review loses roughly half its height as a result. This is the largest single contributor to "compact".

## Testing

| Area | Test |
| --- | --- |
| Row state | Each mark derives from the right `DeliveryAttempt` field; a failed attempt marks the step its `FailureCode` names |
| Selection | An already-delivered row cannot be selected; a fresh row is selected by default |
| Batch | Posts only selected rows, in order; the confirmation's count and total match the selection |
| Batch | A failure part-way leaves earlier rows succeeded and continues to the remainder |
| Batch | Cancelling stops before the next task and never interrupts one in flight |
| Confirmation | No external call happens before the second, explicit click |
| Failure detail | An in-session Tempo failure surfaces the service message; a row rehydrated from the repository falls back to the coded reason |
| Markup | The grid, the single confirmation panel, and the absence of the guided-validation block, via the existing `XDocument` idiom |
| Diagnostics | Guided validation renders and behaves there exactly as it did on Review |

Existing `LiveValidationViewModelTests` must pass unchanged apart from the markup-path assertions that
name `ReviewView.xaml`, which now point at the Diagnostics view.

## Out of scope

- Delivery ordering, idempotency, and the once-per-day Slack claim: unchanged.
- Persisting failure detail to SQLite.
- The History page. It keeps its own job — every attempt ever recorded — and is not merged into Review.
- A "post everything without looking" affordance. The batch still requires an explicit confirmation
  that names what is about to be written.
