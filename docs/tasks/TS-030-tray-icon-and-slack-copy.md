# TS-030 — Fix tray icon, add Slack message copy-to-clipboard

## Status

Implemented.

## Objective

Two reported gaps: the system tray icon still showed the default Windows application icon instead of the custom `GDK.TimeSync.ico` (added in TS-015), and there was no way to copy the composed daily Slack message out of the app.

## Root cause (tray icon)

TS-015 set `<ApplicationIcon>` in the csproj, which controls the icon Explorer/the taskbar show for the exe — but `TrayIconService` builds its `NotifyIcon` separately in code and had `Icon = SystemIcons.Application` hardcoded, which nothing in TS-015 touched. A `NotifyIcon` never inherits the embedded application icon automatically.

## Scope

- `TrayIconService`: extracts the icon from the running executable itself (`Icon.ExtractAssociatedIcon` against `Application.ExecutablePath`) instead of hardcoding `SystemIcons.Application`, falling back to the system icon if extraction fails for any reason. Reading it from the running exe (rather than a loose `Assets\*.ico` file) works correctly under the self-contained single-file publish, where a loose assets folder isn't guaranteed to exist at runtime.
- New `IClipboardService`/`ClipboardService` (`src/GDK.TimeSync.Desktop/Services/`), wrapping `System.Windows.Clipboard.SetText` behind a testable interface, following this codebase's existing convention for OS-facing services (`ICredentialStore`, `IUserSettingsStore`).
- `ReviewViewModel`: new `SlackPreviewText` (Title + Task heading + Extra lines/task list joined, skipping blanks — the readable reconstruction of what TS-027 split into separate Data Variable fields) and `CopySlackPreviewCommand`, enabled only once a preview has been composed.
- `ReviewView.xaml`: the three separate preview `TextBlock`s (title/heading/extra lines) are replaced with one read-only, selectable, multi-line `TextBox` bound to `SlackPreviewText` — so the message can be selected and copied manually — plus a new "Copy message" button next to "Send"/"Cancel" for a one-click copy.

## Safety boundaries

- Clipboard content is the already-composed, already-safe preview text (title/heading/task lines) — nothing new is exposed that wasn't already visible on screen.
- No change to the tray icon's behavior beyond its visual appearance; the fallback keeps the app usable even if icon extraction fails on some environment.

## Verification

- `ReviewViewModelTests`: `CopySlackPreviewCommand` is disabled before a preview exists, copies the expected reconstructed text after composing, and is re-enabled/disabled correctly as `SlackPreview` changes.
- Tray icon change isn't unit-testable (no prior test coverage existed for `TrayIconService`'s icon either) — verified manually after republishing that the tray shows the custom icon instead of the Windows default.
- Full Release build (0 warnings/errors) and full test suite green (322/322).
