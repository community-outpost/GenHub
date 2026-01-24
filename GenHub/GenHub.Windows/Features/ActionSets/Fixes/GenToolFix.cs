namespace GenHub.Windows.Features.ActionSets.Fixes;

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
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

/// <summary>
/// Installs GenTool (d3d8.dll), which provides essential fixes, anti-cheat, and widescreen support.
/// This matches GenPatcher's 'GenTool' action set.
/// </summary>
public class GenToolFix(ILogger<GenToolFix> logger, IHttpClientFactory httpClientFactory) : BaseActionSet(logger)
{
    /// <inheritdoc/>
    public override string Id => "GenToolFix";

    /// <inheritdoc/>
    public override string Title => "GenTool";

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false; // Recommended but not strictly crucial for launch (though highly recommended)

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        bool appliedGenerals = !installation.HasGenerals || File.Exists(Path.Combine(installation.GeneralsPath, "d3d8.dll"));
        bool appliedZeroHour = !installation.HasZeroHour || File.Exists(Path.Combine(installation.ZeroHourPath, "d3d8.dll"));
        return Task.FromResult(appliedGenerals && appliedZeroHour);
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "gentool_setup.zip");
        var details = new List<string>();

        try
        {
            details.Add("Downloading GenTool...");

            using var client = httpClientFactory.CreateClient("Downloader");

            // Add User-Agent to avoid blocking
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            var urls = new[] { ExternalUrls.GenToolDownloadUrlPrimary, ExternalUrls.GenToolDownloadUrlMirror1 };
            bool downloaded = false;

            foreach (var url in urls)
            {
                try
                {
                    logger.LogInformation("Attempting GenTool download from {Url}", url);
                    using var response = await client.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var fileSize = response.Content.Headers.ContentLength ?? 0;

                    // GenTool zip is small but definitely > 100KB
                    if (fileSize < 1024 * 100)
                    {
                         logger.LogWarning("Downloaded file from {Url} is too small ({Size} bytes). Likely blocked.", url, fileSize);
                         continue;
                    }

                    details.Add($"✓ Downloaded {fileSize / 1024.0:F2} KB from {new Uri(url).Host}");

                    using var fs = new FileStream(tempFile, FileMode.Create);
                    await response.Content.CopyToAsync(fs, cancellationToken);
                    fs.Close();
                    downloaded = true;
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Failed to download from {Url}: {Error}", url, ex.Message);
                }
            }

            if (!downloaded)
            {
                return new ActionSetResult(false, "Failed to download GenTool from all mirrors.", details);
            }

            details.Add("Extracting GenTool...");

            // Extract d3d8.dll from zip
            bool dllFound = false;
            using (var archive = ZipFile.OpenRead(tempFile))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.Equals("d3d8.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        dllFound = true;

                        // Extract to Generals path if valid
                        if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
                        {
                            var dest = Path.Combine(installation.GeneralsPath, "d3d8.dll");
                            entry.ExtractToFile(dest, true);
                            details.Add($"✓ Installed GenTool to Generals: {dest}");
                        }

                        // Extract to Zero Hour path if valid
                        if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
                        {
                            var dest = Path.Combine(installation.ZeroHourPath, "d3d8.dll");
                            entry.ExtractToFile(dest, true);
                            details.Add($"✓ Installed GenTool to Zero Hour: {dest}");
                        }

                        break;
                    }
                }
            }

            if (!dllFound)
            {
                return new ActionSetResult(false, "d3d8.dll not found in downloaded archive.", details);
            }

            File.Delete(tempFile);

            // Add Defender exclusions (would require admin, currently just logging)
            details.Add("ℹ Note: You may need to add 'd3d8.dll' to Windows Defender exclusions manually.");

            return new ActionSetResult(true, "GenTool installed successfully.", details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply GenTool fix");
            return new ActionSetResult(false, $"Error: {ex.Message}", details);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
        {
            var p = Path.Combine(installation.GeneralsPath, "d3d8.dll");
            if (File.Exists(p)) File.Delete(p);
        }

        if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
        {
             var p = Path.Combine(installation.ZeroHourPath, "d3d8.dll");
             if (File.Exists(p)) File.Delete(p);
        }

        return Task.FromResult(new ActionSetResult(true, "GenTool removed."));
    }
}
