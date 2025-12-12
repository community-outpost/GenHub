using GenHub.Core.Constants;
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
    /// Parses a Generals Online version string to extract a numeric user version for manifest IDs.
    /// Converts versions like "111825_QFE2" (Nov 18, 2025) to a numeric value like 1118252.
    /// NOTE: Format is dictated by Generals Online CDN API (MMDDYY_QFE#), not our choice.
    /// This method converts it to a sortable numeric format.
    /// </summary>
    /// <param name="version">The version string (e.g., "111825_QFE2").</param>
    /// <returns>A numeric version suitable for manifest IDs.</returns>
    private static int ParseVersionForManifestId(string version)
    {
        try
        {
            var parts = version.Split(new[] { GeneralsOnlineConstants.QfeSeparator }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                return 0;
            }

            var datePart = parts[0]; // "101525"
            var qfePart = parts[1].Replace("QFE", string.Empty, StringComparison.OrdinalIgnoreCase);

            if (!int.TryParse(datePart, out var dateValue) || !int.TryParse(qfePart, out var qfeValue))
            {
                return 0;
            }

            // Combine: 101525 * 10 + 5 = 1015255
            return (dateValue * 10) + qfeValue;
        }
        catch
        {
            return 0;
        }
    }

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
            mapRelativePath = relativePath.Substring(GeneralsOnlineConstants.MapsSubdirectory.Length + 1);
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
    /// Creates all variant manifests (30Hz, 60Hz, MapPack) from the original manifest.
    /// This is called AFTER extraction - we use the original manifest's metadata to create variants.
    /// </summary>
    /// <param name="originalManifest">The manifest from the Resolver (contains version, publisher info, etc.).</param>
    /// <returns>List of variant manifests ready for file hash population.</returns>
    private List<ContentManifest> CreateVariantManifestsFromOriginal(ContentManifest originalManifest)
    {
        var manifests = new List<ContentManifest>();
        var version = originalManifest.Version ?? "unknown";
        var userVersion = ParseVersionForManifestId(version);

        // Get URLs from provider definition
        var provider = providerLoader.GetProvider(PublisherTypeConstants.GeneralsOnline);
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? string.Empty;
        var supportUrl = provider?.Endpoints.SupportUrl ?? string.Empty;
        var downloadPageUrl = provider?.Endpoints.GetEndpoint("downloadPageUrl") ?? string.Empty;
        var iconUrl = provider?.Endpoints.GetEndpoint("iconUrl") ?? string.Empty;

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
                Tags = new List<string>(GeneralsOnlineConstants.Tags),
                ChangelogUrl = changelogUrl,
            },
            Files = new List<ManifestFile>(),
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
                Tags = new List<string>(GeneralsOnlineConstants.Tags),
                ChangelogUrl = changelogUrl,
            },
            Files = new List<ManifestFile>(),
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
                Tags = new List<string> { "maps", "multiplayer", "quickmatch", "competitive" },
                ChangelogUrl = changelogUrl,
            },
            Files = new List<ManifestFile>(),
            Dependencies = new List<ContentDependency>
            {
                GeneralsOnlineDependencyBuilder.CreateZeroHourDependencyForGeneralsOnline(),
            },
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

        var filesWithHashes = new List<(string relativePath, FileInfo fileInfo, string hash, bool isMap)>();

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

        var updatedManifests = new List<ContentManifest>();

        foreach (var manifest in manifests)
        {
            var manifestFiles = new List<ManifestFile>();
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
                    ? GameClientConstants.GeneralsOnline30HzExecutable.ToLowerInvariant()
                    : GameClientConstants.GeneralsOnline60HzExecutable.ToLowerInvariant();

                foreach (var (relativePath, fileInfo, hash, isMap) in filesWithHashes)
                {
                    var fileName = Path.GetFileName(relativePath).ToLowerInvariant();
                    var isExecutable = false;

                    // Handle map files - include them with UserMapsDirectory install target
                    if (isMap)
                    {
                        manifestFiles.Add(CreateMapManifestFile(relativePath, fileInfo, hash));
                        continue;
                    }

                    if (fileName == targetExecutable)
                    {
                        isExecutable = true;
                    }
                    else if (fileName == GameClientConstants.GeneralsOnline30HzExecutable.ToLowerInvariant() ||
                             fileName == GameClientConstants.GeneralsOnline60HzExecutable.ToLowerInvariant())
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

                logger.LogInformation("GameClient manifest '{Name}' updated with {Count} files (including maps)", manifest.Name, manifestFiles.Count);
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
