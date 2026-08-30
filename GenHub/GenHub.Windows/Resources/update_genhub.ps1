# GenHub Windows Update PowerShell Script
param(
    [string]$ProcessId = "{{PROCESS_ID}}",
    [string]$SourceDir = "{{SOURCE_DIR}}",
    [string]$TargetDir = "{{TARGET_DIR}}",
    [string]$CurrentExe = "{{CURRENT_EXE}}",
    [string]$LogFile = "{{LOG_FILE}}",
    [string]$BackupDir = "{{BACKUP_DIR}}"
)

$ErrorActionPreference = 'SilentlyContinue'

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
try {
    Write-Log "Creating backup directory: $BackupDir"
    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
    
    Write-Log "Backing up existing files..."
    if (Test-Path $TargetDir) {
        Copy-Item -Path "$TargetDir\*" -Destination $BackupDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    
    Write-Log "Copying new files from $SourceDir to $TargetDir"
    Copy-Item -Path "$SourceDir\*" -Destination $TargetDir -Recurse -Force -ErrorAction Stop
    
    Write-Log "Update completed successfully"
    
    # Only delete source directory on verified success
    if (Test-Path $SourceDir) {
        Remove-Item -Path $SourceDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Log "Starting updated application: $CurrentExe"
    if (Test-Path $CurrentExe) {
        # Set working directory to the application's directory before starting
        $exeDir = Split-Path -Path $CurrentExe -Parent
        Start-Process -FilePath $CurrentExe -WorkingDirectory $exeDir
        Write-Log "Application started successfully"
    } else {
        Write-Log "Warning: Updated executable not found: $CurrentExe"
    }
}
catch {
    Write-Log "Update failed: $($_.Exception.Message)"
    Write-Log "Attempting to restore backup..."
    if (Test-Path $BackupDir) {
        Remove-Item -Path "$TargetDir\*" -Recurse -Force -ErrorAction SilentlyContinue
        Copy-Item -Path "$BackupDir\*" -Destination $TargetDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Log "Backup restored successfully"
    }
}
finally {
    # Self-destruct the updater script's parent directory
    $updaterDir = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
    Start-Sleep -Seconds 2
    if (Test-Path $updaterDir) {
        Remove-Item -Path $updaterDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Log "Windows update script completed"
