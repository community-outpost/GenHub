using System;
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
/// Downloads ZIP packages, extracts files, and creates dual variant manifests (30Hz/60Hz).
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
        return manifest.Publisher?.Name?.Equals(GeneralsOnlineConstants.PublisherName, StringComparison.OrdinalIgnoreCase) == true &&
               manifest.Files.Any(f => f.DownloadUrl?.EndsWith(GeneralsOnlineConstants.PortableExtension, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> DeliverContentAsync(
        ContentManifest packageManifest,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Starting Generals Online content delivery for {Version}", packageManifest.Version);

            // Step 1: Download ZIP file
            var zipFile = packageManifest.Files.FirstOrDefault(f => f.DownloadUrl?.EndsWith(GeneralsOnlineConstants.PortableExtension, StringComparison.OrdinalIgnoreCase) == true);
            if (zipFile == null)
            {
                return OperationResult<ContentManifest>.CreateFailure("No ZIP file found in manifest");
            }

            var zipPath = Path.Combine(targetDirectory, "GeneralsOnline.zip");

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
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Failed to download ZIP: {downloadResult.FirstError}");
            }

            // Step 2: Extract ZIP
            var extractPath = Path.Combine(targetDirectory, "extracted");
            Directory.CreateDirectory(extractPath);

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Extracting,
                ProgressPercentage = 40,
                CurrentOperation = "Extracting Generals Online files",
            });

            logger.LogDebug("Extracting ZIP to {Path}", extractPath);
            ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);

            // Step 3: Create all variant manifests using the factory (30Hz, 60Hz GameClients + QuickMatch MapPack)
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Copying,
                ProgressPercentage = 50,
                CurrentOperation = "Creating manifests for all variants",
            });

            logger.LogInformation("Creating manifests for GeneralsOnline variants (30Hz, 60Hz, QuickMatch MapPack)");
            var manifests = await manifestFactory.CreateManifestsFromExtractedContentAsync(
                packageManifest,
                extractPath,
                cancellationToken);

            if (manifests.Count != 3)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Expected 3 manifests (30Hz, 60Hz, MapPack) but got {manifests.Count}");
            }

            // Step 4: Register all variant manifests to CAS
            // This ensures all files (including both executables and map pack) are stored before validation
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Copying,
                ProgressPercentage = 90,
                CurrentOperation = "Registering all variant manifests to content library",
            });

            logger.LogInformation("Registering all variant manifests (30Hz, 60Hz, QuickMatch MapPack) in pool");

            // Register 30Hz manifest first
            var hz30Manifest = manifests[0];
            var add30Result = await manifestPool.AddManifestAsync(hz30Manifest, extractPath, cancellationToken);
            if (!add30Result.Success)
            {
                logger.LogWarning(
                    "Failed to register 30Hz manifest {ManifestId}: {Error}",
                    hz30Manifest.Id,
                    add30Result.FirstError);
            }
            else
            {
                logger.LogInformation("Successfully registered 30Hz manifest: {ManifestId}", hz30Manifest.Id);
            }

            // Register 60Hz manifest
            var hz60Manifest = manifests[1];
            var add60Result = await manifestPool.AddManifestAsync(hz60Manifest, extractPath, cancellationToken);
            if (!add60Result.Success)
            {
                logger.LogWarning(
                    "Failed to register 60Hz manifest {ManifestId}: {Error}",
                    hz60Manifest.Id,
                    add60Result.FirstError);
            }
            else
            {
                logger.LogInformation("Successfully registered 60Hz manifest: {ManifestId}", hz60Manifest.Id);
            }

            // Register QuickMatch MapPack manifest
            var mapPackManifest = manifests[2];
            var addMapPackResult = await manifestPool.AddManifestAsync(mapPackManifest, extractPath, cancellationToken);
            if (!addMapPackResult.Success)
            {
                logger.LogWarning(
                    "Failed to register QuickMatch MapPack manifest {ManifestId}: {Error}",
                    mapPackManifest.Id,
                    addMapPackResult.FirstError);
            }
            else
            {
                logger.LogInformation("Successfully registered QuickMatch MapPack manifest: {ManifestId}", mapPackManifest.Id);
            }

            var parentDir = Directory.GetParent(extractPath)?.FullName;
            if (parentDir != null)
            {
                logger.LogInformation("Moving extracted files from {ExtractPath} to parent {ParentDir}", extractPath, parentDir);
                foreach (var file in Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(extractPath, file);
                    var targetPath = Path.Combine(parentDir, relativePath);
                    var targetDir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    File.Move(file, targetPath, overwrite: true);
                }

                try
                {
                    Directory.Delete(extractPath, recursive: true);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete extracted directory {ExtractPath}", extractPath);
                }
            }

            try
            {
                File.Delete(zipPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete ZIP file {ZipPath}", zipPath);
            }

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Completed,
                ProgressPercentage = 100,
                CurrentOperation = "Generals Online content delivered successfully (all variants)",
            });

            var primaryManifest = manifests[0];
            logger.LogInformation(
                "Successfully delivered Generals Online content: 3 manifests created, all registered to pool");

            return OperationResult<ContentManifest>.CreateSuccess(primaryManifest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver Generals Online content");
            return OperationResult<ContentManifest>.CreateFailure($"Content delivery failed: {ex.Message}");
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
}
