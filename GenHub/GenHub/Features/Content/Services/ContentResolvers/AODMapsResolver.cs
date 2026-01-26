using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Parsers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Parsers;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging;
using File = GenHub.Core.Models.Parsers.File;
using ParsedContentDetails = GenHub.Core.Models.Content.ParsedContentDetails;

namespace GenHub.Features.Content.Services.ContentResolvers;

/// <summary>
/// Resolves AODMaps content details from discovered content items.
/// Uses AODMapsPageParser to parse the page and extracts specific map details.
/// </summary>
public class AODMapsResolver(
    AODMapsPageParser pageParser,
    AODMapsManifestFactory manifestFactory,
    ILogger<AODMapsResolver> logger) : IContentResolver
{
    /// <summary>
    /// Gets the unique resolver ID for AODMaps.
    /// </summary>
    public string ResolverId => AODMapsConstants.PublisherType;

    /// <summary>
    /// Resolves content details for a discovered AODMaps item and creates a ContentManifest.
    /// </summary>
    /// <param name="discoveredItem">The discovered content item containing the source URL and resolver metadata used to locate the corresponding file on the AODMaps page.</param>
    /// <param name="cancellationToken">Token to observe while waiting for the operation to complete.</param>
    /// <returns>An <see cref="OperationResult{ContentManifest}"/> containing the created <see cref="ContentManifest"/> on success; a failure result with an error message otherwise.</returns>
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        if (discoveredItem?.SourceUrl == null)
        {
            return OperationResult<ContentManifest>.CreateFailure("Invalid discovered item or source URL");
        }

        try
        {
            logger.LogInformation("Resolving AODMaps content from {Url}", discoveredItem.SourceUrl);

            // Parse the web page (which is likely a list/gallery page)
            var parsedPage = await pageParser.ParseAsync(discoveredItem.SourceUrl, cancellationToken);

            // Find the specific file section that corresponds to our discovered item
            // We use the DownloadURL from metadata to identify it
            if (!discoveredItem.ResolverMetadata.TryGetValue(AODMapsConstants.DownloadUrlMetadataKey, out var targetDownloadUrl))
            {
                logger.LogWarning("No download URL found in metadata for {Name}", discoveredItem.Name);
                return OperationResult<ContentManifest>.CreateFailure("Download URL not found in metadata");
            }

            // Fallback: If no download URL match, try Name match
            var section = parsedPage.Sections.OfType<File>().FirstOrDefault(f =>
                string.Equals(f.DownloadUrl, targetDownloadUrl, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.Name, discoveredItem.Name, StringComparison.OrdinalIgnoreCase));

            if (section == null)
            {
                 logger.LogWarning("Could not find content section for {Name} in parsed page {Url}", discoveredItem.Name, discoveredItem.SourceUrl);
                 return OperationResult<ContentManifest>.CreateFailure("Content section not found on page");
            }

            // Convert to MapDetails
            var details = ConvertToMapDetails(section, parsedPage.Context, discoveredItem);

            // Use factory to create manifest
            var manifest = await manifestFactory.CreateManifestAsync(details);

            logger.LogInformation(
                "Successfully resolved AODMaps content: {ManifestId} - {Name}",
                manifest.Id.Value,
                manifest.Name);

            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve content details from {Url}", discoveredItem.SourceUrl);
            return OperationResult<ContentManifest>.CreateFailure($"Resolution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Build a ParsedContentDetails object from a discovered file, its page context, and the originating search item.
    /// </summary>
    /// <param name="file">Parsed file section containing metadata such as name, uploader, thumbnail, sizes, dates, and URLs.</param>
    /// <param name="context">Page-level context (title, developer, etc.) used as fallback metadata.</param>
    /// <param name="item">Original discovered content search result, used for resolver metadata and referer URL.</param>
    /// <returns>A ParsedContentDetails populated with name, description, author, images, file metadata, inferred game and content type, file type extension, rating (0), and the referer URL.</returns>
    private static ParsedContentDetails ConvertToMapDetails(File file, GlobalContext context, ContentSearchResult item)
    {
        // Determine GameType and ContentType
        // AODMaps are mostly Zero Hour or Generals.
        // We can guess from tags or item metadata if available.
        // Default to Zero Hour for AOD
        var gameType = GameType.ZeroHour;
        if (item.ResolverMetadata.TryGetValue("Game", out var gameStr) && Enum.TryParse<GameType>(gameStr, out var g))
        {
            gameType = g;
        }

        var contentType = ContentType.Map; // Default

        // Parse date if available
        var subDate = file.UploadDate ?? DateTime.UnixEpoch;

        // Use Author as request
        var author = file.Uploader ?? context.Developer ?? AODMapsConstants.DefaultAuthorName;

        return new ParsedContentDetails(
            Name: file.Name,
            Description: file.SizeDisplay ?? context.Title, // Use SizeDisplay (where we stored info) or Title
            Author: author,
            PreviewImage: file.ThumbnailUrl ?? string.Empty,
            Screenshots: file.ThumbnailUrl != null ? [file.ThumbnailUrl] : [],
            FileSize: file.SizeBytes ?? 0,
            DownloadCount: file.DownloadCount ?? 0,
            SubmissionDate: subDate,
            DownloadUrl: file.DownloadUrl ?? string.Empty,
            TargetGame: gameType,
            ContentType: contentType,
            FileType: !string.IsNullOrEmpty(file.DownloadUrl) ? Path.GetExtension(file.DownloadUrl) : ".zip",
            Rating: 0f,
            RefererUrl: item.SourceUrl);
    }
}