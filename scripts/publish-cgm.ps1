[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$dotnetPath = if (Get-Command dotnet -ErrorAction SilentlyContinue) { 'dotnet' } elseif (Test-Path 'C:\Program Files\dotnet\dotnet.exe') { 'C:\Program Files\dotnet\dotnet.exe' } else { throw 'The .NET 10 SDK is required.' }
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\GDK.TimeSync.Desktop\GDK.TimeSync.Desktop.csproj'
$artifacts = Join-Path $repositoryRoot 'artifacts'
$publishDirectory = Join-Path $artifacts 'CGM-Windows-x64'
$packageDirectory = Join-Path $artifacts 'GDK.TimeSync-CGM-Windows-x64'
$zipPath = Join-Path $artifacts 'GDK.TimeSync-CGM-Windows-x64.zip'

& $dotnetPath publish $project --configuration $Configuration --runtime win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=embedded -p:DebugSymbols=false -p:CopyOutputSymbolsToPublishDirectory=false --output $publishDirectory

if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }

$executable = Join-Path $publishDirectory 'GDK.TimeSync.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'GDK.TimeSync.exe was not produced by publish.' }

if (Test-Path -LiteralPath $packageDirectory) { Remove-Item -LiteralPath $packageDirectory -Recurse -Force }
[IO.Directory]::CreateDirectory($packageDirectory) | Out-Null
Copy-Item -LiteralPath $executable -Destination (Join-Path $packageDirectory 'GDK.TimeSync.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'setup-current-user.ps1') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'remove-current-user.ps1') -Destination $packageDirectory

$readme = @'
GDK TimeSync - CGM Windows x64

1. Extract this ZIP.
2. Open PowerShell in the extracted folder.
3. Run:
   .\setup-current-user.ps1 -CreateDesktopShortcut -Launch
4. In Settings, enter the Jira base URL and workspace details, then add the Toggl API
   token, CGM Jira personal access token, and (optional) GDK Slack Incoming Webhook.
5. On the Review screen, run "Run diagnostics" and the guided Toggl/Jira/Tempo checks.
6. Run Dry Run before posting anything for real.
7. Confirm each task with "Post task", then use "Compose daily Slack update" and "Send
   daily Slack update" once at the end of the day. Nothing is posted automatically.

See docs/user-guide.md in the repository for the full guide and
docs/operations/recovery-and-reconciliation.md for partial-failure recovery.

The setup does not request, store, or pass credentials. It runs only for the current user and does not require administrator rights. To remove GDK TimeSync, run .\remove-current-user.ps1 (add -RemoveUserData -RemoveCredentials to also delete local data and stored credentials).
'@
[IO.File]::WriteAllText((Join-Path $packageDirectory 'README.txt'), $readme, [Text.UTF8Encoding]::new($false))

if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $zipPath -Force
Write-Host "Deployment package created: $zipPath"
