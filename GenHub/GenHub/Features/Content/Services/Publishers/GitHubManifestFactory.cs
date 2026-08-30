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

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Manifest factory for generic GitHub content.
/// Handles extracted content from GitHub releases (e.g., Mod ZIPs).
/// </summary>
public class GitHubManifestFactory(
    ILogger<GitHubManifestFactory> logger,
    IFileHashProvider hashProvider,
    IArchivePayloadProcessor archivePayloadProcessor,
    IControlBarPackageProcessor? controlBarProcessor = null)
    : IPublisherManifestFactory
{
    /// <inheritdoc />
    public string PublisherId => "github";

    /// <inheritdoc />
    public bool CanHandle(ContentManifest manifest)
    {
        // Handle standard "github" publisher
        var publisherMatches = manifest.Publisher?.PublisherType?.Equals("github", StringComparison.OrdinalIgnoreCase) == true;
        return publisherMatches;
    }

    /// <inheritdoc />
    public Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        return CreateManifestsFromExtractedContentAsync(originalManifest, extractedDirectory, progress: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        IProgress<GenHub.Core.Models.Content.ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Creating GitHub manifests from extracted content in: {Directory}", extractedDirectory);

        if (!Directory.Exists(extractedDirectory))
        {
            logger.LogWarning("Extracted directory does not exist: {Directory}", extractedDirectory);
            return [];
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
            logger.LogInformation("Detected Control Bar content in GitHub payload, repacking into BIG archives");
            await controlBarProcessor.ProcessAndRepackControlBarAsync(
                extractedDirectory,
                originalManifest,
                cancellationToken: cancellationToken);
        }

        var files = new List<ManifestFile>();
        var allFiles = Directory.GetFiles(extractedDirectory, "*", SearchOption.AllDirectories);

        logger.LogInformation("Found {FileCount} files in {Directory}", allFiles.Length, extractedDirectory);

        // Parallelize hashing for better performance
        var fileProcessingTasks = allFiles.Select(async filePath =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            var relativePath = Path.GetRelativePath(extractedDirectory, filePath);
            var fileInfo = new FileInfo(filePath);

            // Compute hash for ContentAddressable storage
            string fileHash = await hashProvider.ComputeFileHashAsync(filePath, cancellationToken);

            bool isExecutable = ExecutableFileClassifier.RequiresExecutePermission(relativePath, filePath);

            return new ManifestFile
            {
                RelativePath = relativePath,
                Size = fileInfo.Length,
                Hash = fileHash,
                IsRequired = true,
                IsExecutable = isExecutable,
                SourceType = ContentSourceType.ContentAddressable,
                SourcePath = filePath,
            };
        });

        var processedFiles = await Task.WhenAll(fileProcessingTasks);
        files.AddRange(processedFiles.Where(f => f != null)!);

        // Clone the original manifest but replace files
        var manifest = new ContentManifest
        {
            SchemaVersion = originalManifest.SchemaVersion,
            Id = originalManifest.Id, // Keep original ID
            Name = originalManifest.Name,
            Version = originalManifest.Version,
            ContentType = originalManifest.ContentType,
            TargetGame = originalManifest.TargetGame,
            Publisher = originalManifest.Publisher,
            Metadata = originalManifest.Metadata,
            Dependencies = originalManifest.Dependencies,
            ContentReferences = originalManifest.ContentReferences,
            KnownAddons = originalManifest.KnownAddons,
            Files = files,
            RequiredDirectories = originalManifest.RequiredDirectories,
            InstallationInstructions = originalManifest.InstallationInstructions,
        };

        return [manifest];
    }

    /// <inheritdoc />
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        return extractedDirectory;
    }
}
