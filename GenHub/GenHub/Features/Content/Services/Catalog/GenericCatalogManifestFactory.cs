using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Factory for creating manifests from generic publisher catalogs.
/// Handles post-extraction processing for catalog-based content.
/// </summary>
public class GenericCatalogManifestFactory(
    IFileHashProvider hashProvider,
    ILogger<GenericCatalogManifestFactory> logger,
    IArchivePayloadProcessor archivePayloadProcessor,
    IControlBarPackageProcessor? controlBarProcessor = null) : IPublisherManifestFactory
{
    /// <inheritdoc/>
    public string PublisherId => CatalogConstants.GenericCatalogResolverId;

    /// <inheritdoc/>
    public bool CanHandle(ContentManifest manifest)
    {
        var type = manifest.Publisher?.PublisherType;
        return string.IsNullOrWhiteSpace(type) ||
               type.Equals(CatalogConstants.GenericCatalogResolverId, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        return CreateManifestsFromExtractedContentAsync(originalManifest, extractedDirectory, progress: null, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        IProgress<GenHub.Core.Models.Content.ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "Processing extracted content for manifest {ManifestId} from directory {Directory}",
            originalManifest.Id,
            extractedDirectory);

        // Scan extracted directory for all files
        if (!Directory.Exists(extractedDirectory))
        {
            logger.LogWarning("Extracted directory does not exist: {Directory}", extractedDirectory);
            return [originalManifest];
        }

        // Dependency-only packages (ContentBundle) intentionally have an empty staging dir.
        var topLevelFiles = Directory.GetFiles(extractedDirectory, "*", SearchOption.TopDirectoryOnly);
        if (topLevelFiles.Length == 0 &&
            Directory.GetDirectories(extractedDirectory, "*", SearchOption.TopDirectoryOnly).Length == 0)
        {
            logger.LogDebug(
                "No staged files for manifest {ManifestId}; treating as dependency-only package",
                originalManifest.Id);
            return [originalManifest];
        }

        await archivePayloadProcessor.ProcessPayloadAsync(
            extractedDirectory,
            originalManifest.ContentType,
            originalManifest.TargetGame,
            normalizeInactiveArchives: true,
            progress: progress,
            cancellationToken: cancellationToken);

        if (controlBarProcessor?.IsControlBarContent(extractedDirectory, originalManifest) == true)
        {
            logger.LogDebug("Detected Control Bar content in catalog payload, repacking into BIG archives");
            await controlBarProcessor.ProcessAndRepackControlBarAsync(
                extractedDirectory,
                originalManifest,
                cancellationToken: cancellationToken);
        }

        var extractedFiles = Directory.GetFiles(extractedDirectory, "*", SearchOption.AllDirectories);
        if (extractedFiles.Length == 0)
        {
            logger.LogWarning("No files found in extracted directory: {Directory}", extractedDirectory);
            return [originalManifest];
        }

        logger.LogDebug("Found {Count} files in extracted directory", extractedFiles.Length);

        // Create updated file entries with computed hashes
        var updatedFiles = new List<ManifestFile>();
        foreach (var filePath in extractedFiles)
        {
            try
            {
                var relativePath = Path.GetRelativePath(extractedDirectory, filePath);
                var fileInfo = new FileInfo(filePath);
                var hash = await hashProvider.ComputeFileHashAsync(filePath, cancellationToken);

                var manifestFile = new ManifestFile
                {
                    RelativePath = relativePath,
                    SourceType = ContentSourceType.ContentAddressable,
                    Size = fileInfo.Length,
                    Hash = hash,
                    IsExecutable = ExecutableFileClassifier.RequiresExecutePermission(relativePath, filePath),
                };

                updatedFiles.Add(manifestFile);
                logger.LogDebug(
                    "Computed hash for file {RelativePath}: {Hash}, Size: {Size}",
                    relativePath,
                    hash,
                    fileInfo.Length);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to compute hash for file: {FilePath}", filePath);
                throw;
            }
        }

        // Create updated manifest with computed hashes
        var updatedManifest = new ContentManifest
        {
            SchemaVersion = originalManifest.SchemaVersion,
            Id = originalManifest.Id,
            Name = originalManifest.Name,
            Version = !string.IsNullOrWhiteSpace(originalManifest.Version)
                ? originalManifest.Version
                : CommunityOutpostCatalogConstants.DefaultMetadataVersion,
            ContentType = originalManifest.ContentType,
            TargetGame = originalManifest.TargetGame,
            Publisher = originalManifest.Publisher,
            Metadata = originalManifest.Metadata,
            OriginalProviderName = originalManifest.OriginalProviderName,
            OriginalContentId = originalManifest.OriginalContentId,
            SourcePath = originalManifest.SourcePath,
            Dependencies = originalManifest.Dependencies,
            ContentReferences = originalManifest.ContentReferences,
            KnownAddons = originalManifest.KnownAddons,
            Files = updatedFiles,
            Variants = originalManifest.Variants,
            EntryPoint = originalManifest.EntryPoint,
            RequiredDirectories = originalManifest.RequiredDirectories,
            InstallationInstructions = originalManifest.InstallationInstructions,
        };

        if (string.IsNullOrWhiteSpace(updatedManifest.EntryPoint))
        {
            var entryPointResolution = ManifestVariantResolver.ResolveEntryPoint(updatedManifest);
            if (entryPointResolution.Success)
            {
                updatedManifest.EntryPoint = entryPointResolution.RelativePath;
                logger.LogInformation(
                    "Inferred entry point '{EntryPoint}' for manifest {ManifestId} ({Reason})",
                    updatedManifest.EntryPoint,
                    updatedManifest.Id,
                    entryPointResolution.Reason);
            }
        }

        logger.LogInformation(
            "Successfully processed {Count} files for manifest {ManifestId}",
            updatedFiles.Count,
            originalManifest.Id);

        return [updatedManifest];
    }

    /// <inheritdoc/>
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        return extractedDirectory;
    }
}
