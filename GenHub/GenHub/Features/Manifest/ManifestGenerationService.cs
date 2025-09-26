using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Manifest;

/// <summary>
/// High-level service for generating different types of content manifests.
/// </summary>
public class ManifestGenerationService(ILogger<ManifestGenerationService> logger, IServiceProvider serviceProvider) : IManifestGenerationService
{
    /// <summary>
    /// Static JSON serializer options for manifest serialization.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ILogger<ManifestGenerationService> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    /// <inheritdoc/>
    public async Task<IContentManifestBuilder> CreateContentManifestAsync(
        string contentDirectory,
        string contentId,
        string contentName,
        int manifestVersion,
        ContentType contentType,
        GameType targetGame,
        params ContentDependency[] dependencies)
    {
        _logger.LogInformation("Creating content manifest for {ContentName} v{ManifestVersion}", contentName, manifestVersion);

        var builder = CreateBuilder()
            .WithBasicInfo(contentId, contentName, manifestVersion)
            .WithContentType(contentType, targetGame)
            .WithMetadata($"Content manifest for {contentName}");
        foreach (var dep in dependencies)
        {
            builder.AddDependency(
                dep.Id,
                dep.Name,
                dep.DependencyType,
                dep.InstallBehavior,
                dep.MinVersion ?? string.Empty,
                dep.MaxVersion ?? string.Empty,
                dep.CompatibleVersions,
                dep.IsExclusive,
                dep.ConflictsWith);
        }

        await builder.AddFilesFromDirectoryAsync(contentDirectory, ContentSourceType.ContentAddressable);
        return builder;
    }

    /// <summary>
    /// Creates a content bundle with the specified items and publisher.
    /// </summary>
    /// <param name="bundleId">The bundle identifier.</param>
    /// <param name="bundleName">The bundle name.</param>
    /// <param name="manifestVersion">The manifest version.</param>
    /// <param name="publisher">The publisher information.</param>
    /// <param name="items">The bundle items.</param>
    /// <returns>The created <see cref="ContentBundle"/>.</returns>
    public async Task<ContentBundle> CreateContentBundleAsync(
        string bundleId,
        string bundleName,
        int manifestVersion,
        PublisherInfo? publisher,
        params BundleItem[] items)
    {
        _logger.LogInformation("Creating content bundle {BundleId} with {ItemCount} items", bundleId, items.Length);

        var bundle = new ContentBundle
        {
            Id = bundleId,
            Name = bundleName,
            Version = manifestVersion.ToString(),
            Publisher = publisher ?? new PublisherInfo { Name = "Unknown Publisher" },
            Items = items.OrderBy(i => i.DisplayOrder).ToList(),
            Metadata = new ContentMetadata
            {
                Description = $"Content bundle containing {items.Length} items",
                ReleaseDate = DateTime.UtcNow,
            },
        };

        await ValidateBundleItemsAsync(bundle);
        return bundle;
    }

    /// <summary>
    /// Creates a game installation manifest for the specified game installation.
    /// </summary>
    /// <param name="gameInstallationPath">The path to the game installation.</param>
    /// <param name="gameType">The game type.</param>
    /// <param name="installationType">The installation type.</param>
    /// <param name="manifestVersion">The manifest version.</param>
    /// <returns>The manifest builder.</returns>
    public async Task<IContentManifestBuilder> CreateGameInstallationManifestAsync(
        string gameInstallationPath,
        GameType gameType,
        GameInstallationType installationType,
        int manifestVersion)
    {
        _logger.LogInformation(
            "Creating game installation manifest for {GameType} {InstallationType} v{ManifestVersion}",
            gameType,
            installationType,
            manifestVersion);

        var builder = CreateBuilder()
            .WithBasicInfo(installationType, gameType, manifestVersion)
            .WithContentType(ContentType.GameInstallation, gameType)
            .WithPublisher(
                "EA Games",
                "https://www.ea.com",
                "https://help.ea.com",
                "support@ea.com")
            .WithMetadata($"Game installation of {gameType} (manifest version {manifestVersion}) from {installationType}")
            .AddRequiredDirectories(DirectoryNames.Data, "Maps")
            .WithInstallationInstructions(WorkspaceStrategy.SymlinkOnly);

        // Add all game files
        await builder.AddFilesFromDirectoryAsync(gameInstallationPath, ContentSourceType.GameInstallation);

        return builder;
    }

    /// <summary>
    /// Creates a standalone game manifest for a given directory and executable.
    /// </summary>
    /// <param name="gameDirectory">The game directory.</param>
    /// <param name="gameId">The game identifier.</param>
    /// <param name="gameName">The game name.</param>
    /// <param name="manifestVersion">The manifest version.</param>
    /// <param name="executablePath">The path to the main executable.</param>
    /// <returns>The manifest builder.</returns>
    public async Task<IContentManifestBuilder> CreateGameClientManifestAsync(
        string gameDirectory,
        string gameId,
        string gameName,
        int manifestVersion,
        string executablePath)
    {
        _logger.LogInformation("Creating standalone game manifest for {GameName} v{ManifestVersion}", gameName, manifestVersion);

        var builder = CreateBuilder()
            .WithBasicInfo("EA Games", gameName, manifestVersion)
            .WithContentType(ContentType.GameClient, GameType.Generals)
            .WithMetadata($"Standalone game version: {gameName} (manifest version {manifestVersion})")
            .WithInstallationInstructions(WorkspaceStrategy.FullCopy);

        // Add all game files
        await builder.AddFilesFromDirectoryAsync(gameDirectory, ContentSourceType.ContentAddressable);

        // Mark the main executable
        await builder.AddLocalFileAsync(executablePath, string.Empty, ContentSourceType.ContentAddressable, isExecutable: true);

        return builder;
    }

    /// <summary>
    /// Creates a publisher referral manifest.
    /// </summary>
    /// <param name="publisherId">The publisher identifier used to generate the referral id.</param>
    /// <param name="referralName">Display name for the referral.</param>
    /// <param name="manifestVersion">Manifest version.</param>
    /// <param name="targetPublisherId">The target publisher id being referred to.</param>
    /// <param name="referralUrl">The URL for the referral.</param>
    /// <param name="description">Optional description for the referral.</param>
    /// <returns>The created <see cref="ContentManifest"/>.</returns>
    public Task<ContentManifest> CreatePublisherReferralAsync(
        string publisherId,
        string referralName,
        int manifestVersion,
        string targetPublisherId,
        string referralUrl,
        string description)
    {
        _logger.LogInformation(
            "Creating publisher referral {ReferralId} to {TargetPublisherId}",
            $"{publisherId}.{referralName}.{manifestVersion}",
            targetPublisherId);

        var referral = new ContentManifest
        {
            Id = ManifestId.Create($"{publisherId}.{referralName}.{manifestVersion}"),
            Name = referralName,
            Version = manifestVersion.ToString(),
            ContentType = ContentType.PublisherReferral,
            Metadata = new ContentMetadata
            {
                Description = description,
                ReleaseDate = DateTime.UtcNow,
            },
            Publisher = new PublisherInfo
            {
                Name = "System Generated",
                Website = referralUrl,
            },
        };

        return Task.FromResult(referral);
    }

    /// <summary>
    /// Creates a content referral manifest.
    /// </summary>
    /// <param name="publisherId">The publisher identifier used to generate the referral id.</param>
    /// <param name="referralName">Display name for the referral.</param>
    /// <param name="manifestVersion">Manifest version.</param>
    /// <param name="targetContentId">The id of the content being referred to.</param>
    /// <param name="targetPublisherId">The publisher id of the target content.</param>
    /// <param name="referralUrl">The URL for the referral.</param>
    /// <param name="description">Optional description for the referral.</param>
    /// <returns>The created <see cref="ContentManifest"/>.</returns>
    public Task<ContentManifest> CreateContentReferralAsync(
        string publisherId,
        string referralName,
        int manifestVersion,
        string targetContentId,
        string targetPublisherId,
        string referralUrl,
        string description)
    {
        _logger.LogInformation(
            "Creating content referral {ReferralId} to {TargetContentId}",
            $"{publisherId}.{referralName}.{manifestVersion}",
            targetContentId);

        var referral = new ContentManifest
        {
            Id = ManifestId.Create($"{publisherId}.{referralName}.{manifestVersion}"),
            Name = referralName,
            Version = manifestVersion.ToString(),
            ContentType = ContentType.ContentReferral,
            Metadata = new ContentMetadata
            {
                Description = description,
                ReleaseDate = DateTime.UtcNow,
            },
            Dependencies = new List<ContentDependency>
            {
                new ContentDependency
                {
                    Id = targetContentId,
                    Name = targetContentId,
                    DependencyType = ContentType.ContentReferral,
                    InstallBehavior = DependencyInstallBehavior.Suggest,
                },
            },
        };

        return Task.FromResult(referral);
    }

    /// <summary>
    /// Saves a manifest to the specified output path as JSON.
    /// </summary>
    /// <param name="manifest">The manifest to save.</param>
    /// <param name="outputPath">The output file path.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task SaveManifestAsync(ContentManifest manifest, string outputPath)
    {
        _logger.LogInformation("Saving manifest {ManifestId} to {OutputPath}", manifest.Id, outputPath);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions);

        _logger.LogInformation("Manifest saved successfully to {OutputPath}", outputPath);
    }

    /// <summary>
    /// Creates a new manifest builder instance.
    /// </summary>
    /// <returns>The manifest builder.</returns>
    private IContentManifestBuilder CreateBuilder()
    {
        return (IContentManifestBuilder?)_serviceProvider.GetService(typeof(IContentManifestBuilder))
               ?? throw new InvalidOperationException("IContentManifestBuilder service not registered");
    }

    /// <summary>
    /// Validates the items in a content bundle.
    /// </summary>
    /// <param name="bundle">The content bundle to validate.</param>
    private Task ValidateBundleItemsAsync(ContentBundle bundle)
    {
        foreach (var item in bundle.Items)
        {
            if (string.IsNullOrEmpty(item.ContentId))
            {
                throw new ArgumentException($"Bundle item missing ContentId in bundle {bundle.Id}");
            }

            _logger.LogDebug(
                "Validated bundle item {ContentId} in bundle {BundleId}",
                item.ContentId,
                bundle.Id);
        }

        return Task.CompletedTask;
    }
}
