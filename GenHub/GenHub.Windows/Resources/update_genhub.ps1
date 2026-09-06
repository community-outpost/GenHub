# GenHub Windows Update PowerShell Script
param(
    [string]$ProcessId = "{{PROCESS_ID}}",
    [string]$SourceDir = "{{SOURCE_DIR}}",
    [string]$TargetDir = "{{TARGET_DIR}}",
    [string]$CurrentExe = "{{CURRENT_EXE}}",
    [string]$LogFile = "{{LOG_FILE}}",
    [string]$BackupDir = "{{BACKUP_DIR}}"
)

function Write-Log {
    param([string]$Message)
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    "[$timestamp] $Message" | Out-File -FilePath $LogFile -Append -Encoding UTF8
}

Write-Log "GenHub Windows Update Script Started"
Write-Log "Waiting for main application (PID: $ProcessId) to close..."

# Wait for the main process to exit
Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction SilentlyContinue

# Force terminate if still running
$process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
if ($process) {
    Write-Log "Timeout waiting for main process. Attempting to terminate..."
    Stop-Process -Id $ProcessId -Force
    Start-Sleep -Seconds 2
}

Write-Log "Ensuring all GenHub processes are closed..."
Get-Process -Name "GenHub*" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Write-Log "Starting file replacement..."
$updateSuccess = $false
$excluded = @(
    'settings.json',
    'Profiles',
    'Manifests',
    'UserData',
    'workspaces.json',
    'logs',
    'Logs',
    'Data',
    'cas-pool',
    'Workspaces',
    'Cache',
    'cache',
    'upload_history.json',
    'MapPacks',
    'mappacks',
    '.genhub-cas'
)

try {
    Write-Log "Ensuring target directory exists: $TargetDir"
    if (-not (Test-Path $TargetDir)) {
        New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    }

    Write-Log "Creating backup directory: $BackupDir"
    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
    
    Write-Log "Backing up existing files..."
    if (Test-Path $TargetDir) {
        $existingItems = Get-ChildItem -Path $TargetDir -Force -ErrorAction SilentlyContinue
        if ($null -ne $existingItems -and $existingItems.Count -gt 0) {
            Copy-Item -Path "$TargetDir\*" -Destination $BackupDir -Recurse -Force -ErrorAction Stop
        }
    }
    
    Write-Log "Copying application binaries from $SourceDir to $TargetDir"
    Get-ChildItem -Path $SourceDir -Force | Where-Object { $excluded -notcontains $_.Name } | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $TargetDir -Recurse -Force -ErrorAction Stop
    }
    
    Write-Log "Update files copied successfully"
    
    Write-Log "Starting updated application: $CurrentExe"
    if (-not (Test-Path $CurrentExe)) {
        throw "Updated executable not found: $CurrentExe"
    }

    # Set working directory to the application's directory before starting
    $exeDir = Split-Path -Path $CurrentExe -Parent
    $proc = Start-Process -FilePath $CurrentExe -WorkingDirectory $exeDir -PassThru -ErrorAction Stop
    if ($null -eq $proc) {
        throw "Failed to start updated application: process could not be launched"
    }
    Start-Sleep -Seconds 1
    for ($i = 0; $i -lt 5; $i++) {
        if ($proc.HasExited) {
            throw "Application exited prematurely after launch with exit code $($proc.ExitCode)"
        }
        Start-Sleep -Seconds 1
    }
    Write-Log "Application started and verified running (PID: $($proc.Id))"

    # Clean up migrated application binaries from source, preserving user data
    if (Test-Path $SourceDir) {
        Get-ChildItem -Path $SourceDir -Force | Where-Object { $excluded -notcontains $_.Name } | ForEach-Object {
            Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
        # Remove source directory only if it is now completely empty
        $remaining = Get-ChildItem -Path $SourceDir -Force -ErrorAction SilentlyContinue
        if ($null -eq $remaining -or $remaining.Count -eq 0) {
            Remove-Item -Path $SourceDir -Force -ErrorAction SilentlyContinue
        }
    }
    $updateSuccess = $true
}
catch {
    Write-Log "Update failed: $($_.Exception.Message)"
    Write-Log "Attempting to restore backup..."
    if (Test-Path $BackupDir) {
        $backupItems = Get-ChildItem -Path $BackupDir -Force -ErrorAction SilentlyContinue
        if ($null -ne $backupItems -and $backupItems.Count -gt 0) {
            Remove-Item -Path "$TargetDir\*" -Recurse -Force -ErrorAction SilentlyContinue
            Copy-Item -Path "$BackupDir\*" -Destination $TargetDir -Recurse -Force -ErrorAction SilentlyContinue
            Write-Log "Backup restored successfully"
        }
    }
}
finally {
    # Self-destruct the updater script's parent directory only on verified success
    $updaterDir = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
    if ($updateSuccess) {
        Start-Sleep -Seconds 2
        if (Test-Path $updaterDir) {
            Remove-Item -Path $updaterDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    } else {
        Write-Log "Preserving backup and temporary updater directory for recovery: $updaterDir"
    }
}

Write-Log "Windows update script completed"
