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

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Specialized deliverer for Community Outpost content.
/// Downloads ZIP packages, extracts files, and creates manifests via factory.
/// </summary>
public class CommunityOutpostDeliverer(
   IDownloadService downloadService,
   IContentManifestPool manifestPool,
   CommunityOutpostManifestFactory manifestFactory,
   ILogger<CommunityOutpostDeliverer> logger)
   : IContentDeliverer
{
    /// <inheritdoc />
    public string SourceName => CommunityOutpostConstants.PublisherId;

    /// <inheritdoc />
    public string Description => CommunityOutpostConstants.DelivererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc />
    public bool CanDeliver(ContentManifest manifest)
    {
        // Can deliver if it's a Community Outpost manifest with a ZIP download URL
        return manifest.Publisher?.PublisherType?.Equals(
                   CommunityOutpostConstants.PublisherId,
                   StringComparison.OrdinalIgnoreCase) == true &&
               manifest.Files.Any(f =>
                   f.DownloadUrl?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
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
            logger.LogInformation(
                "Starting Community Outpost content delivery for {Version}",
                packageManifest.Version);

            // Step 1: Download ZIP file
            var zipFile = packageManifest.Files.FirstOrDefault(f =>
                f.DownloadUrl?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);

            if (zipFile == null)
            {
                return OperationResult<ContentManifest>.CreateFailure("No ZIP file found in manifest");
            }

            var zipPath = Path.Combine(targetDirectory, "community-patch.zip");

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Downloading,
                ProgressPercentage = 10,
                CurrentOperation = "Downloading Community Outpost ZIP package",
                CurrentFile = zipFile.RelativePath,
            });

            logger.LogDebug("Downloading ZIP from {Url} to {Path}", zipFile.DownloadUrl, zipPath);
            var downloadResult = await downloadService.DownloadFileAsync(
                zipFile.DownloadUrl!,
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
                CurrentOperation = "Extracting Community Outpost files",
            });

            logger.LogDebug("Extracting ZIP to {Path}", extractPath);
            ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);

            // Step 3: Create manifests using the factory
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Copying,
                ProgressPercentage = 50,
                CurrentOperation = "Creating manifests from extracted content",
            });

            logger.LogInformation("Creating manifests for Community Outpost content");
            var manifests = await manifestFactory.CreateManifestsFromExtractedContentAsync(
                packageManifest,
                extractPath,
                cancellationToken);

            if (manifests.Count == 0)
            {
                // If no specialized manifests were created, create a single manifest from all files
                logger.LogWarning(
                    "No specialized content detected, creating generic patch manifest");

                manifests = await CreateGenericPatchManifestAsync(
                    packageManifest,
                    extractPath,
                    cancellationToken);
            }

            // Step 4: Register manifests to CAS
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Copying,
                ProgressPercentage = 70,
                CurrentOperation = "Registering manifests to content library",
            });

            logger.LogInformation(
                "Registering {Count} manifest(s) to pool",
                manifests.Count);

            foreach (var manifest in manifests)
            {
                var addResult = await manifestPool.AddManifestAsync(
                    manifest,
                    extractPath,
                    cancellationToken);

                if (!addResult.Success)
                {
                    logger.LogWarning(
                        "Failed to register manifest {ManifestId}: {Error}",
                        manifest.Id,
                        addResult.FirstError);
                }
                else
                {
                    // After successful storage, update SourceType to ContentAddressable
                    // since the files are now in CAS
                    foreach (var file in manifest.Files)
                    {
                        file.SourceType = ContentSourceType.ContentAddressable;
                    }

                    logger.LogInformation(
                        "Successfully registered manifest: {ManifestId}",
                        manifest.Id);
                }
            }

            // Step 5: Move extracted files to target directory (optional cleanup)
            var parentDir = Directory.GetParent(extractPath)?.FullName;
            if (parentDir != null)
            {
                logger.LogDebug(
                    "Moving extracted files from {ExtractPath} to parent {ParentDir}",
                    extractPath,
                    parentDir);

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
                    logger.LogWarning(
                        ex,
                        "Failed to delete extracted directory {ExtractPath}",
                        extractPath);
                }
            }

            // Cleanup ZIP file
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
                CurrentOperation = "Community Outpost content delivered successfully",
            });

            var primaryManifest = manifests.FirstOrDefault() ?? packageManifest;
            logger.LogInformation(
                "Successfully delivered Community Outpost content: {ManifestCount} manifest(s) created",
                manifests.Count);

            return OperationResult<ContentManifest>.CreateSuccess(primaryManifest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver Community Outpost content");
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
                f.DownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(OperationResult<bool>.CreateSuccess(hasZipFile));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Validation failed for Community Outpost manifest {ManifestId}",
                manifest.Id);
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Validation failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Creates a generic patch manifest when no specialized content types are detected.
    /// </summary>
    private async Task<List<ContentManifest>> CreateGenericPatchManifestAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken)
    {
        var files = Directory.GetFiles(extractedDirectory, "*", SearchOption.AllDirectories);

        if (files.Length == 0)
        {
            return new List<ContentManifest>();
        }

        var manifestFiles = new List<ManifestFile>();

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var relativePath = Path.GetRelativePath(extractedDirectory, file);
            var fileInfo = new FileInfo(file);

            manifestFiles.Add(new ManifestFile
            {
                RelativePath = relativePath,
                Size = fileInfo.Length,
                IsRequired = true,
                IsExecutable = relativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),
                SourceType = ContentSourceType.ExtractedPackage,
            });
        }

        var manifest = new ContentManifest
        {
            Id = originalManifest.Id,
            Name = originalManifest.Name,
            Version = originalManifest.Version,
            ManifestVersion = originalManifest.ManifestVersion,
            ContentType = ContentType.Patch,
            TargetGame = originalManifest.TargetGame,
            Publisher = originalManifest.Publisher,
            Metadata = originalManifest.Metadata,
            Dependencies = originalManifest.Dependencies,
            Files = manifestFiles,
            InstallationInstructions = originalManifest.InstallationInstructions,
        };

        return await Task.FromResult(new List<ContentManifest> { manifest });
    }
}
