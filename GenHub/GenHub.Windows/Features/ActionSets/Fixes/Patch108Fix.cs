namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
/// Installs the Generals 1.08 official patch.
/// </summary>
public class Patch108Fix(IHttpClientFactory httpClientFactory, ILogger<Patch108Fix> logger) : BaseActionSet(logger)
{
    private const string BackupDirectoryName = "_GenHub_Patch108_Backups";

    /// <inheritdoc/>
    public override string Id => "Patch108";

    /// <inheritdoc/>
    public override string Title => "Generals 1.08 Patch (Game Client)";

    /// <inheritdoc/>
    public override string Description => "Official game client patch updating Generals to version 1.08 (also managed in Downloads).";

    /// <inheritdoc/>
    public override string DetailedDescription => "Generals 1.08 is the official game client patch fixing multiplayer desyncs, campaign crashes, and engine bugs. This patch updates your base Generals game files. You can also download and manage this game patch from the Downloads section.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.CoreAndStability;

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(installation.HasGenerals);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            var gameExePath = Path.Combine(installation.GeneralsPath, ActionSetConstants.FileNames.GeneralsExe);
            if (!File.Exists(gameExePath))
            {
                return Task.FromResult(false);
            }

            var versionInfo = FileVersionInfo.GetVersionInfo(gameExePath);
            var version = versionInfo.FileVersion;

            if (version?.StartsWith("1.8") == true)
            {
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check Generals patch version");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();
        var tempPath = Path.Combine(Path.GetTempPath(), $"gn108_patch_{Guid.NewGuid():N}.zip");
        var extractPath = Path.Combine(Path.GetTempPath(), $"gn108_extract_{Guid.NewGuid():N}");
        string? currentBackupDir = null;
        var copiedFiles = new List<(string DestPath, bool ExistedBefore)>();

        try
        {
            details.Add("Starting Generals 1.08 patch installation...");
            details.Add($"Target directory: {installation.GeneralsPath}");

            var downloadResult = await DownloadAndValidatePatchAsync(tempPath, details, ct);
            if (!downloadResult.Success)
            {
                return downloadResult;
            }

            details.Add("Extracting patch files...");
            Directory.CreateDirectory(extractPath);
            await Task.Run(() => ZipFile.ExtractToDirectory(tempPath, extractPath), ct);

            var extractedFiles = Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories);
            details.Add($"✓ Extracted {extractedFiles.Length} files");

            var backupBase = Path.Combine(installation.GeneralsPath, BackupDirectoryName);
            currentBackupDir = Path.Combine(backupBase, $"Backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(currentBackupDir);
            details.Add($"Created backup directory: {currentBackupDir}");

            details.Add($"Installing to: {installation.GeneralsPath}");
            var copiedCount = DeployExtractedFiles(
                extractedFiles,
                extractPath,
                installation.GeneralsPath,
                currentBackupDir,
                copiedFiles,
                ct);

            details.Add($"✓ Installed {copiedCount} files with backup");
            details.Add("✓ Generals 1.08 patch installed successfully");

            logger.LogInformation("Generals 1.08 patch installed successfully with {Count} actions", details.Count);
            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to install Generals 1.08 patch. Rolling back modifications.");
            details.Add($"✗ Error: {ex.Message}");
            RollbackFiles(currentBackupDir, Path.GetFullPath(installation.GeneralsPath), copiedFiles, details);
            return new ActionSetResult(false, ex.Message, details);
        }
        finally
        {
            DeleteFileSafely(tempPath);
            DeleteDirectorySafely(extractPath);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();
        try
        {
            var backupBase = Path.Combine(installation.GeneralsPath, BackupDirectoryName);
            if (!Directory.Exists(backupBase))
            {
                return Task.FromResult(new ActionSetResult(true, null, ["No backups found to restore."]));
            }

            var backupDirs = Directory.GetDirectories(backupBase, "Backup_*")
                .OrderByDescending(d => d)
                .ToList();

            if (backupDirs.Count == 0)
            {
                return Task.FromResult(new ActionSetResult(true, null, ["No backups found to restore."]));
            }

            var latestBackup = backupDirs[0];
            details.Add($"Restoring files from latest backup: {Path.GetFileName(latestBackup)}");

            var backupFiles = Directory.GetFiles(latestBackup, "*.*", SearchOption.AllDirectories);
            int restoredCount = 0;
            foreach (var file in backupFiles)
            {
                ct.ThrowIfCancellationRequested();
                var relativePath = file[latestBackup.Length..].TrimStart(Path.DirectorySeparatorChar);
                var destPath = Path.Combine(installation.GeneralsPath, relativePath);
                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(file, destPath, true);
                restoredCount++;
            }

            details.Add($"✓ Restored {restoredCount} files from backup");
            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to undo Generals 1.08 patch");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    private async Task<ActionSetResult> DownloadAndValidatePatchAsync(
        string tempPath,
        List<string> details,
        CancellationToken ct)
    {
        details.Add($"Download URL: {ExternalUrls.Generals108PatchUrl}");
        details.Add("Downloading patch archive...");
        logger.LogInformation("Downloading Generals 1.08 patch from {Url}", ExternalUrls.Generals108PatchUrl);

        using var client = httpClientFactory.CreateClient("Downloader");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        using var response = await client.GetAsync(ExternalUrls.Generals108PatchUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            await response.Content.CopyToAsync(fs, ct);
        }

        var fileInfo = new FileInfo(tempPath);
        var fileSize = fileInfo.Length;
        if (fileSize < ActionSetConstants.Validation.PatchMinSize)
        {
            logger.LogWarning("Downloaded Generals 1.08 patch file too small ({Size} bytes), likely corrupt.", fileSize);
            DeleteFileSafely(tempPath);
            return new ActionSetResult(false, "Downloaded Generals 1.08 patch is corrupted or incomplete.", details);
        }

        var securityValidation = await DownloadSecurityValidator.ValidateAndLockFileAsync(
            tempPath,
            allowedSha256Hashes: [ActionSetConstants.Security.Generals108PatchSha256],
            ct: ct);

        if (!securityValidation.Success || securityValidation.Data == null)
        {
            var errorSummary = string.Join("; ", securityValidation.Errors);
            logger.LogWarning("Security validation failed for Generals 1.08 patch archive: {Error}", errorSummary);
            DeleteFileSafely(tempPath);
            return new ActionSetResult(false, $"Security validation failed for Generals 1.08 patch: {errorSummary}", details);
        }

        await securityValidation.Data.DisposeAsync();

        try
        {
            await using var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            if (archive.Entries.Count == 0)
            {
                DeleteFileSafely(tempPath);
                return new ActionSetResult(false, "Downloaded Generals 1.08 patch archive contains no files.", details);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Downloaded Generals 1.08 patch archive is corrupted");
            DeleteFileSafely(tempPath);
            return new ActionSetResult(false, $"Downloaded Generals 1.08 patch archive is corrupted: {ex.Message}", details);
        }

        details.Add($"✓ Downloaded and verified SHA-256 ({fileSize / 1024.0 / 1024.0:F2} MB)");
        return new ActionSetResult(true, null, details);
    }

    private int DeployExtractedFiles(
        string[] extractedFiles,
        string extractPath,
        string targetGamePath,
        string currentBackupDir,
        List<(string DestPath, bool ExistedBefore)> copiedFiles,
        CancellationToken ct)
    {
        int copiedCount = 0;
        var canonicalGamePath = Path.GetFullPath(targetGamePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var file in extractedFiles)
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = file[extractPath.Length..].TrimStart(Path.DirectorySeparatorChar);
            var destPath = Path.GetFullPath(Path.Combine(targetGamePath, relativePath));

            if (!destPath.StartsWith(canonicalGamePath, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Potential path traversal detected in patch archive: {Path}", relativePath);
                continue;
            }

            var existedBefore = File.Exists(destPath);
            if (existedBefore)
            {
                var backupFilePath = Path.Combine(currentBackupDir, relativePath);
                var backupFileDir = Path.GetDirectoryName(backupFilePath);
                if (!string.IsNullOrEmpty(backupFileDir) && !Directory.Exists(backupFileDir))
                {
                    Directory.CreateDirectory(backupFileDir);
                }

                File.Copy(destPath, backupFilePath, true);
            }

            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(file, destPath, true);
            copiedFiles.Add((destPath, existedBefore));
            logger.LogDebug("Copied {File}", relativePath);
            copiedCount++;
        }

        return copiedCount;
    }

    private void RollbackFiles(
        string? backupDir,
        string canonicalGamePath,
        List<(string DestPath, bool ExistedBefore)> copiedFiles,
        List<string> details)
    {
        try
        {
            details.Add("Rolling back patch changes...");
            foreach (var (destPath, existedBefore) in copiedFiles)
            {
                if (existedBefore && !string.IsNullOrEmpty(backupDir))
                {
                    var relativePath = destPath[canonicalGamePath.Length..].TrimStart(Path.DirectorySeparatorChar);
                    var backupPath = Path.Combine(backupDir, relativePath);
                    if (File.Exists(backupPath))
                    {
                        File.Copy(backupPath, destPath, true);
                    }
                }
                else if (!existedBefore && File.Exists(destPath))
                {
                    File.Delete(destPath);
                }
            }

            details.Add("✓ Rollback completed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed during rollback of patch files");
            details.Add($"✗ Rollback warning: {ex.Message}");
        }
    }
}
