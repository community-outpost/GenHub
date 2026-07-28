using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
/// 2. We back up and replace the original game executable with the proxy.
/// 3. We write a proxy_config.json telling the Proxy to launch the Workspace executable using direct paths.
/// 4. Steam launches the proxy under the original executable name; the proxy then runs the Workspace game.
///
/// Each profile uses its own adjacent workspace: {installationRoot}\.genhub-workspace\{profileId}\
/// The proxy_config.json is regenerated on each launch with the correct workspace paths for that profile.
/// </summary>
public class SteamLauncher : ISteamLauncher
{
    private const string ProxyConfigFileName = "proxy_config.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _installationMutationLocks =
        new(InstallationPathLockKey.Comparer);

    private readonly ILogger<SteamLauncher> _logger;
    private readonly string? _proxySourcePathOverride;
    private readonly Func<string, string, CancellationToken, Task> _writeAllTextAsync;

    /// <summary>
    /// Initializes a new instance of the <see cref="SteamLauncher"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public SteamLauncher(ILogger<SteamLauncher> logger)
        : this(logger, null, File.WriteAllTextAsync)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SteamLauncher"/> class with test seams.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="proxySourcePathOverride">An optional proxy source path override.</param>
    /// <param name="writeAllTextAsync">The text file writer.</param>
    internal SteamLauncher(
        ILogger<SteamLauncher> logger,
        string? proxySourcePathOverride,
        Func<string, string, CancellationToken, Task> writeAllTextAsync)
    {
        _logger = logger;
        _proxySourcePathOverride = proxySourcePathOverride;
        _writeAllTextAsync = writeAllTextAsync;
    }

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
        PreparationRollback? rollback = null;
        IDisposable? installationMutationLock = null;

        try
        {
            gameInstallPath = Path.GetFullPath(gameInstallPath);
            installationMutationLock = await AcquireInstallationMutationLockAsync(
                gameInstallPath,
                cancellationToken);
            _logger.LogInformation(
                "[SteamLauncher] Preparing game directory {Path} for profile {ProfileId} using Proxy Launcher",
                gameInstallPath,
                profileId);

            cancellationToken.ThrowIfCancellationRequested();

            // Validate every prerequisite that can be checked without changing the installation.
            var proxySourcePath = ResolveProxySourcePath();
            if (!File.Exists(proxySourcePath))
            {
                return OperationResult<SteamLaunchPrepResult>.CreateFailure(
                    $"Proxy Launcher binary not found at {proxySourcePath}. Please build GenHub.ProxyLauncher project.");
            }

            if (!Directory.Exists(gameInstallPath))
            {
                return OperationResult<SteamLaunchPrepResult>.CreateFailure(
                    $"Game installation directory not found: {gameInstallPath}");
            }

            var targetExePath = Path.Combine(gameInstallPath, executableName);
            var backupPath = targetExePath + SteamConstants.BackupExtension;
            var proxyConfigPath = Path.Combine(gameInstallPath, ProxyConfigFileName);

            if (Directory.Exists(targetExePath))
            {
                return OperationResult<SteamLaunchPrepResult>.CreateFailure(
                    $"Game executable path is a directory: {targetExePath}");
            }

            if (!File.Exists(targetExePath) && !File.Exists(backupPath))
            {
                return OperationResult<SteamLaunchPrepResult>.CreateFailure(
                    $"Original game executable not found: {targetExePath}");
            }

            if (Directory.Exists(backupPath))
            {
                return OperationResult<SteamLaunchPrepResult>.CreateFailure(
                    $"Backup executable path is a directory: {backupPath}");
            }

            var effectiveTargetExecutable = Path.GetFullPath(targetExecutablePath);
            if (!File.Exists(effectiveTargetExecutable))
            {
                return OperationResult<SteamLaunchPrepResult>.CreateFailure(
                    $"Target executable not found: {effectiveTargetExecutable}. Workspace may not be properly prepared.");
            }

            var effectiveWorkingDirectory = string.IsNullOrEmpty(targetWorkingDirectory)
                ? Path.GetDirectoryName(effectiveTargetExecutable) ?? string.Empty
                : Path.GetFullPath(targetWorkingDirectory);

            if (!Directory.Exists(effectiveWorkingDirectory))
            {
                return OperationResult<SteamLaunchPrepResult>.CreateFailure(
                    $"Working directory not found: {effectiveWorkingDirectory}");
            }

            var targetDirectory = Path.GetDirectoryName(effectiveTargetExecutable);
            if (string.IsNullOrEmpty(targetDirectory) || !Directory.Exists(targetDirectory))
            {
                return OperationResult<SteamLaunchPrepResult>.CreateFailure(
                    $"Target executable directory not found: {targetDirectory}");
            }

            var config = new ProxyConfig
            {
                TargetExecutable = effectiveTargetExecutable,
                WorkingDirectory = effectiveWorkingDirectory,
                Arguments = targetArguments ?? [],
                SteamAppId = steamAppId,
            };

            var configJson = JsonSerializer.Serialize(config, JsonOptions);
            var appIdDirectories = string.IsNullOrEmpty(steamAppId)
                ? []
                : new[] { effectiveWorkingDirectory, targetDirectory, gameInstallPath }
                    .Distinct(PathComparer)
                    .ToArray();
            var filesToCapture = new List<string> { proxyConfigPath };

            foreach (var directory in appIdDirectories)
            {
                filesToCapture.Add(Path.Combine(directory, "steam_appid.txt"));
            }

            foreach (var path in filesToCapture.Distinct(PathComparer))
            {
                if (Directory.Exists(path))
                {
                    return OperationResult<SteamLaunchPrepResult>.CreateFailure(
                        $"Required file path is a directory: {path}");
                }
            }

            var dependencyCopies = GetRequiredDependencyCopies(
                gameInstallPath,
                [effectiveWorkingDirectory, targetDirectory]);
            foreach (var (_, destinationPath) in dependencyCopies)
            {
                if (Directory.Exists(destinationPath))
                {
                    return OperationResult<SteamLaunchPrepResult>.CreateFailure(
                        $"Runtime dependency path is a directory: {destinationPath}");
                }
            }

            rollback = new PreparationRollback(
                targetExePath,
                backupPath,
                proxySourcePath,
                filesToCapture);

            cancellationToken.ThrowIfCancellationRequested();
            await StopRunningTargetProcessesAsync(targetExePath, cancellationToken);

            rollback.PrepareExecutableBackup();
            rollback.DeployProxy();

            _logger.LogInformation("[SteamLauncher] Successfully deployed proxy as {Exe}", executableName);
            _logger.LogInformation(
                "[SteamLauncher] Using direct workspace paths - Target: {Target}, WorkDir: {WorkDir}",
                effectiveTargetExecutable,
                effectiveWorkingDirectory);

            await rollback.WriteTextAsync(proxyConfigPath, configJson, _writeAllTextAsync, cancellationToken);
            _logger.LogInformation("[SteamLauncher] Wrote proxy config to {Path}", proxyConfigPath);
            _logger.LogInformation(
                "[SteamLauncher] Proxy config - Target: {Target}, WorkDir: {WorkDir}, Args: {ArgCount}",
                config.TargetExecutable,
                config.WorkingDirectory,
                config.Arguments.Length);

            foreach (var directory in appIdDirectories)
            {
                await WriteSteamAppIdAsync(steamAppId!, directory, rollback, cancellationToken);
            }

            foreach (var (sourcePath, destinationPath) in dependencyCopies)
            {
                await rollback.CopyNewFileAsync(sourcePath, destinationPath, cancellationToken);
                _logger.LogInformation(
                    "[SteamLauncher] Copied missing critical file {File} to {Destination}",
                    Path.GetFileName(sourcePath),
                    Path.GetDirectoryName(destinationPath));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var result = new SteamLaunchPrepResult
            {
                ExecutablePath = targetExePath,
                WorkingDirectory = gameInstallPath,
                ProfileId = profileId,
                FilesLinked = 0,
                FilesRemoved = 0,
                FilesBackedUp = 0,
                SteamAppId = steamAppId,
            };

            rollback.Commit();
            return OperationResult<SteamLaunchPrepResult>.CreateSuccess(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SteamLauncher] Failed to prepare proxy for profile {ProfileId}", profileId);

            var errors = new List<string>
            {
                ex is OperationCanceledException
                    ? "Steam proxy preparation was canceled."
                    : $"Failed to prepare proxy: {ex.Message}",
            };

            if (rollback is not null)
            {
                errors.AddRange(rollback.Rollback());
            }

            return OperationResult<SteamLaunchPrepResult>.CreateFailure(errors);
        }
        finally
        {
            installationMutationLock?.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> CleanupGameDirectoryAsync(
        string gameInstallPath,
        string executableName,
        CancellationToken cancellationToken = default)
    {
        IDisposable? installationMutationLock = null;

        try
        {
            gameInstallPath = Path.GetFullPath(gameInstallPath);
            installationMutationLock = await AcquireInstallationMutationLockAsync(
                gameInstallPath,
                cancellationToken);
            _logger.LogInformation("[SteamLauncher] Cleaning up game directory: {Path}", gameInstallPath);

            var targetExePath = Path.Combine(gameInstallPath, executableName);
            var backupPath = targetExePath + SteamConstants.BackupExtension;

            if (File.Exists(backupPath))
            {
                if (File.Exists(targetExePath))
                {
                    var proxySourcePath = ResolveProxySourcePath();
                    if (!File.Exists(proxySourcePath) ||
                        !FilesAreEqual(targetExePath, proxySourcePath))
                    {
                        var error =
                            $"Refusing to replace '{targetExePath}' from the unverified backup '{backupPath}'.";
                        _logger.LogError("[SteamLauncher] {Error}", error);
                        return OperationResult<bool>.CreateFailure(error);
                    }
                }

                _logger.LogInformation(
                    "[SteamLauncher] Restoring original {Exe} from backup",
                    executableName);
                File.Move(backupPath, targetExePath, overwrite: true);
                _logger.LogInformation(
                    "[SteamLauncher] Successfully restored original {Exe}",
                    executableName);
            }
            else
            {
                var proxySourcePath = ResolveProxySourcePath();
                if (File.Exists(targetExePath) &&
                    File.Exists(proxySourcePath) &&
                    FilesAreEqual(targetExePath, proxySourcePath))
                {
                    var error =
                        $"Cannot restore '{targetExePath}' because its original backup is missing.";
                    _logger.LogError("[SteamLauncher] {Error}", error);
                    return OperationResult<bool>.CreateFailure(error);
                }

                _logger.LogDebug(
                    "[SteamLauncher] No backup found for {Exe}, skipping restoration",
                    executableName);
            }

            var proxyConfigPath = Path.Combine(gameInstallPath, ProxyConfigFileName);
            if (File.Exists(proxyConfigPath))
            {
                _logger.LogDebug("[SteamLauncher] Removing proxy config: {Path}", proxyConfigPath);
                File.Delete(proxyConfigPath);
            }

            // Cleanup any tracking file if it still exists from old version
            var trackingPath = Path.Combine(gameInstallPath, SteamConstants.TrackingFileName);
            if (File.Exists(trackingPath))
            {
                File.Delete(trackingPath);
            }

            // Note: .genhub-workspace-active junction cleanup removed - no longer using junctions
            // Each profile uses its own adjacent workspace directly
            _logger.LogInformation("[SteamLauncher] Cleaned up game directory artifacts: {Path}", gameInstallPath);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SteamLauncher] Failed to cleanup game directory: {Path}", gameInstallPath);
            return OperationResult<bool>.CreateFailure($"Failed to cleanup: {ex.Message}");
        }
        finally
        {
            installationMutationLock?.Dispose();
        }
    }

    private static async Task<IDisposable> AcquireInstallationMutationLockAsync(
        string installationPath,
        CancellationToken cancellationToken)
    {
        var normalizedPath = InstallationPathLockKey.Create(installationPath);
        var semaphore = _installationMutationLocks.GetOrAdd(
            normalizedPath,
            _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new SemaphoreReleaser(semaphore);
    }

    private async Task WriteSteamAppIdAsync(
        string steamAppId,
        string directory,
        PreparationRollback rollback,
        CancellationToken cancellationToken)
    {
        var appIdPath = Path.Combine(directory, "steam_appid.txt");

        // Check current content - only rewrite if different (avoid breaking hardlinks unnecessarily)
        var needsWrite = true;
        if (File.Exists(appIdPath))
        {
            var currentContent = await File.ReadAllTextAsync(appIdPath, cancellationToken);
            needsWrite = currentContent.Trim() != steamAppId;
            if (needsWrite)
            {
                _logger.LogWarning(
                    "[SteamLauncher] steam_appid.txt has wrong ID ({WrongId}), overwriting with correct ID ({CorrectId})",
                    currentContent.Trim(),
                    steamAppId);
            }
        }

        if (needsWrite)
        {
            await rollback.WriteTextAsync(appIdPath, steamAppId, _writeAllTextAsync, cancellationToken);
            _logger.LogInformation(
                "[SteamLauncher] Wrote steam_appid.txt ({AppId}) to {Path}",
                steamAppId,
                directory);
        }
    }

    private string ResolveProxySourcePath()
    {
        if (!string.IsNullOrEmpty(_proxySourcePathOverride))
        {
            return Path.GetFullPath(_proxySourcePathOverride);
        }

        var currentBaseDir = AppDomain.CurrentDomain.BaseDirectory;
        var defaultPath = Path.Combine(currentBaseDir, SteamConstants.ProxyLauncherFileName);
        if (File.Exists(defaultPath))
        {
            return defaultPath;
        }

        _logger.LogDebug(
            "[SteamLauncher] Proxy Launcher not found in base directory: {Path}. Checking fallbacks...",
            defaultPath);

        var developmentPaths = new[]
        {
            Path.GetFullPath(Path.Combine(currentBaseDir, "..", "..", "..", "..", "GenHub.ProxyLauncher", "bin", "Debug", "net8.0-windows", "win-x64", "GenHub.ProxyLauncher.exe")),
            Path.GetFullPath(Path.Combine(currentBaseDir, "..", "..", "..", "..", "GenHub.ProxyLauncher", "bin", "Release", "net8.0-windows", "win-x64", "GenHub.ProxyLauncher.exe")),
            Path.GetFullPath(Path.Combine(currentBaseDir, "net8.0-windows", "GenHub.ProxyLauncher.exe")),
        };

        return developmentPaths.FirstOrDefault(File.Exists) ?? defaultPath;
    }

    private async Task StopRunningTargetProcessesAsync(
        string targetExePath,
        CancellationToken cancellationToken)
    {
        var processName = Path.GetFileNameWithoutExtension(targetExePath);
        var runningProcesses = Process.GetProcessesByName(processName);

        try
        {
            foreach (var process in runningProcesses)
            {
                try
                {
                    if (process.MainModule?.FileName is string processPath &&
                        PathComparer.Equals(Path.GetFullPath(processPath), targetExePath))
                    {
                        _logger.LogWarning(
                            "[SteamLauncher] Killing running process {ProcessName} ({Pid}) to update proxy",
                            process.ProcessName,
                            process.Id);
                        process.Kill();
                        process.WaitForExit(1000);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[SteamLauncher] Failed to kill process {Pid}", process.Id);
                }
            }

            if (runningProcesses.Length > 0)
            {
                await Task.Delay(500, cancellationToken);
            }
        }
        finally
        {
            foreach (var process in runningProcesses)
            {
                process.Dispose();
            }
        }
    }

    private List<(string SourcePath, string DestinationPath)> GetRequiredDependencyCopies(
        string sourceDirectory,
        IEnumerable<string> destinationDirectories)
    {
        var filesToEnsure = new[] { "steam_api.dll", "binkw32.dll", "mss32.dll" };
        var copies = new List<(string SourcePath, string DestinationPath)>();

        foreach (var destinationDirectory in destinationDirectories.Distinct(PathComparer))
        {
            foreach (var file in filesToEnsure)
            {
                var sourcePath = Path.Combine(sourceDirectory, file);
                var destinationPath = Path.Combine(destinationDirectory, file);
                if (File.Exists(sourcePath) && !File.Exists(destinationPath))
                {
                    copies.Add((sourcePath, destinationPath));
                }
            }
        }

        return copies;
    }

    private bool FilesAreEqual(string firstPath, string secondPath)
    {
        var firstInfo = new FileInfo(firstPath);
        var secondInfo = new FileInfo(secondPath);
        if (firstInfo.Length != secondInfo.Length)
        {
            return false;
        }

        using var firstStream = File.OpenRead(firstPath);
        using var secondStream = File.OpenRead(secondPath);
        return SHA256.HashData(firstStream).SequenceEqual(SHA256.HashData(secondStream));
    }

    private sealed class SemaphoreReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = semaphore;
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _semaphore.Release();
                _disposed = true;
            }
        }
    }

    private sealed class PreparationRollback
    {
        private readonly string _targetExePath;
        private readonly string _backupPath;
        private readonly string _proxySourcePath;
        private readonly string _executableSnapshotPath;
        private readonly Dictionary<string, byte[]?> _originalFiles = new(PathComparer);
        private readonly Dictionary<string, byte[]> _preparedFiles = new(PathComparer);
        private readonly List<string> _mutatedFiles = [];
        private readonly HashSet<string> _temporaryFiles = new(PathComparer);
        private bool _backupCreated;
        private bool _executableMutationStarted;
        private bool _targetInitiallyExisted;
        private string? _executableRestoreSource;
        private bool _completed;

        public PreparationRollback(
            string targetExePath,
            string backupPath,
            string proxySourcePath,
            IEnumerable<string> filesToCapture)
        {
            _targetExePath = targetExePath;
            _backupPath = backupPath;
            _proxySourcePath = proxySourcePath;
            _executableSnapshotPath = CreateTemporaryPath(targetExePath);

            foreach (var path in filesToCapture.Distinct(PathComparer))
            {
                _originalFiles[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
        }

        public void PrepareExecutableBackup()
        {
            _targetInitiallyExisted = File.Exists(_targetExePath);

            if (!_targetInitiallyExisted)
            {
                if (!File.Exists(_backupPath))
                {
                    throw new FileNotFoundException(
                        "Neither the target executable nor its recovery backup is available.",
                        _targetExePath);
                }

                _executableRestoreSource = _backupPath;
                return;
            }

            _temporaryFiles.Add(_executableSnapshotPath);
            File.Copy(_targetExePath, _executableSnapshotPath, overwrite: false);

            if (File.Exists(_backupPath))
            {
                if (!FilesAreEqual(_targetExePath, _proxySourcePath))
                {
                    throw new IOException(
                        $"Refusing to use unverified pre-existing backup '{_backupPath}' while " +
                        $"'{_targetExePath}' is not the GenHub proxy.");
                }

                _executableRestoreSource = _backupPath;
                return;
            }

            _executableRestoreSource = _executableSnapshotPath;
            var backupStagingPath = CreateTemporaryPath(_backupPath);
            _temporaryFiles.Add(backupStagingPath);
            File.Copy(_targetExePath, backupStagingPath, overwrite: false);
            File.Move(backupStagingPath, _backupPath, overwrite: false);
            _temporaryFiles.Remove(backupStagingPath);
            _backupCreated = true;
        }

        public void DeployProxy()
        {
            var stagingPath = CreateTemporaryPath(_targetExePath);
            _temporaryFiles.Add(stagingPath);
            File.Copy(_proxySourcePath, stagingPath, overwrite: false);

            if (_targetInitiallyExisted)
            {
                if (!File.Exists(_targetExePath) ||
                    !FilesAreEqual(_targetExePath, _executableSnapshotPath))
                {
                    throw new IOException(
                        $"Game executable changed before proxy deployment: {_targetExePath}");
                }
            }
            else if (File.Exists(_targetExePath))
            {
                throw new IOException(
                    $"Game executable appeared before proxy deployment: {_targetExePath}");
            }

            _executableMutationStarted = true;
            File.Move(stagingPath, _targetExePath, overwrite: true);
            _temporaryFiles.Remove(stagingPath);
        }

        public async Task WriteTextAsync(
            string path,
            string contents,
            Func<string, string, CancellationToken, Task> writer,
            CancellationToken cancellationToken)
        {
            var stagingPath = CreateTemporaryPath(path);
            _temporaryFiles.Add(stagingPath);

            await writer(stagingPath, contents, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCapturedFileIsUnchanged(path);
            var preparedContents = File.ReadAllBytes(stagingPath);
            File.Move(stagingPath, path, overwrite: true);
            _preparedFiles[path] = preparedContents;
            TrackMutation(path);
            _temporaryFiles.Remove(stagingPath);
        }

        public async Task CopyNewFileAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);

            _originalFiles[destinationPath] = null;
            TrackMutation(destinationPath);
            await source.CopyToAsync(destination, cancellationToken);
        }

        public void Commit()
        {
            var errors = new List<string>();
            CleanupTemporaryFiles(errors);
            if (errors.Count > 0)
            {
                throw new IOException(string.Join(" ", errors));
            }

            _completed = true;
        }

        public IReadOnlyList<string> Rollback()
        {
            if (_completed)
            {
                return [];
            }

            var errors = new List<string>();

            for (var index = _mutatedFiles.Count - 1; index >= 0; index--)
            {
                var path = _mutatedFiles[index];

                try
                {
                    if (!CanRestoreCapturedFile(path))
                    {
                        errors.Add($"Rollback did not overwrite unexpectedly changed file '{path}'.");
                        continue;
                    }

                    RestoreCapturedFile(path, _originalFiles[path]);
                }
                catch (Exception ex)
                {
                    errors.Add($"Rollback failed for '{path}': {ex.Message}");
                }
            }

            var executableRestored = TryRestoreExecutable(errors);
            if (executableRestored && _backupCreated)
            {
                try
                {
                    File.Delete(_backupPath);
                }
                catch (Exception ex)
                {
                    errors.Add($"Rollback failed to remove new backup '{_backupPath}': {ex.Message}");
                }
            }

            if (executableRestored)
            {
                CleanupTemporaryFiles(errors);
            }
            else
            {
                foreach (var path in _temporaryFiles.Where(File.Exists))
                {
                    errors.Add($"Recovery file retained at '{path}'.");
                }

                if (_backupCreated && File.Exists(_backupPath))
                {
                    errors.Add($"Recovery backup retained at '{_backupPath}'.");
                }
            }

            _completed = true;
            return errors;
        }

        private void TrackMutation(string path)
        {
            if (!_mutatedFiles.Contains(path, PathComparer))
            {
                _mutatedFiles.Add(path);
            }
        }

        private bool CanRestoreCapturedFile(string path)
        {
            if (!_preparedFiles.TryGetValue(path, out var preparedContents))
            {
                return true;
            }

            if (!File.Exists(path))
            {
                return _originalFiles[path] is null;
            }

            return File.ReadAllBytes(path).SequenceEqual(preparedContents);
        }

        private void EnsureCapturedFileIsUnchanged(string path)
        {
            var originalContents = _originalFiles[path];
            if (originalContents is null)
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    throw new IOException($"File appeared before preparation could update it: {path}");
                }

                return;
            }

            if (!File.Exists(path) ||
                !File.ReadAllBytes(path).SequenceEqual(originalContents))
            {
                throw new IOException($"File changed before preparation could update it: {path}");
            }
        }

        private bool TryRestoreExecutable(List<string> errors)
        {
            if (!_executableMutationStarted)
            {
                return true;
            }

            try
            {
                var restoreSource = GetExecutableRestoreSource();
                if (_targetInitiallyExisted &&
                    File.Exists(_targetExePath) &&
                    FilesAreEqual(_targetExePath, restoreSource))
                {
                    return true;
                }

                if (File.Exists(_targetExePath) &&
                    !FilesAreEqual(_targetExePath, _proxySourcePath))
                {
                    errors.Add(
                        $"Rollback did not overwrite unexpectedly changed executable '{_targetExePath}'.");
                    return false;
                }

                AtomicCopy(restoreSource, _targetExePath);
                return true;
            }
            catch (Exception ex)
            {
                errors.Add($"Rollback failed to restore executable '{_targetExePath}': {ex.Message}");
                return false;
            }
        }

        private string GetExecutableRestoreSource()
        {
            if (!string.IsNullOrEmpty(_executableRestoreSource) &&
                File.Exists(_executableRestoreSource))
            {
                return _executableRestoreSource;
            }

            if (File.Exists(_backupPath))
            {
                return _backupPath;
            }

            throw new FileNotFoundException(
                "No recovery copy of the original executable is available.",
                _executableRestoreSource);
        }

        private void CleanupTemporaryFiles(List<string> errors)
        {
            foreach (var path in _temporaryFiles.ToArray())
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    _temporaryFiles.Remove(path);
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to remove preparation artifact '{path}': {ex.Message}");
                }
            }
        }

        private void RestoreCapturedFile(string path, byte[]? originalContents)
        {
            if (originalContents is null)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            var stagingPath = CreateTemporaryPath(path);
            try
            {
                File.WriteAllBytes(stagingPath, originalContents);
                File.Move(stagingPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(stagingPath))
                {
                    File.Delete(stagingPath);
                }
            }
        }

        private void AtomicCopy(string sourcePath, string destinationPath)
        {
            var stagingPath = CreateTemporaryPath(destinationPath);
            _temporaryFiles.Add(stagingPath);
            File.Copy(sourcePath, stagingPath, overwrite: false);
            File.Move(stagingPath, destinationPath, overwrite: true);
            _temporaryFiles.Remove(stagingPath);
        }

        private bool FilesAreEqual(string firstPath, string secondPath)
        {
            var firstInfo = new FileInfo(firstPath);
            var secondInfo = new FileInfo(secondPath);
            if (firstInfo.Length != secondInfo.Length)
            {
                return false;
            }

            using var firstStream = File.OpenRead(firstPath);
            using var secondStream = File.OpenRead(secondPath);
            return SHA256.HashData(firstStream).SequenceEqual(SHA256.HashData(secondStream));
        }

        private string CreateTemporaryPath(string path)
        {
            return $"{path}.genhub-rollback-{Guid.NewGuid():N}";
        }
    }
}
