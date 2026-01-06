using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
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
    IManifestIdService manifestIdService,
    IProviderDefinitionLoader providerLoader,
    ILogger<CNCLabsManifestFactory> logger) : IPublisherManifestFactory
{
    private static readonly Regex AuthorRegex = new(@"[^a-z0-9]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string SlugifyAuthor(string? author)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            return ManifestConstants.UnknownAuthor;
        }

        // Remove all non-alphanumeric characters and convert to lowercase
        var slug = AuthorRegex.Replace(author, string.Empty).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(slug) ? ManifestConstants.UnknownAuthor : slug;
    }

    private static string SlugifyContentName(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return CNCLabsConstants.DefaultContentName;
        }

        try
        {
            var slugHelper = new SlugHelper();
            var slug = slugHelper.GenerateSlug(title);
            return string.IsNullOrEmpty(slug) ? CNCLabsConstants.DefaultContentName : slug;
        }
        catch
        {
            // Fallback to default if slugification fails
            return CNCLabsConstants.DefaultContentName;
        }
    }

    private static List<string> GetTags(MapDetails details)
    {
        var tags = new List<string>(CNCLabsConstants.DefaultTags);

        // Add game-specific tag
        tags.Add(details.TargetGame == GameType.Generals ? GameClientConstants.GeneralsShortName : GameClientConstants.ZeroHourShortName);

        // Add content type tag
        tags.Add(details.ContentType switch
        {
            ContentType.Map => ManifestConstants.MapTag,
            ContentType.Mission => ManifestConstants.MissionTag,
            ContentType.Mod => ManifestConstants.ModTag,
            ContentType.Patch => ManifestConstants.PatchTag,
            ContentType.Skin => ManifestConstants.SkinTag,
            ContentType.Video => ManifestConstants.VideoTag,
            ContentType.Screensaver => ManifestConstants.ScreensaverTag,
            ContentType.Replay => ManifestConstants.ReplayTag,
            ContentType.ModdingTool => ManifestConstants.ModdingToolTag,
            _ => ManifestConstants.OtherTag,
        });

        return tags;
    }

    private static string GetDownloadFilename(MapDetails details)
    {
        if (!string.IsNullOrWhiteSpace(details.DownloadUrl))
        {
            try
            {
                var uri = new Uri(details.DownloadUrl);
                var filename = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrWhiteSpace(filename) && filename.Contains('.'))
                {
                    return filename;
                }
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        // Fallback: generate a generic filename
        return CNCLabsConstants.DefaultDownloadFilename;
    }

    /// <inheritdoc/>
    public string PublisherId => CNCLabsConstants.PublisherPrefix;

    /// <inheritdoc/>
    public bool CanHandle(ContentManifest manifest)
    {
        return manifest.Publisher.PublisherType == CNCLabsConstants.PublisherId;
    }

    /// <inheritdoc/>
    public Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        // CNCLabs content is delivered directly, no extra processing needed post-extraction.
        return Task.FromResult(new List<ContentManifest> { originalManifest });
    }

    /// <inheritdoc/>
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        return extractedDirectory;
    }

    /// <summary>
    /// Creates a manifest from map details.
    /// </summary>
    /// <param name="details">The map details.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation, containing the created manifest.</returns>
    public async Task<ContentManifest> CreateManifestAsync(
        object details,
        CancellationToken cancellationToken = default)
    {
        if (details is not MapDetails mapDetails)
        {
            throw new ArgumentException($"Details must be of type {nameof(MapDetails)}", nameof(details));
        }

        return await CreateManifestInternalAsync(mapDetails, cancellationToken);
    }

    private Task<ContentManifest> CreateManifestInternalAsync(
        MapDetails details,
        CancellationToken cancellationToken)
    {
        // 1. Load provider metadata to get website/support URLs if possible
        var provider = providerLoader.GetProvider(CNCLabsConstants.PublisherPrefix);
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? CNCLabsConstants.PublisherWebsite;
        var detailPageUrl = details.DownloadUrl ?? websiteUrl; // Fallback if source omitted

        // 2. Prepare manifest information
        var contentName = SlugifyContentName(details.Name);
        var publisherId = CNCLabsConstants.PublisherId;

        // 3. Generate the manifest ID
        var manifestIdResult = manifestIdService.GeneratePublisherContentId(
            publisherId,
            details.ContentType,
            contentName);

        if (!manifestIdResult.Success)
        {
            logger.LogError("Failed to generate manifest ID for {ContentName}: {Error}", details.Name, manifestIdResult.FirstError);
            throw new InvalidOperationException($"Failed to generate manifest ID: {manifestIdResult.FirstError}");
        }

        logger.LogDebug("Creating CNC Labs manifest for {ContentName} (ID: {ManifestId})", details.Name, manifestIdResult.Data);

        // 4. Construct the manifest directly
        var manifest = new ContentManifest
        {
            ManifestVersion = ManifestConstants.DefaultManifestVersion,
            Id = manifestIdResult.Data,
            Name = details.Name,
            Version = ManifestConstants.UnknownVersion,
            ContentType = details.ContentType,
            Publisher = new PublisherInfo
            {
                PublisherType = CNCLabsConstants.PublisherId,
                Name = CNCLabsConstants.PublisherName,
                Website = websiteUrl,
                SupportUrl = detailPageUrl,
            },
            Metadata = new ContentMetadata
            {
                Description = details.Description,
                Tags = [.. GetTags(details)],
                IconUrl = details.PreviewImage,
                ReleaseDate = details.SubmissionDate,
            },
            Files =
            [
                new ManifestFile
                {
                    RelativePath = GetDownloadFilename(details),
                    Size = details.FileSize,
                    DownloadUrl = details.DownloadUrl,
                    SourceType = ContentSourceType.RemoteDownload,
                },
            ],
        };

        return Task.FromResult(manifest);
    }
}
