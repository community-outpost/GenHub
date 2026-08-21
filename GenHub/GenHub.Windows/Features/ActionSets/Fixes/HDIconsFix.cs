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
/// Fix that downloads and installs high-definition icons for Generals and Zero Hour.
/// Replaces legacy 32x32 Windows XP icons with 256x256 HD icon assets.
/// </summary>
public class HDIconsFix(IHttpClientFactory httpClientFactory, ILogger<HDIconsFix> logger) : BaseActionSet(logger)
{
    private static readonly IReadOnlyList<string> KnownHdIconFiles =
    [
        "generals_hd.ico",
        "game_hd.ico",
        "zh_hd.ico",
    ];

    private readonly string _markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", ActionSetConstants.Paths.SubActionSetMarkers, "HDIconsFix.done");

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

                    details.Add($"✓ Downloaded {fileInfo.Length / 1024.0:F2} KB icon pack from {new Uri(url).Host}");
                    downloaded = true;
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Failed to download HD icons from {Url}: {Error}", url, ex.Message);
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
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
            int extractedCount = 0;
            var extractedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory && e.Key != null))
            {
                var fileName = Path.GetFileName(entry.Key);
                if (string.IsNullOrEmpty(fileName))
                {
                    continue;
                }

                extractedFiles.Add(fileName);
                var extractedFilePath = Path.Combine(tempExtractDir, fileName);
                using (var entryStream = entry.OpenEntryStream())
                await using (var fs = new FileStream(extractedFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await entryStream.CopyToAsync(fs, cancellationToken);
                }

                extractedCount++;

                // Deploy to Generals installation directory if available
                if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
                {
                    var generalsDest = Path.Combine(installation.GeneralsPath, fileName);
                    File.Copy(extractedFilePath, generalsDest, overwrite: true);
                }

                // Deploy to Zero Hour installation directory if available
                if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
                {
                    var zhDest = Path.Combine(installation.ZeroHourPath, fileName);
                    File.Copy(extractedFilePath, zhDest, overwrite: true);
                }
            }

            if (!KnownHdIconFiles.All(extractedFiles.Contains))
            {
                var missing = KnownHdIconFiles.Where(f => !extractedFiles.Contains(f));
                var missingSummary = string.Join(", ", missing);
                logger.LogWarning("HD icons package is missing required icon files: {Missing}", missingSummary);
                return new ActionSetResult(false, $"HD icons package is missing expected files: {missingSummary}", details);
            }

            details.Add($"✓ Extracted and deployed {extractedCount} HD icon assets to game folders.");

            try
            {
                var markerDir = Path.GetDirectoryName(_markerPath);
                if (!string.IsNullOrEmpty(markerDir))
                {
                    Directory.CreateDirectory(markerDir);
                }

                File.WriteAllText(_markerPath, DateTime.UtcNow.ToString("O"));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create marker file for HDIconsFix");
            }

            return new ActionSetResult(true, null, details);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying HD icons fix");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to delete temp file {TempFile}", tempFile);
            }

            try
            {
                if (Directory.Exists(tempExtractDir))
                {
                    Directory.Delete(tempExtractDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to delete temp directory {TempDir}", tempExtractDir);
            }
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var removedCount = 0;

        try
        {
            if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
            {
                foreach (var icon in KnownHdIconFiles)
                {
                    var p = Path.Combine(installation.GeneralsPath, icon);
                    if (File.Exists(p))
                    {
                        File.Delete(p);
                        removedCount++;
                    }
                }
            }

            if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
            {
                foreach (var icon in KnownHdIconFiles)
                {
                    var p = Path.Combine(installation.ZeroHourPath, icon);
                    if (File.Exists(p))
                    {
                        File.Delete(p);
                        removedCount++;
                    }
                }
            }

            if (File.Exists(_markerPath))
            {
                File.Delete(_markerPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete marker or icon files for HDIconsFix");
        }

        return Task.FromResult(new ActionSetResult(true, null, [$"HD icons removed ({removedCount} files deleted)."]));
    }

    private bool AreHDIconsPresent(GameInstallation installation)
    {
        try
        {
            var hasAnyTarget = false;

            if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
            {
                hasAnyTarget = true;
                if (!KnownHdIconFiles.All(iconFile => File.Exists(Path.Combine(installation.GeneralsPath, iconFile))))
                {
                    return false;
                }
            }

            if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
            {
                hasAnyTarget = true;
                if (!KnownHdIconFiles.All(iconFile => File.Exists(Path.Combine(installation.ZeroHourPath, iconFile))))
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
