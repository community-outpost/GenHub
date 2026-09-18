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

    private const string BigFileSearchPattern = ModBuilderConstants.BigFileSearchPattern;
    private const string UnpackedFolderName = ModBuilderConstants.UnpackedFolderName;
    private const string EnglishLanguageName = ModBuilderConstants.EnglishLanguageName;
    private const string GermanLanguageName = ModBuilderConstants.GermanLanguageName;
    private const string RussianLanguageName = ModBuilderConstants.RussianLanguageName;
    private const string SpanishLanguageName = ModBuilderConstants.SpanishLanguageName;
    private const string GeneralsCsfFileName = ModBuilderConstants.GeneralsCsfFileName;
    private const string WindowDirectoryName = ModBuilderConstants.WindowDirectoryName;
    private const string ArtDirectoryName = ModBuilderConstants.ArtDirectoryName;
    private const string DataDirectoryName = ModBuilderConstants.DataDirectoryName;
    private const string GenToolDirectoryName = ModBuilderConstants.GenToolDirectoryName;

    private sealed record SecondaryLanguageVariantSpec(
        string ZipUrl,
        string ZipPath,
        string AssetLabel,
        string LanguageSubDir);

    private sealed record LeikezeVariantSpec(
        string TargetGameSubDir,
        string PackSubDir,
        string LanguageFolder,
        string BigFileName);

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

        var projectName = Path.GetFileName(projectDir);
        var canonicalName = ResolveCanonicalProjectName(projectName, projectDir);

        return canonicalName switch
        {
            LemonControlBarName => HasLemonControlBarAssets(gameFilesDir),
            ImprovedMenusName => HasImprovedMenusAssets(gameFilesDir),
            GeneralsGamePatch2Name => HasGeneralsGamePatch2Assets(gameFilesDir),
            LeikezeHotkeysName => HasLeikezeHotkeysAssets(gameFilesDir),
            HotkeysName or CustomIconsName => HasHotkeysAssets(gameFilesDir),
            _ => HasGenericSampleAssets(gameFilesDir),
        };
    }

    private static bool HasLemonControlBarAssets(string gameFilesDir)
    {
        var artDir = Path.Combine(gameFilesDir, ArtDirectoryName);
        var wndDir = Path.Combine(gameFilesDir, WindowDirectoryName);
        var dataDir = Path.Combine(gameFilesDir, DataDirectoryName);

        // The bundle config references ControlBarPro.txt explicitly, so stale
        // asset caches predating its acquisition must trigger re-acquisition.
        var controlBarProTxt = Path.Combine(gameFilesDir, ModBuilderConstants.ControlBarProTxtFileName);

        return Directory.Exists(artDir) &&
               Directory.EnumerateFiles(artDir, "*.*", SearchOption.AllDirectories).Any() &&
               Directory.Exists(wndDir) &&
               Directory.EnumerateFiles(wndDir, ModBuilderConstants.FileNames.WndSearchPattern, SearchOption.AllDirectories).Any() &&
               Directory.Exists(dataDir) &&
               Directory.EnumerateFiles(dataDir, ModBuilderConstants.FileNames.IniSearchPattern, SearchOption.AllDirectories).Any() &&
               File.Exists(controlBarProTxt);
    }

    private static bool HasImprovedMenusAssets(string gameFilesDir)
    {
        var wndDir = Path.Combine(gameFilesDir, WindowDirectoryName);
        var hasWnd = Directory.Exists(wndDir) &&
                     Directory.EnumerateFiles(wndDir, ModBuilderConstants.FileNames.WndSearchPattern, SearchOption.AllDirectories).Any();

        var hasTextures = Directory.EnumerateFiles(gameFilesDir, "*.tga", SearchOption.AllDirectories).Any() ||
                          Directory.EnumerateFiles(gameFilesDir, "*.dds", SearchOption.AllDirectories).Any();

        return hasWnd && hasTextures;
    }

    private static bool HasHotkeysAssets(string gameFilesDir)
    {
        return Directory.EnumerateFiles(gameFilesDir, "*.tga", SearchOption.AllDirectories).Any() ||
               Directory.EnumerateFiles(gameFilesDir, ModBuilderConstants.FileNames.IniSearchPattern, SearchOption.AllDirectories).Any() ||
               Directory.EnumerateFiles(gameFilesDir, ModBuilderConstants.FileNames.CsfSearchPattern, SearchOption.AllDirectories).Any();
    }

    private static bool HasGeneralsGamePatch2Assets(string gameFilesDir)
    {
        var iniDir = Path.Combine(gameFilesDir, DataDirectoryName, ModBuilderConstants.DirectoryNames.Ini);
        return Directory.Exists(iniDir) &&
               Directory.EnumerateFiles(iniDir, ModBuilderConstants.FileNames.IniSearchPattern, SearchOption.AllDirectories).Any();
    }

    private static bool HasLeikezeHotkeysAssets(string gameFilesDir)
    {
        return Directory.EnumerateFiles(gameFilesDir, ModBuilderConstants.FileNames.CsfSearchPattern, SearchOption.AllDirectories).Any();
    }

    private static bool HasGenericSampleAssets(string gameFilesDir)
    {
        return Directory.EnumerateFileSystemEntries(gameFilesDir, "*", SearchOption.AllDirectories)
            .Any(file => !ModBuilderConstants.IsIgnoredProjectFile(file));
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
        var preExistingFiles = Directory.Exists(gameFilesDir)
            ? new HashSet<string>(Directory.GetFiles(gameFilesDir, "*", SearchOption.AllDirectories), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                CleanupNewlyCreatedFiles(gameFilesDir, preExistingFiles);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            CleanupNewlyCreatedFiles(gameFilesDir, preExistingFiles);
            throw;
        }
        catch (Exception ex)
        {
            CleanupNewlyCreatedFiles(gameFilesDir, preExistingFiles);
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

    private void CleanupNewlyCreatedFiles(string gameFilesDir, HashSet<string> preExistingFiles)
    {
        if (!Directory.Exists(gameFilesDir))
        {
            return;
        }

        if (preExistingFiles.Count == 0)
        {
            CleanupDirectorySafely(gameFilesDir);
            return;
        }

        try
        {
            var currentFiles = Directory.GetFiles(gameFilesDir, "*", SearchOption.AllDirectories);
            foreach (var file in currentFiles.Where(file => !preExistingFiles.Contains(file)))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to delete partially acquired file {File}", file);
                }
            }

            var subDirs = Directory.GetDirectories(gameFilesDir, "*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.Length);

            foreach (var dir in subDirs)
            {
                try
                {
                    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir, false);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to delete empty directory {Dir}", dir);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to safely clean up newly created files in {Dir}", gameFilesDir);
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

    private async Task<bool> CheckCachedAssetValidAsync(string cachePath, long minLength, string? expectedSha256, CancellationToken cancellationToken)
    {
        if (!File.Exists(cachePath) || new FileInfo(cachePath).Length < minLength)
        {
            return false;
        }

        if (string.IsNullOrEmpty(expectedSha256) || await VerifyFileSha256Async(cachePath, expectedSha256, cancellationToken).ConfigureAwait(false))
        {
            return true;
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

        return false;
    }

    private static async Task<string?> ValidateDownloadedFileAsync(
        string tempPath,
        long minLength,
        string? expectedSha256,
        string assetLabel,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(tempPath) || new FileInfo(tempPath).Length < minLength)
        {
            return $"Downloaded file for {assetLabel} was smaller than expected.";
        }

        if (!string.IsNullOrEmpty(expectedSha256))
        {
            var isShaValid = await VerifyFileSha256Async(tempPath, expectedSha256, cancellationToken).ConfigureAwait(false);
            if (!isShaValid)
            {
                return $"Downloaded file for {assetLabel} failed SHA-256 integrity verification.";
            }
        }

        return null;
    }

    private async Task<OperationResult<bool>> EnsureAssetDownloadedAsync(
        string url,
        string cachePath,
        long minLength,
        string assetLabel,
        CancellationToken cancellationToken,
        string? expectedSha256 = null)
    {
        if (await CheckCachedAssetValidAsync(cachePath, minLength, expectedSha256, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult<bool>.CreateSuccess(true);
        }

        var providerName = url.Contains("legi.cc", StringComparison.OrdinalIgnoreCase) ? "Community Outpost" : "GitHub";
        var contentKey = $"{ContentConstants.SampleContentKeyPrefix}{assetLabel}::{Path.GetFileName(cachePath)}";
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

            var validationError = await ValidateDownloadedFileAsync(tempPath, minLength, expectedSha256, assetLabel, cancellationToken).ConfigureAwait(false);
            if (validationError != null)
            {
                WeakReferenceMessenger.Default.Send(new ContentDownloadCompletedMessage(
                    contentKey, contentId, providerName, assetLabel, false, validationError));
                notificationService?.ShowError("Download Failed", validationError);
                return OperationResult<bool>.CreateFailure(validationError);
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

            var bigFiles = Directory.GetFiles(tempStaging, BigFileSearchPattern, SearchOption.AllDirectories);
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
            var unpackStaging = Path.Combine(tempStaging, UnpackedFolderName);
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

    private static void CopyMovieFiles(string stagingDir, string gameFilesDir)
    {
        var bikFiles = Directory.GetFiles(stagingDir, ModBuilderConstants.FileNames.BikSearchPattern, SearchOption.AllDirectories);
        foreach (var bik in bikFiles)
        {
            var movieDestDir = Path.Combine(gameFilesDir, ModBuilderConstants.DirectoryNames.Data, ModBuilderConstants.DirectoryNames.Movies);
            Directory.CreateDirectory(movieDestDir);
            var destPath = Path.Combine(movieDestDir, Path.GetFileName(bik));
            File.Copy(bik, destPath, overwrite: true);
        }
    }

    private async Task<OperationResult<bool>> UnpackImprovedMenusEnglishAsync(
        string enZipPath,
        string gameFilesDir,
        string? releaseDir,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Extracting Improved Menus English package...");
        var enStaging = Path.Combine(Path.GetTempPath(), $"genhub_menus_en_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(enStaging);
            await ExtractArchiveFileAsync(enZipPath, enStaging, cancellationToken).ConfigureAwait(false);

            var bigFiles = Directory.GetFiles(enStaging, BigFileSearchPattern, SearchOption.AllDirectories);
            if (bigFiles.Length == 0)
            {
                return OperationResult<bool>.CreateFailure("Improved Menus English archive did not contain expected .big file.");
            }

            var primaryBig = bigFiles[0];
            if (!await VerifyFileSha256Async(primaryBig, ModBuilderConstants.SampleProjects.ImprovedMenusSha256, cancellationToken).ConfigureAwait(false))
            {
                return OperationResult<bool>.CreateFailure("Improved Menus English .big file failed SHA-256 integrity verification.");
            }

            progress?.Report("Unpacking English menu files...");
            var unpackStaging = Path.Combine(enStaging, UnpackedFolderName);
            Directory.CreateDirectory(unpackStaging);
            var unpackResult = await BigFilePacker.UnpackAsync(primaryBig, unpackStaging, overwrite: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!unpackResult.Success)
            {
                return OperationResult<bool>.CreateFailure($"Failed to unpack Improved Menus .big file: {unpackResult.FirstError}");
            }

            CopyDirectoryContents(unpackStaging, gameFilesDir, cancellationToken);
            CopyMovieFiles(enStaging, gameFilesDir);

            if (!string.IsNullOrEmpty(releaseDir))
            {
                var destBig = Path.Combine(releaseDir, Path.GetFileName(primaryBig));
                File.Copy(primaryBig, destBig, overwrite: true);
            }

            await TryExtractAndSaveManifestAsync(primaryBig, gameFilesDir, cancellationToken).ConfigureAwait(false);
            return OperationResult<bool>.CreateSuccess(true);
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
    }

    private async Task AcquireSecondaryLanguageVariantAsync(
        SecondaryLanguageVariantSpec spec,
        string gameFilesDir,
        string? releaseDir,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            progress?.Report($"Downloading {spec.AssetLabel}...");
            logger.LogInformation("Downloading {Label} asset from {Url}", spec.AssetLabel, spec.ZipUrl);
            var result = await EnsureAssetDownloadedAsync(
                spec.ZipUrl,
                spec.ZipPath,
                500_000,
                spec.AssetLabel,
                cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                logger.LogWarning("Skipping {Label} download: {Error}", spec.AssetLabel, result.FirstError);
                return;
            }

            progress?.Report($"Extracting {spec.AssetLabel}...");
            var staging = Path.Combine(Path.GetTempPath(), $"genhub_menus_{spec.LanguageSubDir.ToLowerInvariant()}_{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(staging);
                await ExtractArchiveFileAsync(spec.ZipPath, staging, cancellationToken).ConfigureAwait(false);

                var bigFiles = Directory.GetFiles(staging, BigFileSearchPattern, SearchOption.AllDirectories);
                foreach (var big in bigFiles)
                {
                    var unpackDir = Path.Combine(staging, $"unpack_{Path.GetFileNameWithoutExtension(big)}");
                    Directory.CreateDirectory(unpackDir);
                    var unpackRes = await BigFilePacker.UnpackAsync(big, unpackDir, overwrite: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (!unpackRes.Success)
                    {
                        continue;
                    }

                    var sourceCandidates = new[]
                    {
                        Path.Combine(unpackDir, ModBuilderConstants.DirectoryNames.Data, spec.LanguageSubDir, ModBuilderConstants.DirectoryNames.Art, ModBuilderConstants.DirectoryNames.Textures),
                        Path.Combine(unpackDir, ModBuilderConstants.DirectoryNames.Art, ModBuilderConstants.DirectoryNames.Textures, spec.LanguageSubDir),
                        Path.Combine(unpackDir, ModBuilderConstants.DirectoryNames.Art, ModBuilderConstants.DirectoryNames.Textures),
                    };

                    var sourceTexDir = sourceCandidates.FirstOrDefault(Directory.Exists);
                    if (sourceTexDir != null)
                    {
                        var targetDir = Path.Combine(gameFilesDir, ModBuilderConstants.DirectoryNames.Data, spec.LanguageSubDir, ModBuilderConstants.DirectoryNames.Art, ModBuilderConstants.DirectoryNames.Textures);
                        CopyDirectoryContents(sourceTexDir, targetDir, cancellationToken);
                        logger.LogInformation("Successfully unpacked {Label} textures into {Dir}", spec.AssetLabel, targetDir);
                    }

                    if (!string.IsNullOrEmpty(releaseDir))
                    {
                        File.Copy(big, Path.Combine(releaseDir, Path.GetFileName(big)), overwrite: true);
                    }

                    await TryExtractAndSaveManifestAsync(big, gameFilesDir, cancellationToken).ConfigureAwait(false);
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
                catch (Exception ex)
                {
                    logger.LogDebug(ex, StagingCleanupFailedMessage, staging);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to acquire secondary {Language} variant for Improved Menus", spec.LanguageSubDir);
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

        var unpackSuccess = await UnpackImprovedMenusEnglishAsync(enZipPath, gameFilesDir, releaseDir, progress, cancellationToken).ConfigureAwait(false);
        if (!unpackSuccess.Success)
        {
            return unpackSuccess;
        }

        // 2. Russian variant
        await AcquireSecondaryLanguageVariantAsync(
            new SecondaryLanguageVariantSpec(
                ModBuilderConstants.SampleProjects.ImprovedMenusRussianUrl,
                Path.Combine(cacheDir, "0_ImprovedMenusRussian.zip"),
                "Improved Menus Russian",
                RussianLanguageName),
            gameFilesDir,
            releaseDir,
            progress,
            cancellationToken).ConfigureAwait(false);

        // 3. Spanish variant
        await AcquireSecondaryLanguageVariantAsync(
            new SecondaryLanguageVariantSpec(
                ModBuilderConstants.SampleProjects.ImprovedMenusSpanishUrl,
                Path.Combine(cacheDir, "0_ImprovedMenusSpanish.zip"),
                "Improved Menus Spanish",
                SpanishLanguageName),
            gameFilesDir,
            releaseDir,
            progress,
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Successfully unpacked Improved Menus variants into {Dir}", gameFilesDir);
        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task TryExtractAndSaveManifestAsync(string bigFilePath, string gameFilesDir, CancellationToken cancellationToken)
    {
        try
        {
            var manifest = BigFilePacker.ExtractManifest(bigFilePath);
            if (manifest != null)
            {
                var projectDir = Path.GetDirectoryName(gameFilesDir);
                if (string.IsNullOrEmpty(projectDir))
                {
                    return;
                }

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

    private async Task<bool> ProcessLemonResolutionArchiveAsync(
        string zipPath,
        string resolution,
        string gameFilesDir,
        string? releaseDir,
        CancellationToken cancellationToken)
    {
        var staging = Path.Combine(Path.GetTempPath(), $"genhub_lemon_{resolution}_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            await ExtractArchiveFileAsync(zipPath, staging, cancellationToken).ConfigureAwait(false);

            var bigFiles = Directory.GetFiles(staging, BigFileSearchPattern, SearchOption.AllDirectories);
            if (bigFiles.Length == 0)
            {
                logger.LogWarning("No BIG archives found in Lemon Control Bar archive: {Path}", zipPath);
                return false;
            }

            var processedAny = false;
            foreach (var bigFile in bigFiles)
            {
                if (await ProcessSingleLemonBigFileAsync(bigFile, staging, resolution, gameFilesDir, releaseDir, cancellationToken).ConfigureAwait(false))
                {
                    processedAny = true;
                }
            }

            return processedAny;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to process Lemon Control Bar archive for resolution {Res}", resolution);
            return false;
        }
        finally
        {
            CleanupStagingDirectory(staging);
        }
    }

    private async Task<bool> ProcessSingleLemonBigFileAsync(
        string bigFile,
        string staging,
        string resolution,
        string gameFilesDir,
        string? releaseDir,
        CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(bigFile);
        if (string.Equals(resolution, "1080p", StringComparison.OrdinalIgnoreCase) &&
            !await VerifyFileSha256Async(bigFile, ModBuilderConstants.SampleProjects.LemonControlBarSha256, cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning("Lemon Control Bar 1080p BIG file failed SHA-256 integrity verification: {File}", fileName);
            return false;
        }
        var unpackDir = Path.Combine(staging, $"unpack_{Path.GetFileNameWithoutExtension(fileName)}");
        Directory.CreateDirectory(unpackDir);

        var unpackRes = await BigFilePacker.UnpackAsync(bigFile, unpackDir, overwrite: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!unpackRes.Success)
        {
            logger.LogWarning("Failed to unpack BIG file {File}: {Error}", fileName, unpackRes.FirstError);
            return false;
        }

        CopyLemonBigSubdirectories(unpackDir, gameFilesDir, resolution, cancellationToken);

        var cbProTxt = Path.Combine(unpackDir, ModBuilderConstants.ControlBarProTxtFileName);
        if (File.Exists(cbProTxt))
        {
            File.Copy(cbProTxt, Path.Combine(gameFilesDir, ModBuilderConstants.ControlBarProTxtFileName), overwrite: true);
        }

        if (!string.IsNullOrEmpty(releaseDir))
        {
            File.Copy(bigFile, Path.Combine(releaseDir, fileName), overwrite: true);
        }

        await TryExtractAndSaveManifestAsync(bigFile, gameFilesDir, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static void CopyLemonBigSubdirectories(
        string unpackDir,
        string gameFilesDir,
        string resolution,
        CancellationToken cancellationToken)
    {
        var artDir = Path.Combine(unpackDir, ArtDirectoryName);
        if (Directory.Exists(artDir))
        {
            CopyDirectoryContents(artDir, Path.Combine(gameFilesDir, ArtDirectoryName), cancellationToken);
        }

        var dataDir = Path.Combine(unpackDir, DataDirectoryName);
        if (Directory.Exists(dataDir))
        {
            CopyDirectoryContents(dataDir, Path.Combine(gameFilesDir, DataDirectoryName), cancellationToken);
        }

        var wndDir = Path.Combine(unpackDir, WindowDirectoryName);
        if (Directory.Exists(wndDir))
        {
            CopyDirectoryContents(wndDir, Path.Combine(gameFilesDir, WindowDirectoryName, resolution), cancellationToken);
        }

        var genToolDir = Path.Combine(unpackDir, ModBuilderConstants.GenToolDirectoryName);
        if (Directory.Exists(genToolDir))
        {
            CopyDirectoryContents(genToolDir, Path.Combine(gameFilesDir, ModBuilderConstants.GenToolDirectoryName), cancellationToken);
        }
    }

    private void CleanupStagingDirectory(string staging)
    {
        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, StagingCleanupFailedMessage, staging);
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
            (Resolution: "4K", Url: ModBuilderConstants.SampleProjects.LemonControlBar4KUrl, FileName: "ControlBarProLemonEditionZH_v1.3_3840x2160.zip", BigName: "340_ControlBarProLemonEdition2160ZH.big", IsPrimary: false),
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

            var success = await ProcessLemonResolutionArchiveAsync(zipPath, res.Resolution, gameFilesDir, releaseDir, cancellationToken).ConfigureAwait(false);
            if (res.IsPrimary)
            {
                if (success)
                {
                    primarySucceeded = true;
                }
                else
                {
                    logger.LogError("Primary Lemon Control Bar ({Res}) unpack failed; aborting additional downloads", res.Resolution);
                    return OperationResult<bool>.CreateFailure("Failed to unpack primary Lemon Control Bar assets.");
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

    private static string? FindZhEnglishCsf(IReadOnlyList<string> csfFiles, string stagingDir)
    {
        return csfFiles.FirstOrDefault(f => MatchesCsfTokens(f, stagingDir, ["ZH"], ["EN", EnglishLanguageName], [GermanLanguageName, "DE"]))
            ?? csfFiles.FirstOrDefault(f => MatchesCsfTokens(f, stagingDir, ["ZH"], null, [GermanLanguageName, "DE"]))
            ?? csfFiles.FirstOrDefault();
    }

    private static string? FindGeneralsEnglishCsf(IReadOnlyList<string> csfFiles, string stagingDir)
    {
        return csfFiles.FirstOrDefault(f => MatchesCsfTokens(f, stagingDir, [ModBuilderConstants.GeneralsInstallationType, "Gen"], ["EN", EnglishLanguageName], [ModBuilderConstants.ZeroHourInstallationType, "ZH", GermanLanguageName, "DE"]));
    }

    private static string? FindGermanCsf(IReadOnlyList<string> csfFiles, string stagingDir)
    {
        return csfFiles.FirstOrDefault(f => MatchesCsfTokens(f, stagingDir, null, [GermanLanguageName, "DE"], null));
    }

    private static bool MatchesCsfTokens(
        string filePath,
        string stagingDir,
        string[]? requiredTokens,
        string[]? requiredLanguageTokens,
        string[]? excludedTokens)
    {
        var relPath = Path.GetRelativePath(stagingDir, filePath);
        var tokens = relPath.Split(['/', '\\', '_', '.', '-', ' '], StringSplitOptions.RemoveEmptyEntries);

        if (requiredTokens != null && !requiredTokens.Any(t => tokens.Any(tok => tok.Equals(t, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        if (requiredLanguageTokens != null && !requiredLanguageTokens.Any(t => tokens.Any(tok => tok.Equals(t, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        if (excludedTokens != null && excludedTokens.Any(t => tokens.Any(tok => tok.Equals(t, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        return true;
    }

    private async Task SetupLeikezeVariantIfPresentAsync(
        string? csfPath,
        LeikezeVariantSpec spec,
        string gameFilesDir,
        string? releaseDir,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(csfPath) || !File.Exists(csfPath))
        {
            return;
        }

        var targetDir = Path.Combine(gameFilesDir, spec.TargetGameSubDir);
        Directory.CreateDirectory(targetDir);
        var destCsf = Path.Combine(targetDir, GeneralsCsfFileName);
        File.Copy(csfPath, destCsf, overwrite: true);

        await TryConvertCsfToStrInTargetDirAsync(destCsf, targetDir, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(releaseDir))
        {
            await CreateLeikezeReleaseBigAsync(destCsf, releaseDir, gameFilesDir, spec, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task TryConvertCsfToStrInTargetDirAsync(string destCsf, string targetDir, CancellationToken cancellationToken)
    {
        if (stringTableConverter == null)
        {
            return;
        }

        var strPath = Path.Combine(targetDir, ModBuilderConstants.FileNames.GeneralsStr);
        try
        {
            await stringTableConverter.ConvertCsfToStrAsync(destCsf, strPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Optional CSF to STR conversion skipped for {Path}", destCsf);
        }
    }

    private async Task CreateLeikezeReleaseBigAsync(
        string destCsf,
        string releaseDir,
        string gameFilesDir,
        LeikezeVariantSpec spec,
        CancellationToken cancellationToken)
    {
        var packStaging = Path.Combine(Path.GetTempPath(), $"genhub_leikeze_pack_{Guid.NewGuid():N}");
        try
        {
            var stagingLangDir = Path.Combine(packStaging, ModBuilderConstants.DirectoryNames.Data, spec.LanguageFolder);
            Directory.CreateDirectory(stagingLangDir);
            File.Copy(destCsf, Path.Combine(stagingLangDir, GeneralsCsfFileName), overwrite: true);

            var outBigPath = Path.Combine(releaseDir, spec.BigFileName);
            await BigFilePacker.PackAsync(packStaging, outBigPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            await TryExtractAndSaveManifestAsync(outBigPath, gameFilesDir, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create prebuilt release BIG for Leikeze {Big}", spec.BigFileName);
        }
        finally
        {
            try
            {
                if (Directory.Exists(packStaging))
                {
                    Directory.Delete(packStaging, recursive: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, StagingCleanupFailedMessage, packStaging);
            }
        }
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

            var csfFiles = Directory.GetFiles(tempStaging, ModBuilderConstants.FileNames.CsfSearchPattern, SearchOption.AllDirectories);

            // 1. Zero Hour English
            var zhEnSpec = new LeikezeVariantSpec(
                Path.Combine(ModBuilderConstants.DirectoryNames.ZeroHour, EnglishLanguageName, ModBuilderConstants.DirectoryNames.Data, EnglishLanguageName),
                "pack_zhen",
                EnglishLanguageName,
                "400_Hotkeys_ZH_EN.big");
            var zhEnCsf = FindZhEnglishCsf(csfFiles, tempStaging);
            await SetupLeikezeVariantIfPresentAsync(zhEnCsf, zhEnSpec, gameFilesDir, releaseDir, cancellationToken).ConfigureAwait(false);

            // 2. Generals Classic English
            var genEnSpec = new LeikezeVariantSpec(
                Path.Combine(ModBuilderConstants.DirectoryNames.Generals, EnglishLanguageName, ModBuilderConstants.DirectoryNames.Data, EnglishLanguageName),
                "pack_genen",
                EnglishLanguageName,
                "400_Hotkeys_Generals_EN.big");
            var genEnCsf = FindGeneralsEnglishCsf(csfFiles, tempStaging);
            await SetupLeikezeVariantIfPresentAsync(genEnCsf, genEnSpec, gameFilesDir, releaseDir, cancellationToken).ConfigureAwait(false);

            // 3. Zero Hour German
            var zhDeSpec = new LeikezeVariantSpec(
                Path.Combine(ModBuilderConstants.DirectoryNames.ZeroHour, GermanLanguageName, ModBuilderConstants.DirectoryNames.Data, GermanLanguageName),
                "pack_zhde",
                GermanLanguageName,
                "400_Hotkeys_ZH_DE.big");
            var zhDeCsf = FindGermanCsf(csfFiles, tempStaging);
            await SetupLeikezeVariantIfPresentAsync(zhDeCsf, zhDeSpec, gameFilesDir, releaseDir, cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Successfully unpacked Leikeze Hotkeys string tables into {Dir}", gameFilesDir);
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

        var csfPath = Path.Combine(gameFilesDir, ModBuilderConstants.DirectoryNames.Data, EnglishLanguageName, GeneralsCsfFileName);
        var strPath = Path.Combine(gameFilesDir, ModBuilderConstants.DirectoryNames.Data, EnglishLanguageName, ModBuilderConstants.FileNames.GeneralsStr);
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
