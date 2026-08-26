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
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;

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
    /// <param name="BackupDir">The persistent directory for backing up pre-existing game files.</param>
    /// <param name="BackupEntries">The list tracking backup metadata for rollback and undo.</param>
    /// <param name="DeployedFiles">The list accumulating deployed file paths.</param>
    /// <param name="Details">The diagnostic details list.</param>
    public record DeploymentContext(
        string TempExtractDir,
        string BackupDir,
        List<(string DestPath, bool ExistedBefore, string? BackupPath)> BackupEntries,
        List<string> DeployedFiles,
        List<string> Details);

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
        var existingEntryIndex = context.BackupEntries.FindIndex(b => string.Equals(b.DestPath, destPath, StringComparison.OrdinalIgnoreCase));
        if (existingEntryIndex >= 0)
        {
            // Already backed up during this deployment batch; overwrite destination with new file without destroying original backup
            File.Copy(sourceFilePath, destPath, overwrite: true);
            return;
        }

        var existedBefore = File.Exists(destPath);
        string? backupPath = null;

        if (existedBefore)
        {
            Directory.CreateDirectory(context.BackupDir);
            backupPath = Path.Combine(context.BackupDir, $"{Guid.NewGuid():N}_{Path.GetFileName(destPath)}");
            File.Copy(destPath, backupPath, overwrite: true);
        }

        context.BackupEntries.Add((destPath, existedBefore, backupPath));

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

    /// <summary>
    /// Extracts all non-directory archive entries to the destination directory.
    /// </summary>
    /// <param name="archive">The archive to extract.</param>
    /// <param name="extractDir">The destination extraction directory.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A dictionary mapping file name to extracted file path.</returns>
    protected static async Task<Dictionary<string, string>> ExtractArchiveEntriesAsync(
        IArchive archive,
        string extractDir,
        CancellationToken ct)
    {
        var extractedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;

        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory && e.Key != null))
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(entry.Key);
            if (string.IsNullOrEmpty(fileName))
            {
                continue;
            }

            var extractedFilePath = Path.Combine(extractDir, fileName);
            await using (var entryStream = entry.OpenEntryStream())
            {
                expandedBytes += await BoundedArchiveExtractor.CopyEntryToFileAsync(
                    entryStream,
                    extractedFilePath,
                    fileName,
                    ActionSetConstants.Validation.MaximumAddonPackageSizeBytes,
                    ActionSetConstants.Validation.MaximumAddonPackageSizeBytes - expandedBytes,
                    overwrite: true,
                    cancellationToken: ct);
            }

            extractedFiles[fileName] = extractedFilePath;
        }

        return extractedFiles;
    }

    /// <summary>
    /// Gets the resolved marker path for a specific game installation.
    /// </summary>
    /// <param name="installation">The game installation.</param>
    /// <returns>The absolute marker file path.</returns>
    protected string GetMarkerPath(GameInstallation installation)
    {
        if (!string.IsNullOrEmpty(markerPath))
        {
            return markerPath;
        }

        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GenHub",
            ActionSetConstants.Paths.SubActionSetMarkers);

        var key = ComputeInstallationKey(installation);
        var scopedMarker = Path.Combine(baseDir, $"{Path.GetFileNameWithoutExtension(defaultMarkerFileName)}_{key}{Path.GetExtension(defaultMarkerFileName)}");

        // Backward compatibility: check legacy global marker if scoped marker is missing
        var globalMarker = Path.Combine(baseDir, defaultMarkerFileName);
        if (!File.Exists(scopedMarker) && File.Exists(globalMarker))
        {
            return globalMarker;
        }

        return scopedMarker;
    }

    /// <summary>
    /// Gets the persistent backup directory for saving overwritten files.
    /// </summary>
    /// <param name="installation">The game installation.</param>
    /// <returns>The backup directory path.</returns>
    protected string GetBackupDirectory(GameInstallation installation)
    {
        var key = ComputeInstallationKey(installation);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GenHub",
            "Backups",
            $"{Id}_{key}");
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var targetMarkerPath = GetMarkerPath(installation);
        var persistentBackupDir = GetBackupDirectory(installation);
        var tempFile = Path.Combine(Path.GetTempPath(), $"{TempFilePrefix}_{Guid.NewGuid():N}.dat");
        var tempExtractDir = Path.Combine(Path.GetTempPath(), $"{TempFilePrefix}_extract_{Guid.NewGuid():N}");
        var backupEntries = new List<(string DestPath, bool ExistedBefore, string? BackupPath)>();
        var deployedFiles = new List<string>();
        var details = new List<string>();
        var context = new DeploymentContext(tempExtractDir, persistentBackupDir, backupEntries, deployedFiles, details);

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
                RollbackDeployment(backupEntries, persistentBackupDir, details);
                return new ActionSetResult(false, $"Failed to extract and validate {PackageDisplayName} package.", details);
            }

            details.Add($"✓ Extracted and deployed {extractedCount} assets to game folders.");

            if (!RecordDeploymentMarker(targetMarkerPath, backupEntries))
            {
                details.Add("✗ Failed to record the deployment marker. Rolling back deployed files.");
                RollbackDeployment(backupEntries, persistentBackupDir, details);
                return new ActionSetResult(false, $"Failed to record the deployment marker for {Id}.", details);
            }

            return new ActionSetResult(true, null, details);
        }
        catch (OperationCanceledException)
        {
            RollbackDeployment(backupEntries, persistentBackupDir, details);
            throw;
        }
        catch (Exception ex)
        {
            RollbackDeployment(backupEntries, persistentBackupDir, details);
            Logger.LogError(ex, "Error applying {Name} fix", PackageDisplayName);
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
        finally
        {
            DeleteFileSafely(tempFile);
            DeleteDirectorySafely(tempExtractDir);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();
        var targetMarkerPath = GetMarkerPath(installation);
        var persistentBackupDir = GetBackupDirectory(installation);

        try
        {
            if (!File.Exists(targetMarkerPath))
            {
                if (AreAssetsPresent(installation))
                {
                    details.Add($"⚠ No deployment marker found. Custom {PackageDisplayName} files may have been installed manually; please remove them manually if desired.");
                    return Task.FromResult(new ActionSetResult(false, "No deployment marker found to undo.", details));
                }

                return Task.FromResult(new ActionSetResult(true, null, ["No deployment record found to undo."]));
            }

            var lines = ReadMarkerLinesSafely(targetMarkerPath);
            if (lines == null)
            {
                Logger.LogWarning("Failed to read installed file paths from marker {MarkerPath}", targetMarkerPath);
                return Task.FromResult(new ActionSetResult(false, "Failed to read deployment marker", ["✗ Could not read deployment marker."]));
            }

            if (lines.Length == 0)
            {
                DeleteFileSafely(targetMarkerPath);
                DeleteDirectorySafely(persistentBackupDir);
                return Task.FromResult(new ActionSetResult(true, null, ["No deployment record found to undo."]));
            }

            var records = ParseMarkerRecords(lines, installation);
            var (removedCount, restoredCount, restoredBackupPaths, remainingRecords) = RestoreOrDeleteRecordedFiles(records, ct);

            var markerUpdated = UpdateMarkerAfterUndo(targetMarkerPath, remainingRecords);
            if (!markerUpdated)
            {
                details.Add("✗ Failed to update deployment marker after undo. Backups have been retained.");
                return Task.FromResult(new ActionSetResult(false, "Failed to update deployment marker after undo.", details));
            }

            // Clean up restored backup files only after the marker update succeeded
            var remainingBackups = remainingRecords
                .Where(r => !string.IsNullOrEmpty(r.BackupPath))
                .Select(r => r.BackupPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var backupPath in restoredBackupPaths.Where(b => !remainingBackups.Contains(b)))
            {
                DeleteFileSafely(backupPath);
            }

            if (remainingRecords.Count == 0)
            {
                DeleteDirectorySafely(persistentBackupDir);
                var summary = restoredCount > 0
                    ? $"{PackageDisplayName} removed ({removedCount} files deleted, {restoredCount} originals restored)."
                    : $"{PackageDisplayName} removed ({removedCount} files deleted).";
                details.Add(summary);
                return Task.FromResult(new ActionSetResult(true, null, details));
            }

            details.Add($"⚠ Partial undo: {removedCount} files removed, {restoredCount} restored, {remainingRecords.Count} files could not be processed.");
            return Task.FromResult(new ActionSetResult(false, $"Failed to remove/restore {remainingRecords.Count} files during undo.", details));
        }
        catch (IOException ex)
        {
            Logger.LogWarning(ex, "I/O error deleting marker or restoring files for {Name}", PackageDisplayName);
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogWarning(ex, "Permission error deleting marker or restoring files for {Name}", PackageDisplayName);
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

    private static string ComputeInstallationKey(GameInstallation installation)
    {
        if (string.IsNullOrEmpty(installation.InstallationPath))
        {
            return "default";
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(installation.InstallationPath.ToUpperInvariant());
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..12].ToLowerInvariant();
    }

    private List<(string DestPath, string? BackupPath)> ParseMarkerRecords(string[] lines, GameInstallation installation)
    {
        var records = new List<(string DestPath, string? BackupPath)>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('|');
            var dest = parts[0].Trim();
            var backup = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim() : null;
            if (!string.IsNullOrEmpty(dest))
            {
                records.Add((dest, backup));
            }
        }

        var hasRootedPaths = records.Any(r => Path.IsPathRooted(r.DestPath));
        if (!hasRootedPaths)
        {
            var legacyPaths = GetLegacyFilePaths(installation);
            records = legacyPaths.Select(p => (p, (string?)null)).ToList();
        }

        return records;
    }

    private (int RemovedCount, int RestoredCount, List<string> RestoredBackupPaths, List<(string DestPath, string? BackupPath)> RemainingRecords) RestoreOrDeleteRecordedFiles(
        IEnumerable<(string DestPath, string? BackupPath)> records,
        CancellationToken ct)
    {
        var removedCount = 0;
        var restoredCount = 0;
        var restoredBackupPaths = new List<string>();
        var remainingRecords = new List<(string DestPath, string? BackupPath)>();

        foreach (var (destPath, backupPath) in records)
        {
            ct.ThrowIfCancellationRequested();
            var trimmedDest = destPath.Trim();
            if (string.IsNullOrEmpty(trimmedDest) || !Path.IsPathRooted(trimmedDest))
            {
                continue;
            }

            try
            {
                if (!string.IsNullOrEmpty(backupPath))
                {
                    if (TryRestoreBackup(trimmedDest, backupPath))
                    {
                        restoredBackupPaths.Add(backupPath);
                        restoredCount++;
                    }
                    else
                    {
                        remainingRecords.Add((trimmedDest, backupPath));
                    }
                }
                else if (File.Exists(trimmedDest))
                {
                    DeleteFileSafely(trimmedDest);
                    if (File.Exists(trimmedDest))
                    {
                        remainingRecords.Add((trimmedDest, backupPath));
                    }
                    else
                    {
                        removedCount++;
                    }
                }
            }
            catch (IOException ex)
            {
                Logger.LogWarning(ex, "Failed to restore or delete file {FilePath} during undo", trimmedDest);
                remainingRecords.Add((trimmedDest, backupPath));
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.LogWarning(ex, "Permission denied restoring or deleting file {FilePath} during undo", trimmedDest);
                remainingRecords.Add((trimmedDest, backupPath));
            }
        }

        return (removedCount, restoredCount, restoredBackupPaths, remainingRecords);
    }

    private bool TryRestoreBackup(string destPath, string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            Logger.LogWarning("Recorded backup missing for {FilePath} during undo; retaining destination to prevent data loss.", destPath);
            return false;
        }

        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        File.Copy(backupPath, destPath, overwrite: true);
        return true;
    }

    private bool UpdateMarkerAfterUndo(string targetMarkerPath, IReadOnlyList<(string DestPath, string? BackupPath)> remainingRecords)
    {
        if (remainingRecords.Count == 0)
        {
            DeleteFileSafely(targetMarkerPath);
            return !File.Exists(targetMarkerPath);
        }

        string? tempMarker = null;
        try
        {
            var markerDir = Path.GetDirectoryName(targetMarkerPath);
            if (!string.IsNullOrEmpty(markerDir))
            {
                Directory.CreateDirectory(markerDir);
            }

            tempMarker = Path.Combine(markerDir ?? Path.GetTempPath(), $"{Guid.NewGuid():N}.tmp");
            var lines = remainingRecords.Select(r => $"{r.DestPath}|{r.BackupPath ?? string.Empty}");
            File.WriteAllLines(tempMarker, lines);
            File.Move(tempMarker, targetMarkerPath, overwrite: true);
            return true;
        }
        catch (IOException ex)
        {
            Logger.LogWarning(ex, "Failed to rewrite marker file {MarkerPath} with remaining files", targetMarkerPath);
            DeleteFileSafely(tempMarker);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogWarning(ex, "Permission denied rewriting marker file {MarkerPath} with remaining files", targetMarkerPath);
            DeleteFileSafely(tempMarker);
            return false;
        }
    }

    private void RollbackDeployment(
        List<(string DestPath, bool ExistedBefore, string? BackupPath)> backupEntries,
        string backupDir,
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
                    DeleteFileSafely(destPath);
                    if (File.Exists(destPath))
                    {
                        hasRollbackError = true;
                    }
                }
            }
            catch (IOException ex)
            {
                hasRollbackError = true;
                Logger.LogWarning(ex, "Failed to restore or remove file during rollback: {Path}", destPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                hasRollbackError = true;
                Logger.LogWarning(ex, "Permission denied restoring or removing file during rollback: {Path}", destPath);
            }
        }

        if (!hasRollbackError)
        {
            DeleteDirectorySafely(backupDir);
            details.Add("✓ Rollback completed.");
        }
        else
        {
            details.Add("⚠ Rollback completed with some file warnings. Backups have been retained for recovery.");
        }
    }

    private bool RecordDeploymentMarker(
        string targetMarkerPath,
        List<(string DestPath, bool ExistedBefore, string? BackupPath)> backupEntries)
    {
        string? tempMarker = null;
        try
        {
            var markerDir = Path.GetDirectoryName(targetMarkerPath);
            if (!string.IsNullOrEmpty(markerDir))
            {
                Directory.CreateDirectory(markerDir);
            }

            tempMarker = Path.Combine(markerDir ?? Path.GetTempPath(), $"{Guid.NewGuid():N}.tmp");
            var lines = backupEntries.Select(b => $"{b.DestPath}|{b.BackupPath ?? string.Empty}");
            File.WriteAllLines(tempMarker, lines);
            File.Move(tempMarker, targetMarkerPath, overwrite: true);
            return true;
        }
        catch (IOException ex)
        {
            Logger.LogWarning(ex, "Failed to create marker file for {Name}", PackageDisplayName);
            DeleteFileSafely(tempMarker);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogWarning(ex, "Permission denied creating marker file for {Name}", PackageDisplayName);
            DeleteFileSafely(tempMarker);
            return false;
        }
    }
}
