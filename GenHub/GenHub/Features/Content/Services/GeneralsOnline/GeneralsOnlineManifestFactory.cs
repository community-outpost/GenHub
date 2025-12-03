using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
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
/// Factory for creating and updating Generals Online content manifests.
/// Creates separate manifests for each game client variant (30Hz and 60Hz),
/// plus a shared QuickMatch MapPack manifest required for multiplayer.
/// </summary>
public class GeneralsOnlineManifestFactory(ILogger<GeneralsOnlineManifestFactory> logger) : IPublisherManifestFactory
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

        // Create release info from original manifest
        var release = CreateReleaseFromManifest(originalManifest);

        // Create all manifests (30Hz, 60Hz, and QuickMatch MapPack)
        var manifests = CreateManifests(release);

        // Update manifests with extracted files
        return await UpdateManifestsWithExtractedFiles(manifests, extractedDirectory, cancellationToken);
    }

    /// <inheritdoc />
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        // GeneralsOnline uses the root extracted directory for all variants
        return extractedDirectory;
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
        var manifests = new List<ContentManifest>();

        // Create manifest for 30Hz variant
        manifests.Add(CreateVariantManifest(release, GameClientConstants.GeneralsOnline30HzExecutable, GeneralsOnlineConstants.Variant30HzSuffix, GameClientConstants.GeneralsOnline30HzDisplayName));

        // Create manifest for 60Hz variant
        manifests.Add(CreateVariantManifest(release, GameClientConstants.GeneralsOnline60HzExecutable, GeneralsOnlineConstants.Variant60HzSuffix, GameClientConstants.GeneralsOnline60HzDisplayName));

        // Create manifest for QuickMatch MapPack (required dependency for both variants)
        manifests.Add(CreateQuickMatchMapPackManifest(release));

        return manifests;
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
    /// Creates a release info object from a content manifest.
    /// </summary>
    private static GeneralsOnlineRelease CreateReleaseFromManifest(ContentManifest manifest)
    {
        var zipFile = manifest.Files.FirstOrDefault(f =>
            f.DownloadUrl?.EndsWith(GeneralsOnlineConstants.PortableExtension, StringComparison.OrdinalIgnoreCase) == true);

        return new GeneralsOnlineRelease
        {
            Version = manifest.Version ?? "unknown",
            VersionDate = DateTime.Now,
            ReleaseDate = manifest.Metadata?.ReleaseDate ?? DateTime.Now,
            PortableUrl = zipFile?.DownloadUrl ?? string.Empty,
            PortableSize = zipFile?.Size, // Use actual file size, null if unknown
            Changelog = manifest.Metadata?.ChangelogUrl,
        };
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

    /// <summary>
    /// Creates a content manifest for the QuickMatch MapPack.
    /// This manifest contains all maps required for GeneralsOnline QuickMatch multiplayer.
    /// </summary>
    /// <param name="release">The Generals Online release information.</param>
    /// <returns>A content manifest for the QuickMatch MapPack.</returns>
    private ContentManifest CreateQuickMatchMapPackManifest(GeneralsOnlineRelease release)
    {
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
                Website = GeneralsOnlineConstants.WebsiteUrl,
                SupportUrl = GeneralsOnlineConstants.SupportUrl,
                ContentIndexUrl = GeneralsOnlineConstants.DownloadPageUrl,
                UpdateCheckIntervalHours = GeneralsOnlineConstants.UpdateCheckIntervalHours,
            },
            Metadata = new ContentMetadata
            {
                Description = GeneralsOnlineConstants.QuickMatchMapPackDescription,
                ReleaseDate = release.ReleaseDate,
                IconUrl = GeneralsOnlineConstants.IconUrl,
                Tags = new List<string> { "maps", "multiplayer", "quickmatch", "competitive" },
                ChangelogUrl = release.Changelog,
            },
            Files = new List<ManifestFile>(), // Files will be populated during extraction
            Dependencies = new List<ContentDependency>
            {
                // MapPack requires Zero Hour installation
                GeneralsOnlineDependencyBuilder.CreateZeroHourDependencyForGeneralsOnline(),
            },
        };
    }

    /// <summary>
    /// Creates a content manifest for a specific Generals Online variant.
    /// </summary>
    /// <param name="release">The Generals Online release information.</param>
    /// <param name="executableName">The executable filename for this variant.</param>
    /// <param name="variantSuffix">The suffix for the manifest ID (e.g., "30hz").</param>
    /// <param name="displayName">The display name for this variant (e.g., "GeneralsOnline 30Hz").</param>
    /// <returns>A content manifest for the specified variant.</returns>
    private ContentManifest CreateVariantManifest(
        GeneralsOnlineRelease release,
        string executableName,
        string variantSuffix,
        string displayName)
    {
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
                Website = GeneralsOnlineConstants.WebsiteUrl,
                SupportUrl = GeneralsOnlineConstants.SupportUrl,
                ContentIndexUrl = GeneralsOnlineConstants.DownloadPageUrl,
                UpdateCheckIntervalHours = GeneralsOnlineConstants.UpdateCheckIntervalHours,
            },
            Metadata = new ContentMetadata
            {
                Description = GeneralsOnlineConstants.ShortDescription,
                ReleaseDate = release.ReleaseDate,
                IconUrl = GeneralsOnlineConstants.IconUrl,
                Tags = new List<string>(GeneralsOnlineConstants.Tags),
                ChangelogUrl = release.Changelog,
            },
            Files = new List<ManifestFile>
            {
                new ManifestFile
                {
                    RelativePath = Path.GetFileName(release.PortableUrl),
                    DownloadUrl = release.PortableUrl,
                    Size = release.PortableSize ?? 0, // Use 0 when size is unknown
                    SourceType = ContentSourceType.RemoteDownload,
                    Hash = string.Empty,
                },
            },
            Dependencies = variantSuffix == GeneralsOnlineConstants.Variant60HzSuffix
                ? GeneralsOnlineDependencyBuilder.GetDependenciesFor60Hz(userVersion)
                : GeneralsOnlineDependencyBuilder.GetDependenciesFor30Hz(userVersion),
        };
    }
}
