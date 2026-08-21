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

        try
        {
            if (File.Exists(_markerPath))
            {
                try
                {
                    var lines = File.ReadAllLines(_markerPath);
                    foreach (var path in lines)
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                            removedCount++;
                        }
                    }
                }
                catch (IOException ex)
                {
                    logger.LogWarning(ex, "Failed to read installed file paths from marker {MarkerPath}", _markerPath);
                }

                File.Delete(_markerPath);
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to remove custom window files during undo");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied removing custom window files during undo");
        }

        return Task.FromResult(new ActionSetResult(true, null, [$"Removed {removedCount} custom window and expanded LAN lobby files."]));
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

        if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
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
        var existedBefore = File.Exists(destPath);
        string? backupPath = null;

        if (existedBefore)
        {
            Directory.CreateDirectory(tempBackupDir);
            backupPath = Path.Combine(tempBackupDir, $"{Guid.NewGuid():N}_{Path.GetFileName(destPath)}");
            File.Copy(destPath, backupPath, overwrite: true);
        }

        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        backupEntries.Add((destPath, existedBefore, backupPath));
        File.Copy(sourceFilePath, destPath, overwrite: true);
        deployedFiles.Add(destPath);
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
        foreach (var (destPath, existedBefore, backupPath) in backupEntries)
        {
            try
            {
                if (existedBefore && !string.IsNullOrEmpty(backupPath) && File.Exists(backupPath))
                {
                    File.Copy(backupPath, destPath, overwrite: true);
                }
                else if (!existedBefore && File.Exists(destPath))
                {
                    File.Delete(destPath);
                }
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Failed to restore or remove file during rollback: {Path}", destPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, "Access denied during rollback of file: {Path}", destPath);
            }
        }

        details.Add("✓ Rollback completed.");
    }

    private bool RecordDeploymentMarker(List<string> deployedFiles)
    {
        try
        {
            var markerDir = Path.GetDirectoryName(_markerPath);
            if (!string.IsNullOrEmpty(markerDir))
            {
                Directory.CreateDirectory(markerDir);
            }

            File.WriteAllLines(_markerPath, deployedFiles);
            return true;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to create marker file for ExpandedLANLobbyMenu");
            CleanupPartialMarker();
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Permission denied creating marker file for ExpandedLANLobbyMenu");
            CleanupPartialMarker();
            return false;
        }
    }

    private void CleanupPartialMarker()
    {
        try
        {
            if (File.Exists(_markerPath))
            {
                File.Delete(_markerPath);
            }
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Failed to clean up partial marker file {MarkerPath}", _markerPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Access denied cleaning up partial marker file {MarkerPath}", _markerPath);
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
