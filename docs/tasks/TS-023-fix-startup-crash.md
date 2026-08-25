# TS-023 — Fix startup crash (missing IConfiguration registration)

## Status

Implemented.

## Objective

The desktop app launched and terminated immediately. Fix it.

## Root cause

Confirmed from the Windows Application/`.NET Runtime` event log for the crashed process:

```
Exception Info: System.InvalidOperationException: No service for type
'Microsoft.Extensions.Configuration.IConfiguration' has been registered.
   at ...Options.OptionsBuilder`1.<>c__DisplayClass9_0`1.<Configure>b__0(IServiceProvider sp)
```

`AddTimeSyncCore()` (`src/GDK.TimeSync.Core/ServiceCollectionExtensions.cs`) registers `IssueKeyValidator` via `AddOptions<IssueKeyValidationOptions>().BindConfiguration("IssueKeyValidation")`, which requires an `IConfiguration` in the container the moment `IOptions<IssueKeyValidationOptions>.Value` is actually evaluated. The WPF app's `App.ConfigureServices` (`src/GDK.TimeSync.Desktop/App.xaml.cs`) builds a bare `ServiceCollection` and never registered one — this gap existed already (any real Jira-client creation, e.g. a Post-all delivery or live diagnostics run, would have hit the same exception whenever first exercised), but stayed latent because `IssueKeyValidator` was previously only ever resolved lazily, on first actual Jira API use.

TS-020 (`OnStartup` now eagerly resolves `ITogglAutoSyncService`, which depends on `MainViewModel`, which depends on `ITogglSyncService`) plus TS-022 (`TogglSyncService` gained `IssueKeyValidator` as a constructor dependency) combined to make this resolve **eagerly, during `OnStartup`, before the main window is shown** — turning a previously-latent gap into an immediate crash on every launch.

## Scope

- `App.ConfigureServices` now registers `services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())` — an empty configuration, since this desktop app has no config file to bind. `BindConfiguration` against a missing section is a no-op, so `IssueKeyValidationOptions` still lands on its existing default pattern (`^[A-Z][A-Z0-9]*-\d+$`); behavior is unchanged, only the previously-missing registration is now present.
- This fixes both the new startup crash and the pre-existing latent gap in the live Jira-client path.

## Safety boundaries

- No behavior change to Jira key validation itself — same default pattern as before, just now actually resolvable.
- No secrets or external calls involved; this is purely a local DI-wiring fix.

## Verification

- Root-caused from the actual Windows Event Log entry for the crashed process (`Get-WinEvent` against `Application Error` / `.NET Runtime` providers) rather than guessing.
- New regression test `AppConfigureServicesTests.ConfigureServices_ResolvesEveryStartupCriticalServiceWithoutThrowing`: builds the exact container `App.ConfigureServices` produces and resolves every service the real startup path touches (plus the rest of the ViewModel/Service graph) — this is the class of bug (a registered service whose *transitive* dependency chain is unregistered) that no prior test caught, since it only surfaces when something actually resolves the type.
- Full Release build (0 warnings/errors) and full test suite green (310/310).
- Republished the self-contained executable and launched it directly: confirmed it stays running (10+ seconds, process alive) instead of crashing immediately, matching the reported symptom.
