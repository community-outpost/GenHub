using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.CommunityOutpost.Models;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Resolves Community Outpost content into manifests.
/// Supports the GenPatcher dl.dat catalog format with multiple download mirrors.
/// </summary>
/// <param name="manifestBuilderFactory">Factory to create new manifest builders per resolve operation.</param>
/// <param name="logger">The logger.</param>
public class CommunityOutpostResolver(
    Func<IContentManifestBuilder> manifestBuilderFactory,
    ILogger<CommunityOutpostResolver> logger) : IContentResolver
{
    /// <inheritdoc/>
    public string ResolverId => CommunityOutpostConstants.PublisherId;

    /// <inheritdoc/>
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation(
                "Resolving Community Outpost content: {Name} v{Version}",
                discoveredItem.Name,
                discoveredItem.Version);

            // Extract metadata from resolver metadata
            var contentCode = GetMetadataValue(discoveredItem, "contentCode", "unknown");
            var catalogVersion = GetMetadataValue(discoveredItem, "catalogVersion", "unknown");
            var category = GetMetadataValue(discoveredItem, "category", "Other");
            var fileSize = GetMetadataValueLong(discoveredItem, "fileSize", 0);

            // Get content metadata from registry
            var contentMetadata = GenPatcherContentRegistry.GetMetadata(contentCode);

            // Determine filename from URL or content code
            var downloadUrl = discoveredItem.SourceUrl ?? throw new InvalidOperationException(
                "SourceUrl cannot be null for Community Outpost content");

            var filename = GetFilenameFromUrl(downloadUrl, contentCode);

            // Get all mirror URLs for fallback support
            var mirrorUrls = GetMirrorUrls(discoveredItem);

            logger.LogDebug(
                "Resolving content code {Code} with {MirrorCount} mirrors, file size: {Size} bytes",
                contentCode,
                mirrorUrls.Count,
                fileSize);

            // Generate a deterministic content name from the content code
            // For patches like "104p", create name like "patch104polish"
            // For addons like "cbbs", use the content code directly
            var contentName = GenerateContentName(contentCode, contentMetadata);

            // Extract version number for manifest ID
            // For patches like "1.04", extract as 104
            // For content with dynamic versions (like community-patch), use the discovered version
            var versionSource = !string.IsNullOrEmpty(contentMetadata.Version)
                ? contentMetadata.Version
                : discoveredItem.Version;
            var manifestVersion = ExtractManifestVersion(versionSource);

            logger.LogDebug(
                "Generating manifest ID: Publisher={Publisher}, ContentType={ContentType}, ContentName={ContentName}, Version={Version}",
                CommunityOutpostConstants.PublisherType,
                contentMetadata.ContentType,
                contentName,
                manifestVersion);

            // Create a fresh manifest builder instance for each resolve operation
            // Using factory pattern ensures we get a new Transient instance each time
            var manifestBuilder = manifestBuilderFactory();

            // Build manifest with correct parameters
            // Use PublisherType (e.g., "communityoutpost") as the publisher ID, NOT combined with content code
            var manifest = manifestBuilder
                .WithBasicInfo(
                    CommunityOutpostConstants.PublisherType,
                    contentName,
                    manifestVersion)
                .WithContentType(contentMetadata.ContentType, contentMetadata.TargetGame)
                .WithPublisher(
                    name: CommunityOutpostConstants.PublisherName,
                    website: CommunityOutpostConstants.PublisherWebsite,
                    supportUrl: CommunityOutpostConstants.PatchPageUrl,
                    contactEmail: string.Empty,
                    publisherType: CommunityOutpostConstants.PublisherType)
                .WithMetadata(
                    contentMetadata.Description,
                    tags: BuildTags(discoveredItem, contentMetadata),
                    changelogUrl: CommunityOutpostConstants.PatchPageUrl)
                .WithInstallationInstructions(WorkspaceStrategy.HybridCopySymlink);

            // Add dependencies based on content type and category
            var dependencies = contentMetadata.GetDependencies();
            foreach (var dependency in dependencies)
            {
                manifest.AddDependency(
                    id: dependency.Id,
                    name: dependency.Name,
                    dependencyType: dependency.DependencyType,
                    installBehavior: dependency.InstallBehavior,
                    minVersion: dependency.MinVersion ?? string.Empty,
                    maxVersion: dependency.MaxVersion ?? string.Empty,
                    compatibleVersions: dependency.CompatibleVersions,
                    isExclusive: GenPatcherDependencyBuilder.IsCategoryExclusive(contentMetadata.Category),
                    conflictsWith: dependency.ConflictsWith);

                logger.LogDebug(
                    "Added dependency {DepName} ({DepType}) to manifest for {ContentCode}",
                    dependency.Name,
                    dependency.DependencyType,
                    contentCode);
            }

            // Add the file as a remote download
            // Note: .dat files are actually .7z archives that need extraction
            await manifest.AddRemoteFileAsync(
                filename,
                downloadUrl,
                ContentSourceType.RemoteDownload,
                isExecutable: false);

            // Store additional metadata in the manifest for the deliverer
            var builtManifest = manifest.Build();

            // Store the install target from content metadata
            builtManifest.InstallationInstructions ??= new InstallationInstructions();

            // Add custom properties to track mirrors and archive type
            builtManifest.Metadata ??= new ContentMetadata();

            // Store mirror URLs in metadata for fallback support during delivery
            if (mirrorUrls.Count > 1)
            {
                // Store as custom tag since Metadata doesn't have arbitrary storage
                builtManifest.Metadata.Tags ??= new List<string>();
                builtManifest.Metadata.Tags.Add($"mirrors:{mirrorUrls.Count}");
            }

            // Store the content code for the factory to use
            builtManifest.Metadata.Tags ??= new List<string>();
            builtManifest.Metadata.Tags.Add($"contentCode:{contentCode}");
            builtManifest.Metadata.Tags.Add($"installTarget:{contentMetadata.InstallTarget}");

            // Mark file as 7z archive if it's a .dat file
            if (filename.EndsWith(CommunityOutpostConstants.DatFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var file in builtManifest.Files)
                {
                    if (file.RelativePath == filename)
                    {
                        // Store the archive type in SourcePath temporarily
                        // The deliverer will use this to know to extract as 7z
                        file.SourcePath = "archive:7z";

                        // Set the install target from content metadata
                        file.InstallTarget = contentMetadata.InstallTarget;
                    }
                }
            }

            // Update file size if available
            if (fileSize > 0 && builtManifest.Files.Count > 0)
            {
                builtManifest.Files[0].Size = fileSize;
            }

            // Override the display name to be more user-friendly
            builtManifest.Name = discoveredItem.Name ?? contentMetadata.DisplayName;
            builtManifest.Version = !string.IsNullOrEmpty(contentMetadata.Version)
                ? contentMetadata.Version
                : discoveredItem.Version;

            logger.LogInformation(
                "Successfully resolved Community Outpost manifest: {ManifestId} for {ContentCode} ({Category})",
                builtManifest.Id,
                contentCode,
                category);

            return OperationResult<ContentManifest>.CreateSuccess(builtManifest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve Community Outpost content");
            return OperationResult<ContentManifest>.CreateFailure($"Resolution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates a deterministic content name for manifest ID generation.
    /// </summary>
    /// <param name="contentCode">The 4-character content code.</param>
    /// <param name="metadata">The content metadata.</param>
    /// <returns>A normalized content name suitable for manifest IDs.</returns>
    private static string GenerateContentName(string contentCode, GenPatcherContentMetadata metadata)
    {
        // For official patches like "104p" -> "patch104polish"
        if (metadata.Category == GenPatcherContentCategory.OfficialPatch && !string.IsNullOrEmpty(metadata.LanguageCode))
        {
            var languageName = GetLanguageDisplayName(metadata.LanguageCode);
            var codePrefix = contentCode.Length >= 3 ? contentCode.Substring(0, 3) : contentCode;
            return $"patch{codePrefix}{languageName}".ToLowerInvariant();
        }

        // For content with language codes, append language
        if (!string.IsNullOrEmpty(metadata.LanguageCode))
        {
            var languageName = GetLanguageDisplayName(metadata.LanguageCode);
            return $"{contentCode}{languageName}".ToLowerInvariant();
        }

        // For other content, use the content code directly
        return contentCode.ToLowerInvariant();
    }

    /// <summary>
    /// Gets a display name for a language code.
    /// </summary>
    private static string GetLanguageDisplayName(string languageCode)
    {
        return languageCode.ToLowerInvariant() switch
        {
            "en" => "english",
            "de" => "german",
            "de-alt" => "german2",
            "fr" => "french",
            "es" => "spanish",
            "it" => "italian",
            "pt-br" => "brazilian",
            "zh" => "chinese",
            "ko" => "korean",
            "pl" => "polish",
            _ => languageCode.ToLowerInvariant(),
        };
    }

    /// <summary>
    /// Extracts a numeric version suitable for manifest ID.
    /// </summary>
    /// <param name="version">The version string (e.g., "1.04", "1.08", "1.0", "2025-11-07").</param>
    /// <returns>A numeric version string (e.g., "104", "108", "20251107").</returns>
    private static string ExtractManifestVersion(string version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return "0";
        }

        // Handle date versions like "2025-11-07" - exact format check for YYYY-MM-DD
        if (version.Length == 10 && version[4] == '-' && version[7] == '-')
        {
            var dateDigits = version.Replace("-", string.Empty);
            if (dateDigits.Length == 8 && int.TryParse(dateDigits, out var dateValue))
            {
                return dateValue.ToString();
            }
        }

        // Remove dots and leading zeros to get numeric version
        // "1.04" -> "104", "1.08" -> "108", "1.0" -> "10"
        var digits = version.Replace(".", string.Empty);

        // Try to parse as integer to normalize
        if (int.TryParse(digits, out var numericVersion))
        {
            return numericVersion.ToString();
        }

        return "0";
    }

    /// <summary>
    /// Builds the tags list for the manifest.
    /// </summary>
    private static List<string> BuildTags(ContentSearchResult item, GenPatcherContentMetadata metadata)
    {
        var tags = new List<string>(item.Tags);

        // Add language tag if present
        if (!string.IsNullOrEmpty(metadata.LanguageCode))
        {
            tags.Add(metadata.LanguageCode);
        }

        // Add category tag
        tags.Add(metadata.Category.ToString().ToLowerInvariant());

        return tags;
    }

    /// <summary>
    /// Gets a metadata value from the search result.
    /// </summary>
    private static string GetMetadataValue(ContentSearchResult item, string key, string defaultValue)
    {
        if (item.ResolverMetadata != null && item.ResolverMetadata.TryGetValue(key, out var value))
        {
            return value;
        }

        return defaultValue;
    }

    /// <summary>
    /// Gets a long metadata value from the search result.
    /// </summary>
    private static long GetMetadataValueLong(ContentSearchResult item, string key, long defaultValue)
    {
        var stringValue = GetMetadataValue(item, key, string.Empty);
        return long.TryParse(stringValue, out var result) ? result : defaultValue;
    }

    /// <summary>
    /// Gets the filename from the download URL or generates one from the content code.
    /// </summary>
    private static string GetFilenameFromUrl(string url, string contentCode)
    {
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            var lastSegment = path.Split('/')[^1];

            if (!string.IsNullOrEmpty(lastSegment) && lastSegment.Contains('.'))
            {
                return lastSegment;
            }
        }
        catch
        {
            // Fall through to default filename
        }

        // Generate filename from content code
        return $"{contentCode}{CommunityOutpostConstants.DatFileExtension}";
    }

    /// <summary>
    /// Gets the list of mirror URLs from the search result metadata.
    /// </summary>
    private List<string> GetMirrorUrls(ContentSearchResult item)
    {
        var mirrorUrlsJson = GetMetadataValue(item, "mirrorUrls", "[]");

        try
        {
            return JsonSerializer.Deserialize<List<string>>(mirrorUrlsJson) ?? new List<string>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to deserialize mirror URLs");
            return new List<string>();
        }
    }
}
