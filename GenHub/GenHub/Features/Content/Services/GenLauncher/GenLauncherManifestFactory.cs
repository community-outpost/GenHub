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

            var filesWithEtag = originalManifest.Files
                .Where(f => !string.IsNullOrWhiteSpace(f.ETag ?? f.Hash))
                .ToList();

            var expectedEtags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in filesWithEtag)
            {
                var normalizedPath = file.RelativePath.Replace('\\', '/');
                var etag = file.ETag ?? file.Hash!;
                if (!expectedEtags.TryAdd(normalizedPath, etag) &&
                    !string.Equals(expectedEtags[normalizedPath], etag, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Duplicate file entry with conflicting ETag/Hash dropped for {Path} in {ManifestName}", normalizedPath, originalManifest.Name);
                }
            }

            var filenameEtags = filesWithEtag
                .GroupBy(f => Path.GetFileName(f.RelativePath), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.First().ETag ?? g.First().Hash, StringComparer.OrdinalIgnoreCase);

            var allFiles = Directory.GetFiles(extractedDirectory, "*", SearchOption.AllDirectories);
            foreach (var filePath in allFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileResult = await ProcessExtractedFileAsync(
                    extractedDirectory,
                    filePath,
                    expectedEtags,
                    filenameEtags,
                    cancellationToken);

                if (!fileResult.Success)
                {
                    return OperationResult<List<ContentManifest>>.CreateFailure(fileResult.FirstError ?? "File validation failed");
                }

                manifest.Files.Add(fileResult.Data!);
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

    private async Task<OperationResult<ManifestFile>> ProcessExtractedFileAsync(
        string extractedDirectory,
        string filePath,
        Dictionary<string, string> expectedEtags,
        Dictionary<string, string> filenameEtags,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.GetRelativePath(extractedDirectory, filePath).Replace('\\', '/');
        var fileName = Path.GetFileName(filePath);

        string? expectedEtag = null;
        var hasExpectedEtag = expectedEtags.TryGetValue(relativePath, out expectedEtag);
        if (!hasExpectedEtag && filenameEtags.TryGetValue(fileName, out expectedEtag))
        {
            hasExpectedEtag = true;
            logger.LogDebug("Using unique filename-level fallback ETag for extracted file {RelativePath}", relativePath);
        }

        // MD5 validation for engine critical files when ETag is present
        if (hasExpectedEtag &&
            !string.IsNullOrWhiteSpace(expectedEtag) &&
            GenLauncherChecksumValidator.RequiresValidation(relativePath) &&
            !await GenLauncherChecksumValidator.ValidateFileAsync(filePath, expectedEtag, cancellationToken))
        {
            logger.LogError("Checksum mismatch for engine file {File}! Expected ETag: {Expected}", relativePath, expectedEtag);
            return OperationResult<ManifestFile>.CreateFailure($"Checksum mismatch for engine file {relativePath}");
        }

        // Compute SHA256 for CAS
        using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        var sha256Hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var fileInfo = new FileInfo(filePath);
        var manifestFile = new ManifestFile
        {
            RelativePath = relativePath,
            Hash = sha256Hash,
            ETag = expectedEtag,
            Size = fileInfo.Length,
            SourceType = ContentSourceType.ContentAddressable,
            IsRequired = true,
        };

        return OperationResult<ManifestFile>.CreateSuccess(manifestFile);
    }
}
