using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Post-extraction factory for Generals Online content manifests.
/// Computes file hashes, updates manifest entries, and creates variant manifests (60Hz, MapPack)
/// from the extracted archive content.
/// </summary>
public class GeneralsOnlineManifestFactory(
    ILogger<GeneralsOnlineManifestFactory> logger,
    IProviderDefinitionLoader providerLoader) : IPublisherManifestFactory
{
    /// <summary>
    /// File info extracted from archive for manifest generation.
    /// </summary>
    /// <param name="RelativePath">The relative path within archive.</param>
    /// <param name="FileInfo">The file info.</param>
    /// <param name="Hash">The SHA-256 hash.</param>
    /// <param name="IsMap">Whether this is a map file.</param>
    /// <param name="IsGameData">Whether this is a game data file.</param>
    private readonly record struct ExtractedFileInfo(
        string RelativePath,
        FileInfo FileInfo,
        string Hash,
        bool IsMap,
        bool IsGameData);

    /// <inheritdoc />
    public string PublisherId => PublisherTypeConstants.GeneralsOnline;

    /// <summary>
    /// Creates a content manifest for a specific Generals Online variant.
    /// </summary>
    /// <param name="release">The Generals Online release information.</param>
    /// <param name="variantSuffix">The suffix for the manifest ID (e.g., "60hz").</param>
    /// <param name="displayName">The display name for this variant (e.g., "GeneralsOnline 60Hz").</param>
    /// <returns>A content manifest for the specified variant.</returns>
    public ContentManifest CreateVariantManifest(
        GeneralsOnlineRelease release,
        string variantSuffix,
        string displayName)
    {
        var provider = providerLoader.GetProvider(PublisherTypeConstants.GeneralsOnline);
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? GeneralsOnlineConstants.WebsiteUrl;
        var supportUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.SupportUrl) ?? GeneralsOnlineConstants.SupportUrl;
        var downloadPageUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.DownloadPageUrl) ?? GeneralsOnlineConstants.DownloadPageUrl;
        var iconUrl = GeneralsOnlineConstants.LogoSource;
        var coverSource = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.CoverUrl) ?? GeneralsOnlineConstants.CoverSource;
        var description = provider?.Description ?? GeneralsOnlineConstants.ShortDescription;
        var tags = provider?.DefaultTags ?? [.. GeneralsOnlineConstants.Tags];

        // Parse version to extract numeric version (remove dots and QFE markers)
        var userVersion = ParseVersionForManifestId(release.Version);

        // Content name for GeneralsOnline (publisher is "generalsonline", content is the variant)
        // This will create IDs like: 1.1015255.generalsonline.gameclient.60hz
        var contentName = variantSuffix;

        var manifestId = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
            PublisherTypeConstants.GeneralsOnline,
            ContentType.GameClient,
            contentName,
            userVersion));

        return new ContentManifest
        {
            Id = manifestId,
            Name = displayName,
            Version = release.Version,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                Name = GeneralsOnlineConstants.PublisherName,
                PublisherType = PublisherTypeConstants.GeneralsOnline,
                Website = websiteUrl,
                SupportUrl = supportUrl,
                ContentIndexUrl = downloadPageUrl,
                UpdateCheckIntervalHours = GeneralsOnlineConstants.UpdateCheckIntervalHours,
            },
            Metadata = new ContentMetadata
            {
                Description = description,
                ReleaseDate = release.ReleaseDate,
                IconUrl = iconUrl,
                CoverUrl = coverSource,
                ThemeColor = GeneralsOnlineConstants.ThemeColor,
                Tags = [.. tags, .. GetVariantTags(variantSuffix)],
                ChangelogUrl = release.Changelog,
            },
            Files =
            [
                new ManifestFile
                {
                    RelativePath = Path.GetFileName(release.PortableUrl),
                    DownloadUrl = release.PortableUrl,
                    Size = release.PortableSize ?? 0, // Use 0 when size is unknown
                    SourceType = ContentSourceType.RemoteDownload,
                    Hash = release.Sha256 ?? string.Empty,
                },
            ],
            Dependencies = GeneralsOnlineDependencyBuilder.GetDependenciesFor60Hz(userVersion),
            InstallationInstructions = new InstallationInstructions
            {
                WorkspaceStrategy = WorkspaceConstants.DefaultWorkspaceStrategy,
                DownloadHash = release.Sha256,
                PostInstallSteps =
                [
                    new InstallationStep
                    {
                        Name = GeneralsOnlineConstants.EacStepName,
                        Kind = InstallationStepKind.RunVerifiedInstaller,
                        TargetRelativePath = GameClientConstants.GeneralsOnlineEacSetupExecutable,
                        Arguments = [GeneralsOnlineConstants.EacInstallCommand, GeneralsOnlineConstants.EacProductId],
                        RequiresElevation = true,
                        StatusMessage = GeneralsOnlineConstants.EacStatusMessage,
                        StepKey = GeneralsOnlineConstants.EacStepKey,
                        RunOnce = true,
                    },
                ],
            },
        };
    }

    /// <summary>
    /// Creates content manifests from a GeneralsOnline release:
    /// - 60Hz game client variant
    /// - QuickMatch MapPack (required for multiplayer)
    /// - GeneralsOnlineGameData data patch (optional patch, depends on 60Hz game client)
    /// This creates the initial manifests with download URLs.
    /// </summary>
    /// <param name="release">The GeneralsOnlineRelease to create the manifests from.</param>
    /// <returns>A list containing the ContentManifest instances.</returns>
    public List<ContentManifest> CreateManifests(GeneralsOnlineRelease release)
    {
        List<ContentManifest> manifests = [];

        // Create manifest for 60Hz variant
        manifests.Add(CreateVariantManifest(release, GeneralsOnlineConstants.Variant60HzSuffix, GameClientConstants.GeneralsOnline60HzDisplayName));

        // Create manifest for QuickMatch MapPack (required dependency for game client)
        manifests.Add(CreateQuickMatchMapPackManifest(release));

        // Create manifest for GeneralsOnlineGameData data patch (optional patch, depends on 60Hz game client)
        manifests.Add(CreateGameDataPatchManifest(release));

        return manifests;
    }

    /// <inheritdoc />
    public bool CanHandle(ContentManifest manifest)
    {
        var publisherMatches = string.Equals(manifest.Publisher?.PublisherType, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase);
        var isGameClient = manifest.ContentType == ContentType.GameClient;
        var isMapPack = manifest.ContentType == ContentType.MapPack;
        var isPatch = manifest.ContentType == ContentType.Patch;
        return publisherMatches && (isGameClient || isMapPack || isPatch);
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
        logger.LogInformation("Creating GeneralsOnline manifests from extracted content in: {Directory}", extractedDirectory);

        // Create all variant manifests (60Hz, QuickMatch MapPack, and GeneralsOnlineGameData data patch) from extracted files
        var manifests = CreateVariantManifestsFromOriginal(originalManifest);

        // Update manifests with extracted files (compute hashes, set file entries)
        return await UpdateManifestsWithExtractedFiles(manifests, extractedDirectory, cancellationToken);
    }

    /// <inheritdoc />
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        // GeneralsOnline uses the root extracted directory for all variants
        return extractedDirectory;
    }

    /// <summary>
    /// Creates manifests from an existing local installation without downloading.
    /// This is used when importing manually.
    /// </summary>
    /// <param name="installationPath">The path to the local installation directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of created content manifests.</returns>
    public async Task<List<ContentManifest>> CreateManifestsFromLocalInstallAsync(
        string installationPath,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Creating GeneralsOnline manifests from local install at: {Path}", installationPath);

        // Verify key files exist
        var has60Hz = File.Exists(Path.Combine(installationPath, GameClientConstants.GeneralsOnline60HzExecutable));

        if (!has60Hz)
        {
            logger.LogWarning("No GeneralsOnline executables found in {Path}", installationPath);
            return [];
        }

        // Create a synthetic release object
        var release = new GeneralsOnlineRelease
        {
            Version = GameClientConstants.AutoDetectedVersion,
            VersionDate = DateTime.UtcNow,
            ReleaseDate = DateTime.UtcNow,
            PortableUrl = string.Empty,
            PortableSize = 0,
            Changelog = string.Empty,
        };

        // Create the base manifests
        var manifests = CreateManifests(release);

        // Update with file hashes from the installation
        return await UpdateManifestsWithExtractedFiles(manifests, installationPath, cancellationToken);
    }

    private static int ParseVersionForManifestId(string version) => GameVersionHelper.GetGeneralsOnlineManifestIdComponent(version);

    /// <summary>
    /// Determines whether a manifest-relative path is the named file at the archive root.
    /// Nested files that merely share the name are not the published entry point.
    /// </summary>
    /// <param name="relativePath">The candidate path.</param>
    /// <param name="fileName">The target file name.</param>
    /// <returns><c>true</c> if the path resolves to <paramref name="fileName"/> at the root; otherwise, <c>false</c>.</returns>
    private static bool IsArchiveRootFile(string relativePath, string fileName) =>
        string.Equals(
            relativePath.Replace('\\', '/').TrimStart('/'),
            fileName,
            StringComparison.OrdinalIgnoreCase);

    private static ManifestFile CreateMapManifestFile(string relativePath, FileInfo fileInfo, string hash)
    {
        // For maps, relative path should be relative to the Maps directory if present
        var mapRelativePath = relativePath;
        var pathSegments = relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        var mapsIndex = Array.FindIndex(pathSegments, s => s.Equals(GeneralsOnlineConstants.MapsSubdirectory, StringComparison.OrdinalIgnoreCase));
        if (mapsIndex >= 0 && mapsIndex < pathSegments.Length - 1)
        {
            mapRelativePath = string.Join(Path.DirectorySeparatorChar, pathSegments[(mapsIndex + 1)..]);
        }

        return new ManifestFile
        {
            RelativePath = mapRelativePath,
            Size = fileInfo.Length,
            Hash = hash,
            SourceType = ContentSourceType.ContentAddressable,
            SourcePath = fileInfo.FullName,
            InstallTarget = ContentInstallTarget.UserMapsDirectory,
            IsExecutable = false,
        };
    }

    private static ManifestFile CreateGameDataManifestFile(string relativePath, FileInfo fileInfo, string hash)
    {
        return new ManifestFile
        {
            RelativePath = relativePath,
            Size = fileInfo.Length,
            Hash = hash,
            SourceType = ContentSourceType.ContentAddressable,
            SourcePath = fileInfo.FullName,
            InstallTarget = ContentInstallTarget.UserDataDirectory,
            IsExecutable = false,
        };
    }

    /// <summary>
    /// Gets variant-specific tags for a given variant suffix.
    /// </summary>
    /// <param name="variantSuffix">The variant suffix (e.g., "60hz").</param>
    /// <returns>A list of variant-specific tags.</returns>
    private static List<string> GetVariantTags(string variantSuffix)
    {
        if (variantSuffix.Equals(GeneralsOnlineConstants.Variant60HzSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return [GeneralsOnlineVariantTags.Tag60Hz];
        }

        if (variantSuffix.Equals(GeneralsOnlineConstants.QuickMatchMapPackSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return [GeneralsOnlineVariantTags.TagQuickMatchMaps];
        }

        if (variantSuffix.Equals(GeneralsOnlineConstants.GameDataPatchSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return [GeneralsOnlineVariantTags.TagGameData];
        }

        return [];
    }

    /// <summary>
    /// Creates a content manifest for the GeneralsOnlineGameData data patch.
    /// This manifest contains game data files (community balance patch and core INI configuration).
    /// </summary>
    /// <param name="release">The Generals Online release information.</param>
    /// <returns>A content manifest for the GeneralsOnlineGameData data patch.</returns>
    private ContentManifest CreateGameDataPatchManifest(GeneralsOnlineRelease release)
    {
        var provider = providerLoader.GetProvider(PublisherTypeConstants.GeneralsOnline);
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? GeneralsOnlineConstants.WebsiteUrl;
        var supportUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.SupportUrl) ?? GeneralsOnlineConstants.SupportUrl;
        var downloadPageUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.DownloadPageUrl) ?? GeneralsOnlineConstants.DownloadPageUrl;
        var iconUrl = GeneralsOnlineConstants.LogoSource;
        var userVersion = ParseVersionForManifestId(release.Version);
        var manifestId = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
            PublisherTypeConstants.GeneralsOnline,
            ContentType.Patch,
            GeneralsOnlineConstants.GameDataPatchSuffix,
            userVersion));

        return new ContentManifest
        {
            Id = manifestId,
            Name = GeneralsOnlineConstants.GameDataDisplayName,
            Version = release.Version,
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                Name = GeneralsOnlineConstants.PublisherName,
                PublisherType = PublisherTypeConstants.GeneralsOnline,
                Website = websiteUrl,
                SupportUrl = supportUrl,
                ContentIndexUrl = downloadPageUrl,
                UpdateCheckIntervalHours = GeneralsOnlineConstants.UpdateCheckIntervalHours,
            },
            Metadata = new ContentMetadata
            {
                Description = GeneralsOnlineConstants.GameDataDescription,
                ReleaseDate = release.ReleaseDate,
                IconUrl = iconUrl,
                ThemeColor = GeneralsOnlineConstants.ThemeColor,
                Tags = [.. GeneralsOnlineConstants.GameDataTags, .. GetVariantTags(GeneralsOnlineConstants.GameDataPatchSuffix)],
                ChangelogUrl = release.Changelog,
            },

            // Files will be populated during extraction
            Files = [],
            Dependencies = GeneralsOnlineDependencyBuilder.GetDependenciesForGameData(userVersion),
            InstallationInstructions = new InstallationInstructions(),
        };
    }

    /// <summary>
    /// Creates a content manifest for the QuickMatch MapPack.
    /// This manifest contains all maps required for GeneralsOnline QuickMatch multiplayer.
    /// </summary>
    /// <param name="release">The Generals Online release information.</param>
    /// <returns>A content manifest for the QuickMatch MapPack.</returns>
    private ContentManifest CreateQuickMatchMapPackManifest(GeneralsOnlineRelease release)
    {
        var provider = providerLoader.GetProvider(PublisherTypeConstants.GeneralsOnline);
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? GeneralsOnlineConstants.WebsiteUrl;
        var supportUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.SupportUrl) ?? GeneralsOnlineConstants.SupportUrl;
        var downloadPageUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.DownloadPageUrl) ?? GeneralsOnlineConstants.DownloadPageUrl;
        var iconUrl = GeneralsOnlineConstants.LogoSource;
        var userVersion = ParseVersionForManifestId(release.Version);
        var manifestId = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
            PublisherTypeConstants.GeneralsOnline,
            ContentType.MapPack,
            GeneralsOnlineConstants.QuickMatchMapPackSuffix,
            userVersion));

        return new ContentManifest
        {
            Id = manifestId,
            Name = GeneralsOnlineConstants.QuickMatchMapPackDisplayName,
            Version = release.Version,
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                Name = GeneralsOnlineConstants.PublisherName,
                PublisherType = PublisherTypeConstants.GeneralsOnline,
                Website = websiteUrl,
                SupportUrl = supportUrl,
                ContentIndexUrl = downloadPageUrl,
                UpdateCheckIntervalHours = GeneralsOnlineConstants.UpdateCheckIntervalHours,
            },
            Metadata = new ContentMetadata
            {
                Description = GeneralsOnlineConstants.QuickMatchMapPackDescription,
                ReleaseDate = release.ReleaseDate,
                IconUrl = iconUrl,
                ThemeColor = GeneralsOnlineConstants.ThemeColor,
                Tags = [.. GeneralsOnlineConstants.MapPackTags],
                ChangelogUrl = release.Changelog,
            },
            Files = [], // Files will be populated during extraction
            Dependencies =
            [
                GeneralsOnlineDependencyBuilder.CreateZeroHourDependencyForGeneralsOnline(),
            ],
            InstallationInstructions = new InstallationInstructions(),
        };
    }

    /// <summary>
    /// Creates variant manifests (60Hz, QuickMatch MapPack, and GeneralsOnlineGameData data patch) from an original manifest.
    /// This is used after downloading and extracting the portable ZIP.
    /// </summary>
    /// <param name="originalManifest">The original manifest (can be 60Hz or generic).</param>
    /// <returns>List of variant manifests with basic information populated.</returns>
    private List<ContentManifest> CreateVariantManifestsFromOriginal(ContentManifest originalManifest)
    {
        List<ContentManifest> manifests = [];
        var version = originalManifest.Version ?? GeneralsOnlineConstants.UnknownVersion;
        var userVersion = ParseVersionForManifestId(version);

        // Get URLs from provider definition (prefer original manifest metadata if available)
        var provider = providerLoader.GetProvider(PublisherTypeConstants.GeneralsOnline);
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? GeneralsOnlineConstants.WebsiteUrl;
        var supportUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.SupportUrl) ?? GeneralsOnlineConstants.SupportUrl;
        var downloadPageUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.DownloadPageUrl) ?? GeneralsOnlineConstants.DownloadPageUrl;
        var iconUrl = GeneralsOnlineConstants.LogoSource;

        // Create publisher info once (shared by all variants)
        var publisherInfo = new PublisherInfo
        {
            Name = GeneralsOnlineConstants.PublisherName,
            PublisherType = PublisherTypeConstants.GeneralsOnline,
            Website = websiteUrl,
            SupportUrl = supportUrl,
            ContentIndexUrl = downloadPageUrl,
            UpdateCheckIntervalHours = GeneralsOnlineConstants.UpdateCheckIntervalHours,
        };

        // Create metadata template
        var releaseDate = originalManifest.Metadata?.ReleaseDate ?? DateTime.UtcNow;
        var changelogUrl = originalManifest.Metadata?.ChangelogUrl;
        var isOriginalMapPack = originalManifest.ContentType == ContentType.MapPack ||
                                 originalManifest.Id.Value.Contains("mappack", StringComparison.OrdinalIgnoreCase) ||
                                 originalManifest.Id.Value.Contains("quickmatch", StringComparison.OrdinalIgnoreCase);

        if (isOriginalMapPack)
        {
            // When acquiring a MapPack specifically, place MapPack FIRST and preserve the original manifest ID
            manifests.Add(new ContentManifest
            {
                Id = originalManifest.Id,
                Name = originalManifest.Name ?? GeneralsOnlineConstants.QuickMatchMapPackDisplayName,
                Version = version,
                ContentType = ContentType.MapPack,
                TargetGame = GameType.ZeroHour,
                Publisher = publisherInfo,
                OriginalProviderName = originalManifest.OriginalProviderName,
                OriginalContentId = originalManifest.OriginalContentId,
                Metadata = originalManifest.Metadata ?? new ContentMetadata
                {
                    Description = GeneralsOnlineConstants.QuickMatchMapPackDescription,
                    ReleaseDate = releaseDate,
                    IconUrl = iconUrl,
                    ThemeColor = GeneralsOnlineConstants.ThemeColor,
                    Tags = [.. GeneralsOnlineConstants.MapPackTags, .. GetVariantTags(GeneralsOnlineConstants.QuickMatchMapPackSuffix)],
                    ChangelogUrl = changelogUrl,
                },
                Files = [],
                Dependencies =
                [
                    GeneralsOnlineDependencyBuilder.CreateZeroHourDependencyForGeneralsOnline(),
                ],
            });

            // Also include 60Hz variant as secondary option
            manifests.Add(new ContentManifest
            {
                Id = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
                    PublisherTypeConstants.GeneralsOnline,
                    ContentType.GameClient,
                    GeneralsOnlineConstants.Variant60HzSuffix,
                    userVersion)),
                Name = GameClientConstants.GeneralsOnline60HzDisplayName,
                Version = version,
                ContentType = ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
                Publisher = publisherInfo,
                OriginalProviderName = originalManifest.OriginalProviderName,
                OriginalContentId = originalManifest.OriginalContentId,
                Metadata = new ContentMetadata
                {
                    Description = GeneralsOnlineConstants.ShortDescription,
                    ReleaseDate = releaseDate,
                    IconUrl = iconUrl,
                    ThemeColor = GeneralsOnlineConstants.ThemeColor,
                    Tags = [.. GeneralsOnlineConstants.Tags, .. GetVariantTags(GeneralsOnlineConstants.Variant60HzSuffix)],
                    ChangelogUrl = changelogUrl,
                    CoverUrl = GeneralsOnlineConstants.CoverSource,
                },
                Files = [],
                Dependencies = GeneralsOnlineDependencyBuilder.GetDependenciesFor60Hz(userVersion),
            });
        }
        else
        {
            // Standard GameClient acquisition: 60Hz first, QuickMatch MapPack second
            manifests.Add(new ContentManifest
            {
                Id = originalManifest.ContentType == ContentType.GameClient ? originalManifest.Id : ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
                    PublisherTypeConstants.GeneralsOnline,
                    ContentType.GameClient,
                    GeneralsOnlineConstants.Variant60HzSuffix,
                    userVersion)),
                Name = GameClientConstants.GeneralsOnline60HzDisplayName,
                Version = version,
                ContentType = ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
                Publisher = publisherInfo,
                OriginalProviderName = originalManifest.OriginalProviderName,
                OriginalContentId = originalManifest.OriginalContentId,
                Metadata = new ContentMetadata
                {
                    Description = GeneralsOnlineConstants.ShortDescription,
                    ReleaseDate = releaseDate,
                    IconUrl = iconUrl,
                    ThemeColor = GeneralsOnlineConstants.ThemeColor,
                    Tags = [.. GeneralsOnlineConstants.Tags, .. GetVariantTags(GeneralsOnlineConstants.Variant60HzSuffix)],
                    ChangelogUrl = changelogUrl,
                    CoverUrl = GeneralsOnlineConstants.CoverSource,
                },
                Files = [],
                Dependencies = GeneralsOnlineDependencyBuilder.GetDependenciesFor60Hz(userVersion),
            });

            manifests.Add(new ContentManifest
            {
                Id = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
                    PublisherTypeConstants.GeneralsOnline,
                    ContentType.MapPack,
                    GeneralsOnlineConstants.QuickMatchMapPackSuffix,
                    userVersion)),
                Name = GeneralsOnlineConstants.QuickMatchMapPackDisplayName,
                Version = version,
                ContentType = ContentType.MapPack,
                TargetGame = GameType.ZeroHour,
                Publisher = publisherInfo,
                OriginalProviderName = originalManifest.OriginalProviderName,
                OriginalContentId = originalManifest.OriginalContentId,
                Metadata = new ContentMetadata
                {
                    Description = GeneralsOnlineConstants.QuickMatchMapPackDescription,
                    ReleaseDate = releaseDate,
                    IconUrl = iconUrl,
                    ThemeColor = GeneralsOnlineConstants.ThemeColor,
                    Tags = [.. GeneralsOnlineConstants.MapPackTags, .. GetVariantTags(GeneralsOnlineConstants.QuickMatchMapPackSuffix)],
                    ChangelogUrl = changelogUrl,
                },
                Files = [],
                Dependencies =
                [
                    GeneralsOnlineDependencyBuilder.CreateZeroHourDependencyForGeneralsOnline(),
                ],
                InstallationInstructions = new InstallationInstructions(),
            });
        }

        // Create GeneralsOnlineGameData data patch
        manifests.Add(new ContentManifest
        {
            Id = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
                PublisherTypeConstants.GeneralsOnline,
                ContentType.Patch,
                GeneralsOnlineConstants.GameDataPatchSuffix,
                userVersion)),
            Name = GeneralsOnlineConstants.GameDataDisplayName,
            Version = version,
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
            Publisher = publisherInfo,
            Metadata = new ContentMetadata
            {
                Description = GeneralsOnlineConstants.GameDataDescription,
                ReleaseDate = releaseDate,
                IconUrl = iconUrl,
                ThemeColor = GeneralsOnlineConstants.ThemeColor,
                Tags = [.. GeneralsOnlineConstants.GameDataTags, .. GetVariantTags(GeneralsOnlineConstants.GameDataPatchSuffix)],
                ChangelogUrl = changelogUrl,
            },
            Files = [],
            Dependencies = GeneralsOnlineDependencyBuilder.GetDependenciesForGameData(userVersion),
            InstallationInstructions = new InstallationInstructions(),
        });

        return manifests;
    }

    /// <summary>
    /// Updates manifests (60Hz, QuickMatch MapPack, and GeneralsOnlineGameData data patch) with extracted file information.
    /// Computes SHA-256 hashes for all files for CAS integration.
    /// Each variant gets only the files it needs plus shared files.
    /// Maps are extracted to the MapPack manifest with UserMapsDirectory install target.
    /// Game data files are extracted to the Patch manifest with UserDataDirectory install target.
    /// </summary>
    /// <param name="manifests">The original content manifests to update.</param>
    /// <param name="extractPath">The path to the directory containing extracted files.</param>
    /// <param name="cancellationToken">Token to cancel the operation if needed.</param>
    /// <returns>Updated content manifests with file hashes and details.</returns>
    private async Task<List<ContentManifest>> UpdateManifestsWithExtractedFiles(
        List<ContentManifest> manifests,
        string extractPath,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Updating manifests with extracted files from: {Path}", extractPath);

        cancellationToken.ThrowIfCancellationRequested();

        var filesWithHashes = await ScanExtractedFilesAsync(extractPath, cancellationToken);
        var updatedManifests = new List<ContentManifest>();

        foreach (var manifest in manifests)
        {
            var manifestFiles = BuildManifestFilesForManifest(manifest, filesWithHashes);

            if (manifestFiles.Count == 0)
            {
                if (manifest.ContentType is ContentType.MapPack or ContentType.Patch)
                {
                    logger.LogInformation(
                        "Skipping empty {Type} manifest '{Name}' because no matching files were found in extract path",
                        manifest.ContentType,
                        manifest.Name);
                    continue;
                }

                logger.LogError(
                    "Manifest '{Name}' of type {Type} has zero files in extract path '{ExtractPath}'",
                    manifest.Name,
                    manifest.ContentType,
                    extractPath);
                throw new InvalidDataException(
                    $"Manifest '{manifest.Name}' of type {manifest.ContentType} has no files in extract path '{extractPath}'.");
            }

            var instructions = BuildInstallationInstructions(manifest, filesWithHashes);

            updatedManifests.Add(new ContentManifest
            {
                Id = manifest.Id,
                Name = manifest.Name,
                Version = manifest.Version,
                ContentType = manifest.ContentType,
                TargetGame = manifest.TargetGame,
                Publisher = manifest.Publisher,
                OriginalProviderName = manifest.OriginalProviderName,
                OriginalContentId = manifest.OriginalContentId,
                Metadata = manifest.Metadata,
                Files = manifestFiles,
                Dependencies = manifest.Dependencies,
                InstallationInstructions = instructions,
            });
        }

        ReconcileMissingMapPackDependencies(updatedManifests);

        return updatedManifests;
    }

    private async Task<List<ExtractedFileInfo>> ScanExtractedFilesAsync(
        string extractPath,
        CancellationToken cancellationToken)
    {
        var allFiles = Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories);
        logger.LogInformation("Processing {Count} files", allFiles.Length);

        var filesWithHashes = new List<ExtractedFileInfo>(allFiles.Length);

        foreach (var filePath in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(extractPath, filePath);
            var fileInfo = new FileInfo(filePath);

            var isMap = relativePath.StartsWith(GeneralsOnlineConstants.MapsSubdirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        relativePath.StartsWith(GeneralsOnlineConstants.MapsSubdirectory + "/", StringComparison.OrdinalIgnoreCase);

            var isGameData = relativePath.StartsWith(GeneralsOnlineConstants.GameDataSubdirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                             relativePath.StartsWith(GeneralsOnlineConstants.GameDataSubdirectory + "/", StringComparison.OrdinalIgnoreCase);

            var hash = string.Empty;
            using (var stream = File.OpenRead(filePath))
            {
                var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
                hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            filesWithHashes.Add(new ExtractedFileInfo(relativePath, fileInfo, hash, isMap, isGameData));
            logger.LogDebug("Processed file: {File} ({Size} bytes, hash: {Hash}, isMap: {IsMap}, isGameData: {IsGameData})", relativePath, fileInfo.Length, hash[..8], isMap, isGameData);
        }

        return filesWithHashes;
    }

    private List<ManifestFile> BuildManifestFilesForManifest(
        ContentManifest manifest,
        List<ExtractedFileInfo> filesWithHashes)
    {
        var manifestFiles = new List<ManifestFile>();

        if (manifest.ContentType == ContentType.MapPack)
        {
            foreach (var file in filesWithHashes)
            {
                if (file.IsMap)
                {
                    manifestFiles.Add(CreateMapManifestFile(file.RelativePath, file.FileInfo, file.Hash));
                }
            }

            logger.LogInformation("MapPack manifest '{Name}' updated with {Count} map files", manifest.Name, manifestFiles.Count);
        }
        else if (manifest.ContentType == ContentType.Patch)
        {
            foreach (var file in filesWithHashes)
            {
                if (file.IsGameData)
                {
                    manifestFiles.Add(CreateGameDataManifestFile(file.RelativePath, file.FileInfo, file.Hash));
                }
            }

            logger.LogInformation("GameData patch manifest '{Name}' updated with {Count} files", manifest.Name, manifestFiles.Count);
        }
        else
        {
            var hasEacLauncher = filesWithHashes.Any(file =>
                !file.IsMap && !file.IsGameData && IsArchiveRootFile(file.RelativePath, GameClientConstants.GeneralsOnlineEacLauncherExecutable));

            var targetExecutable = hasEacLauncher
                ? GameClientConstants.GeneralsOnlineEacLauncherExecutable
                : GameClientConstants.GeneralsOnline60HzExecutable;

            foreach (var file in filesWithHashes)
            {
                if (file.IsMap || file.IsGameData)
                {
                    continue;
                }

                var isExecutable = IsArchiveRootFile(file.RelativePath, targetExecutable);

                manifestFiles.Add(new ManifestFile
                {
                    RelativePath = file.RelativePath,
                    Size = file.FileInfo.Length,
                    Hash = file.Hash,
                    SourceType = ContentSourceType.ContentAddressable,
                    SourcePath = file.FileInfo.FullName,
                    InstallTarget = ContentInstallTarget.Workspace,
                    IsExecutable = isExecutable,
                });
            }

            logger.LogInformation("GameClient manifest '{Name}' updated with {Count} files", manifest.Name, manifestFiles.Count);
        }

        return manifestFiles;
    }

    private InstallationInstructions BuildInstallationInstructions(
        ContentManifest manifest,
        List<ExtractedFileInfo> filesWithHashes)
    {
        var hasEacSetup = filesWithHashes.Any(file =>
            !file.IsMap && !file.IsGameData &&
            IsArchiveRootFile(file.RelativePath, GameClientConstants.GeneralsOnlineEacSetupExecutable));

        var inheritedPostSteps = (manifest.InstallationInstructions?.PostInstallSteps ?? [])
            .Where(s => s != null && (hasEacSetup || !string.Equals(
                s.TargetRelativePath,
                GameClientConstants.GeneralsOnlineEacSetupExecutable,
                StringComparison.OrdinalIgnoreCase)));

        var instructions = new InstallationInstructions
        {
            WorkspaceStrategy = manifest.InstallationInstructions?.WorkspaceStrategy ?? WorkspaceConstants.DefaultWorkspaceStrategy,
            DownloadHash = manifest.InstallationInstructions?.DownloadHash,
            PostInstallSteps = [.. inheritedPostSteps],
        };

        if (manifest.ContentType == ContentType.GameClient &&
            hasEacSetup &&
            instructions.PostInstallSteps.All(s => s == null || (!string.Equals(s.TargetRelativePath, GameClientConstants.GeneralsOnlineEacSetupExecutable, StringComparison.OrdinalIgnoreCase) && !string.Equals(s.StepKey, GeneralsOnlineConstants.EacStepKey, StringComparison.OrdinalIgnoreCase))))
        {
            instructions.PostInstallSteps.Add(new InstallationStep
            {
                Name = GeneralsOnlineConstants.EacStepName,
                Kind = InstallationStepKind.RunVerifiedInstaller,
                TargetRelativePath = GameClientConstants.GeneralsOnlineEacSetupExecutable,
                Arguments = [GeneralsOnlineConstants.EacInstallCommand, GeneralsOnlineConstants.EacProductId],
                RequiresElevation = true,
                StatusMessage = GeneralsOnlineConstants.EacStatusMessage,
                StepKey = GeneralsOnlineConstants.EacStepKey,
                RunOnce = true,
            });
        }

        return instructions;
    }

    private void ReconcileMissingMapPackDependencies(List<ContentManifest> manifests)
    {
        var hasMapPack = manifests.Any(m => m.ContentType == ContentType.MapPack);
        if (hasMapPack)
        {
            return;
        }

        foreach (var m in manifests)
        {
            if (m.Dependencies.Any(d => d.DependencyType == ContentType.MapPack))
            {
                logger.LogWarning(
                    "Removing MapPack dependency from manifest '{Name}' because MapPack was not found in archive",
                    m.Name);
                m.Dependencies = m.Dependencies.Where(d => d.DependencyType != ContentType.MapPack).ToList();
            }
        }
    }
}
