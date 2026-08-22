namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

/// <summary>
/// Fix that checks for and installs Visual C++ 2005 Redistributable (x86).
/// Required for some legacy components and GenPatcher parity.
/// </summary>
public class VCRedist2005Fix(IHttpClientFactory httpClientFactory, ILogger<VCRedist2005Fix> logger)
    : BaseVCRedistFix(httpClientFactory, logger)
{
    private const string Vc2005ProductCode = "{7299052b-02a4-4627-81f2-1818da5d550d}";

    /// <inheritdoc/>
    public override string Id => "VCRedist2005Fix";

    /// <inheritdoc/>
    public override string Title => "Visual C++ 2005 Runtime";

    /// <inheritdoc/>
    public override string Description => "Installs the Microsoft Visual C++ 2005 x86 system runtime package (also managed in Downloads).";

    /// <inheritdoc/>
    public override string DetailedDescription => "Several legacy game tools and community plugins require the 32-bit Visual C++ 2005 runtime libraries (msvcr80.dll). This package downloads and installs the official Microsoft runtime to prevent missing DLL startup errors. You can also download and manage this package from the Downloads section.";

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    protected override IReadOnlyList<string> DownloadUrls =>
    [
        ExternalUrls.VCRedist2005DownloadUrlPrimary,
        ExternalUrls.VCRedist2005DownloadUrlMirror1,
    ];

    /// <inheritdoc/>
    protected override string InstallerArguments => "/q";

    /// <inheritdoc/>
    protected override string RedistDisplayName => "Visual C++ 2005 Redistributable";

    /// <inheritdoc/>
    protected override string TempFilePrefix => "vcredist_2005_x86";

    /// <inheritdoc/>
    protected override long MinimumFileSizeBytes => 1024 * 1024; // ~2.6 MB

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

    private static bool IsProductInstalled(string productCode)
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
