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
/// Fix that checks for and installs Visual C++ 2008 Redistributable (x86).
/// Required for some legacy components and GenPatcher parity.
/// </summary>
public class VCRedist2008Fix(IHttpClientFactory httpClientFactory, ILogger<VCRedist2008Fix> logger) : BaseActionSet(logger)
{
    // Product Code for VC++ 2008 SP1 Redistributable (x86)
    private const string Vc2008ProductCode = "{9A25302D-30C0-39D9-BD6F-21E6EC160475}";

    /// <inheritdoc/>
    public override string Id => "VCRedist2008Fix";

    /// <inheritdoc/>
    public override string Title => "Visual C++ 2008 Runtime";

    /// <inheritdoc/>
    public override string Description => "Installs the Microsoft Visual C++ 2008 x86 system runtime package (also managed in Downloads).";

    /// <inheritdoc/>
    public override string DetailedDescription => "Community tools, map editors, and mod patchers require the 32-bit Visual C++ 2008 runtime libraries (msvcr90.dll). This package downloads and installs the official Microsoft runtime to ensure community utilities start properly. You can also download and manage this package from the Downloads section.";

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
        if (IsProductInstalled(Vc2008ProductCode))
        {
            return Task.FromResult(true);
        }

        // Also check registry key existence generally
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\Installer\Products\D20352A90C039D93DBF6126ECE614057"); // Compressed GUID
        return Task.FromResult(key != null);
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"vcredist_2008_x86_{Guid.NewGuid():N}.exe");
        var details = new List<string>();
        FileStream? lockedStream = null;

        try
        {
            details.Add("Downloading Visual C++ 2008 Redistributable...");

            using var client = httpClientFactory.CreateClient("Downloader");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            IReadOnlyList<string> urls =
            [
                ExternalUrls.VCRedist2008DownloadUrlPrimary,
                ExternalUrls.VCRedist2008DownloadUrlMirror1,
            ];
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
                            catch (IOException)
                            {
                                // Ignore cleanup failure
                            }
                            catch (UnauthorizedAccessException)
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
                        catch (IOException)
                        {
                            // Ignore cleanup failure
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Ignore cleanup failure
                        }
                    }
                }
            }

            if (!downloaded || lockedStream == null)
            {
                return new ActionSetResult(false, "Failed to download and verify VCRedist 2008 from all mirrors.", details);
            }

            details.Add("Installing Visual C++ 2008...");

            var psi = new ProcessStartInfo
            {
                FileName = tempFile,
                Arguments = "/q", // 2008 uses /q
                UseShellExecute = true,
                Verb = "runas",
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return new ActionSetResult(false, "Failed to start Visual C++ 2008 installer process.", details);
            }

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == ProcessConstants.ExitCodeSuccess || process.ExitCode == ProcessConstants.ExitCodeRebootRequired)
            {
                details.Add("✓ Visual C++ 2008 installed successfully.");
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
            catch (IOException ex)
            {
                logger.LogDebug(ex, "Failed to delete temp file {TempFile}", tempFile);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogDebug(ex, "Access denied deleting temp file {TempFile}", tempFile);
            }
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ActionSetResult(false, "Visual C++ 2008 Redistributable is a system runtime package and cannot be uninstalled automatically.", ["To uninstall, use Windows Settings > Installed Apps / Programs and Features."]));
    }

    private static bool IsProductInstalled(string productCode)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{productCode}");
            return key != null;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
