# TS-037 — Hide the AI draft button until opted in, and fix stale Slack/auto-sync labels

## Status

Implemented.

## Objective

Remaining findings from the same source review that produced [[TS-036-readable-history-and-dead-code-removal]]:

1. Today's "Draft AI description" button was always visible, but `App.xaml.cs` registers
   `UnavailableAssistedTextGenerator`, which unconditionally answers "AI provider is not
   configured." So the button could never do anything for anyone.
2. The settings window and the user guide still called the Slack credential a "GDK Slack Incoming
   Webhook" — [[TS-027-slack-workflow-builder-webhook]] established that a classic Incoming Webhook
   is exactly what does *not* work here, and that the user's original failure was pasting a
   shortcut/link-trigger URL.
3. The user guide claimed there was no settings toggle for background Toggl sync and told the
   reader to hand-edit `settings.json` — `SettingsWindow.xaml` has had both controls since TS-020.

## Task 1 — Gate the AI button on the opt-in

The roadmap's Milestone 6 deliberately ships no AI provider ("Do not add an external AI provider
until the user selects and approves one"), so the fix is to stop advertising the button rather than
to add a provider or delete the consent flow.

- `TodayViewModel.IsAiEnabled` — reads `IAiConsentService.IsEnabled` (i.e. the `AiEnabled` setting)
  live; false when no consent service is wired up at all.
- `TodayView.xaml` — the button's `Visibility` binds to it. A default install now shows no button;
  ticking "Enable optional AI assistance" reveals it, and the consent flow behind it is unchanged.
- `TodayViewModel.RefreshAiAvailability()` + one line in `ShellViewModel.NavigateAsync` — Settings
  is a separate dialog that pushes nothing back into Today, and navigating to Today is the only way
  back to the page after saving, so re-asking on that navigation is enough.

## Task 2 — Correct the labels

- `SettingsWindow.xaml`: field renamed to "GDK Slack Workflow Builder webhook URL", with a hint
  naming the `hooks.slack.com/triggers/…` shape and warning that `slack.com/shortcuts/…` will fail.
- `docs/user-guide.md`: same correction; the auto-sync paragraph now points at the two existing
  settings controls; the AI section states that the button is hidden until opted in and that no
  provider is configured in this build.

Historical planning docs (`docs/superpowers/**`) still say "Incoming Webhook" and were left as-is —
they record what was planned, and TS-027 records the change.

## Tests

`TodayViewModelTests.IsAiEnabled_FollowsTheConsentServiceSoTheDraftButtonIsHiddenWhenAiIsOff` and
`IsAiEnabled_IsFalseWhenNoConsentServiceIsWiredUp`. 351/351 pass; Release build clean, which also
compiles both edited XAML files.
