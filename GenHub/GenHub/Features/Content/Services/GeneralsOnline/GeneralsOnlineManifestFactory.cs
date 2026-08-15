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
                    Hash = string.Empty,
                },
            ],
            Dependencies = GeneralsOnlineDependencyBuilder.GetDependenciesFor60Hz(userVersion),
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
    public async Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
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
    private static bool IsArchiveRootFile(string relativePath, string fileName) =>
        string.Equals(
            relativePath.Replace('\\', '/').TrimStart('/'),
            fileName,
            StringComparison.OrdinalIgnoreCase);

    private static ManifestFile CreateMapManifestFile(string relativePath, FileInfo fileInfo, string hash)
    {
        // For maps, the relative path should be relative to the Maps directory
        // e.g., "Maps/SomeMap/SomeMap.map" -> "SomeMap/SomeMap.map"
        var mapRelativePath = relativePath;
        if (relativePath.StartsWith(GeneralsOnlineConstants.MapsSubdirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            relativePath.StartsWith(GeneralsOnlineConstants.MapsSubdirectory + "/", StringComparison.OrdinalIgnoreCase))
        {
            mapRelativePath = relativePath[(GeneralsOnlineConstants.MapsSubdirectory.Length + 1)..];
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

                // MapPack requires Zero Hour installation
                GeneralsOnlineDependencyBuilder.CreateZeroHourDependencyForGeneralsOnline(),
            ],
        };
    }

    /// <summary>
    /// Creates all variant manifests (60Hz, MapPack) from the original manifest.
    /// This is called AFTER extraction - we use the original manifest's metadata to create variants.
    /// </summary>
    /// <param name="originalManifest">The manifest from the Resolver (contains version, publisher info, etc.).</param>
    /// <returns>List of variant manifests ready for file hash population.</returns>
    private List<ContentManifest> CreateVariantManifestsFromOriginal(ContentManifest originalManifest)
    {
        var manifests = new List<ContentManifest>();
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

        // Create 60Hz variant
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

        // Create QuickMatch MapPack
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
        });

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

        var allFiles = Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories);
        logger.LogInformation("Processing {Count} files", allFiles.Length);

        List<(string RelativePath, FileInfo FileInfo, string Hash, bool IsMap, bool IsGameData)> filesWithHashes = [];

        foreach (var filePath in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(extractPath, filePath);
            var fileInfo = new FileInfo(filePath);

            // Determine if this file is inside the Maps directory
            var isMap = relativePath.StartsWith(GeneralsOnlineConstants.MapsSubdirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        relativePath.StartsWith(GeneralsOnlineConstants.MapsSubdirectory + "/", StringComparison.OrdinalIgnoreCase);

            // Determine if this file is inside the GeneralsOnlineGameData directory
            var isGameData = relativePath.StartsWith(GeneralsOnlineConstants.GameDataSubdirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                             relativePath.StartsWith(GeneralsOnlineConstants.GameDataSubdirectory + "/", StringComparison.OrdinalIgnoreCase);

            var hash = string.Empty;
            using (var stream = File.OpenRead(filePath))
            {
                var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
                hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            filesWithHashes.Add((relativePath, fileInfo, hash, isMap, isGameData));
            logger.LogDebug("Processed file: {File} ({Size} bytes, hash: {Hash}, isMap: {IsMap}, isGameData: {IsGameData})", relativePath, fileInfo.Length, hash[..8], isMap, isGameData);
        }

        List<ContentManifest> updatedManifests = [];

        foreach (var manifest in manifests)
        {
            List<ManifestFile> manifestFiles = [];
            var isMapPackManifest = manifest.ContentType == ContentType.MapPack;
            var isPatchManifest = manifest.ContentType == ContentType.Patch;

            if (isMapPackManifest)
            {
                // MapPack manifest: only include map files with UserMapsDirectory install target
                foreach (var (relativePath, fileInfo, hash, isMap, isGameData) in filesWithHashes)
                {
                    if (!isMap)
                    {
                        continue;
                    }

                    manifestFiles.Add(CreateMapManifestFile(relativePath, fileInfo, hash));
                }

                logger.LogInformation("MapPack manifest '{Name}' updated with {Count} map files", manifest.Name, manifestFiles.Count);
            }
            else if (isPatchManifest)
            {
                // Data patch manifest: only include GeneralsOnlineGameData files with Workspace install target
                foreach (var (relativePath, fileInfo, hash, isMap, isGameData) in filesWithHashes)
                {
                    if (!isGameData)
                    {
                        continue;
                    }

                    manifestFiles.Add(CreateGameDataManifestFile(relativePath, fileInfo, hash));
                }

                logger.LogInformation("GameData patch manifest '{Name}' updated with {Count} files", manifest.Name, manifestFiles.Count);
            }
            else
            {
                // Game client manifest: include executables and shared files (skipping maps and game data files)
                // Since 060526_QFE1 the portable ships an Easy Anti-Cheat bootstrapper that starts the
                // binary named by EasyAntiCheat/Settings.json. When present it is the only launch target;
                // the wrapped binary stays in the workspace as ordinary content for EAC to start.
                var hasEacLauncher = filesWithHashes.Any(file =>
                    !file.IsMap && !file.IsGameData && IsArchiveRootFile(file.RelativePath, GameClientConstants.GeneralsOnlineEacLauncherExecutable));

                var targetExecutable = hasEacLauncher
                    ? GameClientConstants.GeneralsOnlineEacLauncherExecutable
                    : GameClientConstants.GeneralsOnline60HzExecutable;

                foreach (var (relativePath, fileInfo, hash, isMap, isGameData) in filesWithHashes)
                {
                    var isExecutable = false;

                    // Skip map files and game data files in GameClient manifests
                    if (isMap || isGameData)
                    {
                        continue;
                    }

                    if (IsArchiveRootFile(relativePath, targetExecutable))
                    {
                        isExecutable = true;
                    }

                    manifestFiles.Add(new ManifestFile
                    {
                        RelativePath = relativePath,
                        Size = fileInfo.Length,
                        Hash = hash,
                        SourceType = ContentSourceType.ContentAddressable,
                        SourcePath = fileInfo.FullName,
                        InstallTarget = ContentInstallTarget.Workspace,
                        IsExecutable = isExecutable,
                    });
                }

                logger.LogInformation("GameClient manifest '{Name}' updated with {Count} files", manifest.Name, manifestFiles.Count);
            }

            if (manifestFiles.Count == 0)
            {
                if (isMapPackManifest || isPatchManifest)
                {
                    logger.LogInformation(
                        "Skipping empty {Type} manifest '{Name}' because no matching files were found in extract path",
                        manifest.ContentType,
                        manifest.Name);
                    continue;
                }

                if (manifest.ContentType == ContentType.GameClient)
                {
                    logger.LogError(
                        "GameClient manifest '{Name}' has zero files in extract path '{ExtractPath}'",
                        manifest.Name,
                        extractPath);
                    throw new InvalidDataException(
                        $"GameClient manifest '{manifest.Name}' has no files in extract path '{extractPath}'.");
                }

                logger.LogError(
                    "Manifest '{Name}' of type {Type} has zero files in extract path '{ExtractPath}'",
                    manifest.Name,
                    manifest.ContentType,
                    extractPath);
                throw new InvalidDataException(
                    $"Manifest '{manifest.Name}' of type {manifest.ContentType} has no files in extract path '{extractPath}'.");
            }

            updatedManifests.Add(new ContentManifest
            {
                Id = manifest.Id,
                Name = manifest.Name,
                Version = manifest.Version,
                ContentType = manifest.ContentType,
                TargetGame = manifest.TargetGame,
                Publisher = manifest.Publisher,
                Metadata = manifest.Metadata,
                Files = manifestFiles,
                Dependencies = manifest.Dependencies,
            });
        }

        // If MapPack was not created from archive, remove MapPack dependency so dependency resolution does not fail
        var hasMapPack = updatedManifests.Any(m => m.ContentType == ContentType.MapPack);
        if (!hasMapPack)
        {
            foreach (var m in updatedManifests)
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

        return updatedManifests;
    }
}
