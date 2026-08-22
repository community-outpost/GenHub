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
/// Installs the Visual C++ 2010 Redistributable (x86) which is required for Generals/Zero Hour.
/// </summary>
public class VCRedist2010Fix(IHttpClientFactory httpClientFactory, ILogger<VCRedist2010Fix> logger)
    : BaseVCRedistFix(httpClientFactory, logger)
{
    /// <inheritdoc/>
    public override string Id => "VCRedist2010";

    /// <inheritdoc/>
    public override string Title => "Visual C++ 2010 Runtime";

    /// <inheritdoc/>
    public override string Description => "Installs the Microsoft Visual C++ 2010 x86 system runtime package (also managed in Downloads).";

    /// <inheritdoc/>
    public override string DetailedDescription => "GenTool, widescreen hooks, and community tools depend on the 32-bit Visual C++ 2010 runtime libraries (msvcr100.dll). This package downloads and installs the official Microsoft runtime to prevent missing DLL errors. You can also download and manage this package from the Downloads section.";

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    protected override IReadOnlyList<string> DownloadUrls => [ExternalUrls.VCRedist2010DownloadUrl];

    /// <inheritdoc/>
    protected override string InstallerArguments => "/quiet /norestart";

    /// <inheritdoc/>
    protected override string RedistDisplayName => "Visual C++ 2010 Redistributable";

    /// <inheritdoc/>
    protected override string TempFilePrefix => "vcredist_x86_2010";

    /// <inheritdoc/>
    protected override long MinimumFileSizeBytes => 1024 * 1024; // ~4.8 MB

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryConstants.VCRedist2010x86Key);
            if (key != null)
            {
                var val = key.GetValue(RegistryConstants.InstalledValueName);
                if (val != null && (int)val == 1)
                {
                    return Task.FromResult(true);
                }
            }

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
            logger.LogDebug(ex, "Failed to check VCRedist 2010 registry status");
            return Task.FromResult(false);
        }
    }
}
