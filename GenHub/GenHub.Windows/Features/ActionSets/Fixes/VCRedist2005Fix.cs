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
using GenHub.Core.Helpers;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

/// <summary>
/// Fix that checks for and installs Visual C++ 2005 Redistributable (x86).
/// Required for some legacy components and GenPatcher parity.
/// </summary>
public class VCRedist2005Fix(IHttpClientFactory httpClientFactory, ILogger<VCRedist2005Fix> logger) : BaseActionSet(logger)
{
    // Product Code for VC++ 2005 SP1 Redistributable (x86)
    // Common code: {7299052b-02a4-4627-81f2-1818da5d550d}
    private const string Vc2005ProductCode = "{7299052b-02a4-4627-81f2-1818da5d550d}";

    /// <inheritdoc/>
    public override string Id => "VCRedist2005Fix";

    /// <inheritdoc/>
    public override string Title => "Visual C++ 2005 Redistributable";

    /// <inheritdoc/>
    public override string Description => "Installs the Microsoft Visual C++ 2005 (x86) runtime to prevent side-by-side configuration and missing DLL errors.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Several legacy mod tools, video decoding plugins, and game utilities require the 32-bit Visual C++ 2005 runtime. This fix downloads and silently installs the official Microsoft runtime package, resolving side-by-side configuration startup crashes.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.CoreAndStability;

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        if (IsProductInstalled(Vc2005ProductCode))
        {
            return Task.FromResult(true);
        }

        try
        {
            using var key1 = Registry.LocalMachine.OpenSubKey(RegistryConstants.VCRedist2005InstallerProductsKey);
            if (key1 != null)
            {
                return Task.FromResult(true);
            }

            using var key2 = Registry.LocalMachine.OpenSubKey(RegistryConstants.VCRedist2005InstallerProductsKeyWow64);
            if (key2 != null)
            {
                return Task.FromResult(true);
            }

            using var key3 = Registry.LocalMachine.OpenSubKey(RegistryConstants.VCRedist2005ClassesKey);
            if (key3 != null)
            {
                return Task.FromResult(true);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to inspect VC++ 2005 redistributable registry subkey");
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"vcredist_2005_x86_{Guid.NewGuid():N}.exe");
        var details = new List<string>();
        FileStream? lockedStream = null;

        try
        {
            details.Add("Downloading Visual C++ 2005 Redistributable...");

            using var client = httpClientFactory.CreateClient("Downloader");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            var urls = new[] { ExternalUrls.VCRedist2005DownloadUrlPrimary, ExternalUrls.VCRedist2005DownloadUrlMirror1 };
            bool downloaded = false;

            foreach (var url in urls)
            {
                try
                {
                    logger.LogInformation("Attempting download from {Url}", url);
                    using var response = await client.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        await response.Content.CopyToAsync(fs, cancellationToken);
                    }

                    // Size validation check
                    if (new FileInfo(tempFile).Length < ActionSetConstants.Validation.VCRedistMinSize)
                    {
                        logger.LogWarning("Downloaded file too small, likely corrupt.");
                        if (File.Exists(tempFile))
                        {
                            File.Delete(tempFile);
                        }

                        continue;
                    }

                    // Security signature validation (Authenticode publisher verification) and lock file immutable
                    var securityValidation = await DownloadSecurityValidator.ValidateAndLockFileAsync(
                        tempFile,
                        expectedAuthenticodePublisher: ActionSetConstants.Security.MicrosoftPublisher,
                        ct: cancellationToken);

                    if (!securityValidation.Success || securityValidation.Data == null)
                    {
                        var errorSummary = string.Join("; ", securityValidation.Errors);
                        logger.LogWarning("Security validation failed for download from {Url}: {Error}", url, errorSummary);
                        if (File.Exists(tempFile))
                        {
                            try
                            {
                                File.SetAttributes(tempFile, FileAttributes.Normal);
                                File.Delete(tempFile);
                            }
                            catch (Exception)
                            {
                                // Ignore cleanup failure
                            }
                        }

                        continue;
                    }

                    lockedStream = securityValidation.Data;
                    details.Add($"✓ Downloaded and verified from {new Uri(url).Host}");
                    downloaded = true;
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Failed to download from {Url}: {Error}", url, ex.Message);
                    if (File.Exists(tempFile))
                    {
                        try
                        {
                            File.SetAttributes(tempFile, FileAttributes.Normal);
                            File.Delete(tempFile);
                        }
                        catch (Exception)
                        {
                            // Ignore cleanup failure
                        }
                    }
                }
            }

            if (!downloaded || lockedStream == null)
            {
                return new ActionSetResult(false, "Failed to download and verify VCRedist 2005 from all mirrors.", details);
            }

            details.Add("Installing Visual C++ 2005...");

            var psi = new ProcessStartInfo
            {
                FileName = tempFile,
                Arguments = "/Q", // Quiet install
                UseShellExecute = true,
                Verb = "runas",
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return new ActionSetResult(false, "Failed to start Visual C++ 2005 installer process.", details);
            }

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == ProcessConstants.ExitCodeSuccess || process.ExitCode == ProcessConstants.ExitCodeRebootRequired)
            {
                details.Add("✓ Visual C++ 2005 installed successfully.");
                return new ActionSetResult(true, null, details);
            }

            return new ActionSetResult(false, $"Installer exited with code {process.ExitCode}", details);
        }
        catch (Exception ex)
        {
            return new ActionSetResult(false, $"Error: {ex.Message}", details);
        }
        finally
        {
            if (lockedStream != null)
            {
                await lockedStream.DisposeAsync();
            }

            try
            {
                if (File.Exists(tempFile))
                {
                    File.SetAttributes(tempFile, FileAttributes.Normal);
                    File.Delete(tempFile);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to delete temp file {TempFile}", tempFile);
            }
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ActionSetResult(true, null, ["Uninstalling runtime not supported automatically. Use Control Panel."]));
    }

    private static bool IsProductInstalled(string productCode)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{productCode}");
            if (key != null) return true;

            using var wowKey = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{productCode}");
            return wowKey != null;
        }
        catch
        {
            return false;
        }
    }
}
