[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$dotnetPath = if (Get-Command dotnet -ErrorAction SilentlyContinue) { 'dotnet' } elseif (Test-Path 'C:\Program Files\dotnet\dotnet.exe') { 'C:\Program Files\dotnet\dotnet.exe' } else { throw 'The .NET 10 SDK is required.' }
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\GDK.TimeSync.Desktop\GDK.TimeSync.Desktop.csproj'
$output = Join-Path $projectRoot "artifacts\GDK.TimeSync-$RuntimeIdentifier"

& $dotnetPath publish $project --configuration $Configuration --runtime $RuntimeIdentifier --self-contained true `
    -p:PublishSingleFile=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -p:DebugSymbols=false -p:CopyOutputSymbolsToPublishDirectory=false --output $output

if ($LASTEXITCODE -ne 0) {
    throw 'Desktop publish failed.'
}

Write-Host "Self-contained executable published to $output"