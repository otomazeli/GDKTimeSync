[CmdletBinding()]
param(
    [switch]$RemoveUserData,
    [switch]$RemoveCredentials,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-GdkPaths {
    $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $dataRoot = Join-Path $localAppData 'GDK\TimeSync'
    $desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    $programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    $startup = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
    if ([string]::IsNullOrWhiteSpace($desktop)) { $desktop = Join-Path $userProfile 'Desktop' }
    if ([string]::IsNullOrWhiteSpace($programs)) { $programs = Join-Path $localAppData 'Microsoft\Windows\Start Menu\Programs' }
    if ([string]::IsNullOrWhiteSpace($startup)) { $startup = Join-Path $programs 'Startup' }
    [pscustomobject]@{
        Application = Join-Path $userProfile 'GDK-TimeSync'
        Data = $dataRoot
        DesktopShortcut = Join-Path $desktop 'GDK TimeSync.lnk'
        StartMenuShortcut = Join-Path $programs 'GDK TimeSync.lnk'
        StartupShortcut = Join-Path $startup 'GDK TimeSync.lnk'
    }
}

function Remove-PathIfPresent {
    param([string]$Path, [string]$Description)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }
    if ($DryRun) {
        Write-Host "[DRY RUN] Would remove $($Description): $Path"
        return
    }
    Remove-Item -LiteralPath $Path -Recurse -Force
    Write-Host "[OK] Removed $Description"
}

function Remove-GdkCredential {
    param([string]$Target)

    if ($DryRun) {
        Write-Host "[DRY RUN] Would remove Windows Credential Manager entry: $Target"
        return
    }

    & cmdkey.exe "/delete:$Target" 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "[OK] Removed credential: $Target"
    }
}

try {
    $paths = Get-GdkPaths
    Write-Host '==================================================='
    Write-Host ' GDK TimeSync - Current User Removal'
    Write-Host '=================================================='

    Remove-PathIfPresent $paths.Application 'application directory'
    Remove-PathIfPresent $paths.DesktopShortcut 'desktop shortcut'
    Remove-PathIfPresent $paths.StartMenuShortcut 'Start Menu shortcut'
    Remove-PathIfPresent $paths.StartupShortcut 'Startup shortcut'

    if ($RemoveUserData) {
        Remove-PathIfPresent $paths.Data 'application data directory'
    }
    else {
        Write-Host "Preserved application data: $($paths.Data)"
    }

    if ($RemoveCredentials) {
        Remove-GdkCredential 'GDK.TimeSync.Toggl.ApiToken'
        Remove-GdkCredential 'GDK.TimeSync.CGM.JiraPAT'
    }
    else {
        Write-Host 'Preserved Windows Credential Manager credentials.'
    }

    Write-Host 'Removal completed.'
    exit 0
}
catch {
    Write-Host "ERROR:`n$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
