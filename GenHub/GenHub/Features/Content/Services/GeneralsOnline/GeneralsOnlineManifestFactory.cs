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
/// Computes file hashes, updates manifest entries, and creates variant manifests (30Hz, 60Hz, MapPack)
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
    /// <param name="variantSuffix">The suffix for the manifest ID (e.g., "30hz").</param>
    /// <param name="displayName">The display name for this variant (e.g., "GeneralsOnline 30Hz").</param>
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
        // This will create IDs like: 1.1015255.generalsonline.gameclient.30hz
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
                Tags = [.. tags],
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
            Dependencies = variantSuffix == GeneralsOnlineConstants.Variant60HzSuffix
                ? GeneralsOnlineDependencyBuilder.GetDependenciesFor60Hz(userVersion)
                : GeneralsOnlineDependencyBuilder.GetDependenciesFor30Hz(userVersion),
        };
    }

    /// <summary>
    /// Creates three content manifests from a GeneralsOnline release:
    /// - 30Hz game client variant
    /// - 60Hz game client variant
    /// - QuickMatch MapPack (required for multiplayer)
    /// This creates the initial manifests with download URLs.
    /// </summary>
    /// <param name="release">The GeneralsOnlineRelease to create the manifests from.</param>
    /// <returns>A list containing three ContentManifest instances.</returns>
    public List<ContentManifest> CreateManifests(GeneralsOnlineRelease release)
    {
        List<ContentManifest> manifests = [];

        // Create manifest for 30Hz variant
        manifests.Add(CreateVariantManifest(release, GeneralsOnlineConstants.Variant30HzSuffix, GameClientConstants.GeneralsOnline30HzDisplayName));

        // Create manifest for 60Hz variant
        manifests.Add(CreateVariantManifest(release, GeneralsOnlineConstants.Variant60HzSuffix, GameClientConstants.GeneralsOnline60HzDisplayName));

        // Create manifest for QuickMatch MapPack (required dependency for both variants)
        manifests.Add(CreateQuickMatchMapPackManifest(release));

        return manifests;
    }

    /// <inheritdoc />
    public bool CanHandle(ContentManifest manifest)
    {
        var publisherMatches = manifest.Publisher?.PublisherType?.Equals(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) == true;
        var isGameClient = manifest.ContentType == ContentType.GameClient;
        var isMapPack = manifest.ContentType == ContentType.MapPack;
        return publisherMatches && (isGameClient || isMapPack);
    }

    /// <inheritdoc />
    public async Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Creating GeneralsOnline manifests from extracted content in: {Directory}", extractedDirectory);

        // Create all variant manifests (30Hz, 60Hz, and QuickMatch MapPack) from extracted files
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
        var has30Hz = File.Exists(Path.Combine(installationPath, GameClientConstants.GeneralsOnline30HzExecutable));
        var has60Hz = File.Exists(Path.Combine(installationPath, GameClientConstants.GeneralsOnline60HzExecutable));

        if (!has30Hz && !has60Hz)
        {
            logger.LogWarning("No GeneralsOnline executables found in {Path}", installationPath);
            return [];
        }

        // Create a synthetic release object
        var release = new GeneralsOnlineRelease
        {
            Version = GameClientConstants.AutoDetectedVersion,
            VersionDate = DateTime.Now,
            ReleaseDate = DateTime.Now,
            PortableUrl = string.Empty,
            PortableSize = 0,
            Changelog = string.Empty,
        };

        // Create the base manifests
        var manifests = CreateManifests(release);

        // Update with file hashes from the installation
        return await UpdateManifestsWithExtractedFiles(manifests, installationPath, cancellationToken);
    }

    /// <summary>
    /// Parses a Generals Online version string to extract a numeric user version for manifest IDs.
    /// Converts versions like "111825_QFE2" (Nov 18, 2025) to a numeric value like 1118252.
    /// NOTE: Format is dictated by Generals Online CDN API (MMDDYY_QFE#), not our choice.
    /// This method converts it to a sortable numeric format.
    /// </summary>
    /// <param name="version">The version string (e.g., "111825_QFE2").</param>
    /// <returns>A numeric version suitable for manifest IDs.</returns>
    private static int ParseVersionForManifestId(string version) => GameVersionHelper.GetGeneralsOnlineSortableVersion(version);

    /// <summary>
    /// Creates a ManifestFile for a map file, normalizing the relative path.
    /// </summary>
    /// <param name="relativePath">The relative path from the extract directory.</param>
    /// <param name="fileInfo">The file information.</param>
    /// <param name="hash">The SHA-256 hash of the file.</param>
    /// <returns>A ManifestFile configured for user maps directory installation.</returns>
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
    /// Creates all variant manifests (30Hz, 60Hz, MapPack) from the original manifest.
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
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? string.Empty;
        var supportUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.SupportUrl) ?? string.Empty;
        var downloadPageUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.DownloadPageUrl) ?? string.Empty;
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
        var releaseDate = originalManifest.Metadata?.ReleaseDate ?? DateTime.Now;
        var changelogUrl = originalManifest.Metadata?.ChangelogUrl;

        // Create 30Hz variant
        manifests.Add(new ContentManifest
        {
            Id = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
                PublisherTypeConstants.GeneralsOnline,
                ContentType.GameClient,
                GeneralsOnlineConstants.Variant30HzSuffix,
                userVersion)),
            Name = GameClientConstants.GeneralsOnline30HzDisplayName,
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
                Tags = [..GeneralsOnlineConstants.Tags],
                ChangelogUrl = changelogUrl,
                CoverUrl = GeneralsOnlineConstants.CoverSource,
            },
            Files = [],
            Dependencies = GeneralsOnlineDependencyBuilder.GetDependenciesFor30Hz(userVersion),
        });

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
                Tags = [..GeneralsOnlineConstants.Tags],
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
                Tags = [.. GeneralsOnlineConstants.MapPackTags],
                ChangelogUrl = changelogUrl,
            },
            Files = [],
            Dependencies =
            [
                GeneralsOnlineDependencyBuilder.CreateZeroHourDependencyForGeneralsOnline(),
            ],
        });

        return manifests;
    }

    /// <summary>
    /// Updates manifests (30Hz, 60Hz, and QuickMatch MapPack) with extracted file information.
    /// Computes SHA-256 hashes for all files for CAS integration.
    /// Each variant gets only the files it needs plus shared files.
    /// Maps are extracted to the MapPack manifest with UserMapsDirectory install target.
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

        var allFiles = Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories);
        logger.LogInformation("Processing {Count} files", allFiles.Length);

        List<(string RelativePath, FileInfo FileInfo, string Hash, bool IsMap)> filesWithHashes = [];

        // Detect Maps directory (case-insensitive)
        var mapsDirectory = Directory.GetDirectories(extractPath, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(d => Path.GetFileName(d).Equals(GeneralsOnlineConstants.MapsSubdirectory, StringComparison.OrdinalIgnoreCase));

        foreach (var filePath in allFiles)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var relativePath = Path.GetRelativePath(extractPath, filePath);
            var fileInfo = new FileInfo(filePath);

            // Determine if this file is inside the Maps directory
            var isMap = mapsDirectory != null && filePath.StartsWith(mapsDirectory, StringComparison.OrdinalIgnoreCase);

            string hash;
            using (var stream = File.OpenRead(filePath))
            {
                var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
                hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            filesWithHashes.Add((relativePath, fileInfo, hash, isMap));
            logger.LogDebug("Processed file: {File} ({Size} bytes, hash: {Hash}, isMap: {IsMap})", relativePath, fileInfo.Length, hash[..8], isMap);
        }

        List<ContentManifest> updatedManifests = [];

        foreach (var manifest in manifests)
        {
            List<ManifestFile> manifestFiles = [];
            var isMapPackManifest = manifest.ContentType == ContentType.MapPack;

            if (isMapPackManifest)
            {
                // MapPack manifest: only include map files with UserMapsDirectory install target
                foreach (var (relativePath, fileInfo, hash, isMap) in filesWithHashes)
                {
                    if (!isMap)
                    {
                        continue;
                    }

                    manifestFiles.Add(CreateMapManifestFile(relativePath, fileInfo, hash));
                }

                logger.LogInformation("MapPack manifest '{Name}' updated with {Count} map files", manifest.Name, manifestFiles.Count);
            }
            else
            {
                // Game client manifest: include executables, shared files, AND map files
                // Map files are included with UserMapsDirectory install target so they install to Documents
                var is30Hz = manifest.Name.Contains(GeneralsOnlineConstants.Variant30HzSuffix, StringComparison.OrdinalIgnoreCase);
                var targetExecutable = is30Hz
                    ? GameClientConstants.GeneralsOnline30HzExecutable
                    : GameClientConstants.GeneralsOnline60HzExecutable;

                foreach (var (relativePath, fileInfo, hash, isMap) in filesWithHashes)
                {
                    var fileName = Path.GetFileName(relativePath);
                    var isExecutable = false;

                    // Skip map files in GameClient manifests - they belong in the MapPack manifest
                    if (isMap)
                    {
                        continue;
                    }

                    if (string.Equals(fileName, targetExecutable, StringComparison.OrdinalIgnoreCase))
                    {
                        isExecutable = true;
                    }
                    else if (string.Equals(fileName, GameClientConstants.GeneralsOnline30HzExecutable, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(fileName, GameClientConstants.GeneralsOnline60HzExecutable, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
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

        return updatedManifests;
    }
}
