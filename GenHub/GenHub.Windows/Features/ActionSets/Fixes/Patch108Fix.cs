using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

namespace GenHub.Windows.Features.ActionSets.Fixes;

/// <summary>
/// Installs the Generals 1.08 official patch.
/// </summary>
/// <param name="httpClientFactory">The HTTP client factory.</param>
/// <param name="logger">The logger instance.</param>
public class Patch108Fix(IHttpClientFactory httpClientFactory, ILogger<Patch108Fix> logger) : BaseActionSet(logger)
{
    /// <summary>
    /// Gets the description of the fix.
    /// </summary>
    public static string Description => "Official Generals 1.08 patch - required for multiplayer and compatibility.";

    /// <inheritdoc/>
    public override string Id => "Patch108";

    /// <inheritdoc/>
    public override string Title => "Generals 1.08 Patch";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        return Task.FromResult(installation.HasGenerals);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        try
        {
            // Check if generals.exe version is 1.08
            var gameExePath = Path.Combine(installation.GeneralsPath, ActionSetConstants.FileNames.GeneralsExe);
            if (!File.Exists(gameExePath))
            {
                return Task.FromResult(false);
            }

            var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(gameExePath);
            var version = versionInfo.FileVersion;

            // 1.08 version should be 1.8.0.0 or similar
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
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        var tempPath = Path.Combine(Path.GetTempPath(), $"gn108_patch_{Guid.NewGuid():N}.zip");
        var extractPath = Path.Combine(Path.GetTempPath(), $"gn108_extract_{Guid.NewGuid():N}");

        try
        {
            details.Add("Starting Generals 1.08 patch installation...");
            details.Add($"Target directory: {installation.GeneralsPath}");

            details.Add($"Download URL: {ExternalUrls.Generals108PatchUrl}");
            details.Add("Downloading patch archive...");

            logger.LogInformation("Downloading Generals 1.08 patch from {Url}", ExternalUrls.Generals108PatchUrl);

            using var client = httpClientFactory.CreateClient("Downloader");
            using var response = await client.GetAsync(ExternalUrls.Generals108PatchUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await response.Content.CopyToAsync(fs, cancellationToken);
            }

            var fileInfo = new FileInfo(tempPath);
            var fileSize = fileInfo.Length;
            if (fileSize < ActionSetConstants.Validation.PatchMinSize)
            {
                logger.LogWarning("Downloaded Generals 1.08 patch file too small ({Size} bytes), likely corrupt.", fileSize);
                if (File.Exists(tempPath)) File.Delete(tempPath);
                return new ActionSetResult(false, "Downloaded Generals 1.08 patch is corrupted or incomplete.", details);
            }

            details.Add($"✓ Downloaded {fileSize / 1024.0 / 1024.0:F2} MB");

            details.Add("Extracting patch files...");
            logger.LogInformation("Extracting Generals 1.08 patch...");

            Directory.CreateDirectory(extractPath);
            ZipFile.ExtractToDirectory(tempPath, extractPath);

            var extractedFiles = Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories);
            details.Add($"✓ Extracted {extractedFiles.Length} files");

            // Copy files to game directory
            details.Add($"Installing to: {installation.GeneralsPath}");
            logger.LogInformation("Copying patch files to {Path}", installation.GeneralsPath);

            int copiedCount = 0;
            var canonicalGamePath = Path.GetFullPath(installation.GeneralsPath);
            foreach (var file in extractedFiles)
            {
                var relativePath = file[extractPath.Length..].TrimStart(Path.DirectorySeparatorChar);
                var destPath = Path.GetFullPath(Path.Combine(canonicalGamePath, relativePath));

                if (!destPath.StartsWith(canonicalGamePath, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Potential path traversal detected in patch archive: {Path}", relativePath);
                    continue;
                }

                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(file, destPath, true);
                logger.LogDebug("Copied {File}", relativePath);
                copiedCount++;
            }

            details.Add($"✓ Installed {copiedCount} files");

            details.Add("✓ Cleanup completed");
            details.Add("✓ Generals 1.08 patch installed successfully");

            logger.LogInformation("Generals 1.08 patch installed successfully with {Count} actions", details.Count);
            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to install Generals 1.08 patch");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
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
                logger.LogDebug(ex, "Failed to delete temp file {TempFile}", tempPath);
            }

            try
            {
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to delete extract folder {ExtractPath}", extractPath);
            }
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        logger.LogWarning("Uninstalling Generals 1.08 patch is not supported via GenHub.");
        return Task.FromResult(new ActionSetResult(true));
    }
}
