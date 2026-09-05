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
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Service that manages pre-flight validation and post-install relocation of the GenHub installation directory,
/// CAS pools, and workspaces.
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
            var sanityCheck = ValidatePathSanity(targetPath);
            if (!sanityCheck.IsValid)
            {
                return OperationResult<StorageMigrationPreflightResult>.CreateSuccess(sanityCheck);
            }

            var normalizedTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));
            var sourceRoot = GetSourceRootDirectory();

            var hasWritePermission = writabilityProbe.CanCreateStorageAt(normalizedTarget);
            var (hasActiveProcesses, processNames) = await CheckActiveProcessesAsync(cancellationToken);

            var requiredBytes = CalculateRequiredSpace(sourceRoot, relocateCasAndWorkspace);
            var availableBytes = GetAvailableFreeSpace(normalizedTarget);
            var hasSufficientSpace = availableBytes >= requiredBytes;

            var errorMessage = DeterminePreflightErrorMessage(
                hasWritePermission,
                hasActiveProcesses,
                hasSufficientSpace,
                requiredBytes,
                availableBytes);
            var isValid = hasWritePermission && !hasActiveProcesses && hasSufficientSpace;

            var result = new StorageMigrationPreflightResult
            {
                IsValid = isValid,
                RequiredBytes = requiredBytes,
                AvailableBytes = availableBytes,
                HasSufficientSpace = hasSufficientSpace,
                HasWritePermission = hasWritePermission,
                HasActiveProcesses = hasActiveProcesses,
                ActiveProcessNames = processNames,
                IsTargetInsideApplicationDirectory = false,
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

        return await Task.Run(
            async () =>
            {
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
                    if (!preflight.Success || preflight.Data is null || !preflight.Data.IsValid)
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
                            Percentage = 30,
                            Message = "Relocating CAS storage pool and game workspaces...",
                        });

                        var relocated = await RelocateStorageAsync(targetRoot, sourceRoot);
                        if (!relocated)
                        {
                            return OperationResult<bool>.CreateFailure("Failed to update and persist storage configuration during relocation.");
                        }
                    }

                    // Phase 3: Prepare binary migration helper script
                    progress?.Report(new StorageMigrationProgress
                    {
                        Stage = StorageMigrationConstants.StagePreparingBinaries,
                        Percentage = 65,
                        Message = "Staging binary migration helper script...",
                    });

                    var tempDir = Path.Combine(Path.GetTempPath(), $"genhub_migrate_{Guid.NewGuid():N}");
                    Directory.CreateDirectory(tempDir);
                    var logFile = Path.Combine(tempDir, "migration.log");
                    var backupDir = Path.Combine(tempDir, "backup");
                    var relativeExe = GetRelativeExecutablePath(sourceRoot);

                    var scriptPath = PrepareMigrationScript(tempDir);

                    // Phase 4: Launch helper process
                    progress?.Report(new StorageMigrationProgress
                    {
                        Stage = StorageMigrationConstants.StageLaunchingAssistant,
                        Percentage = 85,
                        Message = "Launching migration assistant process...",
                    });

                    if (request.LaunchHelperProcess)
                    {
                        LaunchHelperProcess(scriptPath, sourceRoot, targetRoot, relativeExe, logFile, backupDir);
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
            },
            cancellationToken);
    }

    /// <summary>
    /// Gets the top-level Velopack installation root directory or falls back to AppContext.BaseDirectory.
    /// </summary>
    /// <returns>The source root directory path.</returns>
    internal static string GetSourceRootDirectory()
    {
        var appBaseDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));

        if (OperatingSystem.IsMacOS())
        {
            var appBundleIndex = appBaseDir.IndexOf(".app", StringComparison.OrdinalIgnoreCase);
            if (appBundleIndex > 0)
            {
                return appBaseDir[..(appBundleIndex + 4)];
            }
        }

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

    private static StorageMigrationPreflightResult ValidatePathSanity(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return new StorageMigrationPreflightResult
            {
                IsValid = false,
                ErrorMessage = "Target installation directory path cannot be empty.",
            };
        }

        var normalizedTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));
        var appBaseDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        var sourceRoot = GetSourceRootDirectory();

        if (normalizedTarget.Equals(sourceRoot, PathHelper.PathComparison) ||
            normalizedTarget.Equals(appBaseDir, PathHelper.PathComparison))
        {
            return new StorageMigrationPreflightResult
            {
                IsValid = false,
                ErrorMessage = "The target directory is the same as the current installation directory.",
            };
        }

        if (IsInsideDirectory(normalizedTarget, sourceRoot) || IsInsideDirectory(normalizedTarget, appBaseDir))
        {
            return new StorageMigrationPreflightResult
            {
                IsValid = false,
                IsTargetInsideApplicationDirectory = true,
                ErrorMessage = "The target directory cannot be located inside the current installation directory.",
            };
        }

        if (IsInsideDirectory(sourceRoot, normalizedTarget) || IsInsideDirectory(appBaseDir, normalizedTarget))
        {
            return new StorageMigrationPreflightResult
            {
                IsValid = false,
                ErrorMessage = "The target directory cannot be a parent of the current installation directory.",
            };
        }

        return new StorageMigrationPreflightResult { IsValid = true };
    }

    private static string? DeterminePreflightErrorMessage(
        bool hasWritePermission,
        bool hasActiveProcesses,
        bool hasSufficientSpace,
        long requiredBytes,
        long availableBytes)
    {
        if (!hasWritePermission)
        {
            return "The target directory is not writable or cannot be created.";
        }

        if (hasActiveProcesses)
        {
            return "Active game instances or launches are currently running. Please close all running games before migrating.";
        }

        if (!hasSufficientSpace)
        {
            var reqMb = requiredBytes / ConversionConstants.BytesPerMegabyte;
            var availMb = availableBytes / ConversionConstants.BytesPerMegabyte;
            return $"Insufficient free disk space on target volume. Required: {reqMb} MB, Available: {availMb} MB.";
        }

        return null;
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

    private static string GetFallbackScriptTemplate(bool isWindows)
    {
        if (isWindows)
        {
            return @"# GenHub Windows Update PowerShell Script
param(
    [string]$ProcessId = ""{{PROCESS_ID}}"",
    [string]$SourceDir = ""{{SOURCE_DIR}}"",
    [string]$TargetDir = ""{{TARGET_DIR}}"",
    [string]$CurrentExe = ""{{CURRENT_EXE}}"",
    [string]$LogFile = ""{{LOG_FILE}}"",
    [string]$BackupDir = ""{{BACKUP_DIR}}""
)

$ErrorActionPreference = 'SilentlyContinue'

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
        Copy-Item -Path ""$TargetDir\*"" -Destination $BackupDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    Copy-Item -Path ""$SourceDir\*"" -Destination $TargetDir -Recurse -Force -ErrorAction Stop
    Write-Log ""Migration completed successfully""
    if (Test-Path $SourceDir) {
        Remove-Item -Path $SourceDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path $CurrentExe) {
        $exeDir = Split-Path -Path $CurrentExe -Parent
        Start-Process -FilePath $CurrentExe -WorkingDirectory $exeDir
    }
}
catch {
    Write-Log ""Migration failed: $($_.Exception.Message)""
    if (Test-Path $BackupDir) {
        Remove-Item -Path ""$TargetDir\*"" -Recurse -Force -ErrorAction SilentlyContinue
        Copy-Item -Path ""$BackupDir\*"" -Destination $TargetDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
finally {
    $updaterDir = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
    Start-Sleep -Seconds 2
    if (Test-Path $updaterDir) {
        Remove-Item -Path $updaterDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
";
        }

        return @"#!/bin/bash
PROCESS_ID=""${1:-{{PROCESS_ID}}}""
SOURCE_DIR=""${2:-{{SOURCE_DIR}}}""
TARGET_DIR=""${3:-{{TARGET_DIR}}}""
CURRENT_EXE=""${4:-{{CURRENT_EXE}}}""
LOG_FILE=""${5:-{{LOG_FILE}}}""
BACKUP_DIR=""${6:-{{BACKUP_DIR}}}""

write_log() {
    echo ""[$(date '+%Y-%m-%d %H:%M:%S')] $1"" >> ""$LOG_FILE""
}

write_log ""GenHub Linux Migration Script Started""
for i in {1..60}; do
    if ! kill -0 ""$PROCESS_ID"" 2>/dev/null; then
        break
    fi
    sleep 1
done

if kill -0 ""$PROCESS_ID"" 2>/dev/null; then
    kill -TERM ""$PROCESS_ID"" 2>/dev/null
    sleep 2
    kill -KILL ""$PROCESS_ID"" 2>/dev/null
fi

pkill -f ""^$CURRENT_EXE\$"" || true
sleep 2

mkdir -p ""$BACKUP_DIR""
if [ -d ""$TARGET_DIR"" ]; then
    cp -a ""$TARGET_DIR/."" ""$BACKUP_DIR/"" 2>/dev/null || true
fi

mkdir -p ""$TARGET_DIR""
if ! cp -a ""$SOURCE_DIR/."" ""$TARGET_DIR/"" 2>&1; then
    write_log ""Error: Failed to copy migration files""
    if [ -d ""$BACKUP_DIR"" ]; then
        rm -rf ""${TARGET_DIR:?}""/* ""${TARGET_DIR:?}""/.[!.]* 2>/dev/null || true
        cp -a ""${BACKUP_DIR:?}/."" ""$TARGET_DIR/"" 2>/dev/null || true
    fi
    exit 1
fi

if [ -f ""$CURRENT_EXE"" ]; then
    EXE_DIR=$(dirname ""$CURRENT_EXE"")
    EXE_NAME=$(basename ""$CURRENT_EXE"")
    cd ""$EXE_DIR"" || exit 1
    if [ ! -x ""$EXE_NAME"" ]; then
        chmod +x ""$EXE_NAME""
    fi
    nohup ""./$EXE_NAME"" > /dev/null 2>&1 &
fi

rm -rf ""${SOURCE_DIR:?}"" 2>/dev/null || true
UPDATER_DIR=$(dirname ""$0"")
sleep 2
rm -rf ""${UPDATER_DIR:?}"" 2>/dev/null || true
";
    }

    private static string? GetScriptResource(string scriptName)
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

    private static string GetPowerShellPath()
    {
        var systemPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        return File.Exists(systemPowerShell) ? systemPowerShell : "powershell.exe";
    }

    private async Task<(bool HasActiveProcesses, List<string> ProcessNames)> CheckActiveProcessesAsync(CancellationToken cancellationToken)
    {
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

        return (hasActiveProcesses, processNames);
    }

    private long CalculateRequiredSpace(string sourceRoot, bool relocateCasAndWorkspace)
    {
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

        return requiredBytes + StorageMigrationConstants.DiskSpaceSafetyMarginBytes;
    }

    private async Task<bool> RelocateStorageAsync(string targetRoot, string sourceRoot)
    {
        var currentCasRoot = configurationProvider.GetCasConfiguration().CasRootPath;
        var currentWorkspaceRoot = userSettingsService.Get().WorkspacePath;

        var targetDataDir = Path.Combine(targetRoot, DirectoryNames.Data);
        var targetCasRoot = Path.Combine(targetDataDir, DirectoryNames.CasPool);
        var targetWorkspaceRoot = Path.Combine(targetDataDir, DirectoryNames.Workspaces);

        var isCasNested = !string.IsNullOrWhiteSpace(currentCasRoot) && IsInsideDirectory(currentCasRoot, sourceRoot);
        var isWorkspaceNested = !string.IsNullOrWhiteSpace(currentWorkspaceRoot) && IsInsideDirectory(currentWorkspaceRoot, sourceRoot);

        var finalCasRoot = isCasNested
            ? Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, currentCasRoot!))
            : targetCasRoot;

        var finalWorkspaceRoot = isWorkspaceNested
            ? Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, currentWorkspaceRoot!))
            : targetWorkspaceRoot;

        var casMoved = false;
        var workspaceMoved = false;

        try
        {
            // Move CAS pool if existing and not already inside source root
            if (!string.IsNullOrWhiteSpace(currentCasRoot) && Directory.Exists(currentCasRoot) && !isCasNested)
            {
                logger.LogInformation("Moving CAS storage pool from {Source} to {Target}", currentCasRoot, targetCasRoot);
                MigrateDirectorySafely(currentCasRoot, targetCasRoot);
                casMoved = true;
            }

            // Move workspaces if existing and not already inside source root
            if (!string.IsNullOrWhiteSpace(currentWorkspaceRoot) && Directory.Exists(currentWorkspaceRoot) && !isWorkspaceNested)
            {
                logger.LogInformation("Moving workspaces from {Source} to {Target}", currentWorkspaceRoot, targetWorkspaceRoot);
                MigrateDirectorySafely(currentWorkspaceRoot, targetWorkspaceRoot);
                workspaceMoved = true;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed while moving storage directories. Rolling back partial moves.");
            if (casMoved && !string.IsNullOrWhiteSpace(currentCasRoot))
            {
                MigrateDirectorySafely(targetCasRoot, currentCasRoot);
            }

            if (workspaceMoved && !string.IsNullOrWhiteSpace(currentWorkspaceRoot))
            {
                MigrateDirectorySafely(targetWorkspaceRoot, currentWorkspaceRoot);
            }

            return false;
        }

        // Update and persist settings
        var saved = await userSettingsService.TryUpdateAndSaveAsync(settings =>
        {
            settings.CasConfiguration.CasRootPath = finalCasRoot;
            settings.WorkspacePath = finalWorkspaceRoot;
            settings.MarkAsExplicitlySet(nameof(UserSettings.WorkspacePath));
            return true;
        });

        if (!saved)
        {
            logger.LogError("Failed to persist relocated storage settings. Rolling back storage relocation.");
            if (casMoved && !string.IsNullOrWhiteSpace(currentCasRoot))
            {
                MigrateDirectorySafely(targetCasRoot, currentCasRoot);
            }

            if (workspaceMoved && !string.IsNullOrWhiteSpace(currentWorkspaceRoot))
            {
                MigrateDirectorySafely(targetWorkspaceRoot, currentWorkspaceRoot);
            }

            return false;
        }

        // Reinitialize CAS pool with the new path
        casPoolManager.ReinitializeInstallationPool();
        return true;
    }

    private string PrepareMigrationScript(string targetDirectory)
    {
        var isWindows = OperatingSystem.IsWindows();
        var scriptName = isWindows
            ? StorageMigrationConstants.WindowsUpdateScriptName
            : StorageMigrationConstants.LinuxUpdateScriptName;

        var scriptTemplate = GetScriptResource(scriptName) ?? GetFallbackScriptTemplate(isWindows);
        var scriptFilePath = Path.Combine(targetDirectory, scriptName);
        File.WriteAllText(scriptFilePath, scriptTemplate);

        if (!isWindows)
        {
            try
            {
                var filePermissions = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                      UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                      UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
                File.SetUnixFileMode(scriptFilePath, filePermissions);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to set Unix permissions on migration script {Path}", scriptFilePath);
            }
        }

        logger.LogInformation("Migration script generated at {ScriptPath}", scriptFilePath);
        return scriptFilePath;
    }

    private void LaunchHelperProcess(
        string scriptPath,
        string sourceDir,
        string targetDir,
        string relativeExePath,
        string logFile,
        string backupDir)
    {
        try
        {
            var targetExe = Path.Combine(targetDir, relativeExePath);
            var pid = Environment.ProcessId.ToString();

            var startInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? GetPowerShellPath() : "/bin/bash",
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (OperatingSystem.IsWindows())
            {
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-File");
            }

            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(pid);
            startInfo.ArgumentList.Add(sourceDir);
            startInfo.ArgumentList.Add(targetDir);
            startInfo.ArgumentList.Add(targetExe);
            startInfo.ArgumentList.Add(logFile);
            startInfo.ArgumentList.Add(backupDir);

            Process.Start(startInfo);
            logger.LogInformation("Started detached helper migration process: {ScriptPath}", scriptPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start helper migration process for '{scriptPath}'.", ex);
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
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Exception during application lifetime shutdown for migration");
        }
    }
}
