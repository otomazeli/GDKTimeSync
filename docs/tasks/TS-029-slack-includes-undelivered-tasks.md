# TS-029 — Compose the Slack digest even when tasks haven't posted to Jira/Tempo yet

## Status

Implemented.

## Objective

TS-009's original design excluded any task without a `Succeeded` Jira/Tempo delivery from the daily Slack update entirely, and blocked composition down to only the successfully-delivered subset. The user asked for this to change: the Slack update should always be composable regardless of Jira/Tempo delivery status, with undelivered tasks included and clearly marked rather than silently dropped — so a Jira/Tempo outage never blocks the daily Slack summary.

## Scope

- `SlackDailyCompletedItem` (`src/GDK.TimeSync.Slack/SlackDailyUpdate.cs`) gained `PostedToJira` (defaults `true`, so existing callers are unaffected).
- `SlackDailyUpdateComposer.Compose`: a task line gets `" (not posted in Jira)"` appended when `PostedToJira` is `false`; every task is still included in the digest, none are dropped.
- `ReviewViewModel.ComposeSlackPreviewAsync`: every plan item is now added to the composed list (previously only `Succeeded`-with-a-Tempo-worklog items were). `PostedToJira` is computed per item from the existing delivery-attempt lookup. The old per-item "excluded from Slack" blocker became a single summary blocker ("N task(s) not yet posted to Jira/Tempo are included, marked..."), still informational only — it was never used to gate the confirm button before, and still isn't.
- The "no tasks" fallback message no longer references Tempo specifically, since a plan can now compose with zero Tempo-succeeded tasks — it's empty only when there are literally no plan items.

## Safety boundaries

- No change to the existing duplicate/idempotency safeguards (`ContentFingerprint`, `IDailySlackDeliveryRepository.TryClaimAsync`) — those still gate the actual send regardless of what's in the composed text.
- No change to per-task Jira/Tempo delivery itself — this only affects what the Slack summary includes and how it's labeled; a task still needs its own explicit confirmation to actually post to Toggl/Jira/Tempo.

## Verification

- `SlackDailyUpdateComposerTests`: a mixed batch of posted/not-posted items keeps both in the output, with only the not-posted one annotated.
- `ReviewViewModelTests`: a plan with zero delivered tasks still composes and remains send-eligible (`CanConfirmSlack` true); a mixed batch keeps both tasks in the digest with the correct one marked, and still surfaces a single informational blocker.
- Full Release build (0 warnings/errors) and full test suite green (321/321).
