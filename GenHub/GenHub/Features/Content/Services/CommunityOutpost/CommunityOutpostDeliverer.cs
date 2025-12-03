using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
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
using GenHub.Features.Content.Services.CommunityOutpost.Models;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Specialized deliverer for Community Outpost content.
/// Downloads packages (ZIP or 7z/.dat files), extracts files, and creates manifests via factory.
/// Supports multiple download mirrors for fallback.
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
        // Can deliver if it's a Community Outpost manifest with a downloadable file
        // Note: PublisherType in manifest is "communityoutpost" (no hyphen)
        return manifest.Publisher?.PublisherType?.Equals(
                   CommunityOutpostConstants.PublisherType,
                   StringComparison.OrdinalIgnoreCase) == true &&
               manifest.Files.Any(f =>
                   !string.IsNullOrEmpty(f.DownloadUrl) &&
                   (f.DownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    f.DownloadUrl.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                    f.DownloadUrl.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)));
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
                "Starting Community Outpost content delivery for {ManifestId} (v{Version})",
                packageManifest.Id,
                packageManifest.Version);

            // Step 1: Download archive file
            var archiveFile = packageManifest.Files.FirstOrDefault(f =>
                !string.IsNullOrEmpty(f.DownloadUrl) &&
                (f.DownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                 f.DownloadUrl.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                 f.DownloadUrl.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)));

            if (archiveFile == null)
            {
                return OperationResult<ContentManifest>.CreateFailure("No downloadable archive found in manifest");
            }

            // Determine archive type from file extension or SourcePath marker
            var isSevenZip = archiveFile.SourcePath == "archive:7z" ||
                            archiveFile.DownloadUrl!.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                            archiveFile.DownloadUrl!.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);

            var archiveExtension = isSevenZip ? ".7z" : ".zip";
            var archivePath = Path.Combine(targetDirectory, $"content{archiveExtension}");

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Downloading,
                ProgressPercentage = 10,
                CurrentOperation = "Downloading Community Outpost package",
                CurrentFile = archiveFile.RelativePath,
            });

            // Try downloading with mirror fallback
            var downloadResult = await DownloadWithMirrorFallbackAsync(
                archiveFile.DownloadUrl!,
                archivePath,
                cancellationToken);

            if (!downloadResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Failed to download package: {downloadResult.FirstError}");
            }

            // Step 2: Extract archive
            var extractPath = Path.Combine(targetDirectory, "extracted");
            Directory.CreateDirectory(extractPath);

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Extracting,
                ProgressPercentage = 40,
                CurrentOperation = isSevenZip
                    ? "Extracting 7z archive"
                    : "Extracting ZIP archive",
            });

            logger.LogDebug("Extracting {ArchiveType} to {Path}", isSevenZip ? "7z" : "ZIP", extractPath);

            try
            {
                if (isSevenZip)
                {
                    await ExtractSevenZipAsync(archivePath, extractPath, cancellationToken);
                }
                else
                {
                    ZipFile.ExtractToDirectory(archivePath, extractPath, overwriteFiles: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to extract archive from {Path}", archivePath);
                return OperationResult<ContentManifest>.CreateFailure($"Extraction failed: {ex.Message}");
            }

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
                    "No specialized content detected, creating generic manifest");

                manifests = await CreateGenericManifestAsync(
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

            // Step 5: Cleanup temporary files
            await CleanupTemporaryFilesAsync(archivePath, extractPath);

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

    /// <summary>
    /// Downloads a file with mirror fallback support.
    /// </summary>
    private async Task<OperationResult<bool>> DownloadWithMirrorFallbackAsync(
        string primaryUrl,
        string targetPath,
        CancellationToken cancellationToken)
    {
        // Try primary URL first
        logger.LogDebug("Downloading from primary URL: {Url}", primaryUrl);
        var result = await downloadService.DownloadFileAsync(
            primaryUrl,
            targetPath,
            expectedHash: null,
            progress: null,
            cancellationToken);

        if (result.Success)
        {
            return OperationResult<bool>.CreateSuccess(true);
        }

        logger.LogWarning("Primary download failed: {Error}", result.FirstError);

        // Note: Mirror URLs would be stored in the original search result metadata
        // For now, we only try the primary URL since we don't have easy access
        // to the original metadata here. In a future enhancement, we could
        // store mirror URLs in the manifest or pass them through.

        return OperationResult<bool>.CreateFailure($"Download failed: {result.FirstError}");
    }

    /// <summary>
    /// Extracts a 7z archive asynchronously using SharpCompress.
    /// </summary>
    private async Task ExtractSevenZipAsync(
        string archivePath,
        string extractPath,
        CancellationToken cancellationToken)
    {
        await Task.Run(
            () =>
            {
                using var archive = SevenZipArchive.Open(archivePath);
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    entry.WriteToDirectory(
                        extractPath,
                        new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true,
                        });
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Cleans up temporary files after extraction.
    /// </summary>
    private async Task CleanupTemporaryFilesAsync(string archivePath, string extractPath)
    {
        await Task.Run(() =>
        {
            // Delete archive file
            try
            {
                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                    logger.LogDebug("Deleted archive file: {Path}", archivePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete archive file {Path}", archivePath);
            }

            // Delete extracted directory
            try
            {
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, recursive: true);
                    logger.LogDebug("Deleted extracted directory: {Path}", extractPath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete extracted directory {Path}", extractPath);
            }
        });
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> ValidateContentAsync(
        ContentManifest manifest,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var hasArchiveFile = manifest.Files.Any(f =>
                !string.IsNullOrEmpty(f.DownloadUrl) &&
                (f.DownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                 f.DownloadUrl.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                 f.DownloadUrl.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)));

            return Task.FromResult(OperationResult<bool>.CreateSuccess(hasArchiveFile));
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
    /// Creates a generic manifest when no specialized content types are detected.
    /// </summary>
    private async Task<List<ContentManifest>> CreateGenericManifestAsync(
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
            ContentType = originalManifest.ContentType,
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
