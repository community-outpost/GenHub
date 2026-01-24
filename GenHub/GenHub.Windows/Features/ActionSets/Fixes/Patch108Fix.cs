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
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        // Disabled per user request - redundant with GenHub Downloads section
        return Task.FromResult(false);
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
            if (version != null && version.StartsWith("1.8"))
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

        var tempPath = Path.Combine(Path.GetTempPath(), "gn108_patch.zip");
        var extractPath = Path.Combine(Path.GetTempPath(), "gn108_extract");

        try
        {
            details.Add("Starting Generals 1.08 patch installation...");
            details.Add($"Target directory: {installation.GeneralsPath}");

            details.Add($"Download URL: {ExternalUrls.Generals108PatchUrl}");
            details.Add("Downloading patch archive...");

            logger.LogInformation("Downloading Generals 1.08 patch from {Url}", ExternalUrls.Generals108PatchUrl);

            using var client = httpClientFactory.CreateClient("Downloader");
            using var response = await client.GetAsync(ExternalUrls.Generals108PatchUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var fileSize = response.Content.Headers.ContentLength ?? 0;
            details.Add($"✓ Downloaded {fileSize / 1024 / 1024:F2} MB");

            using var fs = new FileStream(tempPath, FileMode.Create);
            await response.Content.CopyToAsync(fs, cancellationToken);
            fs.Close();

            details.Add("Extracting patch files...");
            logger.LogInformation("Extracting Generals 1.08 patch...");

            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);

            Directory.CreateDirectory(extractPath);
            ZipFile.ExtractToDirectory(tempPath, extractPath);

            var extractedFiles = Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories);
            details.Add($"✓ Extracted {extractedFiles.Length} files");

            // Copy files to game directory
            details.Add($"Installing to: {installation.GeneralsPath}");
            logger.LogInformation("Copying patch files to {Path}", installation.GeneralsPath);

            int copiedCount = 0;
            foreach (var file in extractedFiles)
            {
                var relativePath = file[extractPath.Length..].TrimStart(Path.DirectorySeparatorChar);
                var destPath = Path.Combine(installation.GeneralsPath, relativePath);

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
            // Cleanup
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }

            if (Directory.Exists(extractPath))
            {
                try
                {
                    Directory.Delete(extractPath, true);
                }
                catch
                {
                }
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
