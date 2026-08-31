# Recovery and reconciliation

GDK TimeSync records every task delivery and every daily Slack update locally before and after
each write, so it can tell you what it knows rather than guess. This page explains what that
recorded state means, and what to do when a delivery didn't fully succeed.

## Where the state lives

- **History page** -- the readable view of every task delivery attempt ever recorded.
- `%LOCALAPPDATA%\GDK TimeSync\timesync.db` -- the SQLite database backing History, the local
  plan, templates, and the daily Slack delivery record. Note this is a different folder from
  `%LOCALAPPDATA%\GDK\TimeSync\settings.json`, which holds your non-secret settings.
- Windows Credential Manager -- the Toggl token, Jira PAT, and Slack webhook. Not covered by any
  backup below; re-enter them if you move to a new machine or profile.

Close GDK TimeSync before backing up or inspecting the database file, and before running any of
the SQL in this page -- the app does not expect another writer touching it concurrently. A free
tool such as [DB Browser for SQLite](https://sqlitebrowser.org/) or the `sqlite3` command-line
tool can open `timesync.db` directly.

## Task delivery: reading a History row

Each row is one planned item's delivery attempt: the plan date and Jira key/description of the
task it belongs to, a Toggl entry ID, a Tempo worklog ID (once posted), a status, and a failure
reason. Rows are listed newest day first. Posting a task always happens in the same order --
**Toggl, then Jira validation, then Tempo** -- and each step's result is saved before the next
step runs, so a row always tells you exactly how far a task got.

| Status | Meaning |
| --- | --- |
| Succeeded | Fully delivered: Toggl entry and Tempo worklog both exist. Nothing to do. |
| Failed | Delivery stopped at the step named by the failure reason (below). **The app does not automatically retry a failed item** -- clicking "Post task" again on it returns the same stored result without contacting Toggl, Jira, or Tempo again. |
| Cancelled | You cancelled, or closed the app, while a delivery was in flight. Treat it like Failed for recovery purposes; check whether the Toggl entry ID is set before retrying (see below). |
| Reconciliation required | The app could not safely persist what actually happened -- see [Reconciliation required](#reconciliation-required) below. |

Failure reasons:

| Failure reason | What happened |
| --- | --- |
| Toggl delivery failed | The Toggl API call itself failed. No Toggl entry was created. |
| Jira delivery failed | The Toggl entry was created, then the Jira issue lookup failed (connectivity, credentials, or permissions). |
| Jira issue was not found | The Toggl entry was created, then the Jira issue key on the task didn't resolve to a real Jira issue. |
| Tempo delivery failed | The Toggl entry was created and the Jira issue resolved, then creating the Tempo worklog failed. |
| Delivery state could not be saved | See [Reconciliation required](#reconciliation-required). |
| Delivery was cancelled | You cancelled, or closed the app, mid-delivery. |

### Recovering a Failed or Cancelled task

First fix the underlying problem: re-check credentials and connectivity on the Review page's
"Run diagnostics" and guided Toggl/Jira/Tempo checks, and check the task's Jira issue key on
Today.

Then look at the History row's **Toggl entry ID**:

- **Empty** (failure reason "Toggl delivery failed", or a cancellation before Toggl ran) -- no
  Toggl entry exists yet, so nothing has been duplicated. It's safe to clear the stuck row and
  retry from the app:

  ```sql
  DELETE FROM delivery_attempts WHERE planned_work_item_id = '<the task's GUID>';
  ```

  Reopen GDK TimeSync and click "Post task" again for that item.

- **Set** (any other failure reason, or a cancellation after Toggl ran) -- a Toggl time entry
  already exists for this task. Deleting the row and retrying through the app would create a
  **second** Toggl entry for the same work, because a locally-created task has no way in the
  current UI to be re-linked to an existing Toggl entry ID. Instead:
  1. Confirm the Toggl entry (use the ID from History) has the right project, comment, and
     duration.
  2. Complete the remaining step by hand in Jira/Tempo (add the Tempo worklog for the resolved
     Jira issue, using the Toggl entry's date, duration, and comment).
  3. Optionally clear the row with the `DELETE` statement above once you've confirmed everything
     is correct, so History no longer shows it as failed.

A History row shows its task's date, Jira key, and description. A row reading "(task no longer in
any plan)" is an attempt whose planned item was since removed -- match it in Toggl by the Toggl
entry ID instead.

### Reconciliation required

This status means the app successfully attempted delivery but then could not safely record the
outcome (for example, the local database was locked or briefly unavailable while writing the
result). Rather than guess and risk creating a duplicate, the app stops touching that item and
flags it instead of automatically retrying.

There is no in-app way to clear this status. To resolve it:

1. Check Toggl and Tempo directly for the task (by date, comment, and Jira issue) to see what
   was actually created.
2. Clean up any duplicate you find.
3. Treat the task as settled once you've confirmed its real state; the History row will keep
   showing "Reconciliation required" until you manually correct or delete it in the database.

There is no guided in-app reconciliation flow; the SQL and manual checks above are the whole
recovery path.

## Daily Slack update: recovering a stuck day

A daily Slack update can be sent at most once per date; the app claims the date before posting so
a retry can never post the message twice. If sending fails or is interrupted after the claim, the
day is marked **Reconciliation required** in `daily_slack_deliveries`, and Review blocks composing
or sending again for that date -- "A daily Slack delivery already exists and cannot be sent
again."

To recover:

1. **Check the Slack channel first.** If the message arrived, no action is needed -- the block is
   working as intended, preventing a duplicate post.
2. If it did **not** arrive and you want to resend, clear the claim for that date:

   ```sql
   DELETE FROM daily_slack_deliveries WHERE delivery_date = '2026-08-26'; -- yyyy-MM-dd
   ```

   Reopen Review for that day, "Compose daily Slack update", check the preview, then "Send daily
   Slack update".

## Idempotency guarantees you can rely on

- Confirming "Post task" twice on the same item, including two near-simultaneous clicks, results
  in exactly one delivery attempt -- the second call is turned away with "Reconciliation
  required" rather than posting twice, because the app claims the item before writing to Toggl.
- A task whose Toggl entry is already known (imported from Toggl, or previously linked) is never
  reposted to Toggl -- delivery reuses the known entry ID and goes straight to Jira/Tempo.
- Sending the daily Slack update is similarly claimed per calendar date before the Slack API is
  called, so a duplicate click cannot produce two Slack messages for the same day.

None of this backs up or corrects data in Toggl, Jira, Tempo, or Slack themselves -- those systems
are the ultimate source of truth for what was actually recorded there.
