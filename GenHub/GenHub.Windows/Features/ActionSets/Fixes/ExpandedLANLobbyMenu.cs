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
using SharpCompress.Archives;

/// <summary>
/// Downloads and installs custom widescreen window definitions and the expanded LAN lobby menu addon.
/// </summary>
public class ExpandedLANLobbyMenu(IHttpClientFactory httpClientFactory, ILogger<ExpandedLANLobbyMenu> logger, string? markerPath = null) : BaseActionSet(logger)
{
    private static readonly IReadOnlyList<string> KnownMenuBigFiles =
    [
        "400_ControlBarHDBaseZH.big",
        "400_ControlBarHDBaseCCG.big",
        "!ExpandedLANMenu.big",
        "CustomWindows.big",
    ];

    private readonly string _markerPath = markerPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", ActionSetConstants.Paths.SubActionSetMarkers, "ExpandedLANLobbyMenu.done");

    /// <inheritdoc/>
    public override string Id => "ExpandedLANLobbyMenu";

    /// <inheritdoc/>
    public override string Title => "Expanded LAN Lobby Menu (Addon)";

    /// <inheritdoc/>
    public override string Description => "Downloads and installs custom widescreen UI definitions and the expanded LAN lobby menu addon.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Replaces the legacy 4-row LAN lobby interface and cramped window definitions with a widescreen-adapted layout. This addon downloads the official widescreen window assets and installs them into your game folder. You can also download and manage this addon from the Downloads section.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.QualityOfLife;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(_markerPath))
            {
                return Task.FromResult(true);
            }

            if (installation.HasZeroHour &&
                !string.IsNullOrEmpty(installation.ZeroHourPath) &&
                KnownMenuBigFiles.Any(f => File.Exists(Path.Combine(installation.ZeroHourPath, f))))
            {
                return Task.FromResult(true);
            }

            if (installation.HasGenerals &&
                !string.IsNullOrEmpty(installation.GeneralsPath) &&
                KnownMenuBigFiles.Any(f => File.Exists(Path.Combine(installation.GeneralsPath, f))))
            {
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Error checking LAN lobby menu status");
            return Task.FromResult(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Permission error checking LAN lobby menu status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();
        var tempFile = Path.Combine(Path.GetTempPath(), $"cbbs_{Guid.NewGuid():N}.dat");
        var tempExtractDir = Path.Combine(Path.GetTempPath(), $"cbbs_extract_{Guid.NewGuid():N}");
        var tempBackupDir = Path.Combine(Path.GetTempPath(), $"cbbs_backup_{Guid.NewGuid():N}");
        var backupEntries = new List<(string DestPath, bool ExistedBefore, string? BackupPath)>();

        try
        {
            details.Add("Downloading Expanded LAN Lobby & Custom Windows package...");

            var downloaded = await DownloadPackageAsync(tempFile, details, cancellationToken);
            if (!downloaded)
            {
                return new ActionSetResult(false, "Failed to download Expanded LAN Lobby assets from all available mirrors.", details);
            }

            var validation = await DownloadSecurityValidator.ValidateFileAsync(
                tempFile,
                allowedSha256Hashes: [ActionSetConstants.Security.ExpandedLANLobbySha256],
                ct: cancellationToken);

            if (!validation.Success)
            {
                var errorSummary = string.Join("; ", validation.Errors);
                logger.LogWarning("Security validation failed for Expanded LAN Lobby package: {Error}", errorSummary);
                return new ActionSetResult(false, $"Package failed security verification: {errorSummary}", details);
            }

            details.Add("✓ Package integrity verified via SHA-256 checksum.");
            details.Add("Extracting widescreen window and LAN lobby definitions...");

            var (extractedCount, deployedFiles) = await ExtractAndDeployAssetsAsync(
                tempFile,
                tempExtractDir,
                tempBackupDir,
                installation,
                backupEntries,
                cancellationToken);

            details.Add($"✓ Extracted and deployed {extractedCount} widescreen window assets to game folders.");

            if (!RecordDeploymentMarker(deployedFiles))
            {
                details.Add("✗ Failed to record the deployment marker. Rolling back deployed files.");
                RollbackDeployment(backupEntries, details);
                return new ActionSetResult(false, "Failed to record the deployment marker for ExpandedLANLobbyMenu.", details);
            }

            return new ActionSetResult(true, null, details);
        }
        catch (OperationCanceledException)
        {
            RollbackDeployment(backupEntries, details);
            throw;
        }
        catch (HttpRequestException ex)
        {
            RollbackDeployment(backupEntries, details);
            logger.LogError(ex, "Network error downloading LAN lobby menu fix");
            details.Add($"✗ Network error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
        catch (IOException ex)
        {
            RollbackDeployment(backupEntries, details);
            logger.LogError(ex, "Disk I/O error applying LAN lobby menu fix");
            details.Add($"✗ Disk error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
        catch (UnauthorizedAccessException ex)
        {
            RollbackDeployment(backupEntries, details);
            logger.LogError(ex, "Permission error applying LAN lobby menu fix");
            details.Add($"✗ Access denied: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
        catch (InvalidOperationException ex)
        {
            RollbackDeployment(backupEntries, details);
            logger.LogError(ex, "Archive extraction error applying LAN lobby menu fix");
            details.Add($"✗ Archive error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
        finally
        {
            CleanupTempFiles(tempFile, tempExtractDir, tempBackupDir);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var removedCount = 0;
        var details = new List<string>();

        try
        {
            if (!File.Exists(_markerPath))
            {
                var filesPresent = false;
                if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
                {
                    filesPresent = KnownMenuBigFiles.Any(f => File.Exists(Path.Combine(installation.ZeroHourPath, f)));
                }

                if (!filesPresent && installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
                {
                    filesPresent = KnownMenuBigFiles.Any(f => File.Exists(Path.Combine(installation.GeneralsPath, f)));
                }

                if (filesPresent)
                {
                    details.Add("⚠ No deployment marker found. Custom window files may have been installed manually; please remove them manually if desired.");
                    return Task.FromResult(new ActionSetResult(false, "No deployment marker found to undo.", details));
                }

                return Task.FromResult(new ActionSetResult(true, null, ["No deployment record found to undo."]));
            }

            var remainingFiles = new List<string>();
            string[] lines;
            try
            {
                lines = File.ReadAllLines(_markerPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Failed to read installed file paths from marker {MarkerPath}", _markerPath);
                return Task.FromResult(new ActionSetResult(false, $"Failed to read deployment marker: {ex.Message}", ["✗ Could not read deployment marker."]));
            }

            // Check if this is a legacy timestamp-only marker (no rooted paths)
            var hasRootedPaths = lines.Any(l => !string.IsNullOrWhiteSpace(l) && Path.IsPathRooted(l.Trim()));
            if (!hasRootedPaths && lines.Length > 0)
            {
                var legacyFiles = new List<string>();
                if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
                {
                    foreach (var file in KnownMenuBigFiles)
                    {
                        var path = Path.Combine(installation.ZeroHourPath, file);
                        if (File.Exists(path) && !legacyFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
                        {
                            legacyFiles.Add(path);
                        }
                    }
                }

                if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
                {
                    foreach (var file in KnownMenuBigFiles)
                    {
                        var path = Path.Combine(installation.GeneralsPath, file);
                        if (File.Exists(path) && !legacyFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
                        {
                            legacyFiles.Add(path);
                        }
                    }
                }

                lines = [.. legacyFiles];
            }

            foreach (var path in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    logger.LogWarning(ex, "Failed to delete recorded custom window file {FilePath} during undo", trimmed);
                    remainingFiles.Add(trimmed);
                }
            }

            if (remainingFiles.Count == 0)
            {
                try
                {
                    File.Delete(_markerPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Failed to delete marker file {MarkerPath} after undo", _markerPath);
                }

                details.Add($"Removed {removedCount} custom window and expanded LAN lobby files.");
                return Task.FromResult(new ActionSetResult(true, null, details));
            }
            else
            {
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
                    logger.LogWarning(ex, "Failed to rewrite marker file {MarkerPath} with remaining files", _markerPath);
                }

                details.Add($"⚠ Partial undo: removed {removedCount} files, {remainingFiles.Count} files could not be deleted.");
                return Task.FromResult(new ActionSetResult(false, $"Failed to remove {remainingFiles.Count} custom window files during undo.", details));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to delete marker or custom window files for ExpandedLANLobbyMenu");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    private static void DeployEntryToInstallations(
        GameInstallation installation,
        string fileName,
        string sourceFilePath,
        string tempBackupDir,
        List<string> deployedFiles,
        List<(string DestPath, bool ExistedBefore, string? BackupPath)> backupEntries)
    {
        if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
        {
            var zhDest = Path.Combine(installation.ZeroHourPath, fileName);
            DeployFileWithBackup(sourceFilePath, zhDest, tempBackupDir, deployedFiles, backupEntries);
        }

        if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath) &&
            !string.Equals(installation.GeneralsPath, installation.ZeroHourPath, StringComparison.OrdinalIgnoreCase))
        {
            var generalsDest = Path.Combine(installation.GeneralsPath, fileName);
            DeployFileWithBackup(sourceFilePath, generalsDest, tempBackupDir, deployedFiles, backupEntries);
        }
    }

    private static void DeployFileWithBackup(
        string sourceFilePath,
        string destPath,
        string tempBackupDir,
        List<string> deployedFiles,
        List<(string DestPath, bool ExistedBefore, string? BackupPath)> backupEntries)
    {
        var alreadyBackedUp = backupEntries.Any(b => string.Equals(b.DestPath, destPath, StringComparison.OrdinalIgnoreCase));
        var existedBefore = File.Exists(destPath);
        string? backupPath = null;

        if (existedBefore && !alreadyBackedUp)
        {
            Directory.CreateDirectory(tempBackupDir);
            backupPath = Path.Combine(tempBackupDir, $"{Guid.NewGuid():N}_{Path.GetFileName(destPath)}");
            File.Copy(destPath, backupPath, overwrite: true);
            backupEntries.Add((destPath, existedBefore, backupPath));
        }
        else if (!alreadyBackedUp)
        {
            backupEntries.Add((destPath, existedBefore, null));
        }

        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        File.Copy(sourceFilePath, destPath, overwrite: true);
        if (!deployedFiles.Contains(destPath, StringComparer.OrdinalIgnoreCase))
        {
            deployedFiles.Add(destPath);
        }
    }

    private async Task<bool> DownloadPackageAsync(string tempFile, List<string> details, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient("Downloader");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        var urls = new[] { ExternalUrls.ExpandedLANLobbyDownloadUrlPrimary, ExternalUrls.ExpandedLANLobbyDownloadUrlMirror1 };

        foreach (var url in urls)
        {
            try
            {
                logger.LogInformation("Attempting Custom Windows / Expanded LAN download from {Url}", url);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await response.Content.CopyToAsync(fs, cancellationToken);
                }

                var fileInfo = new FileInfo(tempFile);
                if (fileInfo.Length < ActionSetConstants.Validation.MinimumAddonPackageSizeBytes)
                {
                    logger.LogWarning("Downloaded file from {Url} is too small ({Size} bytes).", url, fileInfo.Length);
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }

                    continue;
                }

                details.Add($"✓ Downloaded {fileInfo.Length / 1024.0:F2} KB package from {new Uri(url).Host}");
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to download Custom Windows from {Url}", url);
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        return false;
    }

    private async Task<(int ExtractedCount, List<string> DeployedFiles)> ExtractAndDeployAssetsAsync(
        string tempFile,
        string tempExtractDir,
        string tempBackupDir,
        GameInstallation installation,
        List<(string DestPath, bool ExistedBefore, string? BackupPath)> backupEntries,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(tempExtractDir);
        using var archive = ArchiveFactory.OpenArchive(new FileInfo(tempFile));
        var extractedCount = 0;
        var deployedFiles = new List<string>();

        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory && e.Key != null))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(entry.Key);
            if (string.IsNullOrEmpty(fileName))
            {
                continue;
            }

            var extractedFilePath = Path.Combine(tempExtractDir, fileName);
            using (var entryStream = entry.OpenEntryStream())
            await using (var fs = new FileStream(extractedFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await entryStream.CopyToAsync(fs, cancellationToken);
            }

            extractedCount++;
            DeployEntryToInstallations(installation, fileName, extractedFilePath, tempBackupDir, deployedFiles, backupEntries);
        }

        return (extractedCount, deployedFiles);
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
                        logger.LogWarning("Original backup missing for {DestPath} during rollback", destPath);
                    }
                }
                else if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                }
            }
            catch (IOException ex)
            {
                hasRollbackError = true;
                logger.LogWarning(ex, "Failed to restore or remove file during rollback: {Path}", destPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                hasRollbackError = true;
                logger.LogWarning(ex, "Access denied during rollback of file: {Path}", destPath);
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
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to create marker file for ExpandedLANLobbyMenu");
            CleanupTempFile(tempMarker);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Permission denied creating marker file for ExpandedLANLobbyMenu");
            CleanupTempFile(tempMarker);
            return false;
        }
    }

    private void CleanupTempFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Failed to clean up temporary file {TempPath}", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Access denied cleaning up temporary file {TempPath}", path);
        }
    }

    private void CleanupTempFiles(string tempFile, string tempExtractDir, string tempBackupDir)
    {
        try
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Failed to delete temp file {TempFile}", tempFile);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Access denied deleting temp file {TempFile}", tempFile);
        }

        try
        {
            if (Directory.Exists(tempExtractDir))
            {
                Directory.Delete(tempExtractDir, recursive: true);
            }
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Failed to delete temp directory {TempDir}", tempExtractDir);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Access denied deleting temp directory {TempDir}", tempExtractDir);
        }

        try
        {
            if (Directory.Exists(tempBackupDir))
            {
                Directory.Delete(tempBackupDir, recursive: true);
            }
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Failed to delete temp backup directory {TempDir}", tempBackupDir);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Access denied deleting temp backup directory {TempDir}", tempBackupDir);
        }
    }
}
