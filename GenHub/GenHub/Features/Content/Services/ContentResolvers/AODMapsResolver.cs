using System;
using System.Diagnostics.CodeAnalysis;
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
using ParsedContentDetails = GenHub.Core.Models.Content.ParsedContentDetails;

namespace GenHub.Features.Content.Services.ContentResolvers;

/// <summary>
/// Resolves AODMaps content details from discovered content items.
/// Uses AODMapsPageParser to parse the page and extracts specific map details.
/// </summary>
[SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Domain acronym")]
public class AODMapsResolver(
    AODMapsPageParser pageParser,
    AODMapsManifestFactory manifestFactory,
    ILogger<AODMapsResolver> logger) : IContentResolver
{
    /// <summary>
    /// Gets the unique resolver ID for AODMaps.
    /// </summary>
    public string ResolverId => AODMapsConstants.ResolverId;

    /// <summary>
    /// Resolves the details of a discovered AODMaps content item.
    /// </summary>
    /// <param name="discoveredItem">The discovered content item to resolve.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result containing the resolved content manifest.</returns>
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
            string pageUrl;
            if (discoveredItem.ResolverMetadata.TryGetValue(AODMapsConstants.ListPageUrlMetadataKey, out var listPageUrl))
            {
                pageUrl = listPageUrl;
                logger.LogInformation("Resolving AODMaps content from list page: {Url}", pageUrl);
            }
            else
            {
                // Fallback to SourceUrl if ListPageUrl not available (backward compatibility)
                pageUrl = discoveredItem.SourceUrl;
                logger.LogWarning("ListPageUrl not found in metadata, falling back to SourceUrl: {Url}", pageUrl);
            }

            // Parse the web page (which is likely a list/gallery page)
            var parsedPage = await pageParser.ParseAsync(pageUrl, cancellationToken);

            // Find the specific file section that corresponds to our discovered item
            // We use the DownloadURL from metadata to identify it
            if (!discoveredItem.ResolverMetadata.TryGetValue(AODMapsConstants.DownloadUrlMetadataKey, out var targetDownloadUrl))
            {
                logger.LogWarning("No download URL found in metadata for {Name}", discoveredItem.Name);
                return OperationResult<ContentManifest>.CreateFailure("Download URL not found in metadata");
            }

            // Fallback: If no download URL match, try Name match
            var section = parsedPage.Sections.OfType<DownloadableFile>().FirstOrDefault(f =>
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

    private static ParsedContentDetails ConvertToMapDetails(DownloadableFile file, GlobalContext context, ContentSearchResult item)
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
        var subDate = file.UploadDate ?? DateTime.MinValue;

        // Use Author as request
        var author = context.Developer ?? AODMapsConstants.DefaultAuthorName;
        if (!string.IsNullOrWhiteSpace(file.Uploader) && !file.Uploader.Equals(AODMapsConstants.DefaultAuthorName, StringComparison.OrdinalIgnoreCase))
        {
            author = file.Uploader;
        }
        else if (!string.IsNullOrWhiteSpace(item.AuthorName) && !item.AuthorName.Equals(AODMapsConstants.DefaultAuthorName, StringComparison.OrdinalIgnoreCase))
        {
            author = item.AuthorName;
        }

        var description = context.Title;
        if (!string.IsNullOrWhiteSpace(file.SizeDisplay))
        {
            description = file.SizeDisplay;
        }
        else if (!string.IsNullOrWhiteSpace(item.Description))
        {
            description = item.Description;
        }

        return new ParsedContentDetails(
            Name: file.Name,
            Description: description,
            Author: author,
            PreviewImage: file.ThumbnailUrl ?? string.Empty,
            Screenshots: file.ThumbnailUrl is not null ? [file.ThumbnailUrl] : [],
            FileSize: file.SizeBytes ?? 0,
            DownloadCount: file.DownloadCount ?? 0,
            SubmissionDate: subDate,
            DownloadUrl: file.DownloadUrl ?? string.Empty,
            TargetGame: gameType,
            ContentType: contentType,
            FileType: Path.GetExtension(file.DownloadUrl) ?? ".zip",
            Rating: 0f,
            RefererUrl: item?.SourceUrl);
    }
}
