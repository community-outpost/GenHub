using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Manifest factory for generic GitHub content.
/// Handles extracted content from GitHub releases (e.g., Mod ZIPs).
/// </summary>
public class GitHubManifestFactory(
    ILogger<GitHubManifestFactory> logger,
    IFileHashProvider hashProvider)
    : IPublisherManifestFactory
{
    /// <inheritdoc />
    public string PublisherId => "github";

    /// <inheritdoc />
    public bool CanHandle(ContentManifest manifest)
    {
        // Handle standard "github" publisher
        var publisherMatches = manifest.Publisher?.PublisherType?.Equals("github", StringComparison.OrdinalIgnoreCase) == true;

        // Also handle legacy or owner-based publisher types if they map to GitHub
        // (But usually GitHubResolver sets PublisherType to "github")
        // Only handle Mod or MapPack content types for now (GameClients might be handled, but usually specific factories exist)
        // If we want to support any GitHub ZIP extraction, we can make this broader.
        // But for safety, let's start with Mod.
        // Update: We want to support Mod. GamClient is handled if no specific factory picks it up?
        // Actually, GitHubManifestFactory can be a fallback for any GitHub content that was extracted.
        return publisherMatches;
    }

    /// <inheritdoc />
    public async Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Creating GitHub manifests from extracted content in: {Directory}", extractedDirectory);

        if (!Directory.Exists(extractedDirectory))
        {
            logger.LogWarning("Extracted directory does not exist: {Directory}", extractedDirectory);
            return [];
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

            // Determine if executable (simple heuristic for now, can be improved)
            bool isExecutable = filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                filePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                                filePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);

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
            ManifestVersion = originalManifest.ManifestVersion,
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
