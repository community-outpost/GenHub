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
/// Fix that checks for and installs Visual C++ 2008 Redistributable (x86).
/// Required for some legacy components and GenPatcher parity.
/// </summary>
public class VCRedist2008Fix(IHttpClientFactory httpClientFactory, ILogger<VCRedist2008Fix> logger)
    : BaseVCRedistFix(httpClientFactory, logger)
{
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
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    protected override IReadOnlyList<string> DownloadUrls =>
    [
        ExternalUrls.VCRedist2008DownloadUrlPrimary,
        ExternalUrls.VCRedist2008DownloadUrlMirror1,
    ];

    /// <inheritdoc/>
    protected override string InstallerArguments => "/q";

    /// <inheritdoc/>
    protected override string RedistDisplayName => "Visual C++ 2008 Redistributable";

    /// <inheritdoc/>
    protected override string TempFilePrefix => "vcredist_2008_x86";

    /// <inheritdoc/>
    protected override long MinimumFileSizeBytes => 1024 * 1024; // ~4.3 MB

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

        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\Installer\Products\D20352A90C039D93DBF6126ECE614057");
        return Task.FromResult(key != null);
    }
}
