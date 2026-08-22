namespace GenHub.Windows.Features.ActionSets.Fixes;

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
using GenHub.Core.Helpers;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Downloads and installs the official Command &amp; Conquer: Generals Zero Hour 1.04 Patch.
/// Matches GenPatcher's 'Patch104' action set.
/// </summary>
public class Patch104Fix(ILogger<Patch104Fix> logger, IHttpClientFactory httpClientFactory) : BaseActionSet(logger)
{
    /// <inheritdoc/>
    public override string Id => "Patch104";

    /// <inheritdoc/>
    public override string Title => "Zero Hour 1.04 Patch";

    /// <inheritdoc/>
    public override string Description => "Downloads and installs the official Command & Conquer: Generals Zero Hour 1.04 update patch.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Upgrades Zero Hour to the official final 1.04 release. Fixes numerous multiplayer synchronization bugs, unit balance discrepancies, and exploit vulnerabilities. Required for compatibility with all modern mods, GenTool, and online multiplayer matches.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.CoreAndStability;

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath));
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            var gameExePath = Path.Combine(installation.ZeroHourPath, ActionSetConstants.FileNames.GameExe);
            if (!File.Exists(gameExePath))
            {
                return Task.FromResult(false);
            }

            var versionInfo = FileVersionInfo.GetVersionInfo(gameExePath);
            var version = versionInfo.FileVersion;

            if (version?.StartsWith("1.4") == true)
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
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();
        var downloadPath = string.Empty;
        var extractPath = Path.Combine(Path.GetTempPath(), "zh104_extract");

        try
        {
            details.Add("Starting Zero Hour 1.04 patch installation...");
            details.Add($"Target directory: {installation.ZeroHourPath}");

            var (path, isExe) = await DownloadPatchAsync(details, ct);
            downloadPath = path;

            if (isExe)
            {
                var installerResult = await RunPatchInstallerAsync(downloadPath, details, ct);
                if (installerResult != null)
                {
                    return installerResult;
                }
            }
            else
            {
                ExtractAndCopyPatchFiles(downloadPath, extractPath, installation.ZeroHourPath, details);
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
            CleanupTemp(downloadPath, extractPath);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        return Task.FromResult(new ActionSetResult(
            false,
            "Zero Hour 1.04 official patch executable cannot be automatically rolled back without base game archives. Please repair/re-verify files through your game launcher.",
            ["Official game patch binaries remain in place."]));
    }

    private async Task<(string DownloadPath, bool IsExe)> DownloadPatchAsync(
        List<string> details,
        CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient("Downloader");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        var urls = new[] { ExternalUrls.ZeroHour104PatchUrlPrimary, ExternalUrls.ZeroHour104PatchUrlMirror1 };

        foreach (var url in urls)
        {
            var result = await TryDownloadMirrorAsync(client, url, details, ct);
            if (result.Success)
            {
                return (result.DownloadPath, result.IsExe);
            }
        }

        throw new HttpRequestException("Failed to download Zero Hour 1.04 Patch from all mirrors.");
    }

    private async Task<(bool Success, string DownloadPath, bool IsExe)> TryDownloadMirrorAsync(
        HttpClient client,
        string url,
        List<string> details,
        CancellationToken ct)
    {
        var uri = new Uri(url);
        var isExe = uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        var downloadPath = isExe
            ? Path.Combine(Path.GetTempPath(), $"GeneralsZH-104-english_{Guid.NewGuid():N}.exe")
            : Path.Combine(Path.GetTempPath(), $"zh104_patch_{Guid.NewGuid():N}.zip");

        try
        {
            logger.LogInformation("Attempting download from {Url}", url);

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            logger.LogInformation("Streaming response content to disk at {Path}...", downloadPath);
            await using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fileStream, ct);
            }

            var downloadedFileInfo = new FileInfo(downloadPath);
            if (downloadedFileInfo.Length < ActionSetConstants.Validation.PatchMinSize)
            {
                logger.LogWarning("Downloaded file from {Url} is too small ({Size} bytes). Likely blocked.", url, downloadedFileInfo.Length);
                return (false, downloadPath, isExe);
            }

            details.Add($"✓ Downloaded {downloadedFileInfo.Length / 1024.0 / 1024.0:F2} MB from {uri.Host}");

            if (!isExe)
            {
                if (!ValidateZipArchive(downloadPath, url))
                {
                    return (false, downloadPath, isExe);
                }
            }
            else
            {
                var securityValidation = await DownloadSecurityValidator.ValidateAndLockFileAsync(
                    downloadPath,
                    expectedAuthenticodePublisher: ActionSetConstants.Security.ElectronicArtsPublisher,
                    allowExpiredCertificates: true,
                    ct: ct);

                if (!securityValidation.Success || securityValidation.Data == null)
                {
                    logger.LogWarning("Authenticode verification failed for patch executable from {Url}: {Error}", url, securityValidation.FirstError);
                    DeleteFileSafely(downloadPath);
                    return (false, downloadPath, isExe);
                }

                await securityValidation.Data.DisposeAsync();
            }

            return (true, downloadPath, isExe);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to download from {Url}", url);
            return (false, downloadPath, isExe);
        }
    }

    private bool ValidateZipArchive(string downloadPath, string url)
    {
        try
        {
            using var archive = ZipFile.OpenRead(downloadPath);
            var entryCount = archive.Entries.Count;
            logger.LogInformation("Validated zip archive from {Url} ({Count} entries)", url, entryCount);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Downloaded file from {Url} is corrupt. Trying next mirror.", url);
            return false;
        }
    }

    private async Task<ActionSetResult?> RunPatchInstallerAsync(
        string downloadPath,
        List<string> details,
        CancellationToken ct)
    {
        details.Add("Running Zero Hour 1.04 Patch Installer...");
        logger.LogInformation("Executing installer {Path}...", downloadPath);

        var processInfo = new ProcessStartInfo
        {
            FileName = downloadPath,
            UseShellExecute = true,
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            return new ActionSetResult(false, "Failed to start patch installer process.", details);
        }

        details.Add("⚠ Please complete the installation wizard on screen.");
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != ProcessConstants.ExitCodeSuccess && process.ExitCode != ProcessConstants.ExitCodeRebootRequired)
        {
            return new ActionSetResult(false, $"Installer exited with non-zero code {process.ExitCode}.", details);
        }

        return null;
    }

    private void ExtractAndCopyPatchFiles(
        string downloadPath,
        string extractPath,
        string targetDirectory,
        List<string> details)
    {
        details.Add("Extracting patch archive...");
        Directory.CreateDirectory(extractPath);
        ZipFile.ExtractToDirectory(downloadPath, extractPath, overwriteFiles: true);

        details.Add("Copying patch files to game directory...");
        var files = Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories);
        int copiedCount = 0;

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(extractPath, file);
            var destPath = Path.Combine(targetDirectory, relativePath);

            var fullTarget = Path.GetFullPath(targetDirectory);
            var fullDest = Path.GetFullPath(destPath);
            if (!fullDest.StartsWith(fullTarget, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Skipping file {File} due to path traversal detected.", relativePath);
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
    }

    private void CleanupTemp(string downloadPath, string extractPath)
    {
        DeleteFileSafely(downloadPath);

        if (Directory.Exists(extractPath))
        {
            try
            {
                foreach (var file in Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories))
                {
                    DeleteFileSafely(file);
                }

                Directory.Delete(extractPath, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Failed to clean up extract directory {Path}", extractPath);
            }
        }
    }

    private void DeleteFileSafely(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Failed to safely delete temporary file {Path}", path);
        }
    }
}
