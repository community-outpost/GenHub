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
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

/// <summary>
/// Abstract base class for Visual C++ Redistributable fixes.
/// Manages secure download, digital signature verification, silent execution, and cleanup.
/// </summary>
public abstract class BaseVCRedistFix : BaseActionSet
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseVCRedistFix"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory for downloading redistributables.</param>
    /// <param name="logger">Logger instance.</param>
    protected BaseVCRedistFix(
        IHttpClientFactory httpClientFactory,
        ILogger logger)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.CoreAndStability;

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <summary>
    /// Gets the list of download URLs for the redistributable installer.
    /// </summary>
    protected abstract IReadOnlyList<string> DownloadUrls { get; }

    /// <summary>
    /// Gets the arguments to pass to the installer for silent installation.
    /// </summary>
    protected abstract string InstallerArguments { get; }

    /// <summary>
    /// Gets the human-readable display name of the redistributable.
    /// </summary>
    protected abstract string RedistDisplayName { get; }

    /// <summary>
    /// Gets the temporary file prefix for downloads.
    /// </summary>
    protected abstract string TempFilePrefix { get; }

    /// <summary>
    /// Gets the minimum expected file size in bytes for the installer.
    /// </summary>
    protected virtual long MinimumFileSizeBytes => ActionSetConstants.Validation.MinimumAddonPackageSizeBytes;

    /// <summary>
    /// Gets the optional collection of pinned SHA-256 hashes.
    /// </summary>
    protected virtual IReadOnlyList<string>? AllowedSha256Hashes => null;

    /// <summary>
    /// Gets the expected Authenticode publisher substring.
    /// </summary>
    protected virtual string ExpectedPublisher => ActionSetConstants.Security.MicrosoftPublisher;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{TempFilePrefix}_{Guid.NewGuid():N}.exe");
        var details = new List<string>();
        FileStream? lockedStream = null;

        try
        {
            details.Add($"Downloading {RedistDisplayName}...");

            var downloaded = await DownloadInstallerAsync(tempFile, ct);
            if (!downloaded)
            {
                return new ActionSetResult(false, $"Failed to download {RedistDisplayName} from all available sources.", details);
            }

            var fileInfo = new FileInfo(tempFile);
            var fileSize = fileInfo.Length;

            var securityValidation = await DownloadSecurityValidator.ValidateAndLockFileAsync(
                tempFile,
                allowedSha256Hashes: AllowedSha256Hashes,
                expectedAuthenticodePublisher: ExpectedPublisher,
                allowExpiredCertificates: true,
                ct: ct);

            if (!securityValidation.Success || securityValidation.Data == null)
            {
                var errorSummary = string.Join("; ", securityValidation.Errors);
                _logger.LogWarning("Security validation failed for {Name}: {Error}", RedistDisplayName, errorSummary);
                DeleteTempFile(tempFile);
                return new ActionSetResult(false, $"Security validation failed: {errorSummary}", details);
            }

            lockedStream = securityValidation.Data;
            await lockedStream.DisposeAsync();
            lockedStream = null;

            details.Add($"✓ Downloaded and verified {fileSize / 1024.0 / 1024.0:F2} MB");
            details.Add($"Installing {RedistDisplayName} (silent mode)...");
            details.Add("  ⚠ This may require administrator privileges");
            _logger.LogInformation("Installing {Name}...", RedistDisplayName);

            var (success, exitCode, errorMsg) = await RunInstallerProcessAsync(tempFile, InstallerArguments, ct);
            if (success)
            {
                details.Add($"✓ {RedistDisplayName} installed successfully (exit code: {exitCode})");
                return new ActionSetResult(true, null, details);
            }

            details.Add($"✗ Installation failed with exit code: {exitCode}");
            return new ActionSetResult(false, errorMsg ?? $"Installation failed with exit code {exitCode}", details);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error installing {Name}", RedistDisplayName);
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
        finally
        {
            if (lockedStream != null)
            {
                await lockedStream.DisposeAsync();
            }

            DeleteTempFile(tempFile);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        return Task.FromResult(new ActionSetResult(
            true,
            null,
            [$"ℹ {RedistDisplayName} is a shared system component and does not need to be uninstalled."]));
    }

    private static async Task<(bool Success, int ExitCode, string? ErrorMessage)> RunInstallerProcessAsync(
        string installerPath,
        string arguments,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = arguments,
            UseShellExecute = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return (false, -1, "Failed to start installer process");
        }

        await process.WaitForExitAsync(ct);
        var exitCode = process.ExitCode;

        if (exitCode is ProcessConstants.ExitCodeSuccess or ProcessConstants.ExitCodeRebootRequired)
        {
            return (true, exitCode, null);
        }

        return (false, exitCode, $"Installer returned non-zero exit code: {exitCode}");
    }

    private async Task<bool> DownloadInstallerAsync(string tempFile, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient("Downloader");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        foreach (var url in DownloadUrls)
        {
            try
            {
                _logger.LogInformation("Attempting download from {Url}", url);
                using var response = await client.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();

                await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await response.Content.CopyToAsync(fs, ct);
                }

                var fileInfo = new FileInfo(tempFile);
                if (fileInfo.Length < MinimumFileSizeBytes)
                {
                    _logger.LogWarning("Downloaded file from {Url} is too small ({Size} bytes)", url, fileInfo.Length);
                    DeleteTempFile(tempFile);
                    continue;
                }

                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Download failed from {Url}", url);
            }
        }

        return false;
    }

    private void DeleteTempFile(string path)
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
            _logger.LogDebug(ex, "Failed to delete temporary redist installer {Path}", path);
        }
    }

    /// <summary>
    /// Checks whether an MSI product code is installed in either 32-bit or 64-bit registry views.
    /// </summary>
    /// <param name="productCode">The MSI product GUID.</param>
    /// <returns>True if installed; otherwise false.</returns>
    protected static bool IsProductInstalled(string productCode)
    {
        try
        {
            var uninstallKeyPath = RegistryConstants.UninstallKeyPath;
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var uninstallKey = baseKey.OpenSubKey(uninstallKeyPath);
            if (uninstallKey != null)
            {
                using var subKey = uninstallKey.OpenSubKey(productCode);
                if (subKey != null)
                {
                    return true;
                }
            }

            using var baseKey64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var uninstallKey64 = baseKey64.OpenSubKey(uninstallKeyPath);
            if (uninstallKey64 != null)
            {
                using var subKey64 = uninstallKey64.OpenSubKey(productCode);
                if (subKey64 != null)
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignored - fallback to other detection methods
        }

        return false;
    }
}
