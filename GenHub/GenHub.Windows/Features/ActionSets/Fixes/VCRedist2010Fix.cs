using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GenHub.Windows.Features.ActionSets.Fixes;

/// <summary>
/// Installs the Visual C++ 2010 Redistributable (x86) which is required for Generals/Zero Hour.
/// </summary>
/// <param name="httpClientFactory">The HTTP client factory.</param>
/// <param name="logger">The logger instance.</param>
public class VCRedist2010Fix(IHttpClientFactory httpClientFactory, ILogger<VCRedist2010Fix> logger) : BaseActionSet(logger)
{
    /// <summary>
    /// Gets the description of the fix.
    /// </summary>
    public static string Description => "Mandatory dependency for C&C Generals and Zero Hour errors.";

    /// <inheritdoc/>
    public override string Id => "VCRedist2010";

    /// <inheritdoc/>
    public override string Title => "Visual C++ 2010 Runtime";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false; // Network failures shouldn't abort entire sequence

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        // This fix is applicable regardless of installation path as it's a system dependency
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        try
        {
            // Check specific registry key for VC++ 2010 x86
            using var key = Registry.LocalMachine.OpenSubKey(RegistryConstants.VCRedist2010x86Key);
            if (key != null)
            {
                var val = key.GetValue(RegistryConstants.InstalledValueName);
                if (val != null && (int)val == 1)
                {
                    return Task.FromResult(true);
                }
            }

            // Fallback check: try WOW6432Node
            using var key64 = Registry.LocalMachine.OpenSubKey(RegistryConstants.VCRedist2010x86KeyWow64);
            if (key64 != null)
            {
                var val = key64.GetValue(RegistryConstants.InstalledValueName);
                if (val != null && (int)val == 1)
                {
                    return Task.FromResult(true);
                }
            }

            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check VCRedist registry status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        try
        {
            details.Add("Starting Visual C++ 2010 Runtime installation...");
            details.Add($"Download URL: {ExternalUrls.VCRedist2010DownloadUrl}");

            var tempPath = Path.Combine(Path.GetTempPath(), "vcredist_x86_2010.exe");
            details.Add($"Temp file: {tempPath}");

            details.Add("Downloading VCRedist 2010...");
            logger.LogInformation("Downloading VCRedist 2010 from {Url}", ExternalUrls.VCRedist2010DownloadUrl);

            using var client = httpClientFactory.CreateClient("Downloader");
            using var response = await client.GetAsync(ExternalUrls.VCRedist2010DownloadUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var fileSize = response.Content.Headers.ContentLength ?? 0;
            details.Add($"✓ Downloaded {fileSize / 1024 / 1024:F2} MB");

            using var fs = new FileStream(tempPath, FileMode.Create);
            await response.Content.CopyToAsync(fs, cancellationToken);
            fs.Close();

            details.Add("Installing VCRedist 2010 (silent mode)...");
            details.Add("  ⚠ This may require administrator privileges");
            logger.LogInformation("Installing VCRedist 2010...");

            var psi = new ProcessStartInfo
            {
                FileName = tempPath,
                Arguments = "/q /norestart", // Silent install
                UseShellExecute = true,
                Verb = "runas", // Request elevation just in case
            };

            var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(cancellationToken);

                // 3010 is restart required
                if (process.ExitCode != ProcessConstants.ExitCodeSuccess && process.ExitCode != 3010)
                {
                    logger.LogWarning("VCRedist install exited with code {Code}", process.ExitCode);
                    details.Add($"⚠ VCRedist install exited with code {process.ExitCode}");
                    details.Add($"✗ Installation may have failed");
                    return new ActionSetResult(false, $"VCRedist install failed with code {process.ExitCode}", details);
                }
                else
                {
                    if (process.ExitCode == 3010)
                    {
                        details.Add("✓ VCRedist 2010 installed successfully");
                        details.Add("  ⚠ System restart may be required");
                    }
                    else
                    {
                        details.Add("✓ VCRedist 2010 installed successfully");
                    }

                    logger.LogInformation("VCRedist 2010 installed successfully");
                }
            }

            // Cleanup
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            details.Add("✓ VCRedist 2010 installation completed");
            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to install VCRedist 2010");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        logger.LogWarning("Uninstalling VCRedist 2010 is not supported via GenHub.");
        return Task.FromResult(new ActionSetResult(true));
    }
}
