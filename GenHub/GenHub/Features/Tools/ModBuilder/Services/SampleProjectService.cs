using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Messages;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Core.Utilities;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Service responsible for managing, discovering, and acquiring sample project assets on-demand.
/// </summary>
[SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Sample project download URLs")]
public class SampleProjectService(
    IDownloadService downloadService,
    CompressedImageToTgaConverter imageConverter,
    ILogger<SampleProjectService> logger,
    IStringTableConversionService? stringTableConverter = null,
    INotificationService? notificationService = null) : ISampleProjectService
{
    private static readonly string[] SampleProjectNames =
    [
        "GeneralsGamePatch2",
        "ImprovedMenus",
        "LemonControlBar",
        "LeikezeHotkeys",
        "Hotkeys",
        "CustomIcons",
    ];

    private const string GeneralsGamePatch2Name = "GeneralsGamePatch2";
    private const string ImprovedMenusName = "ImprovedMenus";
    private const string LemonControlBarName = "LemonControlBar";
    private const string LeikezeHotkeysName = "LeikezeHotkeys";
    private const string HotkeysName = "Hotkeys";
    private const string CustomIconsName = "CustomIcons";

    private const string UnknownError = "Unknown error";
    private const string StagingCleanupFailedMessage = "Failed to clean up temporary staging directory {Dir}";

    /// <inheritdoc />
    public bool IsSampleProject(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return false;
        }

        var normalized = projectPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Replace('\\', '/');
        var dirName = Path.GetFileName(Path.GetDirectoryName(normalized) ?? string.Empty);
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(normalized);

        return SampleProjectNames.Contains(fileNameWithoutExt, StringComparer.OrdinalIgnoreCase) ||
               SampleProjectNames.Contains(dirName, StringComparer.OrdinalIgnoreCase) ||
               normalized.Contains("/Samples/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains($"/{ModBuilderConstants.SampleProjectsDirectoryName}/", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool HasSampleAssets(string projectDir)
    {
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
        {
            return false;
        }

        var gameFilesDir = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir);
        if (!Directory.Exists(gameFilesDir))
        {
            return false;
        }

        return Directory.EnumerateFileSystemEntries(gameFilesDir, "*", SearchOption.AllDirectories)
            .Any(file =>
            {
                var name = Path.GetFileName(file);
                return !name.Equals("README.md", StringComparison.OrdinalIgnoreCase) &&
                       !name.Equals("README.txt", StringComparison.OrdinalIgnoreCase) &&
                       !name.Equals(".gitkeep", StringComparison.OrdinalIgnoreCase) &&
                       !name.StartsWith(".git", StringComparison.OrdinalIgnoreCase);
            });
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> EnsureSampleAssetsAsync(
        string projectDir,
        string projectName,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectDir))
        {
            return OperationResult<bool>.CreateFailure("Project directory path cannot be empty.");
        }

        if (HasSampleAssets(projectDir))
        {
            logger.LogDebug("Sample assets already present in {ProjectDir}", projectDir);
            return OperationResult<bool>.CreateSuccess(true);
        }

        var gameFilesDir = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir);
        Directory.CreateDirectory(gameFilesDir);

        var cacheDir = GetSampleCacheDirectory();
        Directory.CreateDirectory(cacheDir);

        var canonicalName = ResolveCanonicalProjectName(projectName, projectDir);

        OperationResult<bool> result;
        try
        {
            switch (canonicalName)
            {
                case GeneralsGamePatch2Name:
                    result = await AcquireGeneralsGamePatch2AssetsAsync(gameFilesDir, cacheDir, progress, cancellationToken).ConfigureAwait(false);
                    break;

                case ImprovedMenusName:
                    result = await AcquireImprovedMenusAssetsAsync(gameFilesDir, cacheDir, progress, cancellationToken).ConfigureAwait(false);
                    break;

                case LemonControlBarName:
                    result = await AcquireLemonControlBarAssetsAsync(gameFilesDir, cacheDir, progress, cancellationToken).ConfigureAwait(false);
                    break;

                case LeikezeHotkeysName:
                    result = await AcquireLeikezeHotkeysAssetsAsync(gameFilesDir, cacheDir, progress, cancellationToken).ConfigureAwait(false);
                    break;

                case HotkeysName:
                case CustomIconsName:
                    result = await AcquireHotkeysAssetsAsync(gameFilesDir, cacheDir, progress, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    logger.LogWarning("Unrecognized sample project name: {ProjectName}", projectName);
                    result = OperationResult<bool>.CreateFailure($"Unknown sample project: {projectName}");
                    break;
            }

            if (!result.Success)
            {
                CleanupDirectorySafely(gameFilesDir);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            CleanupDirectorySafely(gameFilesDir);
            throw;
        }
        catch (Exception ex)
        {
            CleanupDirectorySafely(gameFilesDir);
            logger.LogError(ex, "Failed to download and extract sample assets for {ProjectName}", projectName);
            return OperationResult<bool>.CreateFailure($"Failed to acquire sample assets for {projectName}: {ex.Message}");
        }
    }

    private static string ResolveCanonicalProjectName(string projectName, string projectDir)
    {
        if (projectName.Contains(GeneralsGamePatch2Name, StringComparison.OrdinalIgnoreCase) ||
            projectDir.Contains(GeneralsGamePatch2Name, StringComparison.OrdinalIgnoreCase))
        {
            return GeneralsGamePatch2Name;
        }

        if (projectName.Contains(ImprovedMenusName, StringComparison.OrdinalIgnoreCase) ||
            projectDir.Contains(ImprovedMenusName, StringComparison.OrdinalIgnoreCase))
        {
            return ImprovedMenusName;
        }

        if (projectName.Contains(LemonControlBarName, StringComparison.OrdinalIgnoreCase) ||
            projectDir.Contains(LemonControlBarName, StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains("ControlBar", StringComparison.OrdinalIgnoreCase) ||
            projectDir.Contains("ControlBar", StringComparison.OrdinalIgnoreCase))
        {
            return LemonControlBarName;
        }

        if (projectName.Contains(LeikezeHotkeysName, StringComparison.OrdinalIgnoreCase) ||
            projectDir.Contains(LeikezeHotkeysName, StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains("Leikeze", StringComparison.OrdinalIgnoreCase) ||
            projectDir.Contains("Leikeze", StringComparison.OrdinalIgnoreCase))
        {
            return LeikezeHotkeysName;
        }

        if (projectName.Contains(HotkeysName, StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(CustomIconsName, StringComparison.OrdinalIgnoreCase) ||
            projectDir.Contains(HotkeysName, StringComparison.OrdinalIgnoreCase) ||
            projectDir.Contains(CustomIconsName, StringComparison.OrdinalIgnoreCase))
        {
            return HotkeysName;
        }

        return projectName;
    }

    private static string GetSampleCacheDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, AppConstants.AppName, ModBuilderConstants.SampleCacheDirName);
    }

    private static async Task ExtractArchiveFileAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(archivePath);
        using var archive = ArchiveFactory.OpenArchive(fileInfo);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory)
            {
                continue;
            }

            if (!ArchiveEntryName.IsExtractable(entry.Key))
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.Key));
            if (!PathHelper.IsPathWithinDirectory(destinationDirectory, targetPath))
            {
                continue;
            }

            var entryDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(entryDir))
            {
                Directory.CreateDirectory(entryDir);
            }

            await using var entryStream = entry.OpenEntryStream();
            await BoundedArchiveExtractor.CopyEntryToFileAsync(
                entryStream,
                targetPath,
                entry.Key,
                1024L * 1024 * 1024,
                2048L * 1024 * 1024,
                overwrite: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private void CleanupDirectorySafely(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to clean up directory {Dir} after failed sample acquisition", dir);
        }
    }

    private static void CopyDirectoryContents(string sourceDir, string targetDir, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetFilePath = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, targetFilePath, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetSubDirPath = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectoryContents(subDir, targetSubDirPath, cancellationToken);
        }
    }

    private static async Task<bool> VerifyFileSha256Async(string filePath, string expectedSha256, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        var actualHex = Convert.ToHexString(hash);
        return string.Equals(actualHex, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<OperationResult<bool>> EnsureAssetDownloadedAsync(
        string url,
        string cachePath,
        long minLength,
        string assetLabel,
        CancellationToken cancellationToken,
        string? expectedSha256 = null)
    {
        if (File.Exists(cachePath) && new FileInfo(cachePath).Length >= minLength)
        {
            if (string.IsNullOrEmpty(expectedSha256) || await VerifyFileSha256Async(cachePath, expectedSha256, cancellationToken).ConfigureAwait(false))
            {
                return OperationResult<bool>.CreateSuccess(true);
            }

            logger.LogWarning("Cached asset {Path} failed SHA-256 integrity verification, re-downloading...", cachePath);
            try
            {
                File.Delete(cachePath);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to delete corrupt cached asset {Path}", cachePath);
            }
        }

        var providerName = url.Contains("legi.cc", StringComparison.OrdinalIgnoreCase) ? "Community Outpost" : "GitHub";
        var contentKey = $"sample::{assetLabel}::{Path.GetFileName(cachePath)}";
        var contentId = $"sample.{assetLabel.Replace(" ", string.Empty).ToLowerInvariant()}";

        // Broadcast start & trigger notification
        WeakReferenceMessenger.Default.Send(new ContentDownloadStartedMessage(
            contentKey,
            contentId,
            providerName,
            assetLabel));
        notificationService?.ShowInfo("Download Started", $"Downloading {assetLabel} from {providerName}...");

        var tempPath = $"{cachePath}.tmp_{Guid.NewGuid():N}";
        try
        {
            var downloadProgress = new Progress<DownloadProgress>(dp =>
            {
                WeakReferenceMessenger.Default.Send(new ContentDownloadProgressMessage(
                    contentKey,
                    contentId,
                    providerName,
                    assetLabel,
                    dp.Percentage,
                    $"Downloading {assetLabel}..."));
            });

            var downloadResult = await downloadService.DownloadFileAsync(
                new Uri(url),
                tempPath,
                expectedHash: null,
                progress: downloadProgress,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!downloadResult.Success)
            {
                var error = downloadResult.FirstError ?? UnknownError;
                WeakReferenceMessenger.Default.Send(new ContentDownloadCompletedMessage(
                    contentKey, contentId, providerName, assetLabel, false, error));
                notificationService?.ShowError("Download Failed", $"Failed to download {assetLabel}: {error}");
                return OperationResult<bool>.CreateFailure($"Failed to download {assetLabel}: {error}");
            }

            if (!File.Exists(tempPath) || new FileInfo(tempPath).Length < minLength)
            {
                var error = "Downloaded file was smaller than expected.";
                WeakReferenceMessenger.Default.Send(new ContentDownloadCompletedMessage(
                    contentKey, contentId, providerName, assetLabel, false, error));
                notificationService?.ShowError("Download Failed", $"{assetLabel}: {error}");
                return OperationResult<bool>.CreateFailure($"Downloaded file for {assetLabel} was smaller than expected.");
            }

            if (!string.IsNullOrEmpty(expectedSha256))
            {
                var isShaValid = await VerifyFileSha256Async(tempPath, expectedSha256, cancellationToken).ConfigureAwait(false);
                if (!isShaValid)
                {
                    var error = "File failed SHA-256 integrity verification.";
                    WeakReferenceMessenger.Default.Send(new ContentDownloadCompletedMessage(
                        contentKey, contentId, providerName, assetLabel, false, error));
                    notificationService?.ShowError("Download Verification Failed", $"{assetLabel}: {error}");
                    return OperationResult<bool>.CreateFailure($"Downloaded file for {assetLabel} failed SHA-256 integrity verification.");
                }
            }

            File.Move(tempPath, cachePath, overwrite: true);

            WeakReferenceMessenger.Default.Send(new ContentDownloadCompletedMessage(
                contentKey, contentId, providerName, assetLabel, true));
            notificationService?.ShowSuccess("Download Complete", $"Downloaded {assetLabel}");
        }
        catch (OperationCanceledException)
        {
            WeakReferenceMessenger.Default.Send(new ContentDownloadCompletedMessage(
                contentKey, contentId, providerName, assetLabel, false, "Cancelled"));
            throw;
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new ContentDownloadCompletedMessage(
                contentKey, contentId, providerName, assetLabel, false, ex.Message));
            notificationService?.ShowError("Download Failed", $"Failed to download {assetLabel}: {ex.Message}");
            throw;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to clean up temporary download file {Path}", tempPath);
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<OperationResult<bool>> AcquireGeneralsGamePatch2AssetsAsync(
        string gameFilesDir,
        string cacheDir,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Downloading GeneralsGamePatch2 patch archive...");
        logger.LogInformation("Downloading GeneralsGamePatch2 asset from {Url}", ModBuilderConstants.SampleProjects.GeneralsGamePatch2Url);

        var zipCachePath = Path.Combine(cacheDir, "500_900_CommunityPatch_CoreINI.zip");
        var downloadResult = await EnsureAssetDownloadedAsync(
            ModBuilderConstants.SampleProjects.GeneralsGamePatch2Url,
            zipCachePath,
            100_000,
            "GeneralsGamePatch2 Core INI",
            cancellationToken).ConfigureAwait(false);

        if (!downloadResult.Success)
        {
            return downloadResult;
        }

        progress?.Report("Extracting GeneralsGamePatch2 Core INI archive...");
        var tempStaging = Path.Combine(Path.GetTempPath(), $"genhub_ggp2_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempStaging);
            await ExtractArchiveFileAsync(zipCachePath, tempStaging, cancellationToken).ConfigureAwait(false);

            var bigFiles = Directory.GetFiles(tempStaging, "*.big", SearchOption.AllDirectories);
            if (bigFiles.Length == 0)
            {
                return OperationResult<bool>.CreateFailure("GeneralsGamePatch2 archive did not contain expected .big file.");
            }

            var primaryBig = bigFiles[0];
            if (!await VerifyFileSha256Async(primaryBig, ModBuilderConstants.SampleProjects.GeneralsGamePatch2Sha256, cancellationToken).ConfigureAwait(false))
            {
                return OperationResult<bool>.CreateFailure("GeneralsGamePatch2 .big file failed SHA-256 integrity verification.");
            }

            progress?.Report("Unpacking game files into project...");
            var unpackStaging = Path.Combine(tempStaging, "unpacked");
            Directory.CreateDirectory(unpackStaging);
            var unpackResult = await BigFilePacker.UnpackAsync(primaryBig, unpackStaging, overwrite: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!unpackResult.Success)
            {
                return OperationResult<bool>.CreateFailure($"Failed to unpack GeneralsGamePatch2 .big file: {unpackResult.FirstError}");
            }

            CopyDirectoryContents(unpackStaging, gameFilesDir, cancellationToken);
            await TryExtractAndSaveManifestAsync(primaryBig, gameFilesDir, cancellationToken).ConfigureAwait(false);

            // Copy release .big archive into .Release/ so users have prebuilt distribution files
            var projectDir = Path.GetDirectoryName(gameFilesDir);
            if (!string.IsNullOrEmpty(projectDir))
            {
                var releaseDir = Path.Combine(projectDir, ModBuilderConstants.DefaultReleaseDir);
                Directory.CreateDirectory(releaseDir);
                var destBig = Path.Combine(releaseDir, Path.GetFileName(primaryBig));
                File.Copy(primaryBig, destBig, overwrite: true);
            }

            logger.LogInformation("Successfully unpacked GeneralsGamePatch2 game files into {Dir}", gameFilesDir);
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempStaging))
                {
                    Directory.Delete(tempStaging, recursive: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, StagingCleanupFailedMessage, tempStaging);
            }
        }
    }

    private async Task<OperationResult<bool>> AcquireImprovedMenusAssetsAsync(
        string gameFilesDir,
        string cacheDir,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Downloading Improved Menus widescreen releases (EN, RU, ES)...");
        logger.LogInformation("Downloading Improved Menus English asset from {Url}", ModBuilderConstants.SampleProjects.ImprovedMenusEnglishUrl);

        var projectDir = Path.GetDirectoryName(gameFilesDir);
        var releaseDir = string.IsNullOrEmpty(projectDir) ? null : Path.Combine(projectDir, ModBuilderConstants.DefaultReleaseDir);
        if (!string.IsNullOrEmpty(releaseDir))
        {
            Directory.CreateDirectory(releaseDir);
        }

        // 1. English (Primary variant)
        var enZipPath = Path.Combine(cacheDir, "0_ImprovedMenusEnglish.zip");
        var enResult = await EnsureAssetDownloadedAsync(
            ModBuilderConstants.SampleProjects.ImprovedMenusEnglishUrl,
            enZipPath,
            1_000_000,
            "Improved Menus English",
            cancellationToken).ConfigureAwait(false);

        if (!enResult.Success)
        {
            return enResult;
        }

        progress?.Report("Extracting Improved Menus English package...");
        var enStaging = Path.Combine(Path.GetTempPath(), $"genhub_menus_en_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(enStaging);
            await ExtractArchiveFileAsync(enZipPath, enStaging, cancellationToken).ConfigureAwait(false);

            var bigFiles = Directory.GetFiles(enStaging, "*.big", SearchOption.AllDirectories);
            if (bigFiles.Length == 0)
            {
                return OperationResult<bool>.CreateFailure("Improved Menus English archive did not contain expected .big file.");
            }

            var primaryBig = bigFiles[0];
            await VerifyFileSha256Async(primaryBig, ModBuilderConstants.SampleProjects.ImprovedMenusSha256, cancellationToken).ConfigureAwait(false);

            progress?.Report("Unpacking English menu windows and textures...");
            var unpackStaging = Path.Combine(enStaging, "unpacked");
            Directory.CreateDirectory(unpackStaging);
            var unpackResult = await BigFilePacker.UnpackAsync(primaryBig, unpackStaging, overwrite: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!unpackResult.Success)
            {
                return OperationResult<bool>.CreateFailure($"Failed to unpack Improved Menus .big file: {unpackResult.FirstError}");
            }

            CopyDirectoryContents(unpackStaging, gameFilesDir, cancellationToken);
            await TryExtractAndSaveManifestAsync(primaryBig, gameFilesDir, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(releaseDir))
            {
                File.Copy(primaryBig, Path.Combine(releaseDir, Path.GetFileName(primaryBig)), overwrite: true);
            }

            // Copy movie backgrounds if provided in release zip
            var bikFiles = Directory.GetFiles(enStaging, "*.bik", SearchOption.AllDirectories);
            foreach (var bik in bikFiles)
            {
                var movieDestDir = Path.Combine(gameFilesDir, "Data", "Movies");
                Directory.CreateDirectory(movieDestDir);
                var destPath = Path.Combine(movieDestDir, Path.GetFileName(bik));
                File.Copy(bik, destPath, overwrite: true);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(enStaging))
                {
                    Directory.Delete(enStaging, recursive: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, StagingCleanupFailedMessage, enStaging);
            }
        }

        // 2. Russian variant
        try
        {
            progress?.Report("Downloading Improved Menus Russian variant...");
            var ruZipPath = Path.Combine(cacheDir, "0_ImprovedMenusRussian.zip");
            var ruResult = await EnsureAssetDownloadedAsync(
                ModBuilderConstants.SampleProjects.ImprovedMenusRussianUrl,
                ruZipPath,
                1_000_000,
                "Improved Menus Russian",
                cancellationToken).ConfigureAwait(false);

            if (ruResult.Success)
            {
                var ruStaging = Path.Combine(Path.GetTempPath(), $"genhub_menus_ru_{Guid.NewGuid():N}");
                try
                {
                    Directory.CreateDirectory(ruStaging);
                    await ExtractArchiveFileAsync(ruZipPath, ruStaging, cancellationToken).ConfigureAwait(false);
                    var ruBigs = Directory.GetFiles(ruStaging, "*.big", SearchOption.AllDirectories);
                    if (ruBigs.Length > 0)
                    {
                        var ruBig = ruBigs[0];
                        var ruUnpack = Path.Combine(ruStaging, "unpacked");
                        Directory.CreateDirectory(ruUnpack);
                        await BigFilePacker.UnpackAsync(ruBig, ruUnpack, overwrite: true, cancellationToken: cancellationToken).ConfigureAwait(false);

                        // Copy Russian textures specifically
                        var ruTexDir = Path.Combine(ruUnpack, "Data", "Russian");
                        if (Directory.Exists(ruTexDir))
                        {
                            var targetRu = Path.Combine(gameFilesDir, "Data", "Russian");
                            CopyDirectoryContents(ruTexDir, targetRu, cancellationToken);
                        }

                        if (!string.IsNullOrEmpty(releaseDir))
                        {
                            File.Copy(ruBig, Path.Combine(releaseDir, Path.GetFileName(ruBig)), overwrite: true);
                        }

                        await TryExtractAndSaveManifestAsync(ruBig, gameFilesDir, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(ruStaging))
                        {
                            Directory.Delete(ruStaging, recursive: true);
                        }
                    }
                    catch
                    {
                        // best-effort cleanup
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to acquire secondary Russian variant for Improved Menus");
        }

        // 3. Spanish variant
        try
        {
            progress?.Report("Downloading Improved Menus Spanish variant...");
            var esZipPath = Path.Combine(cacheDir, "0_ImprovedMenusSpanish.zip");
            var esResult = await EnsureAssetDownloadedAsync(
                ModBuilderConstants.SampleProjects.ImprovedMenusSpanishUrl,
                esZipPath,
                1_000_000,
                "Improved Menus Spanish",
                cancellationToken).ConfigureAwait(false);

            if (esResult.Success)
            {
                var esStaging = Path.Combine(Path.GetTempPath(), $"genhub_menus_es_{Guid.NewGuid():N}");
                try
                {
                    Directory.CreateDirectory(esStaging);
                    await ExtractArchiveFileAsync(esZipPath, esStaging, cancellationToken).ConfigureAwait(false);
                    var esBigs = Directory.GetFiles(esStaging, "*.big", SearchOption.AllDirectories);
                    if (esBigs.Length > 0)
                    {
                        var esBig = esBigs[0];
                        var esUnpack = Path.Combine(esStaging, "unpacked");
                        Directory.CreateDirectory(esUnpack);
                        await BigFilePacker.UnpackAsync(esBig, esUnpack, overwrite: true, cancellationToken: cancellationToken).ConfigureAwait(false);

                        // Copy Spanish textures specifically
                        var esTexDir = Path.Combine(esUnpack, "Data", "Spanish");
                        if (Directory.Exists(esTexDir))
                        {
                            var targetEs = Path.Combine(gameFilesDir, "Data", "Spanish");
                            CopyDirectoryContents(esTexDir, targetEs, cancellationToken);
                        }

                        if (!string.IsNullOrEmpty(releaseDir))
                        {
                            File.Copy(esBig, Path.Combine(releaseDir, Path.GetFileName(esBig)), overwrite: true);
                        }

                        await TryExtractAndSaveManifestAsync(esBig, gameFilesDir, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(esStaging))
                        {
                            Directory.Delete(esStaging, recursive: true);
                        }
                    }
                    catch
                    {
                        // best-effort cleanup
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to acquire secondary Spanish variant for Improved Menus");
        }

        logger.LogInformation("Successfully unpacked Improved Menus variants into {Dir}", gameFilesDir);
        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task TryExtractAndSaveManifestAsync(
        string bigFilePath,
        string gameFilesDir,
        CancellationToken cancellationToken)
    {
        try
        {
            var manifest = BigFilePacker.ExtractManifest(bigFilePath);
            manifest.BigFileName = Path.GetFileName(bigFilePath);
            using (var fs = File.OpenRead(bigFilePath))
            {
                using var sha = SHA256.Create();
                var hash = await sha.ComputeHashAsync(fs, cancellationToken).ConfigureAwait(false);
                manifest.Sha256 = Convert.ToHexString(hash).ToLowerInvariant();
            }

            var projectDir = Path.GetDirectoryName(gameFilesDir);
            if (!string.IsNullOrEmpty(projectDir))
            {
                var configDir = Path.Combine(projectDir, ModBuilderConstants.LowercaseConfigDir);
                Directory.CreateDirectory(configDir);
                var manifestPath = Path.Combine(configDir, $"{Path.GetFileName(bigFilePath)}.manifest.json");
                await BigFilePacker.SaveManifestAsync(manifest, manifestPath, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract or save archive manifest for {File}", bigFilePath);
        }
    }

    private async Task<OperationResult<bool>> AcquireLemonControlBarAssetsAsync(
        string gameFilesDir,
        string cacheDir,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Downloading Lemon Control Bar resolutions...");
        var projectDir = Path.GetDirectoryName(gameFilesDir);
        var releaseDir = string.IsNullOrEmpty(projectDir) ? null : Path.Combine(projectDir, ModBuilderConstants.DefaultReleaseDir);
        if (!string.IsNullOrEmpty(releaseDir))
        {
            Directory.CreateDirectory(releaseDir);
        }

        var resolutions = new[]
        {
            (Resolution: "1080p", Url: ModBuilderConstants.SampleProjects.LemonControlBar1080pUrl, FileName: "ControlBarProLemonEditionZH_v1.3_1920x1080.zip", BigName: "340_ControlBarProLemonEdition1080ZH.big", IsPrimary: true),
            (Resolution: "720p", Url: ModBuilderConstants.SampleProjects.LemonControlBar720pUrl, FileName: "ControlBarProLemonEditionZH_v1.3_1280x720.zip", BigName: "340_ControlBarProLemonEdition720ZH.big", IsPrimary: false),
            (Resolution: "1440p", Url: ModBuilderConstants.SampleProjects.LemonControlBar1440pUrl, FileName: "ControlBarProLemonEditionZH_v1.3_2560x1440.zip", BigName: "340_ControlBarProLemonEdition1440ZH.big", IsPrimary: false),
            (Resolution: "4K", Url: ModBuilderConstants.SampleProjects.LemonControlBar4KUrl, FileName: "ControlBarProLemonEditionZH_v1.3_3840x2160.zip", BigName: "340_ControlBarProLemonEdition4KZH.big", IsPrimary: false),
        };

        var primarySucceeded = false;
        foreach (var res in resolutions)
        {
            progress?.Report($"Downloading Lemon Control Bar ({res.Resolution})...");
            var zipPath = Path.Combine(cacheDir, res.FileName);
            var dlResult = await EnsureAssetDownloadedAsync(
                res.Url,
                zipPath,
                500_000,
                $"Lemon Control Bar {res.Resolution}",
                cancellationToken).ConfigureAwait(false);

            if (!dlResult.Success)
            {
                if (res.IsPrimary)
                {
                    return dlResult;
                }

                logger.LogWarning("Skipping Lemon Control Bar {Res} download: {Error}", res.Resolution, dlResult.FirstError);
                continue;
            }

            var staging = Path.Combine(Path.GetTempPath(), $"genhub_lemon_{res.Resolution}_{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(staging);
                await ExtractArchiveFileAsync(zipPath, staging, cancellationToken).ConfigureAwait(false);

                var bigFiles = Directory.GetFiles(staging, "*.big", SearchOption.AllDirectories);
                var targetBig = bigFiles.FirstOrDefault(b => Path.GetFileName(b).Equals(res.BigName, StringComparison.OrdinalIgnoreCase)) ?? bigFiles.FirstOrDefault();

                if (targetBig != null && File.Exists(targetBig))
                {
                    var unpackDir = Path.Combine(staging, "unpacked");
                    Directory.CreateDirectory(unpackDir);
                    var unpackRes = await BigFilePacker.UnpackAsync(targetBig, unpackDir, overwrite: true, cancellationToken: cancellationToken).ConfigureAwait(false);

                    if (unpackRes.Success)
                    {
                        // If primary (1080p), copy common Art/ folder to GameFilesEdited/Art
                        var artDir = Path.Combine(unpackDir, "Art");
                        if (Directory.Exists(artDir))
                        {
                            var targetArtDir = Path.Combine(gameFilesDir, "Art");
                            CopyDirectoryContents(artDir, targetArtDir, cancellationToken);
                        }

                        // Copy window files to resolution-specific subdirectory: GameFilesEdited/Window/{Resolution}
                        var wndDir = Path.Combine(unpackDir, "Window");
                        if (Directory.Exists(wndDir))
                        {
                            var targetWndDir = Path.Combine(gameFilesDir, "Window", res.Resolution);
                            CopyDirectoryContents(wndDir, targetWndDir, cancellationToken);
                        }

                        // Copy release .big
                        if (!string.IsNullOrEmpty(releaseDir))
                        {
                            File.Copy(targetBig, Path.Combine(releaseDir, Path.GetFileName(targetBig)), overwrite: true);
                        }

                        await TryExtractAndSaveManifestAsync(targetBig, gameFilesDir, cancellationToken).ConfigureAwait(false);

                        if (res.IsPrimary)
                        {
                            primarySucceeded = true;
                        }
                    }
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(staging))
                    {
                        Directory.Delete(staging, recursive: true);
                    }
                }
                catch
                {
                    // best-effort cleanup
                }
            }
        }

        if (!primarySucceeded)
        {
            return OperationResult<bool>.CreateFailure("Failed to unpack primary Lemon Control Bar assets.");
        }

        logger.LogInformation("Successfully unpacked Lemon Control Bar resolutions into {Dir}", gameFilesDir);
        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<OperationResult<bool>> AcquireLeikezeHotkeysAssetsAsync(
        string gameFilesDir,
        string cacheDir,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Downloading Leikeze Hotkeys archive...");
        logger.LogInformation("Downloading Leikeze Hotkeys asset from {Url}", ModBuilderConstants.SampleProjects.LeikezeHotkeysUrl);

        var datCachePath = Path.Combine(cacheDir, "hlei.dat");
        var downloadResult = await EnsureAssetDownloadedAsync(
            ModBuilderConstants.SampleProjects.LeikezeHotkeysUrl,
            datCachePath,
            500_000,
            "Leikeze Hotkeys",
            cancellationToken,
            ModBuilderConstants.SampleProjects.LeikezeHotkeysSha256).ConfigureAwait(false);

        if (!downloadResult.Success)
        {
            return downloadResult;
        }

        progress?.Report("Extracting Leikeze Hotkeys string tables...");
        var tempStaging = Path.Combine(Path.GetTempPath(), $"genhub_leikeze_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempStaging);
            await ExtractArchiveFileAsync(datCachePath, tempStaging, cancellationToken).ConfigureAwait(false);

            var projectDir = Path.GetDirectoryName(gameFilesDir);
            var releaseDir = string.IsNullOrEmpty(projectDir) ? null : Path.Combine(projectDir, ModBuilderConstants.DefaultReleaseDir);
            if (!string.IsNullOrEmpty(releaseDir))
            {
                Directory.CreateDirectory(releaseDir);
            }

            var csfFiles = Directory.GetFiles(tempStaging, "*.csf", SearchOption.AllDirectories);

            // 1. Zero Hour English
            var zhEnCsf = csfFiles.FirstOrDefault(f => f.Contains("ZH", StringComparison.OrdinalIgnoreCase) && (f.Contains("EN", StringComparison.OrdinalIgnoreCase) || f.Contains("English", StringComparison.OrdinalIgnoreCase)))
                ?? csfFiles.FirstOrDefault(f => f.Contains("ZH", StringComparison.OrdinalIgnoreCase))
                ?? csfFiles.FirstOrDefault();

            if (zhEnCsf != null && File.Exists(zhEnCsf))
            {
                var zhEnDir = Path.Combine(gameFilesDir, "ZeroHour", "English", "Data", "English");
                Directory.CreateDirectory(zhEnDir);
                var destCsf = Path.Combine(zhEnDir, "generals.csf");
                File.Copy(zhEnCsf, destCsf, overwrite: true);

                if (!string.IsNullOrEmpty(releaseDir))
                {
                    var zhEnPackDir = Path.Combine(tempStaging, "pack_zhen");
                    var zhEnInner = Path.Combine(zhEnPackDir, "Data", "English");
                    Directory.CreateDirectory(zhEnInner);
                    File.Copy(destCsf, Path.Combine(zhEnInner, "generals.csf"), overwrite: true);

                    var outBig = Path.Combine(releaseDir, "!HotkeysLeikezeENZH.big");
                    await BigFilePacker.PackAsync(zhEnPackDir, outBig, cancellationToken: cancellationToken).ConfigureAwait(false);
                    await TryExtractAndSaveManifestAsync(outBig, gameFilesDir, cancellationToken).ConfigureAwait(false);
                }
            }

            // 2. Generals English
            var genEnCsf = csfFiles.FirstOrDefault(f => (f.Contains("Generals", StringComparison.OrdinalIgnoreCase) || f.Contains("Gen", StringComparison.OrdinalIgnoreCase)) && !f.Contains("ZH", StringComparison.OrdinalIgnoreCase) && (f.Contains("EN", StringComparison.OrdinalIgnoreCase) || f.Contains("English", StringComparison.OrdinalIgnoreCase)));
            if (genEnCsf != null && File.Exists(genEnCsf))
            {
                var genEnDir = Path.Combine(gameFilesDir, "Generals", "English", "Data", "English");
                Directory.CreateDirectory(genEnDir);
                var destCsf = Path.Combine(genEnDir, "generals.csf");
                File.Copy(genEnCsf, destCsf, overwrite: true);

                if (!string.IsNullOrEmpty(releaseDir))
                {
                    var genPackDir = Path.Combine(tempStaging, "pack_genen");
                    var genInner = Path.Combine(genPackDir, "Data", "English");
                    Directory.CreateDirectory(genInner);
                    File.Copy(destCsf, Path.Combine(genInner, "generals.csf"), overwrite: true);

                    var outBig = Path.Combine(releaseDir, "!HotkeysLeikezeEN.big");
                    await BigFilePacker.PackAsync(genPackDir, outBig, cancellationToken: cancellationToken).ConfigureAwait(false);
                    await TryExtractAndSaveManifestAsync(outBig, gameFilesDir, cancellationToken).ConfigureAwait(false);
                }
            }

            // 3. Zero Hour German
            var deCsf = csfFiles.FirstOrDefault(f => f.Contains("DE", StringComparison.OrdinalIgnoreCase) || f.Contains("German", StringComparison.OrdinalIgnoreCase));
            if (deCsf != null && File.Exists(deCsf))
            {
                var zhDeDir = Path.Combine(gameFilesDir, "ZeroHour", "German", "Data", "German");
                Directory.CreateDirectory(zhDeDir);
                var destCsf = Path.Combine(zhDeDir, "generals.csf");
                File.Copy(deCsf, destCsf, overwrite: true);

                if (!string.IsNullOrEmpty(releaseDir))
                {
                    var dePackDir = Path.Combine(tempStaging, "pack_zhde");
                    var deInner = Path.Combine(dePackDir, "Data", "German");
                    Directory.CreateDirectory(deInner);
                    File.Copy(destCsf, Path.Combine(deInner, "generals.csf"), overwrite: true);

                    var outBig = Path.Combine(releaseDir, "!HotkeysLeikezeDEZH.big");
                    await BigFilePacker.PackAsync(dePackDir, outBig, cancellationToken: cancellationToken).ConfigureAwait(false);
                    await TryExtractAndSaveManifestAsync(outBig, gameFilesDir, cancellationToken).ConfigureAwait(false);
                }
            }

            logger.LogInformation("Successfully unpacked Leikeze Hotkeys into {Dir}", gameFilesDir);
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempStaging))
                {
                    Directory.Delete(tempStaging, recursive: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, StagingCleanupFailedMessage, tempStaging);
            }
        }
    }

    private async Task<OperationResult<bool>> AcquireHotkeysAssetsAsync(
        string gameFilesDir,
        string cacheDir,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Downloading Legionnaire Hotkeys and Indicators...");
        logger.LogInformation("Downloading Hotkeys assets from Community Outpost ({HlegUrl}, {HlenUrl})", ModBuilderConstants.SampleProjects.HotkeysHlegUrl, ModBuilderConstants.SampleProjects.HotkeysHlenUrl);

        var hlegCachePath = Path.Combine(cacheDir, "hleg.dat");
        var hlegResult = await EnsureAssetDownloadedAsync(
            ModBuilderConstants.SampleProjects.HotkeysHlegUrl,
            hlegCachePath,
            10_000,
            "Hotkeys hleg",
            cancellationToken).ConfigureAwait(false);

        if (!hlegResult.Success)
        {
            return hlegResult;
        }

        var hlenCachePath = Path.Combine(cacheDir, "hlen.dat");
        var hlenResult = await EnsureAssetDownloadedAsync(
            ModBuilderConstants.SampleProjects.HotkeysHlenUrl,
            hlenCachePath,
            1_000_000,
            "Hotkeys hlen",
            cancellationToken).ConfigureAwait(false);

        if (!hlenResult.Success)
        {
            return hlenResult;
        }

        progress?.Report("Extracting hotkey overlay textures & definitions...");
        var tempHlenStaging = Path.Combine(Path.GetTempPath(), $"genhub_hlen_{Guid.NewGuid():N}");
        var tempHlegStaging = Path.Combine(Path.GetTempPath(), $"genhub_hleg_{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempHlenStaging);
            Directory.CreateDirectory(tempHlegStaging);

            await ExtractArchiveFileAsync(hlenCachePath, tempHlenStaging, cancellationToken).ConfigureAwait(false);
            await ExtractArchiveFileAsync(hlegCachePath, tempHlegStaging, cancellationToken).ConfigureAwait(false);

            progress?.Report("Converting indicator textures to TGA format...");
            await imageConverter.ConvertDirectoryAsync(tempHlenStaging, cancellationToken).ConfigureAwait(false);

            // Copy Zero Hour indicators from ZH/BIG (or fallback to root)
            var zhBigDir = Path.Combine(tempHlenStaging, "ZH", "BIG");
            var hlenSource = Directory.Exists(zhBigDir) ? zhBigDir : tempHlenStaging;
            CopyDirectoryContents(hlenSource, gameFilesDir, cancellationToken);

            // Copy hotkey string definitions from hleg BIG/
            var hlegBigDir = Path.Combine(tempHlegStaging, "BIG");
            var hlegSource = Directory.Exists(hlegBigDir) ? hlegBigDir : tempHlegStaging;
            CopyDirectoryContents(hlegSource, gameFilesDir, cancellationToken);

            await TryConvertCsfToStrAsync(gameFilesDir, cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Successfully unpacked Hotkeys game files into {Dir}", gameFilesDir);
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempHlenStaging))
                {
                    Directory.Delete(tempHlenStaging, recursive: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to clean up temporary hlen staging directory {Dir}", tempHlenStaging);
            }

            try
            {
                if (Directory.Exists(tempHlegStaging))
                {
                    Directory.Delete(tempHlegStaging, recursive: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to clean up temporary hleg staging directory {Dir}", tempHlegStaging);
            }
        }
    }

    private async Task TryConvertCsfToStrAsync(string gameFilesDir, CancellationToken cancellationToken)
    {
        if (stringTableConverter == null)
        {
            return;
        }

        var csfPath = Path.Combine(gameFilesDir, "Data", "English", "generals.csf");
        var strPath = Path.Combine(gameFilesDir, "Data", "English", "generals.str");
        if (File.Exists(csfPath) && !File.Exists(strPath))
        {
            try
            {
                await stringTableConverter.ConvertCsfToStrAsync(csfPath, strPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Optional CSF to STR conversion skipped for {Path}", csfPath);
            }
        }
    }
}
