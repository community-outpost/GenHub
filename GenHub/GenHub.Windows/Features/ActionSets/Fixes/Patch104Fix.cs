using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// Installs the Zero Hour 1.04 official patch.
/// </summary>
/// <param name="httpClientFactory">The HTTP client factory.</param>
/// <param name="logger">The logger instance.</param>
public class Patch104Fix(IHttpClientFactory httpClientFactory, ILogger<Patch104Fix> logger) : BaseActionSet(logger)
{
    /// <summary>
    /// Gets the description of the fix.
    /// </summary>
    public static string Description => "Official Zero Hour 1.04 patch - required for multiplayer and compatibility.";

    /// <inheritdoc/>
    public override string Id => "Patch104";

    /// <inheritdoc/>
    public override string Title => "Zero Hour 1.04 Patch";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false; // Download failures shouldn't abort entire sequence

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
            // Check if game.exe version is 1.04
            var gameExePath = Path.Combine(installation.ZeroHourPath, ActionSetConstants.FileNames.GameExe);
            if (!File.Exists(gameExePath))
            {
                return Task.FromResult(false);
            }

            var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(gameExePath);
            var version = versionInfo.FileVersion;

            // 1.04 version should be 1.4.0.0 or similar
            if (version != null && version.StartsWith("1.4"))
            {
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check Zero Hour patch version");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        var isExe = false;
        var downloadPath = string.Empty;
        var extractPath = Path.Combine(Path.GetTempPath(), "zh104_extract");

        try
        {
            details.Add("Starting Zero Hour 1.04 patch installation...");
            details.Add($"Target directory: {installation.ZeroHourPath}");

            details.Add("Downloading patch...");

            using var client = httpClientFactory.CreateClient("Downloader");

            // Add User-Agent to avoid blocking
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.Timeout = TimeSpan.FromMinutes(5); // Increase timeout for large downloads

            var urls = new[] { ExternalUrls.ZeroHour104PatchUrlPrimary, ExternalUrls.ZeroHour104PatchUrlMirror1 };
            bool downloaded = false;

            foreach (var url in urls)
            {
                try
                {
                    logger.LogInformation("Attempting download from {Url}", url);

                    var uri = new Uri(url);
                    isExe = uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

                    // Update temp path based on extension
                    downloadPath = isExe
                        ? Path.Combine(Path.GetTempPath(), "GeneralsZH-104-english.exe")
                        : Path.Combine(Path.GetTempPath(), "zh104_patch.zip");

                    using var response = await client.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var fileSize = response.Content.Headers.ContentLength ?? 0;

                    // Validate file size - if it's too small (e.g. < 1MB), it's likely an error page
                    if (fileSize < 1024 * 1024)
                    {
                        logger.LogWarning("Downloaded file from {Url} is too small ({Size} bytes). Likely blocked.", url, fileSize);
                        continue;
                    }

                    details.Add($"✓ Downloaded {fileSize / 1024 / 1024:F2} MB from {uri.Host}");

                    logger.LogInformation("Reading response content to memory...");
                    var fileBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

                    logger.LogInformation("Writing {Size} bytes to disk...", fileBytes.Length);
                    await File.WriteAllBytesAsync(downloadPath, fileBytes, cancellationToken);

                    if (!isExe)
                    {
                        // Validate integrity by attempting to open the archive
                        try
                        {
                            using var archive = ZipFile.OpenRead(downloadPath);
                            var entryCount = archive.Entries.Count;
                            logger.LogInformation("Validated zip archive from {Url} ({Count} entries)", url, entryCount);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning("Downloaded file from {Url} is corrupt: {Error}. Trying next mirror.", url, ex.Message);
                            continue;
                        }
                    }

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
                throw new HttpRequestException("Failed to download Zero Hour 1.04 Patch from all mirrors.");
            }

            if (isExe)
            {
                details.Add("Running Zero Hour 1.04 Patch Installer...");
                logger.LogInformation("Executing installer {Path}...", downloadPath);

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = downloadPath,
                    Arguments = string.Empty, // Standard installer, interactive is fine if silent fails, but usually no args for this old patch or /S
                    UseShellExecute = true,
                    Verb = "runas",
                });

                if (process != null)
                {
                    await process.WaitForExitAsync(cancellationToken);

                    if (process.ExitCode == 0)
                        details.Add("✓ Patch installer completed successfully");
                    else
                        details.Add($"⚠ Patch installer exited with code {process.ExitCode}");
                }
                else
                {
                    details.Add("✗ Failed to start patch installer");
                    return new ActionSetResult(false, "Failed to start patch installer", details);
                }
            }
            else
            {
                details.Add("Extracting patch files...");
                logger.LogInformation("Extracting Zero Hour 1.04 patch...");

                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);

                Directory.CreateDirectory(extractPath);
                ZipFile.ExtractToDirectory(downloadPath, extractPath);

                var extractedFiles = Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories);
                details.Add($"✓ Extracted {extractedFiles.Length} files");

                // Copy files to game directory
                details.Add($"Installing to: {installation.ZeroHourPath}");
                logger.LogInformation("Copying patch files to {Path}", installation.ZeroHourPath);

                int copiedCount = 0;
                foreach (var file in extractedFiles)
                {
                    var relativePath = file[extractPath.Length..].TrimStart(Path.DirectorySeparatorChar);
                    var destPath = Path.Combine(installation.ZeroHourPath, relativePath);

                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    if (!Path.GetFullPath(destPath).StartsWith(Path.GetFullPath(installation.ZeroHourPath), StringComparison.OrdinalIgnoreCase))
                    {
                         logger.LogWarning("Skipping file {File} due to path traversal detected.", relativePath);
                         continue;
                    }

                    File.Copy(file, destPath, true);
                    logger.LogDebug("Copied {File}", relativePath);
                    copiedCount++;
                }

                details.Add($"✓ Installed {copiedCount} files");
            }

            details.Add("✓ Zero Hour 1.04 patch installed successfully");

            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to install Zero Hour 1.04 patch");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
        finally
        {
            // Cleanup
            if (File.Exists(downloadPath))
            {
                try
                {
                    File.Delete(downloadPath);
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
        logger.LogWarning("Uninstalling Zero Hour 1.04 patch is not supported via GenHub.");
        return Task.FromResult(new ActionSetResult(true));
    }
}
