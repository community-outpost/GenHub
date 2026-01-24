namespace GenHub.Windows.Features.ActionSets.Fixes;

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

/// <summary>
/// Fix that checks for and installs Visual C++ 2008 Redistributable (x86).
/// Required for some legacy components and GenPatcher parity.
/// </summary>
public class VCRedist2008Fix(IHttpClientFactory httpClientFactory, ILogger<VCRedist2008Fix> logger) : BaseActionSet(logger)
{
    // Product Code for VC++ 2008 SP1 Redistributable (x86)
    private const string Vc2008ProductCode = "{9A25302D-30C0-39D9-BD6F-21E6EC160475}";

    private readonly ILogger<VCRedist2008Fix> _logger = logger;

    /// <inheritdoc/>
    public override string Id => "VCRedist2008Fix";

    /// <inheritdoc/>
    public override string Title => "Visual C++ 2008 Redistributable";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        if (IsProductInstalled(Vc2008ProductCode))
        {
            return Task.FromResult(true);
        }

        // Also check registry key existence generally
        var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\Installer\Products\D20352A90C039D93DBF6126ECE614057"); // Compressed GUID
        return Task.FromResult(key != null);
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "vcredist_2008_x86.exe");
        var details = new List<string>();

        try
        {
            details.Add("Downloading Visual C++ 2008 Redistributable...");

            using var client = httpClientFactory.CreateClient("Downloader");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            var urls = new[]
            {
                ExternalUrls.VCRedist2008DownloadUrlPrimary,
                ExternalUrls.VCRedist2008DownloadUrlMirror1,
            };
            bool downloaded = false;

            foreach (var url in urls)
            {
                try
                {
                    _logger.LogInformation("Attempting download from {Url}", url);
                    using var response = await client.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    using (var fs = new FileStream(tempFile, FileMode.Create))
                    {
                        await response.Content.CopyToAsync(fs, cancellationToken);
                    }

                    // Simple size validation check
                    if (new FileInfo(tempFile).Length < ActionSetConstants.Validation.VCRedistMinSize)
                    {
                        _logger.LogWarning("Downloaded file too small, likely corrupt.");
                        continue;
                    }

                    details.Add($"✓ Downloaded from {new Uri(url).Host}");
                    downloaded = true;
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to download from {Url}: {Error}", url, ex.Message);
                }
            }

            if (!downloaded)
            {
                return new ActionSetResult(false, "Failed to download VCRedist 2008 from all mirrors.", details);
            }

            details.Add("Installing Visual C++ 2008...");

            var psi = new ProcessStartInfo
            {
                FileName = tempFile,
                Arguments = "/q", // 2008 uses /q
                UseShellExecute = true,
                Verb = "runas",
            };

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start installer.");

            await process.WaitForExitAsync(cancellationToken);

            // 3010 = Reboot required
            if (process.ExitCode == 0 || process.ExitCode == 3010)
            {
                return new ActionSetResult(true, "Visual C++ 2008 installed successfully.", details);
            }
            else
            {
                return new ActionSetResult(false, $"Installer exited with code {process.ExitCode}", details);
            }
        }
        catch (Exception ex)
        {
            return new ActionSetResult(false, $"Error: {ex.Message}", details);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try
            {
                File.Delete(tempFile);
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
        return Task.FromResult(new ActionSetResult(true, "Uninstalling runtime not supported automatically. Use Control Panel."));
    }

    private static bool IsProductInstalled(string productCode)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{productCode}");
            return key != null;
        }
        catch
        {
            return false;
        }
    }
}
