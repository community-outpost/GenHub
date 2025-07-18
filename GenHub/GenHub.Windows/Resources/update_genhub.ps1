# GenHub Windows Update PowerShell Script
$ErrorActionPreference = 'SilentlyContinue'

# Parameters are placeholders to be replaced by the application
$LogFile = "{{LOG_FILE}}"
$ProcessId = {{PROCESS_ID}}
$SourceDir = "{{SOURCE_DIR}}"
$TargetDir = "{{TARGET_DIR}}"
$CurrentExe = "{{CURRENT_EXE}}"
$BackupDir = "{{BACKUP_DIR}}"

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
        Copy-Item -Path "$TargetDir\*" -Destination $BackupDir -Recurse -Force
    }
    
    Write-Log "Copying new files from $SourceDir to $TargetDir"
    Copy-Item -Path "$SourceDir\*" -Destination $TargetDir -Recurse -Force
    
    Write-Log "Update completed successfully"
    
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
        Copy-Item -Path "$BackupDir\*" -Destination $TargetDir -Recurse -Force
        Write-Log "Backup restored successfully"
    }
}
finally {
    Write-Log "Cleaning up..."
    if (Test-Path $SourceDir) {
        Remove-Item -Path $SourceDir -Recurse -Force
    }
    
    # Self-destruct the updater script's parent directory
    $updaterDir = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
    Start-Sleep -Seconds 2
    if (Test-Path $updaterDir) {
        Remove-Item -Path $updaterDir -Recurse -Force
    }
}

Write-Log "Windows update script completed"
