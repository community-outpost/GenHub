using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Specialized deliverer for Generals Online content.
/// Downloads ZIP packages, extracts files, and creates variant manifests (60Hz).
/// </summary>
public class GeneralsOnlineDeliverer(
   IDownloadService downloadService,
   IContentManifestPool manifestPool,
   GeneralsOnlineManifestFactory manifestFactory,
   ILogger<GeneralsOnlineDeliverer> logger)
   : IContentDeliverer
{
    /// <inheritdoc />
    public string SourceName => GeneralsOnlineConstants.DelivererSourceName;

    /// <inheritdoc />
    public string Description => GeneralsOnlineConstants.DelivererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc />
    public bool CanDeliver(ContentManifest manifest)
    {
        // Can deliver if it's a Generals Online manifest with a portable ZIP URL
        var isPublisher = string.Equals(manifest.Publisher?.PublisherType, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrEmpty(manifest.Publisher?.PublisherType) && string.Equals(manifest.Publisher?.Name, GeneralsOnlineConstants.PublisherName, StringComparison.OrdinalIgnoreCase));
        return isPublisher &&
               manifest.Files.Any(f => f.DownloadUrl is { } url && url.EndsWith(GeneralsOnlineConstants.PortableExtension, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> DeliverContentAsync(
        ContentManifest packageManifest,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var newlyRegisteredManifests = new List<ContentManifest>();
        string? zipPath = null;
        string? extractPath = null;

        try
        {
            logger.LogInformation("Starting Generals Online content delivery for {Version}", packageManifest.Version);

            var downloadResult = await DownloadAndExtractPackageAsync(packageManifest, targetDirectory, progress, cancellationToken);
            if (!downloadResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure(downloadResult.FirstError ?? "Failed to download and extract package");
            }

            (zipPath, extractPath) = downloadResult.Data;

            // Step 3: Create variant manifests from extracted files
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Copying,
                ProgressPercentage = 60,
                CurrentOperation = "Generating variant manifests (60Hz, MapPack, and GameData Patch)",
            });

            var manifests = await manifestFactory.CreateManifestsFromExtractedContentAsync(
                packageManifest,
                extractPath,
                cancellationToken);

            if (manifests.Count == 0)
            {
                logger.LogError("No manifests could be created from extracted content");
                CleanupTempArtifacts(zipPath, extractPath, logger);
                return OperationResult<ContentManifest>.CreateFailure(
                    "Failed to create any variant manifests from extracted content");
            }

            // Step 4: Add all variant manifests to the ContentManifestPool
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Copying,
                ProgressPercentage = 80,
                CurrentOperation = "Registering all variant manifests to content library",
            });

            var registrationResult = await RegisterVariantManifestsAsync(manifests, extractPath, newlyRegisteredManifests, cancellationToken);
            if (!registrationResult.Success)
            {
                CleanupTempArtifacts(zipPath, extractPath, logger);
                return OperationResult<ContentManifest>.CreateFailure(registrationResult.FirstError ?? "Failed to register variant manifests");
            }

            MoveExtractedFilesToTarget(extractPath, cancellationToken);
            DeleteZipSafe(zipPath);

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Completed,
                ProgressPercentage = 100,
                CurrentOperation = "Generals Online content delivered successfully (all variants)",
            });

            var primaryManifest = manifests[0];
            logger.LogInformation(
                "Successfully delivered Generals Online content: {Count} manifests created, all registered to pool",
                manifests.Count);

            return OperationResult<ContentManifest>.CreateSuccess(primaryManifest);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Generals Online content delivery was canceled for {Version}", packageManifest.Version);
            if (newlyRegisteredManifests.Count > 0)
            {
                var rollbackErrors = await RollbackManifestsAsync(newlyRegisteredManifests);
                if (rollbackErrors.Count > 0)
                {
                    logger.LogWarning("Rollback warnings during cancellation cleanup: {Errors}", string.Join("; ", rollbackErrors));
                }
            }

            CleanupTempArtifacts(zipPath, extractPath, logger);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver Generals Online content for {Version}", packageManifest.Version);
            var rollbackErrors = new List<string>();
            if (newlyRegisteredManifests.Count > 0)
            {
                rollbackErrors = await RollbackManifestsAsync(newlyRegisteredManifests);
            }

            CleanupTempArtifacts(zipPath, extractPath, logger);

            var errorMessage = $"Content delivery failed: {ex.Message}";
            if (rollbackErrors.Count > 0)
            {
                errorMessage += $"; Rollback warnings: {string.Join("; ", rollbackErrors)}";
            }

            return OperationResult<ContentManifest>.CreateFailure(errorMessage);
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> ValidateContentAsync(
        ContentManifest manifest,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var hasZipFile = manifest.Files.Any(f =>
                !string.IsNullOrEmpty(f.DownloadUrl) &&
                f.DownloadUrl.EndsWith(GeneralsOnlineConstants.PortableExtension, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(OperationResult<bool>.CreateSuccess(hasZipFile));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Validation failed for Generals Online manifest {ManifestId}", manifest.Id);
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Validation failed: {ex.Message}"));
        }
    }

    private static void CleanupTempArtifacts(string? zipPath, string? extractPath, ILogger logger)
    {
        if (!string.IsNullOrEmpty(extractPath) && Directory.Exists(extractPath))
        {
            try
            {
                Directory.Delete(extractPath, recursive: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean up extracted directory {ExtractPath}", extractPath);
            }
        }

        if (!string.IsNullOrEmpty(zipPath) && File.Exists(zipPath))
        {
            try
            {
                File.Delete(zipPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean up ZIP file {ZipPath}", zipPath);
            }
        }
    }

    private async Task<OperationResult<(string ZipPath, string ExtractPath)>> DownloadAndExtractPackageAsync(
        ContentManifest packageManifest,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var zipFile = packageManifest.Files.FirstOrDefault(f => f.DownloadUrl is { } url && url.EndsWith(GeneralsOnlineConstants.PortableExtension, StringComparison.OrdinalIgnoreCase));
        if (zipFile == null)
        {
            return OperationResult<(string, string)>.CreateFailure("No ZIP file found in manifest");
        }

        var zipPath = Path.Combine(targetDirectory, $"GeneralsOnline_{Guid.NewGuid():N}.zip");
        progress?.Report(new ContentAcquisitionProgress
        {
            Phase = ContentAcquisitionPhase.Downloading,
            ProgressPercentage = 10,
            CurrentOperation = "Downloading Generals Online ZIP package",
            CurrentFile = zipFile.RelativePath,
        });

        logger.LogDebug("Downloading ZIP from {Url} to {Path}", zipFile.DownloadUrl, zipPath);
        var downloadResult = await downloadService.DownloadFileAsync(
            new Uri(zipFile.DownloadUrl!),
            zipPath,
            expectedHash: null,
            progress: null,
            cancellationToken);

        if (!downloadResult.Success)
        {
            CleanupTempArtifacts(zipPath, null, logger);
            return OperationResult<(string, string)>.CreateFailure($"Failed to download ZIP: {downloadResult.FirstError}");
        }

        var extractPath = Path.Combine(targetDirectory, $"extracted_{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractPath);

        progress?.Report(new ContentAcquisitionProgress
        {
            Phase = ContentAcquisitionPhase.Extracting,
            ProgressPercentage = 40,
            CurrentOperation = "Extracting Generals Online files",
        });

        logger.LogDebug("Extracting ZIP to {Path}", extractPath);
        ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);

        return OperationResult<(string, string)>.CreateSuccess((zipPath, extractPath));
    }

    private async Task<OperationResult> RegisterVariantManifestsAsync(
        IReadOnlyList<ContentManifest> manifests,
        string extractPath,
        List<ContentManifest> newlyRegisteredManifests,
        CancellationToken cancellationToken)
    {
        foreach (var manifest in manifests)
        {
            var checkResult = await manifestPool.IsManifestAcquiredAsync(manifest.Id, cancellationToken: cancellationToken);
            if (!checkResult.Success)
            {
                logger.LogError("Failed to check acquisition status for manifest {ManifestId}: {Error}", manifest.Id, checkResult.FirstError);
                var rollbackErrors = await RollbackManifestsAsync(newlyRegisteredManifests);
                newlyRegisteredManifests.Clear();

                var errorMessage = $"Failed to check manifest acquisition status for {manifest.Id}: {checkResult.FirstError}";
                if (rollbackErrors.Count > 0)
                {
                    errorMessage += $"; Rollback warnings: {string.Join("; ", rollbackErrors)}";
                }

                return OperationResult.CreateFailure(errorMessage);
            }

            if (checkResult.Data)
            {
                logger.LogInformation("Manifest {ManifestId} ({Name}) is already acquired in pool; skipping registration", manifest.Id, manifest.Name);
                continue;
            }

            var addResult = await manifestPool.AddManifestAsync(manifest, extractPath, cancellationToken: cancellationToken);
            if (!addResult.Success)
            {
                logger.LogError("Failed to register manifest {ManifestId} ({Name}): {Error}", manifest.Id, manifest.Name, addResult.FirstError);
                var rollbackErrors = await RollbackManifestsAsync(newlyRegisteredManifests);
                newlyRegisteredManifests.Clear();

                var errorMessage = $"Failed to register manifest {manifest.Id} ({manifest.Name}): {addResult.FirstError}";
                if (rollbackErrors.Count > 0)
                {
                    errorMessage += $"; Rollback warnings: {string.Join("; ", rollbackErrors)}";
                }

                return OperationResult.CreateFailure(errorMessage);
            }

            newlyRegisteredManifests.Add(manifest);
            logger.LogInformation("Successfully registered manifest: {ManifestId} ({Name})", manifest.Id, manifest.Name);
        }

        return OperationResult.CreateSuccess();
    }

    private void MoveExtractedFilesToTarget(string extractPath, CancellationToken cancellationToken = default)
    {
        var parentDir = Directory.GetParent(extractPath)?.FullName;
        if (parentDir == null)
        {
            return;
        }

        logger.LogInformation("Moving extracted files from {ExtractPath} to parent {ParentDir}", extractPath, parentDir);
        var movedFiles = new List<(string TargetPath, string? BackupPath)>();

        try
        {
            foreach (var file in Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(extractPath, file);
                var targetPath = Path.Combine(parentDir, relativePath);
                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                string? backupPath = null;
                if (File.Exists(targetPath))
                {
                    backupPath = targetPath + ".gh_bak_" + Guid.NewGuid().ToString("N");
                    File.Move(targetPath, backupPath);
                    movedFiles.Add((targetPath, backupPath));
                }

                File.Move(file, targetPath, overwrite: true);
                if (backupPath == null)
                {
                    movedFiles.Add((targetPath, null));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to move extracted files from {ExtractPath} to parent {ParentDir}; rolling back moved files", extractPath, parentDir);
            RollbackMovedFiles(movedFiles);
            throw;
        }

        CleanupBackupFiles(movedFiles);

        try
        {
            Directory.Delete(extractPath, recursive: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete extracted directory {ExtractPath}", extractPath);
        }
    }

    private void RollbackMovedFiles(List<(string TargetPath, string? BackupPath)> movedFiles)
    {
        foreach (var (targetPath, backupPath) in movedFiles)
        {
            try
            {
                if (backupPath != null)
                {
                    if (File.Exists(backupPath))
                    {
                        File.Move(backupPath, targetPath, overwrite: true);
                    }
                    else
                    {
                        logger.LogWarning("Backup file {BackupPath} not found during rollback; leaving {TargetPath} intact to prevent data loss", backupPath, targetPath);
                    }
                }
                else if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }
            }
            catch (Exception restoreEx)
            {
                logger.LogWarning(restoreEx, "Failed to restore target file {TargetPath} during rollback", targetPath);
            }
        }
    }

    private void CleanupBackupFiles(List<(string TargetPath, string? BackupPath)> movedFiles)
    {
        foreach (var (_, backupPath) in movedFiles)
        {
            if (backupPath != null && File.Exists(backupPath))
            {
                try
                {
                    File.Delete(backupPath);
                }
                catch (Exception delEx)
                {
                    logger.LogWarning(delEx, "Failed to delete backup file {BackupPath}", backupPath);
                }
            }
        }
    }

    private void DeleteZipSafe(string? zipPath)
    {
        if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
        {
            return;
        }

        try
        {
            File.Delete(zipPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete ZIP file {ZipPath}", zipPath);
        }
    }

    private async Task<List<string>> RollbackManifestsAsync(IReadOnlyList<ContentManifest> manifestsToRollback)
    {
        var rollbackErrors = new List<string>();
        foreach (var registeredManifest in manifestsToRollback)
        {
            try
            {
                var removeResult = await manifestPool.RemoveManifestAsync(registeredManifest.Id, cancellationToken: CancellationToken.None);
                if (!removeResult.Success)
                {
                    logger.LogWarning(
                        "Failed to rollback manifest {ManifestId} during delivery failure cleanup: {Error}",
                        registeredManifest.Id,
                        removeResult.FirstError);
                    rollbackErrors.Add($"Rollback of manifest {registeredManifest.Id} failed: {removeResult.FirstError}");
                }
            }
            catch (Exception rollbackEx)
            {
                logger.LogWarning(
                    rollbackEx,
                    "Failed to rollback manifest {ManifestId} during delivery failure cleanup",
                    registeredManifest.Id);
                rollbackErrors.Add($"Rollback exception for manifest {registeredManifest.Id}: {rollbackEx.Message}");
            }
        }

        return rollbackErrors;
    }
}
