using System.Diagnostics.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        "Hotkeys",
        "CustomIcons",
    ];

    private const string GeneralsGamePatch2Name = "GeneralsGamePatch2";
    private const string ImprovedMenusName = "ImprovedMenus";
    private const string HotkeysName = "Hotkeys";
    private const string CustomIconsName = "CustomIcons";

    private const string UnknownError = "Unknown error";

#pragma warning disable S1075 // URIs should not be hardcoded
    private const string GeneralsGamePatch2Url = "https://github.com/TheSuperHackers/GeneralsGamePatch2/releases/download/1.0.1/500_900_CommunityPatch_CoreINI.zip";
    private const string ImprovedMenusUrl = "https://github.com/ElTioRata/ImprovedMenus/releases/download/v1.3/0_ImprovedMenusEnglish.zip";
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
               normalized.Contains("/SampleProjects/", StringComparison.OrdinalIgnoreCase);
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
            var downloadResult = await _downloadService.DownloadFileAsync(
                new Uri(url),
                cachePath,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!downloadResult.Success)
            {
                return OperationResult<bool>.CreateFailure($"Failed to download {assetLabel}: {downloadResult.FirstError ?? UnknownError}");
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
            await BigFilePacker.UnpackAsync(primaryBig, gameFilesDir, overwrite: true, cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Successfully unpacked GeneralsGamePatch2 game files into {Dir}", gameFilesDir);
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            if (Directory.Exists(tempStaging))
            {
                Directory.Delete(tempStaging, recursive: true);
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
            await BigFilePacker.UnpackAsync(primaryBig, gameFilesDir, overwrite: true, cancellationToken: cancellationToken).ConfigureAwait(false);

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
            if (Directory.Exists(tempStaging))
            {
                Directory.Delete(tempStaging, recursive: true);
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
            if (Directory.Exists(tempHlenStaging))
            {
                Directory.Delete(tempHlenStaging, recursive: true);
            }

            if (Directory.Exists(tempHlegStaging))
            {
                Directory.Delete(tempHlegStaging, recursive: true);
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
