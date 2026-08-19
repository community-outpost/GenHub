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
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        // This fix is applicable regardless of installation type as it's a system dependency
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
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

            using var client = httpClientFactory.CreateClient("Downloader");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.Timeout = TimeSpan.FromMinutes(5);

            var urls = new[]
            {
                ExternalUrls.DirectXRuntimeDownloadUrlPrimary,
                ExternalUrls.DirectXRuntimeDownloadUrlMirror1,
            };
            bool downloaded = false;

            var isExe = false;
            var downloadPath = string.Empty;

            foreach (var url in urls)
            {
                try
                {
                    logger.LogInformation("Attempting download from {Url}", url);

                    var uri = new Uri(url);
                    isExe = uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                    downloadPath = isExe ? Path.Combine(tempFolder, "dxsetup.exe") : zipFile;

                    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    await using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        await contentStream.CopyToAsync(fileStream, cancellationToken);
                    }

                    var fileInfo = new FileInfo(downloadPath);
                    var fileSize = fileInfo.Length;

                    // Validate file size - 200KB for web installer, 1MB for zip
                    var minSize = isExe ? ActionSetConstants.Validation.DirectXWebSetupMinSize : ActionSetConstants.Validation.DirectXPackageMinSize;

                    if (fileSize < minSize)
                    {
                        logger.LogWarning("Downloaded file from {Url} is too small ({Size} bytes). Likely blocked.", url, fileSize);
                        if (File.Exists(downloadPath)) File.Delete(downloadPath);
                        continue;
                    }

                    details.Add($"✓ Downloaded {fileSize / 1024.0 / 1024.0:F2} MB from {uri.Host}");

                    if (!isExe)
                    {
                        // Validate ZIP integrity
                        try
                        {
                            using var archive = ZipFile.OpenRead(downloadPath);
                            var entryCount = archive.Entries.Count;
                            logger.LogInformation("Validated zip archive from {Url} ({Count} entries)", url, entryCount);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning("Downloaded file from {Url} is corrupt: {Error}. Trying next mirror.", url, ex.Message);
                            if (File.Exists(downloadPath)) File.Delete(downloadPath);
                            continue;
                        }
                    }

                    downloaded = true;
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Failed to download from {Url}: {Error}", url, ex.Message);
                    if (File.Exists(downloadPath)) File.Delete(downloadPath);
                }
            }

            if (!downloaded)
            {
                throw new HttpRequestException("Failed to download or validate DirectX Runtime from all mirrors.");
            }

            string setupExe = string.Empty;
            string arguments = string.Empty;

            if (isExe)
            {
                setupExe = downloadPath;
                arguments = "/Q"; // Silent install for web setup
                details.Add("Running DirectX Web Setup...");
            }
            else
            {
                details.Add("Extracting DirectX Runtime...");
                logger.LogInformation("Extracting DirectX Runtime...");
                ZipFile.ExtractToDirectory(zipFile, extractPath);

                var extractedFiles = Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories);
                details.Add($"✓ Extracted {extractedFiles.Length} files");

                setupExe = Path.Combine(extractPath, "DXSETUP.exe");
                if (!File.Exists(setupExe))
                {
                    details.Add("✗ DXSETUP.exe not found in package");
                    return new ActionSetResult(false, "DXSETUP.exe not found in downloaded package.", details);
                }

                arguments = "/silent";
            }

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
                logger.LogWarning("DirectX setup exited with code {ExitCode}", process.ExitCode);
                details.Add($"⚠ DirectX setup exited with code {process.ExitCode}");
            }
            else
            {
                details.Add("✓ DirectX setup completed successfully");
            }

            details.Add("✓ DirectX Runtime installation completed");

            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error implementing DirectX Runtime Fix");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
        finally
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
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        logger.LogWarning("Uninstalling DirectX Runtime is not supported via GenHub.");
        return Task.FromResult(new ActionSetResult(true));
    }
}
