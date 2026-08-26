[CmdletBinding()]
param(
    [string]$SourceExe,
    [switch]$CreateDesktopShortcut,
    [switch]$CreateStartMenuShortcut,
    [switch]$EnableAutoStart,
    [switch]$DisableAutoStart,
    [switch]$Launch,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-SetupException {
    param([string]$Message, [int]$ExitCode)

    $exception = [System.InvalidOperationException]::new($Message)
    $exception.Data['ExitCode'] = $ExitCode
    return $exception
}

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
        UserProfile = $userProfile
        Application = Join-Path $userProfile 'GDK-TimeSync'
        Executable = Join-Path $userProfile 'GDK-TimeSync\GDK.TimeSync.exe'
        Data = $dataRoot
        Settings = Join-Path $dataRoot 'settings.json'
        DataDirectory = Join-Path $dataRoot 'data'
        Logs = Join-Path $dataRoot 'logs'
        Backup = Join-Path $dataRoot 'backup'
        # The app stores its SQLite database (planned items, delivery history, daily Slack
        # delivery state) under "GDK TimeSync" (with a space), a sibling of "GDK\TimeSync"
        # above where settings.json lives -- see GDK.TimeSync.Desktop/App.xaml.cs. Both
        # locations must be covered for backup/removal to be complete.
        DatabaseDirectory = Join-Path $localAppData 'GDK TimeSync'
        Database = Join-Path $localAppData 'GDK TimeSync\timesync.db'
        Desktop = $desktop
        Programs = $programs
        Startup = $startup
    }
}

function Write-SetupLog {
    param([string]$Message)

    $line = '{0:O} {1}' -f (Get-Date), $Message
    Write-Host $Message
    if (-not $DryRun -and $script:Paths) {
        [IO.File]::AppendAllText((Join-Path $script:Paths.Logs 'setup.log'), "$line`r`n", [Text.UTF8Encoding]::new($false))
    }
}

function Test-SystemArchitecture {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    if ($architecture -ne 'X64') {
        throw (New-SetupException "This GDK TimeSync build targets Windows x64. A compatible application build is required for this computer. Detected: $architecture." 3)
    }
}

function Resolve-SourceExecutable {
    param([string]$RequestedSource)

    $candidates = [Collections.Generic.List[string]]::new()
    if ($RequestedSource) {
        $candidates.Add([IO.Path]::GetFullPath($RequestedSource))
    }
    else {
        $candidates.Add((Join-Path $PSScriptRoot 'GDK.TimeSync.exe'))
        $repositoryRoot = Split-Path -Parent $PSScriptRoot
        $candidates.Add((Join-Path $repositoryRoot 'artifacts\CGM-Windows-x64\GDK.TimeSync.exe'))
        $candidates.Add((Join-Path $repositoryRoot 'artifacts\GDK.TimeSync-win-x64\GDK.TimeSync.exe'))
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw (New-SetupException "GDK.TimeSync.exe could not be found.`n`nSpecify it using:`n`n.\setup-current-user.ps1 -SourceExe `"C:\path\GDK.TimeSync.exe`"" 2)
}

function Initialize-Directories {
    param($Paths)

    $directories = @($Paths.Application, $Paths.Data, $Paths.DataDirectory, $Paths.Logs, $Paths.Backup, $Paths.DatabaseDirectory)
    if ($DryRun) {
        $directories | ForEach-Object { Write-Host "[DRY RUN] Would ensure directory: $_" }
        return
    }

    foreach ($directory in $directories) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    Write-SetupLog '[OK] Application, data, logs, and backup directories'
}

function Test-DirectoryWriteAccess {
    param([string]$Path, [int]$ExitCode)

    if ($DryRun) {
        Write-Host "[DRY RUN] Would verify write access: $Path"
        return
    }

    $testFile = Join-Path $Path ('.gdk-timesync-{0}.tmp' -f [Guid]::NewGuid().ToString('N'))
    try {
        [IO.File]::WriteAllText($testFile, 'GDK TimeSync write test', [Text.UTF8Encoding]::new($false))
        if ([IO.File]::ReadAllText($testFile) -ne 'GDK TimeSync write test') {
            throw 'The write verification content did not match.'
        }
    }
    catch {
        throw (New-SetupException "The current user cannot write to: $Path" $ExitCode)
    }
    finally {
        if (Test-Path -LiteralPath $testFile) {
            Remove-Item -LiteralPath $testFile -Force
        }
    }
}

function Get-DefaultSettingsJson {
    $settings = [ordered]@{
        JiraBaseUrl = 'https://jira.cgm.ag'
        TogglWorkspaceId = $null
        AutoSyncEnabled = $true
        SyncIntervalMinutes = 15
    }
    return ($settings | ConvertTo-Json -Depth 3)
}

function Write-DefaultSettings {
    param([string]$SettingsPath)

    [IO.File]::WriteAllText($SettingsPath, (Get-DefaultSettingsJson), [Text.UTF8Encoding]::new($false))
}

function Initialize-Settings {
    param($Paths)

    if (-not (Test-Path -LiteralPath $Paths.Settings)) {
        if ($DryRun) {
            Write-Host "[DRY RUN] Would create default settings: $($Paths.Settings)"
        }
        else {
            Write-DefaultSettings $Paths.Settings
            Write-SetupLog '[OK] Default settings created'
        }
        return
    }

    try {
        Get-Content -LiteralPath $Paths.Settings -Raw | ConvertFrom-Json -ErrorAction Stop | Out-Null
        Write-SetupLog 'Existing GDK TimeSync settings preserved.'
    }
    catch {
        $backupPath = Join-Path $Paths.Backup ('settings-{0:yyyyMMdd-HHmmss}.json' -f (Get-Date))
        if ($DryRun) {
            Write-Host "[DRY RUN] Invalid settings would be backed up to: $backupPath"
            Write-Host "[DRY RUN] Default settings would replace: $($Paths.Settings)"
        }
        else {
            Copy-Item -LiteralPath $Paths.Settings -Destination $backupPath
            Write-DefaultSettings $Paths.Settings
            Write-SetupLog "WARNING: Invalid settings were backed up to $backupPath and replaced with defaults."
        }
    }
}

function Install-PortableExecutable {
    param([string]$Source, $Paths)

    $sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    $action = 'copied'
    if (Test-Path -LiteralPath $Paths.Executable) {
        $targetHash = (Get-FileHash -LiteralPath $Paths.Executable -Algorithm SHA256).Hash
        if ($sourceHash -eq $targetHash) {
            Write-SetupLog 'GDK TimeSync is already up to date.'
            return $sourceHash
        }

        $backupPath = Join-Path $Paths.Backup ('GDK.TimeSync-{0:yyyyMMdd-HHmmss}.exe' -f (Get-Date))
        if ($DryRun) {
            Write-Host "[DRY RUN] Would back up $($Paths.Executable) to $backupPath"
            Write-Host "[DRY RUN] Would copy $Source to $($Paths.Executable)"
            return $sourceHash
        }
        Copy-Item -LiteralPath $Paths.Executable -Destination $backupPath
        $action = 'upgraded'
    }
    elseif ($DryRun) {
        Write-Host "[DRY RUN] Would copy $Source to $($Paths.Executable)"
        return $sourceHash
    }

    Copy-Item -LiteralPath $Source -Destination $Paths.Executable -Force
    Write-SetupLog "[OK] GDK.TimeSync.exe $action"
    return $sourceHash
}

function New-UserShortcut {
    param([string]$Directory, [string]$Name, $Paths)

    $shortcutPath = Join-Path $Directory "$Name.lnk"
    if ($DryRun) {
        Write-Host "[DRY RUN] Would create shortcut: $shortcutPath"
        return
    }

    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $Paths.Executable
    $shortcut.WorkingDirectory = $Paths.Application
    $shortcut.IconLocation = $Paths.Executable
    $shortcut.Save()
    Write-SetupLog "[OK] Shortcut created: $shortcutPath"
}

function Remove-UserShortcut {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }
    if ($DryRun) {
        Write-Host "[DRY RUN] Would remove shortcut: $Path"
        return
    }
    Remove-Item -LiteralPath $Path -Force
    Write-SetupLog "[OK] Shortcut removed: $Path"
}

function Start-GdkTimeSync {
    param($Paths)

    if ($DryRun) {
        Write-Host "[DRY RUN] Would launch: $($Paths.Executable)"
        return
    }
    try {
        Start-Process -FilePath $Paths.Executable -ErrorAction Stop
        Write-SetupLog '[OK] GDK TimeSync launched'
    }
    catch {
        throw (New-SetupException "GDK TimeSync could not be started.`n`nIf this computer is protected by CGM AppLocker, WDAC, SmartScreen, or endpoint security, contact CGM IT." 1)
    }
}

function Invoke-CurrentUserSetup {
    $script:Paths = Get-GdkPaths
    Write-Host '==================================================='
    Write-Host ' GDK TimeSync - Current User Setup'
    Write-Host '==================================================='
    Write-Host "User: $([Environment]::UserName)"
    Write-Host "Application: $($script:Paths.Application)"
    Write-Host "Data: $($script:Paths.Data)"

    Test-SystemArchitecture
    $source = Resolve-SourceExecutable $SourceExe
    Write-Host "Source: $source"
    Initialize-Directories $script:Paths
    Test-DirectoryWriteAccess $script:Paths.Application 4
    Test-DirectoryWriteAccess $script:Paths.Data 5
    Test-DirectoryWriteAccess $script:Paths.DatabaseDirectory 6
    if (-not $DryRun) { Write-SetupLog '[OK] Write permissions verified' }
    $hash = Install-PortableExecutable $source $script:Paths
    Initialize-Settings $script:Paths

    if ($CreateDesktopShortcut) { New-UserShortcut $script:Paths.Desktop 'GDK TimeSync' $script:Paths }
    if ($CreateStartMenuShortcut) { New-UserShortcut $script:Paths.Programs 'GDK TimeSync' $script:Paths }
    $startupShortcut = Join-Path $script:Paths.Startup 'GDK TimeSync.lnk'
    if ($EnableAutoStart) { New-UserShortcut $script:Paths.Startup 'GDK TimeSync' $script:Paths }
    if ($DisableAutoStart) { Remove-UserShortcut $startupShortcut }
    if ($Launch) { Start-GdkTimeSync $script:Paths }

    if ($DryRun) {
        Write-Host "[DRY RUN] Target executable: $($script:Paths.Executable)"
        Write-Host "[DRY RUN] Settings: $($script:Paths.Settings)"
        Write-Host "[DRY RUN] Database: $($script:Paths.Database)"
        return
    }

    $file = Get-Item -LiteralPath $script:Paths.Executable
    Write-SetupLog "Application: $($script:Paths.Executable)"
    Write-SetupLog "Database: $($script:Paths.Database)"
    Write-SetupLog "SHA256: $hash"
    Write-SetupLog ('Size: {0:N2} MB' -f ($file.Length / 1MB))
    Write-SetupLog "Version: $($file.VersionInfo.FileVersion)"
    Write-Host 'Credentials: Not configured. Configure Toggl and Jira in GDK TimeSync after launch.'
    Write-Host '==================================================='
    Write-Host 'Setup completed successfully.'
    Write-Host "Application: $($script:Paths.Executable)"
    Write-Host '==================================================='
}

try {
    Invoke-CurrentUserSetup
    exit 0
}
catch {
    Write-Host "ERROR:`n$($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Data.Contains('ExitCode')) { exit [int]$_.Exception.Data['ExitCode'] }
    exit 1
}
