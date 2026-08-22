namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Helpers;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Abstract base class for downloadable package deployment fixes (e.g., HD Icons, Expanded LAN Lobby).
/// Handles package download, hash validation, safe materialization with backup tracking, marker persistence, and rollback.
/// </summary>
public abstract class BasePackageDeploymentFix(
    IHttpClientFactory httpClientFactory,
    ILogger logger,
    string defaultMarkerFileName,
    string? markerPath = null)
    : BaseActionSet(logger)
{
    /// <summary>
    /// Execution context for package deployment operations.
    /// </summary>
    /// <param name="TempExtractDir">The temporary directory for archive extraction.</param>
    /// <param name="TempBackupDir">The temporary directory for backing up pre-existing game files.</param>
    /// <param name="BackupEntries">The list tracking backup metadata for rollback.</param>
    /// <param name="DeployedFiles">The list accumulating deployed file paths.</param>
    /// <param name="Details">The diagnostic details list.</param>
    public record DeploymentContext(
        string TempExtractDir,
        string TempBackupDir,
        List<(string DestPath, bool ExistedBefore, string? BackupPath)> BackupEntries,
        List<string> DeployedFiles,
        List<string> Details);

    private readonly string _markerPath = markerPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GenHub",
        ActionSetConstants.Paths.SubActionSetMarkers,
        defaultMarkerFileName);

    /// <summary>
    /// Gets the list of download URLs for the package.
    /// </summary>
    protected abstract IReadOnlyList<string> DownloadUrls { get; }

    /// <summary>
    /// Gets the expected SHA-256 hash for package verification.
    /// </summary>
    protected abstract string ExpectedSha256 { get; }

    /// <summary>
    /// Gets the human-readable package name for logs and messages.
    /// </summary>
    protected abstract string PackageDisplayName { get; }

    /// <summary>
    /// Gets the file prefix used for temporary download files.
    /// </summary>
    protected abstract string TempFilePrefix { get; }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(AreAssetsPresent(installation));
    }

    /// <summary>
    /// Deploys a file with backup tracking, preventing duplicate backups of the same destination path.
    /// </summary>
    /// <param name="sourceFilePath">The path of the source file to deploy.</param>
    /// <param name="destPath">The destination path in the game directory.</param>
    /// <param name="context">The deployment context.</param>
    protected static void DeployFileWithBackup(
        string sourceFilePath,
        string destPath,
        DeploymentContext context)
    {
        var alreadyBackedUp = context.BackupEntries.Any(b => string.Equals(b.DestPath, destPath, StringComparison.OrdinalIgnoreCase));
        var existedBefore = File.Exists(destPath);
        string? backupPath = null;

        if (existedBefore && !alreadyBackedUp)
        {
            Directory.CreateDirectory(context.TempBackupDir);
            backupPath = Path.Combine(context.TempBackupDir, $"{Guid.NewGuid():N}_{Path.GetFileName(destPath)}");
            File.Copy(destPath, backupPath, overwrite: true);
            context.BackupEntries.Add((destPath, existedBefore, backupPath));
        }
        else if (!alreadyBackedUp)
        {
            context.BackupEntries.Add((destPath, existedBefore, null));
        }

        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        File.Copy(sourceFilePath, destPath, overwrite: true);
        if (!context.DeployedFiles.Contains(destPath, StringComparer.OrdinalIgnoreCase))
        {
            context.DeployedFiles.Add(destPath);
        }
    }

    /// <summary>
    /// Collects existing file paths from a directory matching candidate names.
    /// </summary>
    /// <param name="basePath">The base directory path.</param>
    /// <param name="candidateNames">The candidate file names.</param>
    /// <param name="output">The list accumulating found paths.</param>
    protected static void CollectExistingFiles(string? basePath, IReadOnlyList<string> candidateNames, List<string> output)
    {
        if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
        {
            return;
        }

        output.AddRange(candidateNames
            .Select(name => Path.Combine(basePath, name))
            .Where(File.Exists)
            .Except(output, StringComparer.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{TempFilePrefix}_{Guid.NewGuid():N}.dat");
        var tempExtractDir = Path.Combine(Path.GetTempPath(), $"{TempFilePrefix}_extract_{Guid.NewGuid():N}");
        var tempBackupDir = Path.Combine(Path.GetTempPath(), $"{TempFilePrefix}_backup_{Guid.NewGuid():N}");
        var backupEntries = new List<(string DestPath, bool ExistedBefore, string? BackupPath)>();
        var deployedFiles = new List<string>();
        var details = new List<string>();
        var context = new DeploymentContext(tempExtractDir, tempBackupDir, backupEntries, deployedFiles, details);

        try
        {
            details.Add($"Downloading {PackageDisplayName} package...");

            var downloaded = await DownloadPackageAsync(tempFile, details, ct);
            if (!downloaded)
            {
                return new ActionSetResult(false, $"Failed to download {PackageDisplayName} from available sources.", details);
            }

            var validation = await DownloadSecurityValidator.ValidateFileAsync(
                tempFile,
                allowedSha256Hashes: [ExpectedSha256],
                ct: ct);

            if (!validation.Success)
            {
                var errorSummary = string.Join("; ", validation.Errors);
                Logger.LogWarning("Security validation failed for {Name} package: {Error}", PackageDisplayName, errorSummary);
                return new ActionSetResult(false, $"Package failed security verification: {errorSummary}", details);
            }

            details.Add("✓ Package integrity verified via SHA-256 checksum.");
            details.Add($"Extracting {PackageDisplayName} assets...");
            Directory.CreateDirectory(tempExtractDir);

            var (extractedCount, deployed) = await ExtractAndDeployAssetsAsync(
                tempFile,
                context,
                installation,
                ct);

            if (deployed == null)
            {
                RollbackDeployment(backupEntries, details);
                return new ActionSetResult(false, $"Failed to extract and validate {PackageDisplayName} package.", details);
            }

            details.Add($"✓ Extracted and deployed {extractedCount} assets to game folders.");

            if (!RecordDeploymentMarker(deployedFiles))
            {
                details.Add("✗ Failed to record the deployment marker. Rolling back deployed files.");
                RollbackDeployment(backupEntries, details);
                return new ActionSetResult(false, $"Failed to record the deployment marker for {Id}.", details);
            }

            return new ActionSetResult(true, null, details);
        }
        catch (OperationCanceledException)
        {
            RollbackDeployment(backupEntries, details);
            throw;
        }
        catch (Exception ex)
        {
            RollbackDeployment(backupEntries, details);
            Logger.LogError(ex, "Error applying {Name} fix", PackageDisplayName);
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
        finally
        {
            CleanupTempFiles(tempFile, tempExtractDir, tempBackupDir);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();

        try
        {
            if (!File.Exists(_markerPath))
            {
                if (AreAssetsPresent(installation))
                {
                    details.Add($"⚠ No deployment marker found. Custom {PackageDisplayName} files may have been installed manually; please remove them manually if desired.");
                    return Task.FromResult(new ActionSetResult(false, "No deployment marker found to undo.", details));
                }

                return Task.FromResult(new ActionSetResult(true, null, ["No deployment record found to undo."]));
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(_markerPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.LogWarning(ex, "Failed to read installed file paths from marker {MarkerPath}", _markerPath);
                return Task.FromResult(new ActionSetResult(false, $"Failed to read deployment marker: {ex.Message}", ["✗ Could not read deployment marker."]));
            }

            var hasRootedPaths = lines.Any(l => !string.IsNullOrWhiteSpace(l) && Path.IsPathRooted(l.Trim()));
            IReadOnlyList<string> targetFiles = !hasRootedPaths && lines.Length > 0
                ? GetLegacyFilePaths(installation)
                : lines;

            var (removedCount, remainingFiles) = DeleteRecordedFiles(targetFiles, ct);

            UpdateMarkerAfterUndo(remainingFiles);

            if (remainingFiles.Count == 0)
            {
                details.Add($"{PackageDisplayName} removed ({removedCount} files deleted).");
                return Task.FromResult(new ActionSetResult(true, null, details));
            }

            details.Add($"⚠ Partial undo: removed {removedCount} files, {remainingFiles.Count} files could not be deleted.");
            return Task.FromResult(new ActionSetResult(false, $"Failed to remove {remainingFiles.Count} files during undo.", details));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning(ex, "Failed to delete marker or files for {Name}", PackageDisplayName);
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <summary>
    /// Extracts archive contents and deploys them to target game directories with backup tracking.
    /// </summary>
    /// <param name="archivePath">The local path of the downloaded archive.</param>
    /// <param name="context">The deployment context.</param>
    /// <param name="installation">The targeted game installation.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A tuple of extracted file count and list of deployed file paths.</returns>
    protected abstract Task<(int ExtractedCount, List<string>? DeployedFiles)> ExtractAndDeployAssetsAsync(
        string archivePath,
        DeploymentContext context,
        GameInstallation installation,
        CancellationToken ct);

    /// <summary>
    /// Determines whether the deployed assets are present in the game installation.
    /// </summary>
    /// <param name="installation">The game installation to inspect.</param>
    /// <returns><c>true</c> if all required assets are present; otherwise, <c>false</c>.</returns>
    protected abstract bool AreAssetsPresent(GameInstallation installation);

    /// <summary>
    /// Gets legacy file paths if no absolute paths are present in marker.
    /// </summary>
    /// <param name="installation">The game installation.</param>
    /// <returns>List of candidate legacy asset paths.</returns>
    protected abstract List<string> GetLegacyFilePaths(GameInstallation installation);

    /// <summary>
    /// Downloads the package from available mirror URLs.
    /// </summary>
    /// <param name="tempFile">The destination temporary file path.</param>
    /// <param name="details">The diagnostic details list.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns><c>true</c> if download succeeded; otherwise, <c>false</c>.</returns>
    protected async Task<bool> DownloadPackageAsync(
        string tempFile,
        List<string> details,
        CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient("Downloader");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        foreach (var url in DownloadUrls)
        {
            try
            {
                Logger.LogInformation("Attempting {Name} download from {Url}", PackageDisplayName, url);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await response.Content.CopyToAsync(fs, ct);
                }

                var fileInfo = new FileInfo(tempFile);
                if (fileInfo.Length < ActionSetConstants.Validation.MinimumAddonPackageSizeBytes)
                {
                    Logger.LogWarning("Downloaded file from {Url} is too small ({Size} bytes).", url, fileInfo.Length);
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }

                    continue;
                }

                details.Add($"✓ {PackageDisplayName} package downloaded successfully.");
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to download {Name} from {Url}", PackageDisplayName, url);
            }
        }

        return false;
    }

    private (int RemovedCount, List<string> RemainingFiles) DeleteRecordedFiles(
        IEnumerable<string> filePaths,
        CancellationToken ct)
    {
        var removedCount = 0;
        var remainingFiles = new List<string>();

        foreach (var path in filePaths)
        {
            ct.ThrowIfCancellationRequested();
            var trimmed = path.Trim();
            if (string.IsNullOrEmpty(trimmed) || !Path.IsPathRooted(trimmed))
            {
                continue;
            }

            try
            {
                if (File.Exists(trimmed))
                {
                    File.Delete(trimmed);
                    removedCount++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.LogWarning(ex, "Failed to delete recorded file {FilePath} during undo", trimmed);
                remainingFiles.Add(trimmed);
            }
        }

        return (removedCount, remainingFiles);
    }

    private void UpdateMarkerAfterUndo(IReadOnlyList<string> remainingFiles)
    {
        if (remainingFiles.Count == 0)
        {
            try
            {
                if (File.Exists(_markerPath))
                {
                    File.Delete(_markerPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.LogWarning(ex, "Failed to delete marker file {MarkerPath} after undo", _markerPath);
            }

            return;
        }

        try
        {
            var markerDir = Path.GetDirectoryName(_markerPath);
            if (!string.IsNullOrEmpty(markerDir))
            {
                Directory.CreateDirectory(markerDir);
                var tempMarker = Path.Combine(markerDir, $"{Guid.NewGuid():N}.tmp");
                File.WriteAllLines(tempMarker, remainingFiles);
                File.Move(tempMarker, _markerPath, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning(ex, "Failed to rewrite marker file {MarkerPath} with remaining files", _markerPath);
        }
    }

    private void RollbackDeployment(
        List<(string DestPath, bool ExistedBefore, string? BackupPath)> backupEntries,
        List<string> details)
    {
        details.Add("Rolling back deployed assets...");
        var hasRollbackError = false;
        foreach (var (destPath, existedBefore, backupPath) in backupEntries)
        {
            try
            {
                if (existedBefore)
                {
                    if (!string.IsNullOrEmpty(backupPath) && File.Exists(backupPath))
                    {
                        File.Copy(backupPath, destPath, overwrite: true);
                    }
                    else
                    {
                        hasRollbackError = true;
                        Logger.LogWarning("Original backup missing for {DestPath} during rollback", destPath);
                    }
                }
                else if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                hasRollbackError = true;
                Logger.LogWarning(ex, "Failed to restore or remove file during rollback: {Path}", destPath);
            }
        }

        if (hasRollbackError)
        {
            details.Add("⚠ Rollback completed with some file warnings.");
        }
        else
        {
            details.Add("✓ Rollback completed.");
        }
    }

    private bool RecordDeploymentMarker(List<string> deployedFiles)
    {
        string? tempMarker = null;
        try
        {
            var markerDir = Path.GetDirectoryName(_markerPath);
            if (!string.IsNullOrEmpty(markerDir))
            {
                Directory.CreateDirectory(markerDir);
            }

            tempMarker = Path.Combine(markerDir ?? Path.GetTempPath(), $"{Guid.NewGuid():N}.tmp");
            File.WriteAllLines(tempMarker, deployedFiles);
            File.Move(tempMarker, _markerPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning(ex, "Failed to create marker file for {Name}", PackageDisplayName);
            DeleteFileSafely(tempMarker);
            return false;
        }
    }

    private void CleanupTempFiles(string tempFile, string tempExtractDir, string tempBackupDir)
    {
        DeleteFileSafely(tempFile);
        DeleteDirectorySafely(tempExtractDir);
        DeleteDirectorySafely(tempBackupDir);
    }
}
