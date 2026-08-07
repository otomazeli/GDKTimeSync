# Desktop publishing

Run `./scripts/publish-desktop.ps1` from a PowerShell prompt. It publishes one self-contained `GDK.TimeSync.exe` for Windows x64 to `artifacts/GDK.TimeSync-win-x64`.

The executable includes the .NET runtime and WPF native dependencies, so .NET does not need to be installed on the target computer. Configuration and credentials remain per-user under `%LOCALAPPDATA%` and Windows Credential Manager.