using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Launcher;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Launching;

/// <summary>
/// Service for preparing game directories for Steam-tracked profile launches.
/// This approach uses a "Proxy Launcher" mechanism:
/// 1. We start a Workspace as usual (isolated environment).
/// 2. We drop a sidecar ProxyLauncher.exe next to the original game executables (we do NOT overwrite them).
/// 3. We write a proxy_config.json telling the Proxy to launch the Workspace executable using direct paths.
/// 4. Steam launch options can point at the sidecar proxy; the proxy then runs the Workspace game.
///
/// Each profile uses its own adjacent workspace: {installationRoot}\.genhub-workspace\{profileId}\
/// The proxy_config.json is regenerated on each launch with the correct workspace paths for that profile.
/// This keeps the game directory clean (no exe replacement) while maintaining Steam integration.
/// </summary>
public class SteamLauncher(
    ILogger<SteamLauncher> logger) : ISteamLauncher
{
    private const string ProxyConfigFileName = "proxy_config.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Configuration for the proxy launcher.
    /// </summary>
    private class ProxyConfig
    {
        public string? TargetExecutable { get; set; }

        public string? WorkingDirectory { get; set; }

        public string[]? Arguments { get; set; }

        public string? SteamAppId { get; set; }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<SteamLaunchPrepResult>> PrepareForProfileAsync(
        string gameInstallPath,
        string profileId,
        IEnumerable<ContentManifest> manifests,
        string executableName,
        string targetExecutablePath,
        string targetWorkingDirectory,
        string[]? targetArguments = null,
        string? steamAppId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation(
                "[SteamLauncher] Preparing game directory {Path} for profile {ProfileId} using Proxy Launcher",
                gameInstallPath,
                profileId);

            var proxyConfigPath = Path.Combine(gameInstallPath, ProxyConfigFileName);
            var proxyDeployPath = Path.Combine(gameInstallPath, SteamConstants.ProxyLauncherFileName);

            // 1. Ensure we have the ProxyLauncher binary available
            var currentBaseDir = AppDomain.CurrentDomain.BaseDirectory;
            var proxySourcePath = Path.Combine(currentBaseDir, SteamConstants.ProxyLauncherFileName);

            if (!File.Exists(proxySourcePath))
            {
                logger.LogDebug("[SteamLauncher] Proxy Launcher not found in base directory: {Path}. Checking fallbacks...", proxySourcePath);

                // Fallback 1: Development path relative to typical bin output structure
                var devPaths = new[]
                {
                    Path.GetFullPath(Path.Combine(currentBaseDir, "..", "..", "..", "..", "GenHub.ProxyLauncher", "bin", "Debug", "net8.0-windows", "win-x64", "GenHub.ProxyLauncher.exe")),
                    Path.GetFullPath(Path.Combine(currentBaseDir, "..", "..", "..", "..", "GenHub.ProxyLauncher", "bin", "Release", "net8.0-windows", "win-x64", "GenHub.ProxyLauncher.exe")),
                    Path.GetFullPath(Path.Combine(currentBaseDir, "net8.0-windows", "GenHub.ProxyLauncher.exe")), // In case it's in a subfolder
                };

                foreach (var devPath in devPaths)
                {
                    if (File.Exists(devPath))
                    {
                        proxySourcePath = devPath;
                        break;
                    }
                }

                if (!File.Exists(proxySourcePath))
                {
                    return OperationResult<SteamLaunchPrepResult>.CreateFailure(
                        $"Proxy Launcher binary not found at {proxySourcePath}. Please build GenHub.ProxyLauncher project.");
                }
            }

            // 2. Deploy Proxy Launcher as the game executable
            // Steam launches the game executable (e.g., generals.exe), so we replace it with our proxy.
            // The proxy then launches the actual workspace executable.
            // Original game exe is backed up to .ghbak for restoration and version detection.
            var targetExeName = executableName; // e.g., "generals.exe" or "game.exe" (Zero Hour)
            var targetExePath = Path.Combine(gameInstallPath, targetExeName);
            var backupPath = targetExePath + SteamConstants.BackupExtension;

            // Backup original executable if not already backed up
            if (!File.Exists(backupPath) && File.Exists(targetExePath))
            {
                logger.LogInformation(
                    "[SteamLauncher] Backing up original {Exe} to {Backup}",
                    targetExeName,
                    Path.GetFileName(backupPath));

                try
                {
                    File.Copy(targetExePath, backupPath, overwrite: false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[SteamLauncher] Failed to create backup of {Exe}", targetExeName);
                    return OperationResult<SteamLaunchPrepResult>.CreateFailure(
                        $"Failed to backup original executable: {ex.Message}");
                }
            }

            // Deploy proxy (always overwrite - if backup exists, target is our old proxy)
            logger.LogInformation("[SteamLauncher] Deploying proxy launcher as {Exe}", targetExeName);

            try
            {
                // Try to kill any running instances of the target executable to release file lock
                var processName = Path.GetFileNameWithoutExtension(targetExePath);
                var runningProcesses = Process.GetProcessesByName(processName);
                foreach (var process in runningProcesses)
                {
                    try
                    {
                        if (process.MainModule?.FileName?.Equals(targetExePath, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            logger.LogWarning(
                                "[SteamLauncher] Killing running process {ProcessName} ({Pid}) to update proxy",
                                process.ProcessName,
                                process.Id);
                            process.Kill();
                            process.WaitForExit(1000);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[SteamLauncher] Failed to kill process {Pid}", process.Id);
                    }
                }

                // Wait briefly for file lock to release
                if (runningProcesses.Length > 0)
                {
                    Thread.Sleep(500);
                }

                File.Copy(proxySourcePath, targetExePath, overwrite: true);
                logger.LogInformation("[SteamLauncher] Successfully deployed proxy as {Exe}", targetExeName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[SteamLauncher] Failed to deploy proxy as {Exe}", targetExeName);
                return OperationResult<SteamLaunchPrepResult>.CreateFailure(
                    $"Failed to deploy proxy launcher: {ex.Message}");
            }

            var primaryDeployedPath = targetExePath;

            // 3. Create Proxy Config (always points to the workspace executable)
            var effectiveTargetExecutable = targetExecutablePath;

            // Validate that the target executable exists
            if (!File.Exists(effectiveTargetExecutable))
            {
                return OperationResult<SteamLaunchPrepResult>.CreateFailure(
                    $"Target executable not found: {effectiveTargetExecutable}. Workspace may not be properly prepared.");
            }

            // Ensure paths are absolute
            effectiveTargetExecutable = Path.GetFullPath(effectiveTargetExecutable);
            var effectiveWorkingDirectory = string.IsNullOrEmpty(targetWorkingDirectory)
                ? Path.GetDirectoryName(effectiveTargetExecutable) ?? string.Empty
                : Path.GetFullPath(targetWorkingDirectory);

            // Use direct workspace paths - proxy_config.json is regenerated on each launch with correct paths
            // Each profile uses its own adjacent workspace: {installationRoot}\.genhub-workspace\{profileId}\
            // This removes the .genhub-workspace-active junction indirection layer
            logger.LogInformation(
                "[SteamLauncher] Using direct workspace paths - Target: {Target}, WorkDir: {WorkDir}",
                effectiveTargetExecutable,
                effectiveWorkingDirectory);

            // Validate working directory exists
            if (!Directory.Exists(effectiveWorkingDirectory))
            {
                return OperationResult<SteamLaunchPrepResult>.CreateFailure(
                    $"Working directory not found: {effectiveWorkingDirectory}");
            }

            var config = new ProxyConfig
            {
                TargetExecutable = effectiveTargetExecutable,
                WorkingDirectory = effectiveWorkingDirectory,
                Arguments = targetArguments ?? [],
                SteamAppId = steamAppId,
            };

            var configJson = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(proxyConfigPath, configJson, cancellationToken);
            logger.LogInformation("[SteamLauncher] Wrote proxy config to {Path}", proxyConfigPath);
            logger.LogInformation(
                "[SteamLauncher] Proxy config - Target: {Target}, WorkDir: {WorkDir}, Args: {ArgCount}",
                config.TargetExecutable,
                config.WorkingDirectory,
                config.Arguments.Length);

            // 5. CRITICAL: Always write steam_appid.txt next to BOTH the working directory and the target executable.
            // Steam overlays/readiness logic checks the executable directory first. Many modded game clients call SteamAPI_Init
            // and immediately look for steam_appid.txt adjacent to their EXE. If we only write it to the install dir, the
            // workspace executable will trigger SteamAPI_RestartAppIfNecessary and surface the "invalid license" error.
            if (!string.IsNullOrEmpty(steamAppId))
            {
                await WriteSteamAppIdAsync(steamAppId, effectiveWorkingDirectory, cancellationToken);

                var targetDirectory = Path.GetDirectoryName(effectiveTargetExecutable);
                if (!string.IsNullOrEmpty(targetDirectory) &&
                    !string.Equals(targetDirectory, effectiveWorkingDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    await WriteSteamAppIdAsync(steamAppId, targetDirectory, cancellationToken);
                }
            }

            // 6. Ensure steam_appid.txt also exists alongside the proxy (Steam sometimes checks the launch dir)
            if (!string.IsNullOrEmpty(steamAppId))
            {
                await WriteSteamAppIdAsync(steamAppId, gameInstallPath, cancellationToken);
            }

            // 7. Critical: Ensure steam_api.dll and other runtime dependencies exist where the target exe loads from.
            // Some modded clients are distributed without these files in their manifests (to keep manifests minimal).
            // Missing steam_api.dll is the primary cause of "invalid license" when the workspace executable calls SteamAPI_Init.
            await EnsureRuntimeDependenciesAsync(gameInstallPath, effectiveWorkingDirectory, cancellationToken);

            var targetDirForDeps = Path.GetDirectoryName(effectiveTargetExecutable);
            if (!string.IsNullOrEmpty(targetDirForDeps) &&
                !string.Equals(targetDirForDeps, effectiveWorkingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                await EnsureRuntimeDependenciesAsync(gameInstallPath, targetDirForDeps, cancellationToken);
            }

            // Return result
            var result = new SteamLaunchPrepResult
            {
                ExecutablePath = primaryDeployedPath, // Sidecar proxy path (leave originals untouched)
                WorkingDirectory = gameInstallPath,
                ProfileId = profileId,
                FilesLinked = 0,
                FilesRemoved = 0,
                FilesBackedUp = 0,
                SteamAppId = steamAppId,
            };

            return OperationResult<SteamLaunchPrepResult>.CreateSuccess(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[SteamLauncher] Failed to prepare proxy for profile {ProfileId}", profileId);
            return OperationResult<SteamLaunchPrepResult>.CreateFailure($"Failed to prepare proxy: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> CleanupGameDirectoryAsync(
        string gameInstallPath,
        string executableName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("[SteamLauncher] Cleaning up game directory: {Path}", gameInstallPath);

            // 1. Remove Proxy Config
            var proxyConfigPath = Path.Combine(gameInstallPath, ProxyConfigFileName);
            if (File.Exists(proxyConfigPath))
            {
                logger.LogDebug("[SteamLauncher] Removing proxy config: {Path}", proxyConfigPath);
                File.Delete(proxyConfigPath);
            }

            // 2. Restore original game executable from backup
            var targetExePath = Path.Combine(gameInstallPath, executableName);
            var backupPath = targetExePath + SteamConstants.BackupExtension;

            if (File.Exists(backupPath))
            {
                logger.LogInformation(
                    "[SteamLauncher] Restoring original {Exe} from backup",
                    executableName);

                try
                {
                    // Delete the proxy (current exe)
                    if (File.Exists(targetExePath))
                    {
                        File.Delete(targetExePath);
                    }

                    // Restore original from backup
                    File.Move(backupPath, targetExePath);
                    logger.LogInformation(
                        "[SteamLauncher] Successfully restored original {Exe}",
                        executableName);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "[SteamLauncher] Failed to restore original {Exe} from backup",
                        executableName);
                }
            }
            else
            {
                logger.LogDebug(
                    "[SteamLauncher] No backup found for {Exe}, skipping restoration",
                    executableName);
            }

            // Cleanup any tracking file if it still exists from old version
            var trackingPath = Path.Combine(gameInstallPath, SteamConstants.TrackingFileName);
            if (File.Exists(trackingPath))
            {
                File.Delete(trackingPath);
            }

            // Note: .genhub-workspace-active junction cleanup removed - no longer using junctions
            // Each profile uses its own adjacent workspace directly
            logger.LogInformation("[SteamLauncher] Cleaned up game directory artifacts: {Path}", gameInstallPath);
            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[SteamLauncher] Failed to cleanup game directory: {Path}", gameInstallPath);
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Failed to cleanup: {ex.Message}"));
        }
    }

    private async Task WriteSteamAppIdAsync(string steamAppId, string directory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var appIdPath = Path.Combine(directory, "steam_appid.txt");

        // Check current content - only rewrite if different (avoid breaking hardlinks unnecessarily)
        var needsWrite = true;
        if (File.Exists(appIdPath))
        {
            var currentContent = await File.ReadAllTextAsync(appIdPath, cancellationToken);
            needsWrite = currentContent.Trim() != steamAppId;
            if (needsWrite)
            {
                logger.LogWarning(
                    "[SteamLauncher] steam_appid.txt has wrong ID ({WrongId}), overwriting with correct ID ({CorrectId})",
                    currentContent.Trim(),
                    steamAppId);

                // Delete the hardlink to avoid modifying original
                File.Delete(appIdPath);
            }
        }

        if (needsWrite)
        {
            await File.WriteAllTextAsync(appIdPath, steamAppId, cancellationToken);
            logger.LogInformation(
                "[SteamLauncher] Wrote steam_appid.txt ({AppId}) to {Path}",
                steamAppId,
                directory);
        }
    }

    private async Task EnsureRuntimeDependenciesAsync(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return;
        }

        var filesToEnsure = new[] { "steam_api.dll", "binkw32.dll", "mss32.dll" };
        foreach (var file in filesToEnsure)
        {
            var sourcePath = Path.Combine(sourceDirectory, file);
            var destPath = Path.Combine(destinationDirectory, file);
            if (File.Exists(sourcePath) && !File.Exists(destPath))
            {
                File.Copy(sourcePath, destPath);
                logger.LogInformation("[SteamLauncher] Copied missing critical file {File} to {Destination}", file, destinationDirectory);
            }
        }

        // Yield control occasionally to respect cancellation
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }
}
