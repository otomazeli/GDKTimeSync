[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet -and (Test-Path 'C:\Program Files\dotnet\dotnet.exe')) {
    $dotnet = Get-Item 'C:\Program Files\dotnet\dotnet.exe'
}

if (-not $dotnet) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw 'Install .NET 10 SDK manually: https://dotnet.microsoft.com/download/dotnet/10.0'
    }

    winget install --id Microsoft.DotNet.SDK.10 --exact --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw 'The .NET 10 SDK installation failed.'
    }

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet -and (Test-Path 'C:\Program Files\dotnet\dotnet.exe')) {
        $dotnet = Get-Item 'C:\Program Files\dotnet\dotnet.exe'
    }
}

$dotnetPath = if ($dotnet -is [System.Management.Automation.CommandInfo]) { $dotnet.Source } else { $dotnet.FullName }

if (-not $dotnetPath -or ([version](& $dotnetPath --version)).Major -ne 10) {
    throw 'The .NET 10 SDK is required.'
}

$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & $dotnetPath restore GDK.TimeSync.slnx
    & $dotnetPath build GDK.TimeSync.slnx --no-restore
    & $dotnetPath test GDK.TimeSync.slnx --no-restore
}
finally {
    Pop-Location
}

Write-Host 'Setup complete. Before connecting to Jira, set Jira__PersonalAccessToken in the environment.'
