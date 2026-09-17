using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GenLauncher;

/// <summary>
/// Initializes a new instance of the <see cref="GenLauncherManifestFactory"/> class.
/// Factory for post-extraction manifest processing of GenLauncher content.
/// Computes SHA256 CAS hashes and verifies engine file MD5 checksums against S3 ETags.
/// </summary>
/// <param name="archivePayloadProcessor">The archive payload processor.</param>
/// <param name="logger">The logger instance.</param>
public class GenLauncherManifestFactory(
    IArchivePayloadProcessor archivePayloadProcessor,
    ILogger<GenLauncherManifestFactory> logger)
    : IPublisherManifestFactory
{
    /// <inheritdoc/>
    public string PublisherId => GenLauncherConstants.PublisherId;

    /// <inheritdoc/>
    public bool CanHandle(ContentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return manifest.Publisher?.PublisherType?.Equals(PublisherTypeConstants.GenLauncher, StringComparison.OrdinalIgnoreCase) == true
            || manifest.OriginalProviderName?.Equals(PublisherTypeConstants.GenLauncher, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <inheritdoc/>
    public async Task<OperationResult<List<ContentManifest>>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalManifest);

        if (!Directory.Exists(extractedDirectory))
        {
            return OperationResult<List<ContentManifest>>.CreateFailure($"Extracted directory does not exist: {extractedDirectory}");
        }

        try
        {
            logger.LogInformation("Processing extracted GenLauncher content for {Name} in {Directory}", originalManifest.Name, extractedDirectory);

            // Safely extract archives if any
            await archivePayloadProcessor.ExtractArchivesSafelyAsync(extractedDirectory, originalManifest.ContentType, cancellationToken);
            await archivePayloadProcessor.NormalizeDirectoryStructureAsync(extractedDirectory, originalManifest.ContentType, originalManifest.TargetGame, cancellationToken);

            var manifest = new ContentManifest
            {
                SchemaVersion = originalManifest.SchemaVersion,
                Id = originalManifest.Id,
                Name = originalManifest.Name,
                Version = originalManifest.Version,
                TargetGame = originalManifest.TargetGame,
                ContentType = originalManifest.ContentType,
                Publisher = originalManifest.Publisher,
                Metadata = originalManifest.Metadata,
                Dependencies = [.. originalManifest.Dependencies],
                OriginalProviderName = originalManifest.OriginalProviderName,
                OriginalContentId = originalManifest.OriginalContentId,
                SourcePath = Directory.Exists(originalManifest.SourcePath) || File.Exists(originalManifest.SourcePath) ? originalManifest.SourcePath : null,
                ContentReferences = [.. originalManifest.ContentReferences],
                KnownAddons = [.. originalManifest.KnownAddons],
                Variants = originalManifest.Variants,
                EntryPoint = originalManifest.EntryPoint,
                RequiredDirectories = [.. originalManifest.RequiredDirectories],
                InstallationInstructions = originalManifest.InstallationInstructions,
                Files = [],
            };

            var expectedEtags = originalManifest.Files
                .Where(f => !string.IsNullOrWhiteSpace(f.Hash))
                .DistinctBy(f => f.RelativePath.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(f => f.RelativePath.Replace('\\', '/'), f => f.Hash, StringComparer.OrdinalIgnoreCase);

            var allFiles = Directory.GetFiles(extractedDirectory, "*", SearchOption.AllDirectories);
            foreach (var filePath in allFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(extractedDirectory, filePath).Replace('\\', '/');

                // MD5 validation for engine critical files when ETag is present
                if (GenLauncherChecksumValidator.RequiresValidation(relativePath) &&
                    expectedEtags.TryGetValue(relativePath, out var expectedEtag) &&
                    !await GenLauncherChecksumValidator.ValidateFileAsync(filePath, expectedEtag, cancellationToken))
                {
                    logger.LogError("Checksum mismatch for engine file {File}! Expected ETag: {Expected}", relativePath, expectedEtag);
                    return OperationResult<List<ContentManifest>>.CreateFailure($"Checksum mismatch for engine file {relativePath}");
                }

                // Compute SHA256 for CAS
                using var stream = File.OpenRead(filePath);
                var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
                var sha256Hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

                var fileInfo = new FileInfo(filePath);
                manifest.Files.Add(new ManifestFile
                {
                    RelativePath = relativePath,
                    Hash = sha256Hash,
                    Size = fileInfo.Length,
                    SourceType = ContentSourceType.ContentAddressable,
                    IsRequired = true,
                });
            }

            if (string.IsNullOrWhiteSpace(manifest.EntryPoint))
            {
                var entryPointResolution = ManifestVariantResolver.ResolveEntryPoint(manifest);
                if (entryPointResolution.Success)
                {
                    manifest.EntryPoint = entryPointResolution.RelativePath;
                }
            }

            return OperationResult<List<ContentManifest>>.CreateSuccess([manifest]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating GenLauncher manifest from extracted content for {Name}", originalManifest.Name);
            return OperationResult<List<ContentManifest>>.CreateFailure($"Failed to create GenLauncher manifest: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        return extractedDirectory;
    }
}
