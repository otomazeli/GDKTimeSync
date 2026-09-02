# TS-041 — End-of-day review redesign

## Status

Implemented.

## Objective

Reported in [issue #2](https://github.com/otomazeli/GDKTimeSync/issues/2):

> I have to tell you that I don't like and I don't find the End-of-day review layout userfriendly at
> all, we need to re-design this an keep compact and user friendly and operational that a developer
> would love to use it because it have him time.

Review is the screen the whole app builds toward — every write to Toggl, Jira, Tempo and Slack
happens there — and it needed to become fast enough that a developer chooses to use it.

## Root cause

The layout was a symptom; the cause was in the view model.

`ReviewViewModel.Items` was a bare `ObservableCollection<PlannedWorkItem>` — the domain record,
carrying no delivery state — alongside a single `SelectedTask` and a single `LastTaskAttempt`. The
page could only think about one task at a time. With that model, a list was impossible: the UI had
to be a sequence of panels that swapped one task in and out, and per-task delivery state had to live
somewhere else, which is why History existed as a separate page.

The visible result was `ReviewView.xaml` as a single vertically-scrolling `StackPanel` holding four
stacked concerns at equal weight — Dry Run, guided integration validation, per-task delivery, and the
daily Slack update — several of them confirmation panels that appeared and disappeared through
visibility bindings, so the page changed height while you worked. A six-task day was six rounds of
scroll, click, scroll, confirm.

The individual interactions were correct — the double confirmation before any external write was
deliberate and stays. What was wrong was the shape, and the shape followed directly from a view
model that could not hold more than one task.

## Scope

**`ReviewTaskViewModel`** (new) wraps one `PlannedWorkItem` with its `DeliveryAttempt`, selection
state, and failure text. `ReviewViewModel.Tasks` is now an `ObservableCollection<ReviewTaskViewModel>`.
Per-destination state is derived, never stored twice: `Toggl` from `TogglEntryId`, `Tempo` from
`TempoWorklogId`, and `Jira` from reaching Tempo at all (`TempoWorklogId` set, or `FailureCode ==
TempoFailed`) — delivery is ordered Toggl → Jira → Tempo, so getting to Tempo proves Jira validated
regardless of what Tempo then did.

**Review is one grid.** A tick box, Jira key, description, duration, and three dots (Toggl / Jira /
Tempo — grey pending, green delivered, red failed) per row. A failed row shows its reason in a row
details section underneath. `ReviewTaskViewModel.CanSelect` refuses selection once
`DeliveryAttemptStatus.Succeeded` is reached, so an already-delivered row cannot be ticked — the
idempotency guard already refused a second delivery; the UI now stops offering one.

**Batch delivery behind one confirmation.** `Post selected (N)` opens one confirmation
(`BatchConfirmationSummary`) naming the count, total duration, and destinations; `Post N task(s)`
or `Cancel`. Nothing external is written before that second click. `ConfirmPostSelectedAsync`
still calls `IConfirmedTaskDeliveryService.DeliverConfirmedAsync` once per selected row, in order,
untouched in its Toggl → Jira → Tempo sequencing and every idempotency guarantee. A failed row does
not stop the run — `succeeded`/`failed` counters advance and the loop continues to the remaining
rows. Cancelling (`CancelBatchCommand`) sets a `CancellationTokenSource` that is only checked
**before** each task starts; the in-flight call always runs with `CancellationToken.None`, so a
cancel never tears a delivery in half. `BatchStatus` reports the outcome afterwards, e.g. `1
succeeded, 0 failed, 4 not attempted.`

**Guided integration validation moved to Diagnostics**, unchanged in code and behaviour —
`LiveValidationViewModel` and its markup simply live under the Diagnostics page now. Diagnostics is
the one place for "is this working?": the audit log, the guided Toggl/Jira/Tempo checks, and the
diagnostics run. Review is purely the worklist.

**The inline failure reason and its session-only limit.** `ReviewTaskViewModel.FailureText` prefers
`DeliveryAttempt.FailureDetail` — a transient field carrying the service's own message (e.g. `Tempo:
User is invalid`) — for the failure codes `PostAllCoordinator` actually pairs a message with
(`JiraFailed`, `JiraIssueNotFound`, `TempoFailed`), and falls back to the fixed coded reason (e.g.
"Tempo delivery failed.") otherwise. Because `FailureDetail` is deliberately not persisted, a row
that failed in this session shows the real message; a row loaded from the database on a later launch
shows only the coded reason, with the full detail still in the audit log on Diagnostics.

**Every action on Review is audited** under the `Review` category through the existing `IAuditLog`:
page load/refresh (with a count of already-delivered tasks), a Dry Run and whether it found
blockers, a post being requested (before the confirmation is shown), confirmed, cancelled before
delivery, cancelled mid-run, and finished with its succeeded/failed counts. Row selection itself is
not logged — a tick has no effect until the batch is confirmed, and the confirmation entry already
records exactly which tasks were chosen. No credential, settings value, or Slack URI appears in any
of it, matching the rule already in place for [[TS-040-audit-log]].

Dry Run and the daily Slack update are unchanged in behaviour; the Slack section now sits in a
collapsible `Expander` below the grid.

## Tests

- `ReviewTaskViewModelTests`: each mark derives from the right `DeliveryAttempt` field; a failed
  attempt marks the step its `FailureCode` names; an already-delivered row cannot be selected; the
  in-session `FailureDetail` is preferred for Jira/Tempo codes, and a stale detail carried onto a
  `PersistenceFailed` attempt is ignored in favour of the coded reason.
- `ReviewViewModelTests`: batch posts only selected rows, in the plan's order; the confirmation
  summary's count and duration match the selection; a failure part-way leaves earlier rows recorded
  and continues to the remainder; cancelling stops before the next task and never interrupts one in
  flight; no external call happens before `ConfirmPostSelectedCommand`; every audited action in the
  table above is asserted against a fake `IAuditLog`, including that no entry contains a credential,
  settings value, or Slack URI.
- `ReviewView.xaml` markup tests (`XDocument` idiom): assert the grid, the single confirmation
  panel, and the absence of the guided-validation block.
- `LiveValidationViewModelTests` continue to pass against their new home on Diagnostics.

See also [[TS-036-readable-history-and-dead-code-removal]] for the History page this redesign leaves
untouched, and [[TS-040-audit-log]] for the audit log and Diagnostics page this redesign extends.
