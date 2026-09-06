using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace;
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
    private readonly record struct StorageRelocationPaths(
        string? CurrentCasRoot,
        string? CurrentWorkspaceRoot,
        string FinalCasRoot,
        string FinalWorkspaceRoot);

    private static readonly HashSet<string> ExcludedUserDataNames = new(PathHelper.PathComparer)
    {
        FileTypes.SettingsFileName,
        DirectoryNames.Profiles,
        FileTypes.ManifestsDirectory,
        DirectoryNames.UserData,
        FileTypes.WorkspaceMetadataFileName,
        DirectoryNames.Data,
        DirectoryNames.CasPool,
        DirectoryNames.Workspaces,
        DirectoryNames.Cache,
        StorageMigrationConstants.CacheLowercaseDirectoryName,
        StorageMigrationConstants.LogsDirectoryName,
        StorageMigrationConstants.LogsCapitalizedDirectoryName,
        StorageMigrationConstants.UploadHistoryFileName,
        MapManagerConstants.MapPacksSubdirectoryName,
        StorageMigrationConstants.MapPacksLowercaseDirectoryName,
        StorageMigrationConstants.DotGenHubCasDirectoryName,
    };

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

            if (relocateCasAndWorkspace)
            {
                var casRoot = ResolveEffectiveCasRoot();
                var workspaceRoot = ResolveEffectiveWorkspaceRoot();
                if (!string.IsNullOrWhiteSpace(casRoot) && (IsInsideDirectory(normalizedTarget, casRoot) || IsInsideDirectory(casRoot, normalizedTarget)))
                {
                    return OperationResult<StorageMigrationPreflightResult>.CreateSuccess(new StorageMigrationPreflightResult
                    {
                        IsValid = false,
                        ErrorMessage = "The target directory cannot be located inside or contain the CAS storage directory.",
                    });
                }

                if (!string.IsNullOrWhiteSpace(workspaceRoot) && (IsInsideDirectory(normalizedTarget, workspaceRoot) || IsInsideDirectory(workspaceRoot, normalizedTarget)))
                {
                    return OperationResult<StorageMigrationPreflightResult>.CreateSuccess(new StorageMigrationPreflightResult
                    {
                        IsValid = false,
                        ErrorMessage = "The target directory cannot be located inside or contain the workspace directory.",
                    });
                }
            }

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
            () => ExecuteMigrationAsync(request, progress, cancellationToken),
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
            var segments = appBaseDir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length; i++)
            {
                if (segments[i].EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                {
                    var prefix = OperatingSystem.IsWindows() ? string.Empty : "/";
                    return prefix + string.Join('/', segments.Take(i + 1));
                }
            }
        }

        var parentDir = Directory.GetParent(appBaseDir)?.FullName;
        if (parentDir != null)
        {
            // Check for Velopack markers (Update.exe / Update, packages dir, app-* directories, or companion executable)
            var hasUpdateExe = File.Exists(Path.Combine(parentDir, "Update.exe")) || File.Exists(Path.Combine(parentDir, "Update"));
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
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
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
    internal static bool IsInsideDirectory(string? path, string? parentDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(parentDirectory))
        {
            return false;
        }

        return PathHelper.AreSamePath(parentDirectory, path) ||
               PathHelper.IsPathWithinDirectory(parentDirectory, path);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
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

        if (!Directory.Exists(destDir))
        {
            try
            {
                Directory.Move(sourceDir, destDir);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Move across volumes or permissions fallback to copy-then-delete
                Directory.CreateDirectory(destDir);
            }
        }

        try
        {
            CopyDirectoryRecursive(sourceDir, destDir);
            FileOperationsService.DeleteDirectoryIfExists(sourceDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteDirectory(destDir);
            throw;
        }
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

        if (Directory.Exists(normalizedTarget) && Directory.EnumerateFileSystemEntries(normalizedTarget).Any())
        {
            return new StorageMigrationPreflightResult
            {
                IsValid = false,
                ErrorMessage = "The target directory already exists and is not empty. Please select an empty or new folder.",
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
            return "The target directory is not writable. Please choose a location with write permissions.";
        }

        if (hasActiveProcesses)
        {
            return "Cannot migrate while active game or GenHub processes are running. Please close all games and try again.";
        }

        if (!hasSufficientSpace)
        {
            var reqMb = requiredBytes / (1024 * 1024);
            var availMb = availableBytes / (1024 * 1024);
            return $"Insufficient disk space on target drive. Required: {reqMb:N0} MB, Available: {availMb:N0} MB.";
        }

        return null;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(destDir, fileName);
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(subDir);
            var destSubDir = Path.Combine(destDir, dirName);
            CopyDirectoryRecursive(subDir, destSubDir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Suppress cleanup exceptions during rollback
        }
    }

    private static string GetPowerShellExcludedList()
    {
        return string.Join(", ", ExcludedUserDataNames.Select(name => $"'{name}'"));
    }

    private static string GetBashExcludedPattern()
    {
        return string.Join("|", ExcludedUserDataNames);
    }

    private static string GetFallbackScriptTemplate(bool isWindows)
    {
        if (isWindows)
        {
            return $@"# GenHub Windows Migration Script (Fallback)
param(
    [string]$ProcessId = ""{{PROCESS_ID}}"",
    [string]$SourceDir = ""{{SOURCE_DIR}}"",
    [string]$TargetDir = ""{{TARGET_DIR}}"",
    [string]$CurrentExe = ""{{CURRENT_EXE}}"",
    [string]$LogFile = ""{{LOG_FILE}}"",
    [string]$BackupDir = ""{{BACKUP_DIR}}""
)

function Write-Log {{
    param([string]$Message)
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    ""[$timestamp] $Message"" | Out-File -FilePath $LogFile -Append -Encoding UTF8
}}

Write-Log ""GenHub Migration Script Started""
Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction SilentlyContinue
$process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
if ($process) {{
    Stop-Process -Id $ProcessId -Force
    Start-Sleep -Seconds 2
}}
Get-Process -Name ""GenHub*"" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

$updateSuccess = $false
$excluded = @({GetPowerShellExcludedList()})
try {{
    if (-not (Test-Path $TargetDir)) {{
        New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    }}
    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
    if (Test-Path $TargetDir) {{
        $existingItems = Get-ChildItem -Path $TargetDir -Force -ErrorAction SilentlyContinue
        if ($null -ne $existingItems -and $existingItems.Count -gt 0) {{
            Copy-Item -Path ""$TargetDir\*"" -Destination $BackupDir -Recurse -Force -ErrorAction Stop
        }}
    }}
    Get-ChildItem -Path $SourceDir -Force | Where-Object {{ $excluded -notcontains $_.Name }} | ForEach-Object {{
        Copy-Item -Path $_.FullName -Destination $TargetDir -Recurse -Force -ErrorAction Stop
    }}
    Write-Log ""Migration copied successfully""
    if (-not (Test-Path $CurrentExe)) {{
        throw ""Updated executable not found: $CurrentExe""
    }}
    $exeDir = Split-Path -Path $CurrentExe -Parent
    $proc = Start-Process -FilePath $CurrentExe -WorkingDirectory $exeDir -PassThru -ErrorAction Stop
    if ($null -eq $proc) {{
        throw ""Failed to start updated application: process could not be launched""
    }}
    Start-Sleep -Seconds 1
    for ($i = 0; $i -lt 5; $i++) {{
        if ($proc.HasExited) {{
            throw ""Application exited prematurely after launch with exit code $($proc.ExitCode)""
        }}
        Start-Sleep -Seconds 1
    }}
    Write-Log ""Application started and verified running""
    if (Test-Path $SourceDir) {{
        Get-ChildItem -Path $SourceDir -Force | Where-Object {{ $excluded -notcontains $_.Name }} | ForEach-Object {{
            Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }}
        $remaining = Get-ChildItem -Path $SourceDir -Force -ErrorAction SilentlyContinue
        if ($null -eq $remaining -or $remaining.Count -eq 0) {{
            Remove-Item -Path $SourceDir -Force -ErrorAction SilentlyContinue
        }}
    }}
    $updateSuccess = $true
}}
catch {{
    Write-Log ""Migration failed: $($_.Exception.Message)""
    if (Test-Path $BackupDir) {{
        $backupItems = Get-ChildItem -Path $BackupDir -Force -ErrorAction SilentlyContinue
        if ($null -ne $backupItems -and $backupItems.Count -gt 0) {{
            Remove-Item -Path ""$TargetDir\*"" -Recurse -Force -ErrorAction SilentlyContinue
            Copy-Item -Path ""$BackupDir\*"" -Destination $TargetDir -Recurse -Force -ErrorAction SilentlyContinue
            Write-Log ""Backup restored successfully""
        }}
    }}
}}
finally {{
    $updaterDir = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
    if ($updateSuccess) {{
        Start-Sleep -Seconds 2
        if (Test-Path $updaterDir) {{
            Remove-Item -Path $updaterDir -Recurse -Force -ErrorAction SilentlyContinue
        }}
    }} else {{
        Write-Log ""Preserving backup and temporary updater directory for recovery: $updaterDir""
    }}
}}
";
        }

        return $@"#!/bin/bash
trap '' HUP
UPDATER_DIR=$(cd -- ""$(dirname -- ""$0"")"" && pwd)
PROCESS_ID=""${{1:-{{{{PROCESS_ID}}}}}}""
SOURCE_DIR=""${{2:-{{{{SOURCE_DIR}}}}}}""
TARGET_DIR=""${{3:-{{{{TARGET_DIR}}}}}}""
CURRENT_EXE=""${{4:-{{{{CURRENT_EXE}}}}}}""
LOG_FILE=""${{5:-{{{{LOG_FILE}}}}}}""
BACKUP_DIR=""${{6:-{{{{BACKUP_DIR}}}}}}""

write_log() {{
    echo ""[$(date '+%Y-%m-%d %H:%M:%S')] $1"" >> ""$LOG_FILE""
}}

is_excluded() {{
    case ""$1"" in
        {GetBashExcludedPattern()})
            return 0
            ;;
        *)
            return 1
            ;;
    esac
}}

write_log ""GenHub Linux Migration Script Started""
for i in {{1..60}}; do
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

pkill -x ""GenHub"" 2>/dev/null || true
pkill -x ""GenHub.Linux"" 2>/dev/null || true
sleep 2

mkdir -p ""$BACKUP_DIR""
if [ -d ""$TARGET_DIR"" ] && [ ""$(ls -A ""$TARGET_DIR"" 2>/dev/null)"" ]; then
    write_log ""Backing up existing files...""
    if ! cp -a ""$TARGET_DIR/."" ""$BACKUP_DIR/"" 2>> ""$LOG_FILE""; then
        write_log ""Error: Failed to create backup of existing files.""
        exit 1
    fi
fi

mkdir -p ""$TARGET_DIR""
copy_failed=0
for item in ""$SOURCE_DIR""/* ""$SOURCE_DIR""/.[!.]*; do
    [ -e ""$item"" ] || continue
    name=$(basename ""$item"")
    if is_excluded ""$name""; then
        continue
    fi
    if ! cp -a ""$item"" ""$TARGET_DIR/"" 2>> ""$LOG_FILE""; then
        copy_failed=1
        break
    fi
done

if [ ""$copy_failed"" -eq 1 ]; then
    write_log ""Error: Failed to copy migration files""
    if [ -d ""$BACKUP_DIR"" ] && [ ""$(ls -A ""$BACKUP_DIR"" 2>/dev/null)"" ]; then
        write_log ""Attempting to restore backup...""
        rm -rf ""${{TARGET_DIR:?}}""/* ""${{TARGET_DIR:?}}""/.[!.]* 2>/dev/null || true
        if cp -a ""${{BACKUP_DIR:?}}/."" ""$TARGET_DIR/"" 2>/dev/null; then
            write_log ""Backup restored.""
        else
            write_log ""Error: Failed to restore backup.""
        fi
    fi
    exit 1
fi

write_log ""Starting updated application: $CURRENT_EXE""
if [ -f ""$CURRENT_EXE"" ]; then
    EXE_DIR=$(dirname ""$CURRENT_EXE"")
    EXE_NAME=$(basename ""$CURRENT_EXE"")
    cd ""$EXE_DIR"" || exit 1
    if [ ! -x ""$EXE_NAME"" ]; then
        chmod +x ""$EXE_NAME""
    fi
    nohup ""./$EXE_NAME"" > /dev/null 2>&1 &
    APP_PID=$!
    for i in $(seq 1 5); do
        sleep 1
        if ! kill -0 ""$APP_PID"" 2>/dev/null; then
            write_log ""Error: Application exited prematurely after launch""
            if [ -d ""$BACKUP_DIR"" ] && [ ""$(ls -A ""$BACKUP_DIR"" 2>/dev/null)"" ]; then
                write_log ""Attempting to restore backup...""
                rm -rf ""${{TARGET_DIR:?}}""/* ""${{TARGET_DIR:?}}""/.[!.]* 2>/dev/null || true
                if cp -a ""${{BACKUP_DIR:?}}/."" ""$TARGET_DIR/"" 2>/dev/null; then
                    write_log ""Backup restored.""
                else
                    write_log ""Error: Failed to restore backup.""
                fi
            fi
            exit 1
        fi
    done
    write_log ""Application started and verified running (PID: $APP_PID)""
    for item in ""$SOURCE_DIR""/* ""$SOURCE_DIR""/.[!.]*; do
        [ -e ""$item"" ] || continue
        name=$(basename ""$item"")
        if is_excluded ""$name""; then
            continue
        fi
        rm -rf ""$item"" 2>/dev/null || true
    done
    rmdir ""$SOURCE_DIR"" 2>/dev/null || true
else
    write_log ""Error: Updated executable not found: $CURRENT_EXE""
    if [ -d ""$BACKUP_DIR"" ] && [ ""$(ls -A ""$BACKUP_DIR"" 2>/dev/null)"" ]; then
        write_log ""Attempting to restore backup...""
        rm -rf ""${{TARGET_DIR:?}}""/* ""${{TARGET_DIR:?}}""/.[!.]* 2>/dev/null || true
        if cp -a ""${{BACKUP_DIR:?}}/."" ""$TARGET_DIR/"" 2>/dev/null; then
            write_log ""Backup restored.""
        else
            write_log ""Error: Failed to restore backup.""
        fi
    fi
    exit 1
fi

sleep 2
rm -rf ""${{UPDATER_DIR:?}}"" 2>/dev/null || true
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
            catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or SecurityException)
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

    private static string ResolveFinalDirectoryPath(
        string? currentRoot,
        string defaultTarget,
        string sourceRoot,
        string targetRoot)
    {
        if (string.IsNullOrWhiteSpace(currentRoot))
        {
            return defaultTarget;
        }

        var relative = Path.GetRelativePath(sourceRoot, currentRoot);
        if (relative != "." && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
        {
            return Path.Combine(targetRoot, relative);
        }

        return defaultTarget;
    }

    private static string? DetermineRollbackPath(bool rolledBack, string? originalPath, string targetPath)
    {
        if (rolledBack)
        {
            return originalPath;
        }

        return Directory.Exists(targetPath) ? targetPath : originalPath;
    }

    private static bool RollbackDirectoryMove(
        bool moved,
        string? originalPath,
        string targetPath,
        ILogger logger)
    {
        if (!moved || string.IsNullOrWhiteSpace(originalPath))
        {
            return true;
        }

        try
        {
            if (Directory.Exists(targetPath))
            {
                MigrateDirectorySafely(targetPath, originalPath);
                logger.LogInformation("Successfully rolled back directory move from {Target} to {Original}", targetPath, originalPath);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogCritical(ex, "Failed to rollback directory move from {Target} to {Original}. Manual recovery may be required.", targetPath, originalPath);
            return false;
        }
    }

    private static bool TryRewritePath(string? path, string oldRoot, string newRoot, Action<string> apply)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var relative = Path.GetRelativePath(oldRoot, path);
        if (relative != "." && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
        {
            apply(Path.Combine(newRoot, relative));
            return true;
        }

        return false;
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
        long requiredBytes = 0;

        if (Directory.Exists(sourceRoot))
        {
            var dirInfo = new DirectoryInfo(sourceRoot);
            foreach (var entry in dirInfo.EnumerateFileSystemInfos())
            {
                if (ExcludedUserDataNames.Contains(entry.Name))
                {
                    continue;
                }

                requiredBytes += entry is FileInfo fi
                    ? fi.Length
                    : CalculateDirectorySize(entry.FullName);
            }
        }

        if (relocateCasAndWorkspace)
        {
            var casRoot = ResolveEffectiveCasRoot();
            if (Directory.Exists(casRoot))
            {
                requiredBytes += CalculateDirectorySize(casRoot);
            }

            var workspaceRoot = ResolveEffectiveWorkspaceRoot();
            if (Directory.Exists(workspaceRoot))
            {
                requiredBytes += CalculateDirectorySize(workspaceRoot);
            }
        }

        return requiredBytes + StorageMigrationConstants.DiskSpaceSafetyMarginBytes;
    }

    private async Task<OperationResult<bool>> RelocateStorageAsync(
        string targetRoot,
        string sourceRoot,
        CancellationToken cancellationToken = default)
    {
        var currentCasRoot = ResolveEffectiveCasRoot();
        var currentWorkspaceRoot = ResolveEffectiveWorkspaceRoot();

        var targetDataDir = Path.Combine(targetRoot, DirectoryNames.Data);
        var targetCasRoot = Path.Combine(targetDataDir, DirectoryNames.CasPool);
        var targetWorkspaceRoot = Path.Combine(targetDataDir, DirectoryNames.Workspaces);

        var finalCasRoot = ResolveFinalDirectoryPath(currentCasRoot, targetCasRoot, sourceRoot, targetRoot);
        var finalWorkspaceRoot = ResolveFinalDirectoryPath(currentWorkspaceRoot, targetWorkspaceRoot, sourceRoot, targetRoot);

        var paths = new StorageRelocationPaths(
            currentCasRoot,
            currentWorkspaceRoot,
            finalCasRoot,
            finalWorkspaceRoot);

        if (!TryMoveStorageDirectory(currentCasRoot, finalCasRoot, out var casMoved))
        {
            return OperationResult<bool>.CreateFailure("Failed to relocate CAS storage pool.");
        }

        if (!TryMoveStorageDirectory(currentWorkspaceRoot, finalWorkspaceRoot, out var workspaceMoved))
        {
            return await HandleWorkspaceMoveFailureAsync(casMoved, paths, cancellationToken);
        }

        if (workspaceMoved && !string.IsNullOrWhiteSpace(currentWorkspaceRoot))
        {
            await RewriteWorkspaceMetadataAsync(currentWorkspaceRoot, finalWorkspaceRoot, cancellationToken);
        }

        var saved = await userSettingsService.TryUpdateAndSaveAsync(settings =>
        {
            settings.CasConfiguration.CasRootPath = finalCasRoot;
            settings.WorkspacePath = finalWorkspaceRoot;
            settings.MarkAsExplicitlySet(nameof(UserSettings.WorkspacePath));
            return true;
        });

        if (!saved)
        {
            return await HandleSettingsPersistFailureAsync(casMoved, workspaceMoved, paths, cancellationToken);
        }

        try
        {
            casPoolManager.ReinitializeInstallationPool();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Failed to reinitialize CAS installation pool after storage relocation.");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<OperationResult<bool>> HandleWorkspaceMoveFailureAsync(
        bool casMoved,
        StorageRelocationPaths paths,
        CancellationToken cancellationToken = default)
    {
        var casRolledBack = RollbackDirectoryMove(casMoved, paths.CurrentCasRoot, paths.FinalCasRoot, logger);
        if (!casRolledBack && paths.CurrentCasRoot != null)
        {
            var casSaved = await PersistRollbackPathsAsync(
                casRolledBack: false,
                workspaceRolledBack: true,
                paths,
                cancellationToken);

            if (!casSaved)
            {
                logger.LogCritical("Failed to persist CAS storage path to {Target} after rollback failure.", paths.FinalCasRoot);
                return OperationResult<bool>.CreateFailure($"Critical: CAS storage rollback failed, and settings could not be saved to target path {paths.FinalCasRoot}.");
            }
        }

        return OperationResult<bool>.CreateFailure("Failed to relocate game workspaces directory.");
    }

    private async Task<OperationResult<bool>> HandleSettingsPersistFailureAsync(
        bool casMoved,
        bool workspaceMoved,
        StorageRelocationPaths paths,
        CancellationToken cancellationToken = default)
    {
        logger.LogError("Failed to persist relocated storage settings. Rolling back storage relocation.");

        var casRolledBack = RollbackDirectoryMove(casMoved, paths.CurrentCasRoot, paths.FinalCasRoot, logger);
        var workspaceRolledBack = RollbackDirectoryMove(workspaceMoved, paths.CurrentWorkspaceRoot, paths.FinalWorkspaceRoot, logger);

        if (workspaceRolledBack && !string.IsNullOrWhiteSpace(paths.CurrentWorkspaceRoot))
        {
            await RewriteWorkspaceMetadataAsync(paths.FinalWorkspaceRoot, paths.CurrentWorkspaceRoot, cancellationToken);
        }

        var rollbackSaved = await PersistRollbackPathsAsync(
            casRolledBack,
            workspaceRolledBack,
            paths,
            cancellationToken);

        if (!rollbackSaved)
        {
            logger.LogCritical("Failed to persist storage paths during rollback. Settings on disk may be out of sync with physical storage.");
            return OperationResult<bool>.CreateFailure("Critical: Failed to persist storage configuration during rollback. Configuration on disk may be unrecovered and out of sync with physical storage.");
        }

        return OperationResult<bool>.CreateFailure("Failed to persist relocated storage settings. Storage locations have been rolled back.");
    }

    private async Task<bool> PersistRollbackPathsAsync(
        bool casRolledBack,
        bool workspaceRolledBack,
        StorageRelocationPaths paths,
        CancellationToken cancellationToken = default)
    {
        var saved = await TrySaveRollbackPathsAsync(casRolledBack, workspaceRolledBack, paths);

        if (!saved)
        {
            await Task.Delay(500, cancellationToken);
            saved = await TrySaveRollbackPathsAsync(casRolledBack, workspaceRolledBack, paths);
        }

        if (saved && !casRolledBack)
        {
            try
            {
                casPoolManager.ReinitializeInstallationPool();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Failed to reinitialize CAS installation pool after rollback.");
            }
        }

        return saved;
    }

    private Task<bool> TrySaveRollbackPathsAsync(
        bool casRolledBack,
        bool workspaceRolledBack,
        StorageRelocationPaths paths)
    {
        return userSettingsService.TryUpdateAndSaveAsync(liveSettings =>
        {
            var casPath = DetermineRollbackPath(casRolledBack, paths.CurrentCasRoot, paths.FinalCasRoot);
            if (casPath != null)
            {
                liveSettings.CasConfiguration.CasRootPath = casPath;
            }

            var workspacePath = DetermineRollbackPath(workspaceRolledBack, paths.CurrentWorkspaceRoot, paths.FinalWorkspaceRoot);
            if (workspacePath != null)
            {
                liveSettings.WorkspacePath = workspacePath;
            }

            return true;
        });
    }

    private bool TryMoveStorageDirectory(string? currentPath, string targetPath, out bool moved)
    {
        moved = false;
        if (string.IsNullOrWhiteSpace(currentPath) || !Directory.Exists(currentPath))
        {
            return true;
        }

        if (PathHelper.AreSamePath(currentPath, targetPath))
        {
            return true;
        }

        try
        {
            MigrateDirectorySafely(currentPath, targetPath);
            moved = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Failed to migrate storage directory from {Current} to {Target}", currentPath, targetPath);
            return false;
        }
    }

    private string? ResolveEffectiveCasRoot()
    {
        return userSettingsService.Get().CasConfiguration?.CasRootPath ??
               configurationProvider.GetCasConfiguration()?.CasRootPath;
    }

    private string? ResolveEffectiveWorkspaceRoot()
    {
        return userSettingsService.Get().WorkspacePath ??
               configurationProvider.GetWorkspacePath();
    }

    private async Task RewriteWorkspaceMetadataAsync(string oldWorkspaceRoot, string newWorkspaceRoot, CancellationToken cancellationToken)
    {
        var appDataMetadataPath = Path.Combine(configurationProvider.GetApplicationDataPath(), FileTypes.WorkspaceMetadataFileName);
        var wsMetadataPath = Path.Combine(newWorkspaceRoot, FileTypes.WorkspaceMetadataFileName);
        var metadataPaths = new[] { appDataMetadataPath, wsMetadataPath }.Distinct();

        foreach (var metadataPath in metadataPaths)
        {
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
                var workspaces = JsonSerializer.Deserialize<List<WorkspaceInfo>>(json);
                if (workspaces == null || workspaces.Count == 0)
                {
                    continue;
                }

                var updated = false;
                foreach (var ws in workspaces)
                {
                    updated |= TryRewritePath(ws.WorkspacePath, oldWorkspaceRoot, newWorkspaceRoot, p => ws.WorkspacePath = p);
                    updated |= TryRewritePath(ws.ExecutablePath, oldWorkspaceRoot, newWorkspaceRoot, p => ws.ExecutablePath = p);
                    updated |= TryRewritePath(ws.WorkingDirectory, oldWorkspaceRoot, newWorkspaceRoot, p => ws.WorkingDirectory = p);
                }

                if (updated)
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var updatedJson = JsonSerializer.Serialize(workspaces, options);
                    var tempPath = Path.Combine(Path.GetDirectoryName(metadataPath)!, $"{Path.GetFileName(metadataPath)}.{Guid.NewGuid():N}.tmp");
                    await File.WriteAllTextAsync(tempPath, updatedJson, cancellationToken);
                    File.Move(tempPath, metadataPath, overwrite: true);
                    logger.LogInformation("Rewrote workspace metadata paths from {OldRoot} to {NewRoot} in {Path}", oldWorkspaceRoot, newWorkspaceRoot, metadataPath);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Failed to rewrite workspace metadata paths in {MetadataFile}", metadataPath);
            }
        }
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
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

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException($"Process.Start returned null for helper process '{scriptPath}'.");
            }

            logger.LogInformation("Started detached helper migration process (PID: {ProcessId}): {ScriptPath}", process.Id, scriptPath);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or PlatformNotSupportedException)
        {
            throw new InvalidOperationException($"Failed to start helper migration process for '{scriptPath}'.", ex);
        }
    }

    private async Task<OperationResult<bool>> ExecuteMigrationAsync(
        StorageMigrationRequest request,
        IProgress<StorageMigrationProgress>? progress,
        CancellationToken cancellationToken)
    {
        string? stagedTempDir = null;
        var helperLaunched = false;
        try
        {
            var preflightResult = await RunPreflightPhaseAsync(request, progress, cancellationToken);
            if (!preflightResult.Success)
            {
                return preflightResult;
            }

            var targetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.TargetPath));
            var sourceRoot = GetSourceRootDirectory();
            Directory.CreateDirectory(targetRoot);

            if (request.RelocateCasAndWorkspace)
            {
                var relocateResult = await RunRelocateStoragePhaseAsync(targetRoot, sourceRoot, progress, cancellationToken);
                if (!relocateResult.Success)
                {
                    return relocateResult;
                }
            }

            helperLaunched = StageAndLaunchAssistant(request, sourceRoot, targetRoot, progress, out stagedTempDir);

            FinalizeMigration(request, progress);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            if (stagedTempDir != null && !helperLaunched)
            {
                FileOperationsService.DeleteDirectoryIfExists(stagedTempDir);
            }

            logger.LogError(ex, "Installation migration failed unexpectedly for target {TargetPath}", request.TargetPath);
            return OperationResult<bool>.CreateFailure($"Migration failed: {ex.Message}");
        }
    }

    private async Task<OperationResult<bool>> RunPreflightPhaseAsync(
        StorageMigrationRequest request,
        IProgress<StorageMigrationProgress>? progress,
        CancellationToken cancellationToken)
    {
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

        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<OperationResult<bool>> RunRelocateStoragePhaseAsync(
        string targetRoot,
        string sourceRoot,
        IProgress<StorageMigrationProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new StorageMigrationProgress
        {
            Stage = StorageMigrationConstants.StageRelocatingStorage,
            Percentage = 30,
            Message = "Relocating CAS storage pool and game workspaces...",
        });

        return await RelocateStorageAsync(targetRoot, sourceRoot, cancellationToken);
    }

    private bool StageAndLaunchAssistant(
        StorageMigrationRequest request,
        string sourceRoot,
        string targetRoot,
        IProgress<StorageMigrationProgress>? progress,
        out string stagedTempDir)
    {
        progress?.Report(new StorageMigrationProgress
        {
            Stage = StorageMigrationConstants.StagePreparingBinaries,
            Percentage = 65,
            Message = "Staging binary migration helper script...",
        });

        var tempDir = Path.Combine(Path.GetTempPath(), $"{StorageMigrationConstants.MigrationTempDirectoryPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        stagedTempDir = tempDir;
        var logFile = Path.Combine(tempDir, "migration.log");
        var backupDir = Path.Combine(tempDir, "backup");
        var relativeExe = GetRelativeExecutablePath(sourceRoot);
        var scriptPath = PrepareMigrationScript(tempDir);

        progress?.Report(new StorageMigrationProgress
        {
            Stage = StorageMigrationConstants.StageLaunchingAssistant,
            Percentage = 85,
            Message = "Launching migration assistant process...",
        });

        if (request.LaunchHelperProcess)
        {
            LaunchHelperProcess(scriptPath, sourceRoot, targetRoot, relativeExe, logFile, backupDir);
            return true;
        }

        return false;
    }

    private void FinalizeMigration(
        StorageMigrationRequest request,
        IProgress<StorageMigrationProgress>? progress)
    {
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
    }

    private void ExitApplication()
    {
        try
        {
            logger.LogInformation("Exiting GenHub to allow migration helper script to proceed.");
            Dispatcher.UIThread.Post(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown(0);
                }
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
        {
            logger.LogWarning(ex, "Exception during application lifetime shutdown for migration");
        }
    }
}
