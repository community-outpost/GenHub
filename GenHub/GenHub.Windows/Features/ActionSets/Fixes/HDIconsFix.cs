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
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;

/// <summary>
/// Fix that downloads and installs high-definition icons for Generals and Zero Hour.
/// Replaces legacy 32x32 Windows XP icons with 256x256 HD icon assets.
/// </summary>
public class HDIconsFix(IHttpClientFactory httpClientFactory, ILogger<HDIconsFix> logger, string? markerPath = null) : BaseActionSet(logger)
{
    private static readonly IReadOnlyList<string> RecognizedGeneralsIconFiles =
    [
        "GeneralsHD.ico",
        "generals_hd.ico",
        "game_hd.ico",
    ];

    private static readonly IReadOnlyList<string> RecognizedZeroHourIconFiles =
    [
        "GeneralsZHHD.ico",
        "zh_hd.ico",
    ];

    private readonly string _markerPath = markerPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", ActionSetConstants.Paths.SubActionSetMarkers, "HDIconsFix.done");

    /// <inheritdoc/>
    public override string Id => "HDIconsFix";

    /// <inheritdoc/>
    public override string Title => "HD Icons (Addon)";

    /// <inheritdoc/>
    public override string Description => "Installs high-definition 256x256 icon assets for game shortcuts (also managed in Downloads).";

    /// <inheritdoc/>
    public override string DetailedDescription => "Replaces low-resolution 32x32 icons with 256x256 icon files for desktop shortcuts and taskbar windows. This addon downloads icon.dat from Community Outpost and extracts HD icons directly into your game directories. You can also download and manage this addon from the Downloads section.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.QualityOfLife;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <summary>
    /// Validates that the downloaded HD icons archive contains the expected icon assets for targeted installations.
    /// </summary>
    /// <param name="archiveFileNames">The set of file names in the archive.</param>
    /// <param name="installation">The targeted game installation.</param>
    /// <returns>A validation result indicating validity and any issues found.</returns>
    internal static ValidationResult ValidateArchiveContents(
        IReadOnlySet<string> archiveFileNames,
        GameInstallation installation)
    {
        var issues = new List<ValidationIssue>();

        if (archiveFileNames.Count == 0)
        {
            issues.Add(new ValidationIssue { Message = "HD icons archive contains no valid files.", Severity = ValidationSeverity.Error });
            return new ValidationResult("HDIconsPackage", issues);
        }

        if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath) && !RecognizedGeneralsIconFiles.Any(f => archiveFileNames.Contains(f)))
        {
            issues.Add(new ValidationIssue { Message = "HD icons package does not contain a recognized icon for Generals.", Severity = ValidationSeverity.Error });
        }

        if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath) && !RecognizedZeroHourIconFiles.Any(f => archiveFileNames.Contains(f)))
        {
            issues.Add(new ValidationIssue { Message = "HD icons package does not contain a recognized icon for Zero Hour.", Severity = ValidationSeverity.Error });
        }

        return new ValidationResult("HDIconsPackage", issues);
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

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(AreHDIconsPresent(installation));
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"hd_icons_{Guid.NewGuid():N}.dat");
        var tempExtractDir = Path.Combine(Path.GetTempPath(), $"hd_icons_extract_{Guid.NewGuid():N}");
        var tempBackupDir = Path.Combine(Path.GetTempPath(), $"hd_icons_backup_{Guid.NewGuid():N}");
        var backupEntries = new List<(string DestPath, bool ExistedBefore, string? BackupPath)>();
        var deployedFiles = new List<string>();
        var details = new List<string>();

        try
        {
            details.Add("Downloading High-Definition Icons package...");

            using var client = httpClientFactory.CreateClient("Downloader");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            var urls = new[] { ExternalUrls.HDIconsDownloadUrlPrimary };
            bool downloaded = false;

            foreach (var url in urls)
            {
                try
                {
                    logger.LogInformation("Attempting HD icons download from {Url}", url);
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

                    details.Add("✓ High-Definition Icons package downloaded successfully.");
                    downloaded = true;
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation("Download canceled by user");
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to download HD icons from {Url}", url);
                }
            }

            if (!downloaded)
            {
                return new ActionSetResult(false, "Failed to download High-Definition Icons from available source.", details);
            }

            var validation = await DownloadSecurityValidator.ValidateFileAsync(
                tempFile,
                allowedSha256Hashes: [ActionSetConstants.Security.HDIconsSha256],
                ct: cancellationToken);

            if (!validation.Success)
            {
                var errorSummary = string.Join("; ", validation.Errors);
                logger.LogWarning("Security validation failed for HD icons package: {Error}", errorSummary);
                return new ActionSetResult(false, $"Package failed security verification: {errorSummary}", details);
            }

            details.Add("✓ Package integrity verified via SHA-256 checksum.");
            details.Add("Extracting high-definition icon assets...");
            Directory.CreateDirectory(tempExtractDir);

            using var archive = ArchiveFactory.OpenArchive(new FileInfo(tempFile));
            var archiveFileNames = archive.Entries
                .Where(e => !e.IsDirectory && e.Key != null)
                .Select(e => Path.GetFileName(e.Key))
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var archiveValidation = ValidateArchiveContents(archiveFileNames, installation);
            if (!archiveValidation.IsValid)
            {
                var errorMessage = archiveValidation.FirstError ?? "HD icons package validation failed.";
                logger.LogWarning("{Error}", errorMessage);
                return new ActionSetResult(false, errorMessage, details);
            }

            int extractedCount = 0;

            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory && e.Key != null))
            {
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

                // Deploy to Generals installation directory if available and recognized for Generals
                if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath) &&
                    RecognizedGeneralsIconFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    var generalsDest = Path.Combine(installation.GeneralsPath, fileName);
                    DeployFileWithBackup(extractedFilePath, generalsDest, tempBackupDir, deployedFiles, backupEntries);
                }

                // Deploy to Zero Hour installation directory if available and recognized for Zero Hour
                if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath) &&
                    RecognizedZeroHourIconFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    var zhDest = Path.Combine(installation.ZeroHourPath, fileName);
                    DeployFileWithBackup(extractedFilePath, zhDest, tempBackupDir, deployedFiles, backupEntries);
                }
            }

            details.Add($"✓ Extracted and deployed {extractedCount} HD icon assets to game folders.");

            if (!RecordDeploymentMarker(deployedFiles))
            {
                details.Add("✗ Failed to record the deployment marker. Rolling back deployed files.");
                RollbackDeployment(backupEntries, details);
                return new ActionSetResult(false, "Failed to record the deployment marker for HDIconsFix.", details);
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
            logger.LogError(ex, "Error applying HD icons fix");
            details.Add($"✗ Error: {ex.Message}");
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
                if (AreHDIconsPresent(installation))
                {
                    details.Add("⚠ No deployment marker found. Custom HD icon files may have been installed manually; please remove them manually if desired.");
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
                logger.LogWarning(ex, "Failed to read installed icon file paths from marker {MarkerPath}", _markerPath);
                return Task.FromResult(new ActionSetResult(false, $"Failed to read deployment marker: {ex.Message}", ["✗ Could not read deployment marker."]));
            }

            // Check if this is a legacy timestamp-only marker (no rooted paths)
            var hasRootedPaths = lines.Any(l => !string.IsNullOrWhiteSpace(l) && Path.IsPathRooted(l.Trim()));
            if (!hasRootedPaths && lines.Length > 0)
            {
                var legacyFiles = new List<string>();
                if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
                {
                    foreach (var icon in RecognizedGeneralsIconFiles)
                    {
                        var path = Path.Combine(installation.GeneralsPath, icon);
                        if (File.Exists(path) && !legacyFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
                        {
                            legacyFiles.Add(path);
                        }
                    }
                }

                if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
                {
                    foreach (var icon in RecognizedZeroHourIconFiles)
                    {
                        var path = Path.Combine(installation.ZeroHourPath, icon);
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
                    logger.LogWarning(ex, "Failed to delete recorded icon file {FilePath} during undo", trimmed);
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

                details.Add($"HD icons removed ({removedCount} files deleted).");
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
                return Task.FromResult(new ActionSetResult(false, $"Failed to remove {remainingFiles.Count} icon files during undo.", details));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to delete marker or icon files for HDIconsFix");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
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
            logger.LogWarning(ex, "Failed to create marker file for HDIconsFix");
            CleanupTempFile(tempMarker);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied creating marker file for HDIconsFix");
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

    private bool AreHDIconsPresent(GameInstallation installation)
    {
        try
        {
            var hasAnyTarget = false;

            if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
            {
                hasAnyTarget = true;
                if (!RecognizedGeneralsIconFiles.Any(iconFile => File.Exists(Path.Combine(installation.GeneralsPath, iconFile))))
                {
                    return false;
                }
            }

            if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
            {
                hasAnyTarget = true;
                if (!RecognizedZeroHourIconFiles.Any(iconFile => File.Exists(Path.Combine(installation.ZeroHourPath, iconFile))))
                {
                    return false;
                }
            }

            return hasAnyTarget;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking for HD icons");
            return false;
        }
    }
}
