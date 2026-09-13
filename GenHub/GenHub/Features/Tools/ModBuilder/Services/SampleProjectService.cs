using System.Security.Cryptography;
using GenHub.Core.Models.Tools.ModBuilder;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Results;
using GenHub.Core.Utilities;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Service responsible for managing, discovering, and acquiring sample project assets on-demand.
/// </summary>
[SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Sample project download URLs")]
public class SampleProjectService : ISampleProjectService
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

#pragma warning disable S1075 // URIs should not be hardcoded
    private const string GeneralsGamePatch2Url = "https://github.com/TheSuperHackers/GeneralsGamePatch2/releases/download/1.0.1/500_900_CommunityPatch_CoreINI.zip";
    private const string ImprovedMenusUrl = "https://github.com/ElTioRata/ImprovedMenus/releases/download/v1.3/0_ImprovedMenusEnglish.zip";
    private const string LemonControlBarUrl = "https://github.com/L3-M/GeneralsControlBar/releases/download/v1.3/ControlBarProLemonEditionZH_v1.3_1920x1080.zip";
    private const string LeikezeHotkeysUrl = "https://legi.cc/gp2/f/hlei.dat";
    private const string HotkeysHlegUrl = "https://legi.cc/gp2/f/hleg.dat";
    private const string HotkeysHlenUrl = "https://legi.cc/gp2/f/hlen.dat";
#pragma warning restore S1075

    private readonly IDownloadService _downloadService;
    private readonly CompressedImageToTgaConverter _imageConverter;
    private readonly IStringTableConversionService? _stringTableConverter;
    private readonly ILogger<SampleProjectService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SampleProjectService"/> class.
    /// </summary>
    /// <param name="downloadService">The download service used to fetch remote asset archives.</param>
    /// <param name="imageConverter">The image converter used to transform textures to TGA format.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="stringTableConverter">The optional string table conversion service.</param>
    public SampleProjectService(
        IDownloadService downloadService,
        CompressedImageToTgaConverter imageConverter,
        ILogger<SampleProjectService> logger,
        IStringTableConversionService? stringTableConverter = null)
    {
        _downloadService = downloadService ?? throw new ArgumentNullException(nameof(downloadService));
        _imageConverter = imageConverter ?? throw new ArgumentNullException(nameof(imageConverter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stringTableConverter = stringTableConverter;
    }

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
        if (string.IsNullOrWhiteSpace(projectDir))
        {
            return false;
        }

        var gameFilesDir = Path.Combine(projectDir, "GameFilesEdited");
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
            _logger.LogDebug("Sample assets already present in {ProjectDir}", projectDir);
            return OperationResult<bool>.CreateSuccess(true);
        }

        var gameFilesDir = Path.Combine(projectDir, "GameFilesEdited");
        Directory.CreateDirectory(gameFilesDir);

        var cacheDir = GetSampleCacheDirectory();
        Directory.CreateDirectory(cacheDir);

        var canonicalName = ResolveCanonicalProjectName(projectName, projectDir);

        try
        {
            switch (canonicalName)
            {
                case GeneralsGamePatch2Name:
                    return await AcquireGeneralsGamePatch2AssetsAsync(gameFilesDir, cacheDir, progress, cancellationToken).ConfigureAwait(false);

                case ImprovedMenusName:
                    return await AcquireImprovedMenusAssetsAsync(gameFilesDir, cacheDir, progress, cancellationToken).ConfigureAwait(false);

                case LemonControlBarName:
                    return await AcquireLemonControlBarAssetsAsync(gameFilesDir, cacheDir, progress, cancellationToken).ConfigureAwait(false);

                case LeikezeHotkeysName:
                    return await AcquireLeikezeHotkeysAssetsAsync(gameFilesDir, cacheDir, progress, cancellationToken).ConfigureAwait(false);

                case HotkeysName:
                case CustomIconsName:
                    return await AcquireHotkeysAssetsAsync(gameFilesDir, cacheDir, progress, cancellationToken).ConfigureAwait(false);

                default:
                    _logger.LogWarning("Unrecognized sample project name: {ProjectName}", projectName);
                    return OperationResult<bool>.CreateFailure($"Unknown sample project: {projectName}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download and extract sample assets for {ProjectName}", projectName);
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
        return Path.Combine(localAppData, "GenHub", "ModBuilderSampleCache");
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

    private static void CopyDirectoryContents(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var targetFilePath = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, targetFilePath, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var targetSubDirPath = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectoryContents(subDir, targetSubDirPath);
        }
    }

    private async Task<OperationResult<bool>> EnsureAssetDownloadedAsync(
        string url,
        string cachePath,
        long minLength,
        string assetLabel,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(cachePath) || new FileInfo(cachePath).Length < minLength)
        {
            var tempPath = $"{cachePath}.tmp_{Guid.NewGuid():N}";
            try
            {
                var downloadResult = await _downloadService.DownloadFileAsync(
                    new Uri(url),
                    tempPath,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (!downloadResult.Success)
                {
                    return OperationResult<bool>.CreateFailure($"Failed to download {assetLabel}: {downloadResult.FirstError ?? UnknownError}");
                }

                if (!File.Exists(tempPath) || new FileInfo(tempPath).Length < minLength)
                {
                    return OperationResult<bool>.CreateFailure($"Downloaded file for {assetLabel} was smaller than expected.");
                }

                File.Move(tempPath, cachePath, overwrite: true);
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
                    _logger.LogDebug(ex, "Failed to clean up temporary download file {Path}", tempPath);
                }
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
        _logger.LogInformation("Downloading GeneralsGamePatch2 asset from {Url}", GeneralsGamePatch2Url);

        var zipCachePath = Path.Combine(cacheDir, "500_900_CommunityPatch_CoreINI.zip");
        var downloadResult = await EnsureAssetDownloadedAsync(
            GeneralsGamePatch2Url,
            zipCachePath,
            100_000,
            "GeneralsGamePatch2",
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
            progress?.Report("Unpacking game files into project...");
            var unpackStaging = Path.Combine(tempStaging, "unpacked");
            Directory.CreateDirectory(unpackStaging);
            await BigFilePacker.UnpackAsync(primaryBig, unpackStaging, overwrite: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            CopyDirectoryContents(unpackStaging, gameFilesDir);
            await TryExtractAndSaveManifestAsync(primaryBig, gameFilesDir, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Successfully unpacked GeneralsGamePatch2 game files into {Dir}", gameFilesDir);
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
                _logger.LogDebug(ex, "Failed to clean up temporary staging directory {Dir}", tempStaging);
            }
        }
    }

    private async Task<OperationResult<bool>> AcquireImprovedMenusAssetsAsync(
        string gameFilesDir,
        string cacheDir,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Downloading ImprovedMenus widescreen release...");
        _logger.LogInformation("Downloading ImprovedMenus asset from {Url}", ImprovedMenusUrl);

        var zipCachePath = Path.Combine(cacheDir, "0_ImprovedMenusEnglish.zip");
        var downloadResult = await EnsureAssetDownloadedAsync(
            ImprovedMenusUrl,
            zipCachePath,
            1_000_000,
            "ImprovedMenus",
            cancellationToken).ConfigureAwait(false);

        if (!downloadResult.Success)
        {
            return downloadResult;
        }

        progress?.Report("Extracting ImprovedMenus widescreen package...");
        var tempStaging = Path.Combine(Path.GetTempPath(), $"genhub_menus_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempStaging);
            await ExtractArchiveFileAsync(zipCachePath, tempStaging, cancellationToken).ConfigureAwait(false);

            var bigFiles = Directory.GetFiles(tempStaging, "*.big", SearchOption.AllDirectories);
            if (bigFiles.Length == 0)
            {
                return OperationResult<bool>.CreateFailure("ImprovedMenus archive did not contain expected .big file.");
            }

            var primaryBig = bigFiles[0];
            progress?.Report("Unpacking menu windows and textures into project...");
            var unpackStaging = Path.Combine(tempStaging, "unpacked");
            Directory.CreateDirectory(unpackStaging);
            await BigFilePacker.UnpackAsync(primaryBig, unpackStaging, overwrite: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            CopyDirectoryContents(unpackStaging, gameFilesDir);
            await TryExtractAndSaveManifestAsync(primaryBig, gameFilesDir, cancellationToken).ConfigureAwait(false);

            // Copy movie backgrounds if provided in release zip
            var bikFiles = Directory.GetFiles(tempStaging, "*.bik", SearchOption.AllDirectories);
            foreach (var bik in bikFiles)
            {
                var movieDestDir = Path.Combine(gameFilesDir, "Data", "Movies");
                Directory.CreateDirectory(movieDestDir);
                var destPath = Path.Combine(movieDestDir, Path.GetFileName(bik));
                File.Copy(bik, destPath, overwrite: true);
            }

            _logger.LogInformation("Successfully unpacked ImprovedMenus game files into {Dir}", gameFilesDir);
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
                _logger.LogDebug(ex, "Failed to clean up temporary staging directory {Dir}", tempStaging);
            }
        }
    }

    private static async Task TryExtractAndSaveManifestAsync(
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
                var configDir = Path.Combine(projectDir, "config");
                Directory.CreateDirectory(configDir);
                var manifestPath = Path.Combine(configDir, $"{Path.GetFileName(bigFilePath)}.manifest.json");
                await BigFilePacker.SaveManifestAsync(manifest, manifestPath, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Manifest extraction is best-effort for reproducibility
        }
    }

    private async Task<OperationResult<bool>> AcquireLemonControlBarAssetsAsync(
        string gameFilesDir,
        string cacheDir,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Downloading Lemon Control Bar (1080p)...");
        _logger.LogInformation("Downloading Lemon Control Bar asset from {Url}", LemonControlBarUrl);

        var zipCachePath = Path.Combine(cacheDir, "ControlBarProLemonEditionZH_v1.3_1920x1080.zip");
        var downloadResult = await EnsureAssetDownloadedAsync(
            LemonControlBarUrl,
            zipCachePath,
            1_000_000,
            "LemonControlBar",
            cancellationToken).ConfigureAwait(false);

        if (!downloadResult.Success)
        {
            return downloadResult;
        }

        progress?.Report("Extracting Lemon Control Bar package...");
        var tempStaging = Path.Combine(Path.GetTempPath(), $"genhub_lemon_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempStaging);
            await ExtractArchiveFileAsync(zipCachePath, tempStaging, cancellationToken).ConfigureAwait(false);

            var bigFiles = Directory.GetFiles(tempStaging, "*.big", SearchOption.AllDirectories);
            if (bigFiles.Length == 0)
            {
                return OperationResult<bool>.CreateFailure("Lemon Control Bar archive did not contain expected .big file.");
            }

            var primaryBig = bigFiles.FirstOrDefault(b => Path.GetFileName(b).Equals("340_ControlBarProLemonEdition1080ZH.big", StringComparison.OrdinalIgnoreCase)) ?? bigFiles[0];
            progress?.Report("Unpacking control bar assets into project...");
            var unpackStaging = Path.Combine(tempStaging, "unpacked");
            Directory.CreateDirectory(unpackStaging);
            await BigFilePacker.UnpackAsync(primaryBig, unpackStaging, overwrite: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            CopyDirectoryContents(unpackStaging, gameFilesDir);
            await TryExtractAndSaveManifestAsync(primaryBig, gameFilesDir, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Successfully unpacked Lemon Control Bar game files into {Dir}", gameFilesDir);
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
                _logger.LogDebug(ex, "Failed to clean up temporary staging directory {Dir}", tempStaging);
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
        _logger.LogInformation("Downloading Leikeze Hotkeys asset from {Url}", LeikezeHotkeysUrl);

        var datCachePath = Path.Combine(cacheDir, "hlei.dat");
        var downloadResult = await EnsureAssetDownloadedAsync(
            LeikezeHotkeysUrl,
            datCachePath,
            500_000,
            "Leikeze Hotkeys",
            cancellationToken).ConfigureAwait(false);

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

            var csfFiles = Directory.GetFiles(tempStaging, "generals.csf", SearchOption.AllDirectories);
            var targetCsf = csfFiles.FirstOrDefault(f => f.Contains("ZH", StringComparison.OrdinalIgnoreCase) && f.Contains("BIG EN", StringComparison.OrdinalIgnoreCase))
                ?? csfFiles.FirstOrDefault();

            if (targetCsf == null || !File.Exists(targetCsf))
            {
                return OperationResult<bool>.CreateFailure("Leikeze Hotkeys archive did not contain generals.csf.");
            }

            var destEnglishDir = Path.Combine(gameFilesDir, "Data", "English");
            Directory.CreateDirectory(destEnglishDir);
            var destCsf = Path.Combine(destEnglishDir, "generals.csf");
            File.Copy(targetCsf, destCsf, overwrite: true);

            var manifest = new BigArchiveManifest
            {
                BigFileName = "!HotkeysLeikezeENZH.big",
                TrailerHex = "0000000000000000",
                Sha256 = "b06677d18c83c108aaa482d571c99a5aad3365c8a492067ef6eaf09364d3ab88",
                EntryOrder = new List<string> { @"Data\English\generals.csf" },
            };

            var projectDir = Path.GetDirectoryName(gameFilesDir);
            if (!string.IsNullOrEmpty(projectDir))
            {
                var configDir = Path.Combine(projectDir, "config");
                Directory.CreateDirectory(configDir);
                var manifestPath = Path.Combine(configDir, "!HotkeysLeikezeENZH.big.manifest.json");
                await BigFilePacker.SaveManifestAsync(manifest, manifestPath, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Successfully unpacked Leikeze Hotkeys into {Dir}", gameFilesDir);
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
                _logger.LogDebug(ex, "Failed to clean up temporary staging directory {Dir}", tempStaging);
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
        _logger.LogInformation("Downloading Hotkeys assets from Community Outpost ({HlegUrl}, {HlenUrl})", HotkeysHlegUrl, HotkeysHlenUrl);

        var hlegCachePath = Path.Combine(cacheDir, "hleg.dat");
        var hlegResult = await EnsureAssetDownloadedAsync(
            HotkeysHlegUrl,
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
            HotkeysHlenUrl,
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
            await _imageConverter.ConvertDirectoryAsync(tempHlenStaging, cancellationToken).ConfigureAwait(false);

            // Copy Zero Hour indicators from ZH/BIG (or fallback to root)
            var zhBigDir = Path.Combine(tempHlenStaging, "ZH", "BIG");
            var hlenSource = Directory.Exists(zhBigDir) ? zhBigDir : tempHlenStaging;
            CopyDirectoryContents(hlenSource, gameFilesDir);

            // Copy hotkey string definitions from hleg BIG/
            var hlegBigDir = Path.Combine(tempHlegStaging, "BIG");
            var hlegSource = Directory.Exists(hlegBigDir) ? hlegBigDir : tempHlegStaging;
            CopyDirectoryContents(hlegSource, gameFilesDir);

            await TryConvertCsfToStrAsync(gameFilesDir, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Successfully unpacked Hotkeys game files into {Dir}", gameFilesDir);
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
                _logger.LogDebug(ex, "Failed to clean up temporary hlen staging directory {Dir}", tempHlenStaging);
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
                _logger.LogDebug(ex, "Failed to clean up temporary hleg staging directory {Dir}", tempHlegStaging);
            }
        }
    }

    private async Task TryConvertCsfToStrAsync(string gameFilesDir, CancellationToken cancellationToken)
    {
        if (_stringTableConverter == null)
        {
            return;
        }

        var csfPath = Path.Combine(gameFilesDir, "Data", "English", "generals.csf");
        var strPath = Path.Combine(gameFilesDir, "Data", "English", "generals.str");
        if (File.Exists(csfPath) && !File.Exists(strPath))
        {
            try
            {
                await _stringTableConverter.ConvertCsfToStrAsync(csfPath, strPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Optional CSF to STR conversion skipped for {Path}", csfPath);
            }
        }
    }
}
