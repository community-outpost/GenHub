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
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;

/// <summary>
/// Fix that downloads and installs high-definition icons for Generals and Zero Hour.
/// Replaces legacy 32x32 Windows XP icons with 256x256 HD icon assets.
/// </summary>
public class HDIconsFix(
    IHttpClientFactory httpClientFactory,
    ILogger<HDIconsFix> logger,
    string? markerPath = null)
    : BasePackageDeploymentFix(httpClientFactory, logger, "HDIconsFix.done", markerPath)
{
    private static readonly IReadOnlyList<string> RecognizedGeneralsIconFiles =
    [
        "GeneralsHD.ico",
        "generals_hd.ico",
        "game_hd.ico",
    ];

    private static readonly IReadOnlyList<string> RecognizedZeroHourIconFiles =
    [
        "GeneralsZHHD.ico",
        "zh_hd.ico",
    ];

    /// <inheritdoc/>
    public override string Id => "HDIconsFix";

    /// <inheritdoc/>
    public override string Title => "HD Icons (Addon)";

    /// <inheritdoc/>
    public override string Description => "Installs high-definition 256x256 icon assets for game shortcuts (also managed in Downloads).";

    /// <inheritdoc/>
    public override string DetailedDescription => "Replaces low-resolution 32x32 icons with 256x256 icon files for desktop shortcuts and taskbar windows. This addon downloads icon.dat from Community Outpost and extracts HD icons directly into your game directories. You can also download and manage this addon from the Downloads section.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.QualityOfLife;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    protected override IReadOnlyList<string> DownloadUrls => [ExternalUrls.HDIconsDownloadUrlPrimary];

    /// <inheritdoc/>
    protected override string ExpectedSha256 => ActionSetConstants.Security.HDIconsSha256;

    /// <inheritdoc/>
    protected override string PackageDisplayName => "High-Definition Icons";

    /// <inheritdoc/>
    protected override string TempFilePrefix => "hd_icons";

    /// <summary>
    /// Validates that the downloaded HD icons archive contains the expected icon assets for targeted installations.
    /// </summary>
    /// <param name="archiveFileNames">The set of file names in the archive.</param>
    /// <param name="installation">The targeted game installation.</param>
    /// <returns>A validation result indicating validity and any issues found.</returns>
    internal static ValidationResult ValidateArchiveContents(
        IReadOnlySet<string> archiveFileNames,
        GameInstallation installation)
    {
        var issues = new List<ValidationIssue>();

        if (archiveFileNames.Count == 0)
        {
            issues.Add(new ValidationIssue { Message = "HD icons archive contains no valid files.", Severity = ValidationSeverity.Error });
            return new ValidationResult("HDIconsPackage", issues);
        }

        if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath) && RecognizedGeneralsIconFiles.All(f => !archiveFileNames.Contains(f)))
        {
            issues.Add(new ValidationIssue { Message = "HD icons package does not contain a recognized icon for Generals.", Severity = ValidationSeverity.Error });
        }

        if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath) && RecognizedZeroHourIconFiles.All(f => !archiveFileNames.Contains(f)))
        {
            issues.Add(new ValidationIssue { Message = "HD icons package does not contain a recognized icon for Zero Hour.", Severity = ValidationSeverity.Error });
        }

        return new ValidationResult("HDIconsPackage", issues);
    }

    /// <inheritdoc/>
    protected override async Task<(int ExtractedCount, List<string>? DeployedFiles)> ExtractAndDeployAssetsAsync(
        string archivePath,
        string tempExtractDir,
        string tempBackupDir,
        GameInstallation installation,
        List<(string DestPath, bool ExistedBefore, string? BackupPath)> backupEntries,
        List<string> deployedFiles,
        List<string> details,
        CancellationToken ct)
    {
        using var archive = ArchiveFactory.OpenArchive(new FileInfo(archivePath));
        var archiveFileNames = archive.Entries
            .Where(e => !e.IsDirectory && e.Key != null)
            .Select(e => Path.GetFileName(e.Key))
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var archiveValidation = ValidateArchiveContents(archiveFileNames, installation);
        if (!archiveValidation.IsValid)
        {
            var errorMessage = archiveValidation.FirstError ?? "HD icons package validation failed.";
            logger.LogWarning("{Error}", errorMessage);
            return (0, null);
        }

        int extractedCount = 0;

        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory && e.Key != null))
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(entry.Key);
            if (string.IsNullOrEmpty(fileName))
            {
                continue;
            }

            var extractedFilePath = Path.Combine(tempExtractDir, fileName);
            using (var entryStream = entry.OpenEntryStream())
            await using (var fs = new FileStream(extractedFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await entryStream.CopyToAsync(fs, ct);
            }

            extractedCount++;

            if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath) &&
                RecognizedGeneralsIconFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            {
                var generalsDest = Path.Combine(installation.GeneralsPath, fileName);
                DeployFileWithBackup(extractedFilePath, generalsDest, tempBackupDir, deployedFiles, backupEntries);
            }

            if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath) &&
                RecognizedZeroHourIconFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            {
                var zhDest = Path.Combine(installation.ZeroHourPath, fileName);
                DeployFileWithBackup(extractedFilePath, zhDest, tempBackupDir, deployedFiles, backupEntries);
            }
        }

        return (extractedCount, deployedFiles);
    }

    /// <inheritdoc/>
    protected override bool AreAssetsPresent(GameInstallation installation)
    {
        try
        {
            var hasAnyTarget = false;

            if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
            {
                hasAnyTarget = true;
                if (RecognizedGeneralsIconFiles.All(iconFile => !File.Exists(Path.Combine(installation.GeneralsPath, iconFile))))
                {
                    return false;
                }
            }

            if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
            {
                hasAnyTarget = true;
                if (RecognizedZeroHourIconFiles.All(iconFile => !File.Exists(Path.Combine(installation.ZeroHourPath, iconFile))))
                {
                    return false;
                }
            }

            return hasAnyTarget;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking for HD icons");
            return false;
        }
    }

    /// <inheritdoc/>
    protected override List<string> GetLegacyFilePaths(GameInstallation installation)
    {
        var legacyFiles = new List<string>();
        if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
        {
            foreach (var icon in RecognizedGeneralsIconFiles)
            {
                var path = Path.Combine(installation.GeneralsPath, icon);
                if (File.Exists(path) && !legacyFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    legacyFiles.Add(path);
                }
            }
        }

        if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
        {
            foreach (var icon in RecognizedZeroHourIconFiles)
            {
                var path = Path.Combine(installation.ZeroHourPath, icon);
                if (File.Exists(path) && !legacyFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    legacyFiles.Add(path);
                }
            }
        }

        return legacyFiles;
    }
}
