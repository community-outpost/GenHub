using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Helpers;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging;
using File = GenHub.Core.Models.Parsers.File;
using ParsedContentDetails = GenHub.Core.Models.Content.ParsedContentDetails;

namespace GenHub.Features.Content.Services.ContentResolvers;

/// <summary>
/// Resolves CNC Labs map details from discovered content items.
/// Parses HTML detail pages and generates content manifests.
/// </summary>
public class CNCLabsMapResolver(
    HttpClient httpClient,
    CNCLabsManifestFactory manifestFactory,
    ILogger<CNCLabsMapResolver> logger) : IContentResolver
{
    /// <summary>
    /// Gets the unique resolver ID for CNC Labs Map.
    /// </summary>
    public string ResolverId => CNCLabsConstants.ResolverId;

    /// <summary>
    /// Resolves the details of a discovered CNC Labs map item.
    /// </summary>
    /// <param name="discoveredItem">The discovered content item to resolve.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="OperationResult{ContentManifest}"/> containing the resolved details.</returns>
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
            var sourceUrl = discoveredItem.SourceUrl;
            if (!Uri.IsWellFormedUriString(sourceUrl, UriKind.Absolute))
            {
                // Ensure raw relative URLs are properly combined with base website URL
                sourceUrl = $"{CNCLabsConstants.PublisherWebsite.TrimEnd('/')}/{sourceUrl.TrimStart('/')}";
                logger.LogDebug("Converted relative URL to absolute: {AbsoluteUrl}", sourceUrl);
            }

            // Extract map ID from metadata early for fallback usage
            int? mapId = null;
            if (discoveredItem.ResolverMetadata.TryGetValue(CNCLabsConstants.MapIdMetadataKey, out var mapIdStr)
                && int.TryParse(mapIdStr, out var id))
            {
                mapId = id;
            }

            logger.LogInformation("Resolving CNC Labs content from {Url} (Map ID: {MapId})", sourceUrl, mapId);

            // Fetch HTML
            var html = await httpClient.GetStringAsync(sourceUrl, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Parse details from HTML
            var mapDetails = await ParseMapDetailPageAsync(html, cancellationToken);

            // Fallback: Construct download URL from Map ID if parsing failed
            if (string.IsNullOrEmpty(mapDetails.DownloadUrl) && mapId.HasValue)
            {
                mapDetails = mapDetails with
                {
                    DownloadUrl = $"{CNCLabsConstants.PublisherWebsite}/downloads/fetch.aspx?id={mapId}",
                };
                logger.LogWarning("Download URL parsing failed. Constructed fallback URL: {FallbackUrl}", mapDetails.DownloadUrl);
            }

            if (string.IsNullOrEmpty(mapDetails.DownloadUrl))
            {
                return OperationResult<ContentManifest>.CreateFailure("No download URL found in map details");
            }

            if (!mapId.HasValue)
            {
                logger.LogWarning("Invalid or missing map ID in resolver metadata for {Url}", discoveredItem.SourceUrl);
                return OperationResult<ContentManifest>.CreateFailure("Invalid map ID in resolver metadata");
            }

            // Use factory to create manifest
            var manifest = await manifestFactory.CreateManifestAsync(mapDetails);

            logger.LogInformation(
                "Successfully resolved CNC Labs content: {ManifestId} - {Name}",
                manifest.Id.Value,
                manifest.Name);

            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error while resolving map details from {Url}", discoveredItem.SourceUrl);
            return OperationResult<ContentManifest>.CreateFailure($"Failed to fetch content: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve map details from {Url}", discoveredItem.SourceUrl);
            return OperationResult<ContentManifest>.CreateFailure($"Resolution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts a metadata value from the document by finding a label and reading the next text sibling.
    /// </summary>
    /// <param name="document">The HTML document.</param>
    /// <param name="label">The label text to search for (e.g., "File Size:").</param>
    /// <returns>The extracted value or null if not found.</returns>
    private static string? ExtractMetadataValue(IDocument document, string label)
    {
        var strongEl = document.QuerySelectorAll("strong")
            .FirstOrDefault(s => s.TextContent?.Trim().EndsWith(label, StringComparison.OrdinalIgnoreCase) == true);

        return CNCLabsHelper.GetNextNonEmptyTextSibling(strongEl);
    }

    /// <summary>
    /// Parses the HTML detail page for a CNC Labs map and extracts all relevant details.
    /// </summary>
    /// <param name="html">The HTML content of the map detail page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="ParsedContentDetails"/> record containing parsed details.</returns>
    private async Task<ParsedContentDetails> ParseMapDetailPageAsync(string html, CancellationToken cancellationToken)
    {
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), cancellationToken);

        // 1. Name (from breadcrumb or .DisplayName selector)
        var name = document.QuerySelector(CNCLabsConstants.NameSelector)?.TextContent?.Trim()
            ?? document.QuerySelector(CNCLabsConstants.BreadcrumbHeaderSelector)
                ?.TextContent
                ?.Split(CNCLabsConstants.BreadcrumbSeparator)
                .LastOrDefault()
                ?.Trim()
            ?? string.Empty;

        logger.LogDebug("Parsed name: {Name}", name);

        // 2. Description
        var descEl = document.QuerySelector(CNCLabsConstants.DetailsPageDescriptionSelector);
        var description = descEl != null
            ? CNCLabsHelper.NormalizeHtmlDescription(descEl.InnerHtml)
            : string.Empty;

        // 3. Author (text node immediately after <strong>Author:</strong>)
        var authorStrong = document.QuerySelectorAll(CNCLabsConstants.AuthorLabelContainerSelector)
            .FirstOrDefault(s => string.Equals(
                s.TextContent?.Trim(),
                CNCLabsConstants.AuthorLabelText,
                StringComparison.OrdinalIgnoreCase));

        var author = CNCLabsHelper.GetNextNonEmptyTextSibling(authorStrong)
                     ?? CNCLabsConstants.DefaultAuthorName;

        logger.LogDebug("Parsed author: {Author}", author);

        // 4. Game Type and Content Type from breadcrumb
        var (gameType, contentType) = CNCLabsHelper.ExtractBreadcrumbCategory(document);
        logger.LogDebug("Detected game type: {GameType}, content type: {ContentType}", gameType, contentType);

        // 5. Download URL
        // 5. Download URL - Try multiple selectors for robustness
        var downloadLink = document.QuerySelector("a[href*='DownloadFile.aspx']")
                           ?? document.QuerySelector("a[href*='downloader.aspx']")
                           ?? document.QuerySelector("#ctl00_Main_MapDisplay_DownloadLink")
                           ?? document.QuerySelector("a[id$='DownloadButton']")
                           ?? document.QuerySelector("div.DownloadButton a");

        var downloadUrl = downloadLink?.GetAttribute(CNCLabsConstants.HrefAttribute) ?? string.Empty;

        // Ensure absolute URL
        if (!string.IsNullOrEmpty(downloadUrl))
        {
            if (!downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                downloadUrl = $"{CNCLabsConstants.PublisherWebsite.TrimEnd('/')}/{downloadUrl.TrimStart('/')}";
            }
        }

        // Rewrite downloader.aspx to fetch.aspx to bypass JS redirect
        if (downloadUrl.Contains("downloader.aspx", StringComparison.OrdinalIgnoreCase))
        {
            downloadUrl = downloadUrl.Replace("downloader.aspx", "fetch.aspx", StringComparison.OrdinalIgnoreCase);
            logger.LogDebug("Rewrote downloader URL to direct fetch URL: {DownloadUrl}", downloadUrl);
        }

        logger.LogDebug("Parsed download URL: {DownloadUrl}", downloadUrl);

        // 6. File metadata (optional but useful)
        var fileSizeText = ExtractMetadataValue(document, "File Size:");
        var fileSize = FileSizeFormatter.ParseToBytes(fileSizeText);

        var maxPlayersText = ExtractMetadataValue(document, "Max Players:");
        var maxPlayers = int.TryParse(maxPlayersText?.Trim(), out var p) ? p : 0;

        var submittedText = ExtractMetadataValue(document, "Submitted:");
        var submissionDate = DateTime.TryParse(submittedText, out var sd) ? sd : DateTime.MinValue;

        var downloadsText = ExtractMetadataValue(document, "Downloads:");
        var downloadCount = int.TryParse(downloadsText?.Replace(",", string.Empty), out var dc) ? dc : 0;

        var ratingText = ExtractMetadataValue(document, "Rating:");
        var rating = float.TryParse(ratingText, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0f;

        // 7. Preview/screenshots (if available)
        var previewImage = document.QuerySelector("img.PreviewImage")?.GetAttribute("src") ?? string.Empty;
        if (!string.IsNullOrEmpty(previewImage) && !previewImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            previewImage = $"https://www.cnclabs.com{previewImage}";
        }

        var screenshots = document.QuerySelectorAll("img.Screenshot")
            .Select(img => img.GetAttribute("src"))
            .Where(src => !string.IsNullOrEmpty(src))
            .Select(src => src!.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? src
                : $"https://www.cnclabs.com{src}")
            .ToList();

        return new ParsedContentDetails(
            Name: name,
            Description: description,
            Author: author,
            PreviewImage: previewImage,
            Screenshots: screenshots,
            FileSize: fileSize,
            DownloadCount: downloadCount,
            SubmissionDate: submissionDate,
            DownloadUrl: downloadUrl,
            TargetGame: gameType,
            ContentType: contentType,
            FileType: Path.GetExtension(downloadUrl),
            Rating: rating);
    }
}
