using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Helpers;
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
    /// <inheritdoc/>
    public override string Id => "VCRedist2010";

    /// <inheritdoc/>
    public override string Title => "Visual C++ 2010 Runtime";

    /// <inheritdoc/>
    public override string Description => "Installs the mandatory Visual C++ 2010 (x86) runtime required for GenTool and modern enhancements.";

    /// <inheritdoc/>
    public override string DetailedDescription => "GenTool, modern widescreen hooks, and community security updates depend directly on the 32-bit Visual C++ 2010 runtime. This fix downloads and installs the official Microsoft runtime package, ensuring GenTool operates without missing DLL errors.";

    /// <inheritdoc/>
    public override string Category => "Core & Stability";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false; // Network failures shouldn't abort entire sequence

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
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
        var tempPath = Path.Combine(Path.GetTempPath(), $"vcredist_x86_2010_{Guid.NewGuid():N}.exe");

        try
        {
            details.Add("Starting Visual C++ 2010 Runtime installation...");
            details.Add($"Download URL: {ExternalUrls.VCRedist2010DownloadUrl}");
            details.Add($"Temp file: {tempPath}");

            details.Add("Downloading VCRedist 2010...");
            logger.LogInformation("Downloading VCRedist 2010 from {Url}", ExternalUrls.VCRedist2010DownloadUrl);

            using var client = httpClientFactory.CreateClient("Downloader");
            using var response = await client.GetAsync(ExternalUrls.VCRedist2010DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await response.Content.CopyToAsync(fs, cancellationToken);
            }

            var fileInfo = new FileInfo(tempPath);
            var fileSize = fileInfo.Length;
            if (fileSize < ActionSetConstants.Validation.VCRedistMinSize)
            {
                logger.LogWarning("Downloaded VCRedist 2010 file too small ({Size} bytes), likely corrupt.", fileSize);
                if (File.Exists(tempPath)) File.Delete(tempPath);
                return new ActionSetResult(false, "Downloaded VCRedist 2010 is corrupted or incomplete.", details);
            }

            // Security signature validation (Authenticode publisher verification)
            var securityValidation = await DownloadSecurityValidator.ValidateFileAsync(
                tempPath,
                expectedAuthenticodePublisher: ActionSetConstants.Security.MicrosoftPublisher,
                ct: cancellationToken);

            if (!securityValidation.Success)
            {
                var errorSummary = string.Join("; ", securityValidation.Errors);
                logger.LogWarning("Security validation failed for VCRedist 2010: {Error}", errorSummary);
                if (File.Exists(tempPath)) File.Delete(tempPath);
                return new ActionSetResult(false, $"Security validation failed: {errorSummary}", details);
            }

            details.Add($"✓ Downloaded and verified {fileSize / 1024.0 / 1024.0:F2} MB");

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

            using var process = Process.Start(psi);
            if (process == null)
            {
                details.Add("✗ Failed to start VCRedist installer process");
                return new ActionSetResult(false, "Failed to start VCRedist installer process", details);
            }

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != ProcessConstants.ExitCodeSuccess && process.ExitCode != ProcessConstants.ExitCodeRebootRequired)
            {
                logger.LogWarning("VCRedist install exited with code {Code}", process.ExitCode);
                details.Add($"⚠ VCRedist install exited with code {process.ExitCode}");
                details.Add("✗ Installation may have failed");
                return new ActionSetResult(false, $"VCRedist install failed with code {process.ExitCode}", details);
            }

            if (process.ExitCode == ProcessConstants.ExitCodeRebootRequired)
            {
                details.Add("✓ VCRedist 2010 installed successfully");
                details.Add("  ⚠ System restart may be required");
            }
            else
            {
                details.Add("✓ VCRedist 2010 installed successfully");
            }

            logger.LogInformation("VCRedist 2010 installed successfully");

            details.Add("✓ VCRedist 2010 installation completed");
            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to install VCRedist 2010");
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
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        logger.LogWarning("Uninstalling VCRedist 2010 is not supported via GenHub.");
        return Task.FromResult(new ActionSetResult(true));
    }
}
