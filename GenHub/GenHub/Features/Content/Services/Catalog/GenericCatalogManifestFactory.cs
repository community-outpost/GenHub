using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Factory for creating ContentManifest objects from generic publisher catalogs.
/// This factory handles the final step of manifest generation after content is extracted.
/// </summary>
/// <remarks>
/// <para>
/// This is the bridge between Publisher Studio catalogs and the GameProfile system.
/// When a user downloads content from a subscribed publisher, this factory:
/// 1. Computes SHA256 hashes for CAS storage.
/// 2. Adds ManifestFile entries with sizes and hashes.
/// 3. Configures installation instructions based on content type.
/// </para>
/// <para>
/// Pipeline: Discoverer → Parser → Resolver → Deliverer → **Factory** → GameProfile.
/// </para>
/// </remarks>
public class GenericCatalogManifestFactory : IPublisherManifestFactory
{
    private const string PublisherIdPrefix = "catalog.";
    private readonly ILogger<GenericCatalogManifestFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenericCatalogManifestFactory"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public GenericCatalogManifestFactory(ILogger<GenericCatalogManifestFactory> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string PublisherId => PublisherIdPrefix + "generic";

    /// <inheritdoc />
    public bool CanHandle(ContentManifest manifest)
    {
        // Handle any manifest from a catalog-based publisher
        // These have publisher types that start with lowercase IDs (not "Steam", "EA", etc.)
        if (manifest?.Publisher?.PublisherType == null)
        {
            return false;
        }

        var publisherType = manifest.Publisher.PublisherType;

        // Don't handle built-in publishers (Steam, EA, Origin, etc.)
        var builtInPublishers = new[]
        {
            "Steam", "steam",
            "EA", "Origin", "ea", "origin",
            "Ultimate", "ultimate",
            "TheSuperHackers", "thesuperhackers",
            "GeneralsOnline", "generalsonline",
            "CommunityOutpost", "communityoutpost",
        };

        foreach (var builtin in builtInPublishers)
        {
            if (publisherType.Equals(builtin, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Handle all other catalog-based publishers
        return true;
    }

    /// <inheritdoc />
    public async Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalManifest);
        ArgumentException.ThrowIfNullOrEmpty(extractedDirectory);

        if (!Directory.Exists(extractedDirectory))
        {
            throw new DirectoryNotFoundException($"Extracted directory not found: {extractedDirectory}");
        }

        _logger.LogInformation(
            "Creating manifest from extracted content: {Name} in {Directory}",
            originalManifest.Name,
            extractedDirectory);

        // Clone the original manifest and enrich with file entries
        var enrichedManifest = CloneManifest(originalManifest);
        enrichedManifest.Files.Clear(); // Clear any existing file entries, we'll rebuild

        // Scan extracted directory for files
        var files = Directory.GetFiles(extractedDirectory, "*", SearchOption.AllDirectories);

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(extractedDirectory, filePath);
            var fileInfo = new FileInfo(filePath);

            // Compute SHA256 hash for CAS storage
            var hash = await ComputeSha256Async(filePath, cancellationToken);

            var manifestFile = new ManifestFile
            {
                RelativePath = relativePath.Replace('\\', '/'),
                SourceType = ContentSourceType.ContentAddressable,
                Hash = hash,
                Size = fileInfo.Length,
                IsExecutable = IsExecutableFile(relativePath),
            };

            enrichedManifest.Files.Add(manifestFile);
        }

        // Configure installation instructions based on content type
        ConfigureInstallationInstructions(enrichedManifest);

        _logger.LogInformation(
            "Created manifest for '{Name}' with {FileCount} files, total size: {TotalSize} bytes",
            enrichedManifest.Name,
            enrichedManifest.Files.Count,
            enrichedManifest.Files.Sum(f => f.Size));

        return [enrichedManifest];
    }

    /// <inheritdoc />
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        // For generic catalog content, files are directly in the extracted directory
        return extractedDirectory;
    }

    private static ContentManifest CloneManifest(ContentManifest original)
    {
        return new ContentManifest
        {
            ManifestVersion = original.ManifestVersion,
            Id = original.Id,
            Name = original.Name,
            Version = original.Version,
            ContentType = original.ContentType,
            TargetGame = original.TargetGame,
            Publisher = new PublisherInfo
            {
                PublisherType = original.Publisher.PublisherType,
                Name = original.Publisher.Name,
                Website = original.Publisher.Website,
                SupportUrl = original.Publisher.SupportUrl,
                ContactEmail = original.Publisher.ContactEmail,
            },
            Metadata = new ContentMetadata
            {
                Description = original.Metadata.Description,
                Tags = new List<string>(original.Metadata.Tags),
                IconUrl = original.Metadata.IconUrl,
                ScreenshotUrls = new List<string>(original.Metadata.ScreenshotUrls),
            },
            Dependencies = new List<ContentDependency>(original.Dependencies),
            ContentReferences = new List<ContentReference>(original.ContentReferences),
            KnownAddons = new List<string>(original.KnownAddons),
            Files = new List<ManifestFile>(),
            RequiredDirectories = new List<string>(original.RequiredDirectories),
            InstallationInstructions = new InstallationInstructions
            {
                WorkspaceStrategy = original.InstallationInstructions.WorkspaceStrategy,
                PreInstallSteps = new List<InstallationStep>(original.InstallationInstructions.PreInstallSteps),
                PostInstallSteps = new List<InstallationStep>(original.InstallationInstructions.PostInstallSteps),
            },
        };
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static bool IsExecutableFile(string relativePath)
    {
        var extension = Path.GetExtension(relativePath).ToLowerInvariant();
        return extension is ".exe" or ".dll" or ".bat" or ".cmd" or ".ps1";
    }

    private static string GetModTargetDirectory(ContentManifest manifest)
    {
        // Generate a mod directory name from the manifest ID
        // Format: Mods/{publisher-id}.{content-id}/
        var idParts = manifest.Id.Value.Split('.');
        if (idParts.Length >= 5)
        {
            // ID format: {schema}.{version}.{publisher}.{type}.{name}
            var publisher = idParts[2];
            var name = idParts[^1]; // Last part is content name
            return $"Mods/{publisher}.{name}/";
        }

        // Fallback: use manifest name
        var safeName = manifest.Name.Replace(" ", "_").ToLowerInvariant();
        return $"Mods/{safeName}/";
    }

    private void ConfigureInstallationInstructions(ContentManifest manifest)
    {
        // Configure workspace strategy based on content type
        // Mods typically use hybrid copy+symlink, game clients need full copy for backup
        switch (manifest.ContentType)
        {
            case ContentType.Mod:
            case ContentType.Addon:
            case ContentType.Map:
            case ContentType.MapPack:
                // Standard content uses hybrid strategy for efficient disk usage
                manifest.InstallationInstructions.WorkspaceStrategy = WorkspaceStrategy.HybridCopySymlink;
                break;

            case ContentType.GameClient:
                // Game clients need full copy to allow file patching
                manifest.InstallationInstructions.WorkspaceStrategy = WorkspaceStrategy.FullCopy;
                break;

            default:
                manifest.InstallationInstructions.WorkspaceStrategy = WorkspaceStrategy.HybridCopySymlink;
                break;
        }

        _logger.LogDebug(
            "Configured installation: Type={ContentType}, Strategy={Strategy}",
            manifest.ContentType,
            manifest.InstallationInstructions.WorkspaceStrategy);
    }
}