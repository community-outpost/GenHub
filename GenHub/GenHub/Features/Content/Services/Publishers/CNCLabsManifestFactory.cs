using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using Slugify;
using MapDetails = GenHub.Core.Models.ModDB.MapDetails;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Factory for creating CNC Labs content manifests from parsed map/mission details.
/// Generates manifest IDs following the format: 1.0.cnclabs-{author}.{contentType}.{contentName}.
/// </summary>
public partial class CNCLabsManifestFactory(
    IContentManifestBuilder manifestBuilder,
    IManifestIdService manifestIdService,
    ILogger<CNCLabsManifestFactory> logger) : IPublisherManifestFactory
{
    // ===== Static Members =====

    /// <summary>
    /// Regex that removes all non-alphanumeric characters.
    /// </summary>
    [GeneratedRegex(@"[^a-zA-Z0-9]")]
    private static partial Regex AlphanumericOnlyRegex();

    /// <summary>
    /// Regex that removes special characters (keeps alphanumeric, spaces, and dashes).
    /// </summary>
    [GeneratedRegex(@"[^a-z0-9\s-]")]
    private static partial Regex NonSlugCharactersRegex();

    /// <summary>
    /// Regex that matches one or more whitespace characters.
    /// </summary>
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// Regex that matches multiple consecutive dashes.
    /// </summary>
    [GeneratedRegex(@"-+")]
    private static partial Regex MultipleDashesRegex();

    // ===== Public Members =====

    /// <inheritdoc />
    public string PublisherId => CNCLabsConstants.PublisherPrefix;

    /// <inheritdoc />
    public bool CanHandle(ContentManifest manifest)
    {
        // CNC Labs publishes Maps, Missions, Mods, Patches, Skins, Videos, Screensavers, Replays, and Modding Tools
        var publisherMatches = manifest.Publisher?.PublisherType?.Equals(CNCLabsConstants.PublisherPrefix, StringComparison.OrdinalIgnoreCase) == true;

        var supportedTypes = manifest.ContentType switch
        {
            ContentType.Map => true,
            ContentType.Mission => true,
            ContentType.Mod => true,
            ContentType.Patch => true,
            ContentType.Skin => true,
            ContentType.Video => true,
            ContentType.Screensaver => true,
            ContentType.Replay => true,
            ContentType.ModdingTool => true,
            _ => false,
        };

        return publisherMatches && supportedTypes;
    }

    /// <inheritdoc />
    public async Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        // CNC Labs content is delivered directly, no extraction needed
        // This method is not used for CNC Labs, but required by interface
        logger.LogWarning("CreateManifestsFromExtractedContentAsync called for CNC Labs content, which does not support extraction");
        return await Task.FromResult<List<ContentManifest>>([originalManifest]);
    }

    /// <inheritdoc />
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        // CNC Labs content is delivered directly to the target directory
        return extractedDirectory;
    }

    /// <summary>
    /// Creates a content manifest from CNC Labs map/mission details.
    /// </summary>
    /// <param name="details">The parsed map/mission details.</param>
    /// <param name="detailPageUrl">The detail page URL.</param>
    /// <returns>A fully constructed ContentManifest.</returns>
    public async Task<ContentManifest> CreateManifestAsync(MapDetails details, string detailPageUrl)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentException.ThrowIfNullOrWhiteSpace(detailPageUrl, nameof(detailPageUrl));

        if (string.IsNullOrWhiteSpace(details.downloadUrl))
        {
            throw new ArgumentException("Download URL cannot be empty", nameof(details));
        }

        // 1. Normalize author for publisher ID
        var normalizedAuthor = NormalizeAuthorForPublisherId(details.author);
        var publisherId = $"{CNCLabsConstants.PublisherPrefix}-{normalizedAuthor}";

        // 2. Slugify content name
        var contentName = SlugifyTitle(details.name);

        // 3. Generate manifest ID
        var manifestIdResult = manifestIdService.GeneratePublisherContentId(
            publisherId,
            details.contentType,
            contentName,
            userVersion: CNCLabsConstants.ManifestVersion);

        if (!manifestIdResult.Success)
        {
            logger.LogError(
                "Failed to generate manifest ID for CNC Labs content '{ContentName}': {Error}",
                details.name,
                manifestIdResult.FirstError);
            throw new InvalidOperationException($"Failed to generate manifest ID for CNC Labs content '{details.name}': {manifestIdResult.FirstError}");
        }

        logger.LogInformation(
            "Creating CNC Labs manifest: ID={ManifestId}, Name={Name}, Author={Author}, Type={ContentType}",
            manifestIdResult.Data.Value,
            details.name,
            details.author,
            details.contentType);

        // 4. Build manifest
        var manifest = manifestBuilder
            .WithBasicInfo(publisherId, details.name, CNCLabsConstants.ManifestVersion)
            .WithContentType(details.contentType, details.targetGame)
            .WithPublisher(
                name: publisherId,
                website: CNCLabsConstants.PublisherWebsite,
                supportUrl: detailPageUrl)
            .WithMetadata(
                description: details.description,
                tags: GenerateTags(details),
                iconUrl: details.previewImage,
                screenshotUrls: details.screenshots);

        // 5. Add the download file
        var fileName = ExtractFileNameFromUrl(details.downloadUrl);
        manifest = await manifest.AddRemoteFileAsync(
            fileName,
            details.downloadUrl,
            ContentSourceType.RemoteDownload);
        return manifest.Build();
    }

    /// <summary>
    /// Normalizes an author name for use in a publisher ID.
    /// Removes special characters, converts to lowercase.
    /// </summary>
    /// <param name="author">The raw author name.</param>
    /// <returns>A normalized publisher ID component.</returns>
    private static string NormalizeAuthorForPublisherId(string author)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            return "unknown";
        }

        // Remove all non-alphanumeric characters and convert to lowercase
        var normalized = AlphanumericOnlyRegex().Replace(author, string.Empty).ToLowerInvariant();

        // If the result is empty after normalization, use "unknown"
        return string.IsNullOrEmpty(normalized) ? "unknown" : normalized;
    }

    /// <summary>
    /// Converts a title into a URL-friendly slug.
    /// </summary>
    /// <param name="title">The content title.</param>
    /// <returns>A slugified version of the title.</returns>
    private static string SlugifyTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "untitled";
        }

        try
        {
            var slugHelper = new SlugHelper();
            var slug = slugHelper.GenerateSlug(title);
            return string.IsNullOrEmpty(slug) ? "untitled" : slug;
        }
        catch
        {
            // Fallback to default if slugification fails
            return "untitled";
        }
    }

    /// <summary>
    /// Generates appropriate tags for a CNC Labs content item.
    /// </summary>
    /// <param name="details">The map/mission details.</param>
    /// <returns>A list of tags.</returns>
    private static List<string> GenerateTags(MapDetails details)
    {
        List<string> tags = ["CNC Labs", "Community"];

        // Add game-specific tag
        if (details.targetGame == GameType.Generals)
        {
            tags.Add("Generals");
        }
        else if (details.targetGame == GameType.ZeroHour)
        {
            tags.Add("Zero Hour");
        }

        // Add content type tag
        tags.Add(details.contentType switch
        {
            ContentType.Map => "Map",
            ContentType.Mission => "Mission",
            ContentType.Mod => "Mod",
            ContentType.Patch => "Patch",
            ContentType.Skin => "Skin",
            ContentType.Video => "Video",
            ContentType.Screensaver => "Screensaver",
            ContentType.Replay => "Replay",
            ContentType.ModdingTool => "Modding Tool",
            _ => "Other",
        });

        return tags;
    }

    /// <summary>
    /// Extracts a filename from a download URL.
    /// </summary>
    /// <param name="downloadUrl">The download URL.</param>
    /// <returns>The extracted filename.</returns>
    private string ExtractFileNameFromUrl(string downloadUrl)
    {
        try
        {
            // Try to get filename from URL path
            var uri = new Uri(downloadUrl);
            var fileName = Path.GetFileName(uri.LocalPath);

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }
        catch (UriFormatException ex)
        {
            logger.LogWarning(ex, "Invalid download URL format: {Url}", downloadUrl);
        }

        // Fallback: generate a generic filename
        return "download.zip";
    }
}
