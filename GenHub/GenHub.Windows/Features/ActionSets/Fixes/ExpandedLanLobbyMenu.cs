namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;

/// <summary>
/// Downloads and installs custom widescreen window definitions and the expanded LAN lobby menu addon.
/// </summary>
public class ExpandedLanLobbyMenu(
    IHttpClientFactory httpClientFactory,
    ILogger<ExpandedLanLobbyMenu> logger,
    string? markerPath = null)
    : BasePackageDeploymentFix(httpClientFactory, logger, "ExpandedLANLobbyMenu.done", markerPath)
{
    private static readonly IReadOnlyList<string> KnownMenuBigFiles =
    [
        "400_ControlBarHDBaseZH.big",
        "400_ControlBarHDBaseCCG.big",
        "!ExpandedLANMenu.big",
        "CustomWindows.big",
    ];

    /// <inheritdoc/>
    public override string Id => "ExpandedLANLobbyMenu";

    /// <inheritdoc/>
    public override string Title => "Expanded LAN Lobby Menu (Addon)";

    /// <inheritdoc/>
    public override string Description => "Downloads and installs custom widescreen UI definitions and the expanded LAN lobby menu addon.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Replaces the legacy 4-row LAN lobby interface and cramped window definitions with a widescreen-adapted layout. This addon downloads the official widescreen window assets and installs them into your game folder. You can also download and manage this addon from the Downloads section.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.QualityOfLife;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    protected override IReadOnlyList<string> DownloadUrls =>
    [
        ExternalUrls.ExpandedLANLobbyDownloadUrlPrimary,
        ExternalUrls.ExpandedLANLobbyDownloadUrlMirror1,
    ];

    /// <inheritdoc/>
    protected override string ExpectedSha256 => ActionSetConstants.Security.ExpandedLANLobbySha256;

    /// <inheritdoc/>
    protected override string PackageDisplayName => "Expanded LAN Lobby & Custom Windows";

    /// <inheritdoc/>
    protected override string TempFilePrefix => "cbbs";

    /// <inheritdoc/>
    protected override async Task<(int ExtractedCount, List<string>? DeployedFiles)> ExtractAndDeployAssetsAsync(
        string archivePath,
        DeploymentContext context,
        GameInstallation installation,
        CancellationToken ct)
    {
        using var archive = ArchiveFactory.OpenArchive(new FileInfo(archivePath));
        var extractedFiles = await ExtractArchiveEntriesAsync(archive, context.TempExtractDir, ct);

        foreach (var (fileName, extractedFilePath) in extractedFiles)
        {
            DeployEntryToInstallations(installation, fileName, extractedFilePath, context);
        }

        return (extractedFiles.Count, context.DeployedFiles);
    }

    /// <inheritdoc/>
    protected override bool AreAssetsPresent(GameInstallation installation)
    {
        try
        {
            if (installation.HasZeroHour &&
                !string.IsNullOrEmpty(installation.ZeroHourPath) &&
                KnownMenuBigFiles.Any(f => File.Exists(Path.Combine(installation.ZeroHourPath, f))))
            {
                return true;
            }

            return installation.HasGenerals &&
                   !string.IsNullOrEmpty(installation.GeneralsPath) &&
                   KnownMenuBigFiles.Any(f => File.Exists(Path.Combine(installation.GeneralsPath, f)));
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "I/O error checking LAN lobby menu status");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Permission denied checking LAN lobby menu status");
            return false;
        }
    }

    /// <inheritdoc/>
    protected override List<string> GetLegacyFilePaths(GameInstallation installation)
    {
        var legacyFiles = new List<string>();
        CollectExistingFiles(installation.ZeroHourPath, KnownMenuBigFiles, legacyFiles);
        CollectExistingFiles(installation.GeneralsPath, KnownMenuBigFiles, legacyFiles);
        return legacyFiles;
    }

    private static void DeployEntryToInstallations(
        GameInstallation installation,
        string fileName,
        string sourceFilePath,
        DeploymentContext context)
    {
        if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
        {
            var zhDest = Path.Combine(installation.ZeroHourPath, fileName);
            DeployFileWithBackup(sourceFilePath, zhDest, context);
        }

        if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath) &&
            !string.Equals(installation.GeneralsPath, installation.ZeroHourPath, StringComparison.OrdinalIgnoreCase))
        {
            var generalsDest = Path.Combine(installation.GeneralsPath, fileName);
            DeployFileWithBackup(sourceFilePath, generalsDest, context);
        }
    }
}
