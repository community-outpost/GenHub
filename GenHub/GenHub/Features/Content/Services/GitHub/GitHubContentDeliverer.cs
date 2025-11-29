using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.ContentDeliverers;

/// <summary>
/// Delivers GitHub content with special handling for releases containing ZIP archives.
/// Uses publisher-specific manifest factories for extensible content handling.
/// </summary>
public class GitHubContentDeliverer(
    IDownloadService downloadService,
    IContentManifestPool manifestPool,
    PublisherManifestFactoryResolver factoryResolver,
    ILogger<GitHubContentDeliverer> logger) : IContentDeliverer
{
    /// <inheritdoc />
    public string SourceName => ContentSourceNames.GitHubDeliverer;

    /// <inheritdoc />
    public string Description => GitHubConstants.GitHubDelivererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc />
    public bool CanDeliver(ContentManifest manifest)
    {
        // Can deliver if files have GitHub download URLs
        return manifest.Files.Any(f =>
            !string.IsNullOrEmpty(f.DownloadUrl) &&
            f.DownloadUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase));
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
            // Download all files (validate no duplicate paths to prevent data loss)
            var filesToDownload = packageManifest.Files
                .Where(f => !string.IsNullOrEmpty(f.DownloadUrl))
                .ToList();

            // Check for duplicate relative paths
            var duplicatePaths = filesToDownload
                .GroupBy(f => f.RelativePath)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicatePaths.Any())
            {
                logger.LogError(
                    "Manifest {ManifestId} contains duplicate relative paths: {Duplicates}. This would cause data loss.",
                    packageManifest.Id,
                    string.Join(", ", duplicatePaths));
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Manifest contains duplicate file paths that would cause data loss: {string.Join(", ", duplicatePaths)}");
            }

            var downloadedFiles = new List<string>();

            foreach (var file in filesToDownload)
            {
                var localPath = Path.Combine(targetDirectory, file.RelativePath);
                var localDir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(localDir))
                {
                    Directory.CreateDirectory(localDir);
                }

                var downloadResult = await downloadService.DownloadFileAsync(
                    file.DownloadUrl!, localPath, file.Hash, null, cancellationToken);

                if (!downloadResult.Success)
                {
                    return OperationResult<ContentManifest>.CreateFailure(
                        $"Failed to download {file.RelativePath}: {downloadResult.FirstError}");
                }

                downloadedFiles.Add(localPath);
                logger.LogInformation("Downloaded {FileName} to {Path}", file.RelativePath, localPath);
            }

            // Check if this is GameClient content with ZIP files
            var zipFiles = downloadedFiles.Where(f => Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase)).ToList();

            if (packageManifest.ContentType == ContentType.GameClient && zipFiles.Any())
            {
                logger.LogInformation(
                    "GameClient content detected with {Count} ZIP files. Extracting...",
                    zipFiles.Count);

                // Extract all ZIPs
                foreach (var zipFile in zipFiles)
                {
                    try
                    {
                        ZipFile.ExtractToDirectory(zipFile, targetDirectory, overwriteFiles: true);
                        logger.LogInformation("Extracted {ZipFile}", Path.GetFileName(zipFile));
                        File.Delete(zipFile);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to extract {ZipFile}", Path.GetFileName(zipFile));
                        return OperationResult<ContentManifest>.CreateFailure(
                            $"Failed to extract {Path.GetFileName(zipFile)}: {ex.Message}");
                    }
                }

                logger.LogInformation(
                    "Successfully extracted {Count} ZIP files for GameClient {ManifestId}. Using publisher factory for manifest generation...",
                    zipFiles.Count,
                    packageManifest.Id);

                // Use publisher-specific factory to create manifests
                return await HandleExtractedContentAsync(packageManifest, targetDirectory, cancellationToken);
            }

            // For non-GameClient content or if no ZIPs, return original manifest
            return OperationResult<ContentManifest>.CreateSuccess(packageManifest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver GitHub content for manifest {ManifestId}", packageManifest.Id);
            return OperationResult<ContentManifest>.CreateFailure($"Content delivery failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> ValidateContentAsync(
        ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate that all required URLs are GitHub URLs
            foreach (var file in manifest.Files.Where(f => f.IsRequired && !string.IsNullOrEmpty(f.DownloadUrl)))
            {
                if (file.DownloadUrl != null && !file.DownloadUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(OperationResult<bool>.CreateSuccess(false));
                }
            }

            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Validation failed for GitHub content manifest {ManifestId}", manifest.Id);
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Validation failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Handles extracted content by using publisher-specific factories to create manifests.
    /// May return multiple manifests if the publisher factory detects multi-variant content.
    /// </summary>
    private async Task<OperationResult<ContentManifest>> HandleExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            // Resolve the appropriate factory for this publisher/content type
            logger.LogInformation(
                "Resolving factory for manifest {ManifestId}, Publisher={PublisherType}, ContentType={ContentType}",
                originalManifest.Id,
                originalManifest.Publisher?.PublisherType,
                originalManifest.ContentType);

            var factory = factoryResolver.ResolveFactory(originalManifest);
            if (factory == null)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"No factory found for manifest {originalManifest.Id} (Publisher: {originalManifest.Publisher?.PublisherType ?? "Unknown"})");
            }

            logger.LogInformation(
                "Using factory {FactoryType} for manifest {ManifestId}",
                factory.GetType().Name,
                originalManifest.Id);

            // Use the factory to create manifests from extracted content
            var manifests = await factory.CreateManifestsFromExtractedContentAsync(
                originalManifest,
                extractedDirectory,
                cancellationToken);

            if (manifests.Count == 0)
            {
                logger.LogWarning("Factory produced no manifests for {ManifestId}", originalManifest.Id);
                return OperationResult<ContentManifest>.CreateFailure("No manifests generated from extracted content");
            }

            logger.LogInformation(
                "Factory generated {Count} manifest(s) from extracted content: {ManifestIds}",
                manifests.Count,
                string.Join(", ", manifests.Select(m => m.Id.Value)));

            // Store all manifests to CAS via manifest pool
            // This ensures files are available in CAS before validation runs
            foreach (var manifest in manifests)
            {
                var manifestDirectory = factory.GetManifestDirectory(manifest, extractedDirectory);

                logger.LogInformation(
                    "Storing manifest {ManifestId} to pool from directory {Directory}",
                    manifest.Id,
                    manifestDirectory);

                var addResult = await manifestPool.AddManifestAsync(manifest, manifestDirectory, cancellationToken);
                if (!addResult.Success)
                {
                    logger.LogWarning(
                        "Failed to store manifest {ManifestId} to pool: {Errors}",
                        manifest.Id,
                        string.Join(", ", addResult.Errors));
                }
                else
                {
                    logger.LogInformation(
                        "Successfully stored manifest {ManifestId} to pool",
                        manifest.Id);

                    // Update file source types to ContentAddressable since files are now in CAS
                    // This ensures validation checks CAS instead of filesystem paths
                    foreach (var file in manifest.Files)
                    {
                        file.SourceType = ContentSourceType.ContentAddressable;
                    }
                }
            }

            // Return primary manifest
            var primaryManifest = manifests[0];

            return OperationResult<ContentManifest>.CreateSuccess(primaryManifest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle extracted content using factory");
            return OperationResult<ContentManifest>.CreateFailure($"Factory content handling failed: {ex.Message}");
        }
    }
}
