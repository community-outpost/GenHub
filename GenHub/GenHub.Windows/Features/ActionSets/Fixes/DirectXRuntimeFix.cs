using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Helpers;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Windows.Features.ActionSets.Fixes;

/// <summary>
/// Fix that downloads and installs DirectX 8.1 and 9.0c runtime components required for Generals and Zero Hour.
/// </summary>
public class DirectXRuntimeFix(IHttpClientFactory httpClientFactory, ILogger<DirectXRuntimeFix> logger) : BaseActionSet(logger)
{
    /// <inheritdoc/>
    public override string Id => "DirectXRuntimeFix";

    /// <inheritdoc/>
    public override string Title => "DirectX Runtime Fix";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false; // Network failures shouldn't abort entire sequence

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        // This fix is applicable regardless of installation type as it's a system dependency
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            // GenPatcher check: If D3DX9_43.dll (DX9) and d3d8.dll (DX8 Core) exist, we are good.
            // Note: Modern dxwebsetup often skips d3dx8.dll (helper), but d3d8.dll is sufficient for the game to launch.
            var sysWow64Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64");
            var dx9Dll = Path.Combine(sysWow64Path, "D3DX9_43.dll");
            var dx8Dll = Path.Combine(sysWow64Path, "d3d8.dll");

            return Task.FromResult(File.Exists(dx9Dll) && File.Exists(dx8Dll));
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();
        var tempFolder = Path.Combine(Path.GetTempPath(), $"GenHub_DirectX_{Guid.NewGuid():N}");
        var zipFile = Path.Combine(tempFolder, "dx_runtime.zip");
        var extractPath = Path.Combine(tempFolder, "Extracted");

        try
        {
            details.Add("Starting DirectX Runtime installation...");
            details.Add($"Download URL: {ExternalUrls.DirectXRuntimeDownloadUrl}");

            Directory.CreateDirectory(extractPath);
            details.Add($"Temp directory: {tempFolder}");
            details.Add("Downloading DirectX Runtime...");

            var downloadResult = await DownloadAndValidateAsync(tempFolder, zipFile, details, cancellationToken);
            if (!downloadResult.Success || downloadResult.Data == default)
            {
                return new ActionSetResult(false, string.Join("; ", downloadResult.Errors), details);
            }

            var (isExe, downloadPath) = downloadResult.Data;
            string setupExe = string.Empty;
            string arguments = string.Empty;

            if (isExe)
            {
                setupExe = downloadPath;
                arguments = "/Q";
                details.Add("Running DirectX Web Setup...");
            }
            else
            {
                var extractResult = ExtractPackage(zipFile, extractPath, details);
                if (!extractResult.Success || string.IsNullOrEmpty(extractResult.Data))
                {
                    return new ActionSetResult(false, string.Join("; ", extractResult.Errors), details);
                }

                setupExe = extractResult.Data;
                arguments = "/silent";

                var exeValidation = await DownloadSecurityValidator.ValidateFileAsync(
                    setupExe,
                    expectedAuthenticodePublisher: ActionSetConstants.Security.MicrosoftPublisher,
                    ct: cancellationToken);

                if (!exeValidation.Success)
                {
                    var errorSummary = string.Join("; ", exeValidation.Errors);
                    logger.LogWarning("Security validation failed for extracted DirectX setup: {Error}", errorSummary);
                    return new ActionSetResult(false, $"Extracted DirectX setup failed security validation: {errorSummary}", details);
                }
            }

            return await RunSetupProcessAsync(setupExe, arguments, details, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error implementing DirectX Runtime Fix");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
        finally
        {
            CleanupTempFolder(tempFolder);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        logger.LogWarning("Uninstalling DirectX Runtime is not supported via GenHub.");
        return Task.FromResult(new ActionSetResult(true));
    }

    private async Task<OperationResult<(bool IsExe, string DownloadPath)>> DownloadAndValidateAsync(
        string tempFolder,
        string zipFile,
        List<string> details,
        CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient("Downloader");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        client.Timeout = TimeSpan.FromMinutes(5);

        var urls = new[]
        {
            ExternalUrls.DirectXRuntimeDownloadUrlPrimary,
            ExternalUrls.DirectXRuntimeDownloadUrlMirror1,
        };

        foreach (var url in urls)
        {
            var result = await TryDownloadFromUrlAsync(client, url, tempFolder, zipFile, details, cancellationToken);
            if (result.Success && result.Data != default)
            {
                return result;
            }
        }

        return OperationResult<(bool IsExe, string DownloadPath)>.CreateFailure("Failed to download or validate DirectX Runtime from all mirrors.");
    }

    private async Task<OperationResult<(bool IsExe, string DownloadPath)>> TryDownloadFromUrlAsync(
        HttpClient client,
        string url,
        string tempFolder,
        string zipFile,
        List<string> details,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(url);
        bool isExe = uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        string downloadPath = isExe ? Path.Combine(tempFolder, "dxsetup.exe") : zipFile;

        try
        {
            logger.LogInformation("Attempting download from {Url}", url);

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await contentStream.CopyToAsync(fileStream, cancellationToken);
            }

            var fileInfo = new FileInfo(downloadPath);
            var fileSize = fileInfo.Length;
            var minSize = isExe ? ActionSetConstants.Validation.DirectXWebSetupMinSize : ActionSetConstants.Validation.DirectXPackageMinSize;

            if (fileSize < minSize)
            {
                logger.LogWarning("Downloaded file from {Url} is too small ({Size} bytes). Likely blocked.", url, fileSize);
                DeleteFileIfExists(downloadPath);
                return OperationResult<(bool IsExe, string DownloadPath)>.CreateFailure($"File from {url} is too small.");
            }

            if (isExe)
            {
                var securityValidation = await DownloadSecurityValidator.ValidateFileAsync(
                    downloadPath,
                    expectedAuthenticodePublisher: ActionSetConstants.Security.MicrosoftPublisher,
                    ct: cancellationToken);

                if (!securityValidation.Success)
                {
                    var errorSummary = string.Join("; ", securityValidation.Errors);
                    logger.LogWarning("Security validation failed for DirectX setup from {Url}: {Error}", url, errorSummary);
                    DeleteFileIfExists(downloadPath);
                    return OperationResult<(bool IsExe, string DownloadPath)>.CreateFailure(securityValidation.Errors);
                }
            }
            else
            {
                var securityValidation = await DownloadSecurityValidator.ValidateFileAsync(
                    downloadPath,
                    allowedSha256Hashes: [ActionSetConstants.Security.DirectXRuntimeZipSha256],
                    ct: cancellationToken);

                if (!securityValidation.Success)
                {
                    var errorSummary = string.Join("; ", securityValidation.Errors);
                    logger.LogWarning("Security validation failed for DirectX zip archive from {Url}: {Error}", url, errorSummary);
                    DeleteFileIfExists(downloadPath);
                    return OperationResult<(bool IsExe, string DownloadPath)>.CreateFailure(securityValidation.Errors);
                }

                if (!ValidateZipArchive(downloadPath, url))
                {
                    DeleteFileIfExists(downloadPath);
                    return OperationResult<(bool IsExe, string DownloadPath)>.CreateFailure($"Corrupted zip archive from {url}.");
                }
            }

            details.Add($"✓ Downloaded and verified {fileSize / 1024.0 / 1024.0:F2} MB from {uri.Host}");
            return OperationResult<(bool IsExe, string DownloadPath)>.CreateSuccess((isExe, downloadPath));
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to download from {Url}: {Error}", url, ex.Message);
            DeleteFileIfExists(downloadPath);
            return OperationResult<(bool IsExe, string DownloadPath)>.CreateFailure(ex.Message);
        }
    }

    private bool ValidateZipArchive(string downloadPath, string url)
    {
        try
        {
            using var archive = ZipFile.OpenRead(downloadPath);
            var entryCount = archive.Entries.Count;
            logger.LogInformation("Validated zip archive from {Url} ({Count} entries)", url, entryCount);
            return entryCount > 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Downloaded file from {Url} is corrupt: {Error}", url, ex.Message);
            return false;
        }
    }

    private OperationResult<string> ExtractPackage(string zipFile, string extractPath, List<string> details)
    {
        details.Add("Extracting DirectX Runtime...");
        logger.LogInformation("Extracting DirectX Runtime...");
        ZipFile.ExtractToDirectory(zipFile, extractPath);

        var extractedFiles = Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories);
        details.Add($"✓ Extracted {extractedFiles.Length} files");

        var setupExe = Path.Combine(extractPath, "DXSETUP.exe");
        if (!File.Exists(setupExe))
        {
            details.Add("✗ DXSETUP.exe not found in package");
            return OperationResult<string>.CreateFailure("DXSETUP.exe not found in downloaded package.");
        }

        return OperationResult<string>.CreateSuccess(setupExe);
    }

    private async Task<ActionSetResult> RunSetupProcessAsync(
        string setupExe,
        string arguments,
        List<string> details,
        CancellationToken cancellationToken)
    {
        details.Add("Running DirectX Setup (silent mode)...");
        details.Add("  ⚠ This may require administrator privileges");
        logger.LogInformation("Running DirectX Setup (Silent)...");

        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = setupExe,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
        });

        if (process == null)
        {
            details.Add("✗ Failed to start DirectX setup process");
            return new ActionSetResult(false, "Failed to start DirectX setup process.", details);
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != ProcessConstants.ExitCodeSuccess && process.ExitCode != ProcessConstants.ExitCodeRebootRequired)
        {
            logger.LogError("DirectX setup failed with exit code {ExitCode}", process.ExitCode);
            details.Add($"✗ DirectX setup failed with exit code {process.ExitCode}");
            return new ActionSetResult(false, $"DirectX setup exited with code {process.ExitCode}", details);
        }

        if (process.ExitCode == ProcessConstants.ExitCodeRebootRequired)
        {
            details.Add("✓ DirectX setup completed successfully (reboot required)");
        }
        else
        {
            details.Add("✓ DirectX setup completed successfully");
        }

        details.Add("✓ DirectX Runtime installation completed");
        return new ActionSetResult(true, null, details);
    }

    private void CleanupTempFolder(string tempFolder)
    {
        try
        {
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, true);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to cleanup temp directory: {TempFolder}", tempFolder);
        }
    }

    private void DeleteFileIfExists(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
