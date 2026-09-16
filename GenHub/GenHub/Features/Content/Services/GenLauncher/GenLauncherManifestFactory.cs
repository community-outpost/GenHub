using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.GenLauncher;

/// <summary>
/// Factory for post-extraction manifest processing of GenLauncher content.
/// Computes SHA256 CAS hashes and verifies engine file MD5 checksums against S3 ETags.
/// </summary>
public class GenLauncherManifestFactory : IPublisherManifestFactory
{
    private readonly IArchivePayloadProcessor _archivePayloadProcessor;
    private readonly ILogger<GenLauncherManifestFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenLauncherManifestFactory"/> class.
    /// </summary>
    public GenLauncherManifestFactory(
        IArchivePayloadProcessor archivePayloadProcessor,
        ILogger<GenLauncherManifestFactory> logger)
    {
        _archivePayloadProcessor = archivePayloadProcessor;
        _logger = logger;
    }

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
            _logger.LogInformation("Processing extracted GenLauncher content for {Name} in {Directory}", originalManifest.Name, extractedDirectory);

            // Safely extract archives if any
            await _archivePayloadProcessor.ExtractArchivesSafelyAsync(extractedDirectory, originalManifest.ContentType, cancellationToken);
            await _archivePayloadProcessor.NormalizeDirectoryStructureAsync(extractedDirectory, originalManifest.ContentType, originalManifest.TargetGame, cancellationToken);

            var manifest = new ContentManifest
            {
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
                Files = [],
            };

            var expectedEtags = originalManifest.Files
                .Where(f => !string.IsNullOrWhiteSpace(f.Hash))
                .ToDictionary(f => f.RelativePath.Replace('\\', '/'), f => f.Hash, StringComparer.OrdinalIgnoreCase);

            var allFiles = Directory.GetFiles(extractedDirectory, "*", SearchOption.AllDirectories);
            foreach (var filePath in allFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(extractedDirectory, filePath).Replace('\\', '/');

                // MD5 validation for engine critical files when ETag is present
                if (GenLauncherChecksumValidator.RequiresValidation(relativePath) &&
                    expectedEtags.TryGetValue(relativePath, out var expectedEtag) &&
                    !GenLauncherChecksumValidator.ValidateFile(filePath, expectedEtag))
                {
                    _logger.LogWarning("Checksum mismatch for engine file {File}! Expected ETag: {Expected}", relativePath, expectedEtag);
                }

                // Compute SHA256 for CAS
                string sha256Hash;
                using (var stream = File.OpenRead(filePath))
                {
                    var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
                    sha256Hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
                }

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

            return OperationResult<List<ContentManifest>>.CreateSuccess([manifest]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating GenLauncher manifest from extracted content for {Name}", originalManifest.Name);
            return OperationResult<List<ContentManifest>>.CreateFailure($"Failed to create GenLauncher manifest: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        return extractedDirectory;
    }
}
