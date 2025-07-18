using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GenHub.Core;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameVersions;
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
        string contentVersion,
        ContentType contentType,
        GameType targetGame,
        params ContentDependency[] dependencies)
    {
        _logger.LogInformation("Creating content manifest for {ContentName} v{ContentVersion}", contentName, contentVersion);

        var builder = CreateBuilder()
            .WithBasicInfo(contentId, contentName, contentVersion)
            .WithContentType(contentType, targetGame)
            .WithMetadata($"Content manifest for {contentName}");
        foreach (var dep in dependencies)
        {
            builder.AddDependency(
                dep.Id,
                dep.Name,
                dep.DependencyType,
                dep.InstallBehavior,
                dep.MinVersion,
                dep.MaxVersion,
                dep.CompatibleVersions,
                dep.IsExclusive,
                dep.ConflictsWith);
        }

        await builder.AddFilesFromDirectoryAsync(contentDirectory, ManifestFileSourceType.CopyUnique);
        return builder;
    }

    /// <summary>
    /// Creates a content bundle with the specified items and publisher.
    /// </summary>
    /// <param name="bundleId">The bundle identifier.</param>
    /// <param name="bundleName">The bundle name.</param>
    /// <param name="bundleVersion">The bundle version.</param>
    /// <param name="publisher">The publisher information.</param>
    /// <param name="items">The bundle items.</param>
    /// <returns>The created <see cref="ContentBundle"/>.</returns>
    public async Task<ContentBundle> CreateContentBundleAsync(
        string bundleId,
        string bundleName,
        string bundleVersion,
        PublisherInfo publisher,
        params BundleItem[] items)
    {
        _logger.LogInformation("Creating content bundle {BundleId} with {ItemCount} items", bundleId, items.Length);

        var bundle = new ContentBundle
        {
            Id = bundleId,
            Name = bundleName,
            Version = bundleVersion,
            Publisher = publisher,
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
    /// Creates a base game manifest for the specified game installation.
    /// </summary>
    /// <param name="gameInstallationPath">The path to the game installation.</param>
    /// <param name="gameType">The game type.</param>
    /// <param name="installationType">The installation type.</param>
    /// <param name="version">The game version.</param>
    /// <returns>The manifest builder.</returns>
    public async Task<IContentManifestBuilder> CreateBaseGameManifestAsync(
        string gameInstallationPath,
        GameType gameType,
        GameInstallationType installationType,
        string version)
    {
        _logger.LogInformation(
            "Creating base game manifest for {GameType} {InstallationType} v{Version}",
            gameType,
            installationType,
            version);

        var builder = CreateBuilder()
            .WithBasicInfo(
                $"{gameType}_{version}_{installationType}",
                $"{gameType} {version}",
                version)
            .WithContentType(ContentType.BaseGame, gameType)
            .WithPublisher(
                "EA Games",
                "https://www.ea.com",
                "https://help.ea.com",
                "support@ea.com")
            .WithMetadata($"Base game installation of {gameType} version {version} from {installationType}")
            .AddRequiredDirectories("Data", "Maps")
            .WithInstallationInstructions(WorkspaceStrategy.FullSymlink);

        // Add all game files
        await builder.AddFilesFromDirectoryAsync(gameInstallationPath, ManifestFileSourceType.LinkFromBase);

        return builder;
    }

    /// <summary>
    /// Creates a standalone game manifest for a given directory and executable.
    /// </summary>
    /// <param name="gameDirectory">The game directory.</param>
    /// <param name="gameId">The game identifier.</param>
    /// <param name="gameName">The game name.</param>
    /// <param name="gameVersion">The game version.</param>
    /// <param name="executablePath">The path to the main executable.</param>
    /// <returns>The manifest builder.</returns>
    public async Task<IContentManifestBuilder> CreateStandaloneGameManifestAsync(
        string gameDirectory,
        string gameId,
        string gameName,
        string gameVersion,
        string executablePath)
    {
        _logger.LogInformation("Creating standalone game manifest for {GameName} v{GameVersion}", gameName, gameVersion);

        var builder = CreateBuilder()
            .WithBasicInfo(gameId, gameName, gameVersion)
            .WithContentType(ContentType.StandaloneVersion, GameType.Generals)
            .WithMetadata($"Standalone game version: {gameName}")
            .WithInstallationInstructions(WorkspaceStrategy.FullCopy);

        // Add all game files
        await builder.AddFilesFromDirectoryAsync(gameDirectory, ManifestFileSourceType.CopyUnique);

        // Mark the main executable
        await builder.AddFileAsync(executablePath, ManifestFileSourceType.CopyUnique, string.Empty, true);

        return builder;
    }

    /// <summary>
    /// Creates a publisher referral manifest.
    /// </summary>
    /// <param name="referralId">The referral identifier.</param>
    /// <param name="referralName">The referral name.</param>
    /// <param name="targetPublisherId">The target publisher identifier.</param>
    /// <param name="referralUrl">The referral URL.</param>
    /// <param name="description">The description.</param>
    /// <returns>The created <see cref="GameManifest"/>.</returns>
    public Task<GameManifest> CreatePublisherReferralAsync(
        string referralId,
        string referralName,
        string targetPublisherId,
        string referralUrl,
        string description)
    {
        _logger.LogInformation(
            "Creating publisher referral {ReferralId} to {TargetPublisherId}",
            referralId,
            targetPublisherId);

        var referral = new GameManifest
        {
            Id = referralId,
            Name = referralName,
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
    /// <param name="referralId">The referral identifier.</param>
    /// <param name="referralName">The referral name.</param>
    /// <param name="targetContentId">The target content identifier.</param>
    /// <param name="targetPublisherId">The target publisher identifier.</param>
    /// <param name="referralUrl">The referral URL.</param>
    /// <param name="description">The description.</param>
    /// <returns>The created <see cref="GameManifest"/>.</returns>
    public Task<GameManifest> CreateContentReferralAsync(
        string referralId,
        string referralName,
        string targetContentId,
        string targetPublisherId,
        string referralUrl,
        string description)
    {
        _logger.LogInformation(
            "Creating content referral {ReferralId} to {TargetContentId}",
            referralId,
            targetContentId);

        var referral = new GameManifest
        {
            Id = referralId,
            Name = referralName,
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
    public async Task SaveManifestAsync(GameManifest manifest, string outputPath)
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
