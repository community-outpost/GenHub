using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Service that manages pre-flight validation and post-install relocation of the GenHub installation directory,
/// CAS pools, workspaces, and application data.
/// </summary>
public class StorageMigrationService(
    IConfigurationProviderService configurationProvider,
    IUserSettingsService userSettingsService,
    ICasPoolManager casPoolManager,
    ILaunchRegistry launchRegistry,
    IGameProcessManager gameProcessManager,
    IStorageWritabilityProbe writabilityProbe,
    ILogger<StorageMigrationService> logger) : IStorageMigrationService
{
    /// <inheritdoc />
    public async Task<OperationResult<StorageMigrationPreflightResult>> ValidatePreflightAsync(
        string targetPath,
        bool relocateCasAndWorkspace,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return OperationResult<StorageMigrationPreflightResult>.CreateSuccess(new StorageMigrationPreflightResult
                {
                    IsValid = false,
                    ErrorMessage = "Target installation directory path cannot be empty.",
                });
            }

            var normalizedTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));
            var appBaseDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
            var sourceRoot = GetSourceRootDirectory();

            // Check if target is same as current installation root
            if (normalizedTarget.Equals(sourceRoot, PathHelper.PathComparison) ||
                normalizedTarget.Equals(appBaseDir, PathHelper.PathComparison))
            {
                return OperationResult<StorageMigrationPreflightResult>.CreateSuccess(new StorageMigrationPreflightResult
                {
                    IsValid = false,
                    ErrorMessage = "The target directory is the same as the current installation directory.",
                });
            }

            // Check if target is inside the current application directory
            var isTargetInsideApp = IsInsideDirectory(normalizedTarget, sourceRoot) || IsInsideDirectory(normalizedTarget, appBaseDir);
            if (isTargetInsideApp)
            {
                return OperationResult<StorageMigrationPreflightResult>.CreateSuccess(new StorageMigrationPreflightResult
                {
                    IsValid = false,
                    IsTargetInsideApplicationDirectory = true,
                    ErrorMessage = "The target directory cannot be located inside the current installation directory.",
                });
            }

            // Check if current installation directory is inside target (e.g. target is root or parent folder)
            if (IsInsideDirectory(sourceRoot, normalizedTarget) || IsInsideDirectory(appBaseDir, normalizedTarget))
            {
                return OperationResult<StorageMigrationPreflightResult>.CreateSuccess(new StorageMigrationPreflightResult
                {
                    IsValid = false,
                    ErrorMessage = "The target directory cannot be a parent of the current installation directory.",
                });
            }

            // Check write permission at target
            var hasWritePermission = writabilityProbe.CanCreateStorageAt(normalizedTarget);

            // Check for active game launches and running game processes
            var activeLaunches = (await launchRegistry.GetAllActiveLaunchesAsync()).ToList();
            var activeProcessesResult = await gameProcessManager.GetActiveProcessesAsync(cancellationToken);
            var activeProcesses = activeProcessesResult.Success && activeProcessesResult.Data != null
                ? activeProcessesResult.Data
                : [];

            var hasActiveProcesses = activeLaunches.Count > 0 || activeProcesses.Count > 0;
            var processNames = new List<string>();

            foreach (var launch in activeLaunches)
            {
                processNames.Add($"Launch: {launch.ProfileId}");
            }

            foreach (var proc in activeProcesses)
            {
                processNames.Add($"Process: {proc.ProcessName} (PID: {proc.ProcessId})");
            }

            // Calculate required disk space
            long requiredBytes = CalculateDirectorySize(sourceRoot);

            if (relocateCasAndWorkspace)
            {
                var casRoot = configurationProvider.GetCasConfiguration().CasRootPath;
                if (!string.IsNullOrWhiteSpace(casRoot) && Directory.Exists(casRoot) && !IsInsideDirectory(casRoot, sourceRoot))
                {
                    requiredBytes += CalculateDirectorySize(casRoot);
                }

                var workspaceRoot = userSettingsService.Get().WorkspacePath;
                if (!string.IsNullOrWhiteSpace(workspaceRoot) && Directory.Exists(workspaceRoot) && !IsInsideDirectory(workspaceRoot, sourceRoot))
                {
                    requiredBytes += CalculateDirectorySize(workspaceRoot);
                }
            }

            requiredBytes += StorageMigrationConstants.DiskSpaceSafetyMarginBytes;

            // Get available free disk space on the target volume
            long availableBytes = GetAvailableFreeSpace(normalizedTarget);
            var hasSufficientSpace = availableBytes >= requiredBytes;

            // Compose error message if any check failed
            string? errorMessage = null;
            if (!hasWritePermission)
            {
                errorMessage = "The target directory is not writable or cannot be created.";
            }
            else if (hasActiveProcesses)
            {
                errorMessage = "Active game instances or launches are currently running. Please close all running games before migrating.";
            }
            else if (!hasSufficientSpace)
            {
                var reqMb = requiredBytes / ConversionConstants.BytesPerMegabyte;
                var availMb = availableBytes / ConversionConstants.BytesPerMegabyte;
                errorMessage = $"Insufficient free disk space on target volume. Required: {reqMb} MB, Available: {availMb} MB.";
            }

            var isValid = hasWritePermission && !hasActiveProcesses && hasSufficientSpace && !isTargetInsideApp;

            var result = new StorageMigrationPreflightResult
            {
                IsValid = isValid,
                RequiredBytes = requiredBytes,
                AvailableBytes = availableBytes,
                HasSufficientSpace = hasSufficientSpace,
                HasWritePermission = hasWritePermission,
                HasActiveProcesses = hasActiveProcesses,
                ActiveProcessNames = processNames,
                IsTargetInsideApplicationDirectory = isTargetInsideApp,
                ErrorMessage = errorMessage,
            };

            return OperationResult<StorageMigrationPreflightResult>.CreateSuccess(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to perform migration pre-flight checks for target: {TargetPath}", targetPath);
            return OperationResult<StorageMigrationPreflightResult>.CreateFailure($"Pre-flight validation error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> MigrateAsync(
        StorageMigrationRequest request,
        IProgress<StorageMigrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            logger.LogInformation(
                "Starting installation migration to {TargetPath} (RelocateStorage: {RelocateStorage})",
                request.TargetPath,
                request.RelocateCasAndWorkspace);

            // Phase 1: Pre-flight validation
            progress?.Report(new StorageMigrationProgress
            {
                Stage = StorageMigrationConstants.StagePreflight,
                Percentage = 10,
                Message = "Validating target directory and pre-flight constraints...",
            });

            var preflight = await ValidatePreflightAsync(request.TargetPath, request.RelocateCasAndWorkspace, cancellationToken);
            if (!preflight.Success || preflight.Data?.IsValid != true)
            {
                var error = preflight.Data?.ErrorMessage ?? preflight.FirstError ?? "Pre-flight validation failed.";
                logger.LogError("Migration pre-flight validation failed: {Error}", error);
                return OperationResult<bool>.CreateFailure(error);
            }

            var targetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.TargetPath));
            var sourceRoot = GetSourceRootDirectory();

            // Phase 2: Relocate CAS and Workspaces if requested
            if (request.RelocateCasAndWorkspace)
            {
                progress?.Report(new StorageMigrationProgress
                {
                    Stage = StorageMigrationConstants.StageRelocatingStorage,
                    Percentage = 25,
                    Message = "Relocating CAS storage pool and game workspaces...",
                });

                var currentCasRoot = configurationProvider.GetCasConfiguration().CasRootPath;
                var currentWorkspaceRoot = userSettingsService.Get().WorkspacePath;

                var targetDataDir = Path.Combine(targetRoot, DirectoryNames.Data);
                var targetCasRoot = Path.Combine(targetDataDir, DirectoryNames.CasPool);
                var targetWorkspaceRoot = Path.Combine(targetDataDir, DirectoryNames.Workspaces);

                // Move CAS pool if existing and not already inside source root
                if (!string.IsNullOrWhiteSpace(currentCasRoot) && Directory.Exists(currentCasRoot) && !IsInsideDirectory(currentCasRoot, sourceRoot))
                {
                    logger.LogInformation("Moving CAS storage pool from {Source} to {Target}", currentCasRoot, targetCasRoot);
                    MigrateDirectorySafely(currentCasRoot, targetCasRoot);
                }

                progress?.Report(new StorageMigrationProgress
                {
                    Stage = StorageMigrationConstants.StageRelocatingStorage,
                    Percentage = 45,
                    Message = "Relocating game workspaces...",
                });

                // Move workspaces if existing and not already inside source root
                if (!string.IsNullOrWhiteSpace(currentWorkspaceRoot) && Directory.Exists(currentWorkspaceRoot) && !IsInsideDirectory(currentWorkspaceRoot, sourceRoot))
                {
                    logger.LogInformation("Moving workspaces from {Source} to {Target}", currentWorkspaceRoot, targetWorkspaceRoot);
                    MigrateDirectorySafely(currentWorkspaceRoot, targetWorkspaceRoot);
                }

                // Update and persist settings
                await userSettingsService.TryUpdateAndSaveAsync(settings =>
                {
                    settings.CasConfiguration.CasRootPath = targetCasRoot;
                    settings.WorkspacePath = targetWorkspaceRoot;
                    settings.ApplicationDataPath = targetDataDir;
                    return true;
                });

                // Reinitialize CAS pool with the new path
                casPoolManager.ReinitializeInstallationPool();
            }

            // Phase 3: Prepare binary migration and updater script
            progress?.Report(new StorageMigrationProgress
            {
                Stage = StorageMigrationConstants.StagePreparingBinaries,
                Percentage = 65,
                Message = "Staging binary migration helper script...",
            });

            var scriptPath = PrepareMigrationScript(sourceRoot, targetRoot);

            // Phase 4: Launch helper process
            progress?.Report(new StorageMigrationProgress
            {
                Stage = StorageMigrationConstants.StageLaunchingAssistant,
                Percentage = 85,
                Message = "Launching migration assistant process...",
            });

            if (request.LaunchHelperProcess)
            {
                LaunchHelperProcess(scriptPath);
            }

            // Phase 5: Finalize and exit application
            progress?.Report(new StorageMigrationProgress
            {
                Stage = StorageMigrationConstants.StageFinalizing,
                Percentage = 100,
                Message = "Migration staged successfully. GenHub will now restart from the new location.",
            });

            if (request.ExitApplicationOnSuccess)
            {
                ExitApplication();
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Installation migration failed unexpectedly for target {TargetPath}", request.TargetPath);
            return OperationResult<bool>.CreateFailure($"Migration failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the top-level Velopack installation root directory or falls back to AppContext.BaseDirectory.
    /// </summary>
    /// <returns>The source root directory path.</returns>
    internal static string GetSourceRootDirectory()
    {
        var appBaseDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        var parentDir = Directory.GetParent(appBaseDir)?.FullName;

        if (parentDir != null)
        {
            // Check for Velopack markers (Update.exe, packages dir, app-* directories, or companion executable)
            var hasUpdateExe = File.Exists(Path.Combine(parentDir, "Update.exe"));
            var hasPackagesDir = Directory.Exists(Path.Combine(parentDir, "packages"));
            var hasAppDirs = Directory.GetDirectories(parentDir, "app-*").Length > 0;

            if (hasUpdateExe || hasPackagesDir || hasAppDirs)
            {
                return parentDir;
            }
        }

        return appBaseDir;
    }

    /// <summary>
    /// Calculates the relative path of the current process executable from the given root directory.
    /// </summary>
    /// <param name="sourceRoot">The source root directory.</param>
    /// <returns>The relative executable path.</returns>
    internal static string GetRelativeExecutablePath(string sourceRoot)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
        {
            return OperatingSystem.IsWindows() ? "GenHub.Windows.exe" : "GenHub.Linux";
        }

        try
        {
            return Path.GetRelativePath(sourceRoot, processPath);
        }
        catch
        {
            return Path.GetFileName(processPath);
        }
    }

    /// <summary>
    /// Checks whether a path is equal to or contained within a parent directory.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <param name="parentDirectory">The parent directory path.</param>
    /// <returns><see langword="true"/> if the path is inside or equal to the parent directory; otherwise, <see langword="false"/>.</returns>
    internal static bool IsInsideDirectory(string path, string parentDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(parentDirectory))
        {
            return false;
        }

        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentDirectory));

        return normalizedPath.Equals(normalizedParent, PathHelper.PathComparison) ||
               normalizedPath.StartsWith(normalizedParent + Path.DirectorySeparatorChar, PathHelper.PathComparison);
    }

    /// <summary>
    /// Calculates the total size of all files in a directory in bytes.
    /// </summary>
    /// <param name="directoryPath">The directory path.</param>
    /// <returns>Total size in bytes.</returns>
    internal static long CalculateDirectorySize(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        try
        {
            var dirInfo = new DirectoryInfo(directoryPath);
            return dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Gets the available free space on the drive containing the given path.
    /// </summary>
    /// <param name="path">The filesystem path.</param>
    /// <returns>Available free space in bytes.</returns>
    internal static long GetAvailableFreeSpace(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    return drive.AvailableFreeSpace;
                }
            }
        }
        catch
        {
            // Ignore exceptions and assume ample space
        }

        return long.MaxValue;
    }

    /// <summary>
    /// Safely moves a directory with rollback on copy failure.
    /// </summary>
    /// <param name="sourceDir">The source directory path.</param>
    /// <param name="destDir">The destination directory path.</param>
    internal static void MigrateDirectorySafely(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        if (Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }
        else
        {
            try
            {
                Directory.Move(sourceDir, destDir);
                return;
            }
            catch (IOException)
            {
                // Move across volumes or locked directory fallback to copy-then-delete
                Directory.CreateDirectory(destDir);
            }
        }

        CopyDirectoryRecursive(sourceDir, destDir);
        TryDeleteDirectory(sourceDir);
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, destSubDir);
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private string PrepareMigrationScript(string sourceRoot, string targetRoot)
    {
        var isWindows = OperatingSystem.IsWindows();
        var scriptName = isWindows
            ? StorageMigrationConstants.WindowsUpdateScriptName
            : StorageMigrationConstants.LinuxUpdateScriptName;

        var scriptTemplate = GetScriptResource(scriptName) ?? GetFallbackScriptTemplate(isWindows);

        var relativeExe = GetRelativeExecutablePath(sourceRoot);
        var targetExe = Path.Combine(targetRoot, relativeExe);

        var logFile = Path.Combine(Path.GetTempPath(), $"genhub_migration_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
        var backupDir = Path.Combine(Path.GetTempPath(), $"genhub_migration_backup_{Guid.NewGuid():N}");

        var scriptContent = scriptTemplate
            .Replace("{{LOG_FILE}}", logFile, StringComparison.Ordinal)
            .Replace("{{PROCESS_ID}}", Environment.ProcessId.ToString(), StringComparison.Ordinal)
            .Replace("{{SOURCE_DIR}}", sourceRoot, StringComparison.Ordinal)
            .Replace("{{TARGET_DIR}}", targetRoot, StringComparison.Ordinal)
            .Replace("{{CURRENT_EXE}}", targetExe, StringComparison.Ordinal)
            .Replace("{{BACKUP_DIR}}", backupDir, StringComparison.Ordinal);

        var tempDir = Path.Combine(Path.GetTempPath(), $"genhub_migrate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var scriptFilePath = Path.Combine(tempDir, scriptName);
        File.WriteAllText(scriptFilePath, scriptContent);

        if (!isWindows)
        {
            try
            {
                File.SetUnixFileMode(
                    scriptFilePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to set Unix permissions on migration script {Path}", scriptFilePath);
            }
        }

        logger.LogInformation("Migration script generated at {ScriptPath}", scriptFilePath);
        return scriptFilePath;
    }

    private string? GetScriptResource(string scriptName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var resourceNames = assembly.GetManifestResourceNames();
                var match = resourceNames.FirstOrDefault(n => n.EndsWith(scriptName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    using var stream = assembly.GetManifestResourceStream(match);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        return reader.ReadToEnd();
                    }
                }
            }
            catch
            {
                // Continue searching other assemblies
            }
        }

        return null;
    }

    private static string GetFallbackScriptTemplate(bool isWindows)
    {
        if (isWindows)
        {
            return @"# GenHub Windows Update PowerShell Script
$ErrorActionPreference = 'SilentlyContinue'
$LogFile = ""{{LOG_FILE}}""
$ProcessId = {{PROCESS_ID}}
$SourceDir = ""{{SOURCE_DIR}}""
$TargetDir = ""{{TARGET_DIR}}""
$CurrentExe = ""{{CURRENT_EXE}}""
$BackupDir = ""{{BACKUP_DIR}}""

function Write-Log {
    param([string]$Message)
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    ""[$timestamp] $Message"" | Out-File -FilePath $LogFile -Append -Encoding UTF8
}

Write-Log ""GenHub Migration Script Started""
Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction SilentlyContinue
$process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
if ($process) {
    Stop-Process -Id $ProcessId -Force
    Start-Sleep -Seconds 2
}
Get-Process -Name ""GenHub*"" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

try {
    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
    if (Test-Path $TargetDir) {
        Copy-Item -Path ""$TargetDir\*"" -Destination $BackupDir -Recurse -Force
    }
    Copy-Item -Path ""$SourceDir\*"" -Destination $TargetDir -Recurse -Force
    Write-Log ""Migration completed successfully""
    if (Test-Path $CurrentExe) {
        $exeDir = Split-Path -Path $CurrentExe -Parent
        Start-Process -FilePath $CurrentExe -WorkingDirectory $exeDir
    }
}
catch {
    Write-Log ""Migration failed: $($_.Exception.Message)""
    if (Test-Path $BackupDir) {
        Copy-Item -Path ""$BackupDir\*"" -Destination $TargetDir -Recurse -Force
    }
}
finally {
    if (Test-Path $SourceDir) {
        Remove-Item -Path $SourceDir -Recurse -Force
    }
    $updaterDir = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
    Start-Sleep -Seconds 2
    if (Test-Path $updaterDir) {
        Remove-Item -Path $updaterDir -Recurse -Force
    }
}
";
        }

        return @"#!/bin/bash
LOG_FILE=""{{LOG_FILE}}""
PROCESS_ID={{PROCESS_ID}}
SOURCE_DIR=""{{SOURCE_DIR}}""
TARGET_DIR=""{{TARGET_DIR}}""
CURRENT_EXE=""{{CURRENT_EXE}}""
BACKUP_DIR=""{{BACKUP_DIR}}""

write_log() {
    echo ""[$(date '+%Y-%m-%d %H:%M:%S')] $1"" >> ""$LOG_FILE""
}

write_log ""GenHub Linux Migration Script Started""
for i in {1..60}; do
    if ! kill -0 $PROCESS_ID 2>/dev/null; then
        break
    fi
    sleep 1
done

if kill -0 $PROCESS_ID 2>/dev/null; then
    kill -TERM $PROCESS_ID 2>/dev/null
    sleep 2
    kill -KILL $PROCESS_ID 2>/dev/null
fi

pkill -f ""^$CURRENT_EXE\$"" || true
sleep 2

mkdir -p ""$BACKUP_DIR""
if [ -d ""$TARGET_DIR"" ]; then
    cp -r ""$TARGET_DIR""/* ""$BACKUP_DIR"" 2>/dev/null || true
fi

if ! cp -r ""$SOURCE_DIR""/* ""$TARGET_DIR"" 2>&1; then
    write_log ""Error: Failed to copy migration files""
    if [ -d ""$BACKUP_DIR"" ]; then
        cp -r ""$BACKUP_DIR""/* ""$TARGET_DIR"" 2>/dev/null || true
    fi
    exit 1
fi

if [ -f ""$CURRENT_EXE"" ]; then
    EXE_DIR=$(dirname ""$CURRENT_EXE"")
    EXE_NAME=$(basename ""$CURRENT_EXE"")
    cd ""$EXE_DIR""
    if [ ! -x ""$EXE_NAME"" ]; then
        chmod +x ""$EXE_NAME""
    fi
    nohup ""./$EXE_NAME"" > /dev/null 2>&1 &
fi

rm -rf ""$SOURCE_DIR"" 2>/dev/null || true
UPDATER_DIR=$(dirname ""$0"")
sleep 2
rm -rf ""$UPDATER_DIR"" 2>/dev/null || true
";
    }

    private void LaunchHelperProcess(string scriptPath)
    {
        try
        {
            ProcessStartInfo startInfo;
            if (OperatingSystem.IsWindows())
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -NoProfile -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
            }
            else
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
            }

            Process.Start(startInfo);
            logger.LogInformation("Started detached helper migration process: {ScriptPath}", scriptPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start helper migration process for {ScriptPath}", scriptPath);
            throw;
        }
    }

    private void ExitApplication()
    {
        try
        {
            logger.LogInformation("Exiting GenHub to allow migration helper script to proceed.");
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown(0);
            }
            else
            {
                Environment.Exit(0);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Exception during application exit for migration; calling Environment.Exit");
            Environment.Exit(0);
        }
    }
}
