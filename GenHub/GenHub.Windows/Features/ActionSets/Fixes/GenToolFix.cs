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
/// Installs GenTool (d3d8.dll), which provides essential fixes, anti-cheat, and widescreen support.
/// This matches GenPatcher's 'GenTool' action set.
/// </summary>
public class GenToolFix(ILogger<GenToolFix> logger, IHttpClientFactory httpClientFactory) : BaseActionSet(logger)
{
    private const string D3D8Dll = "d3d8.dll";

    /// <inheritdoc/>
    public override string Id => "GenToolFix";

    /// <inheritdoc/>
    public override string Title => "GenTool (Addon)";

    /// <inheritdoc/>
    public override string Description => "Installs the community GenTool engine wrapper for widescreen resolutions and anti-cheat (also managed in Downloads).";

    /// <inheritdoc/>
    public override string DetailedDescription => "GenTool is the standard community add-on for Generals and Zero Hour operating via Direct3D hook (d3d8.dll). It enables true widescreen display rendering without vertical image cropping, uncap/smooth camera controls, enhanced match recording, and anti-cheat validation. You can also download and manage GenTool from the Downloads section.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.QualityOfLife;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        bool appliedGenerals = !installation.HasGenerals || File.Exists(Path.Combine(installation.GeneralsPath, D3D8Dll));
        bool appliedZeroHour = !installation.HasZeroHour || File.Exists(Path.Combine(installation.ZeroHourPath, D3D8Dll));
        return Task.FromResult(appliedGenerals && appliedZeroHour);
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"gentool_setup_{Guid.NewGuid():N}.dat");
        var tempExtractDir = Path.Combine(Path.GetTempPath(), $"gentool_extract_{Guid.NewGuid():N}");
        var details = new List<string>();

        try
        {
            details.Add("Downloading GenTool...");
            var downloadSuccess = await TryDownloadFromMirrorsAsync(tempFile, details, ct);
            if (!downloadSuccess)
            {
                return new ActionSetResult(false, "Failed to download and authenticate GenTool from all mirrors.", details);
            }

            details.Add($"Extracting and verifying GenTool ({D3D8Dll})...");
            var (extractSuccess, extractedDllPath, extractError) = await ExtractAndVerifyDllAsync(tempFile, tempExtractDir, ct);
            if (!extractSuccess || string.IsNullOrEmpty(extractedDllPath))
            {
                return new ActionSetResult(false, extractError ?? $"Failed to extract {D3D8Dll}.", details);
            }

            var deployResult = await DeployDllAsync(extractedDllPath, installation, details, ct);
            if (!deployResult.Success)
            {
                return deployResult;
            }

            details.Add($"ℹ Note: You may need to add '{D3D8Dll}' to Windows Defender exclusions manually.");
            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply GenTool fix");
            return new ActionSetResult(false, $"Error: {ex.Message}", details);
        }
        finally
        {
            DeleteFileSafely(tempFile);
            DeleteDirectorySafely(tempExtractDir);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
        {
            var p = Path.Combine(installation.GeneralsPath, D3D8Dll);
            if (File.Exists(p))
            {
                File.Delete(p);
            }
        }

        if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
        {
            var p = Path.Combine(installation.ZeroHourPath, D3D8Dll);
            if (File.Exists(p))
            {
                File.Delete(p);
            }
        }

        return Task.FromResult(new ActionSetResult(true, null, ["GenTool (d3d8.dll) removed from installation."]));
    }

    private static Task<ActionSetResult> DeployDllAsync(string extractedDllPath, GameInstallation installation, List<string> details, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        int deployedCount = 0;

        if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
        {
            var dest = Path.Combine(installation.GeneralsPath, D3D8Dll);
            File.Copy(extractedDllPath, dest, overwrite: true);
            details.Add($"✓ Installed GenTool to Generals: {dest}");
            deployedCount++;
        }

        if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
        {
            var dest = Path.Combine(installation.ZeroHourPath, D3D8Dll);
            File.Copy(extractedDllPath, dest, overwrite: true);
            details.Add($"✓ Installed GenTool to Zero Hour: {dest}");
            deployedCount++;
        }

        if (deployedCount == 0)
        {
            return Task.FromResult(new ActionSetResult(false, "No valid game installation directory found to install GenTool.", details));
        }

        return Task.FromResult(new ActionSetResult(true, null, details));
    }

    private async Task<bool> TryDownloadFromMirrorsAsync(string tempFile, List<string> details, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient("Downloader");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        var urls = new[]
        {
            ExternalUrls.GenToolDownloadUrlPrimary,
            ExternalUrls.GenToolDownloadUrlMirror1,
        };

        foreach (var url in urls)
        {
            try
            {
                logger.LogInformation("Attempting GenTool download from {Url}", url);
                using var response = await client.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();

                await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await response.Content.CopyToAsync(fs, ct);
                }

                var fileInfo = new FileInfo(tempFile);
                if (fileInfo.Length < ActionSetConstants.Validation.MinimumAddonPackageSizeBytes)
                {
                    logger.LogWarning("Downloaded file from {Url} is too small ({Size} bytes). Skipping mirror.", url, fileInfo.Length);
                    DeleteFileSafely(tempFile);
                    continue;
                }

                var validation = await DownloadSecurityValidator.ValidateFileAsync(
                    tempFile,
                    allowedSha256Hashes: [ActionSetConstants.Security.GenToolArchiveSha256],
                    ct: ct);

                if (!validation.Success)
                {
                    logger.LogWarning("Validation failed for {Url}: {Error}", url, string.Join("; ", validation.Errors));
                    DeleteFileSafely(tempFile);
                    continue;
                }

                details.Add($"✓ Downloaded and authenticated from {new Uri(url).Host}");
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Download failed from {Url}", url);
                DeleteFileSafely(tempFile);
            }
        }

        return false;
    }

    private async Task<(bool Success, string? ExtractedDllPath, string? ErrorMessage)> ExtractAndVerifyDllAsync(string zipPath, string tempExtractDir, CancellationToken ct)
    {
        Directory.CreateDirectory(tempExtractDir);
        using var archive = ArchiveFactory.OpenArchive(new FileInfo(zipPath));

        var d3d8Entry = archive.Entries.FirstOrDefault(e =>
            !e.IsDirectory &&
            string.Equals(Path.GetFileName(e.Key), D3D8Dll, StringComparison.OrdinalIgnoreCase));

        if (d3d8Entry == null)
        {
            return (false, null, $"Archive does not contain required '{D3D8Dll}'.");
        }

        var extractedDllPath = Path.Combine(tempExtractDir, D3D8Dll);
        await using var entryStream = await d3d8Entry.OpenEntryStreamAsync();
        await using var fs = new FileStream(extractedDllPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await entryStream.CopyToAsync(fs, ct);

        var dllValidation = await DownloadSecurityValidator.ValidateAndLockFileAsync(
            extractedDllPath,
            allowedSha256Hashes: [ActionSetConstants.Security.GenToolD3D8DllSha256],
            ct: ct);

        if (!dllValidation.Success || dllValidation.Data == null)
        {
            var errorSummary = string.Join("; ", dllValidation.Errors);
            logger.LogWarning("Security validation failed for extracted GenTool {Dll}: {Error}", D3D8Dll, errorSummary);
            return (false, null, $"Security validation failed for GenTool {D3D8Dll}: {errorSummary}");
        }

        await dllValidation.Data.DisposeAsync();
        return (true, extractedDllPath, null);
    }
}
