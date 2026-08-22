using System;
using System.Diagnostics.CodeAnalysis;
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
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Helpers;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging;
using ParsedContentDetails = GenHub.Core.Models.Content.ParsedContentDetails;

namespace GenHub.Features.Content.Services.ContentResolvers;

/// <summary>
/// Resolves CNC Labs map details from discovered content items.
/// Parses HTML detail pages and generates content manifests.
/// </summary>
[SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Domain acronym")]
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
        logger.LogInformation(
            "[TEMP] CNCLabsMapResolver.ResolveAsync called - Item: {Name}, SourceUrl: {Url}",
            discoveredItem?.Name,
            discoveredItem?.SourceUrl);

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

            if (string.IsNullOrEmpty(mapDetails.DownloadUrl))
            {
                return OperationResult<ContentManifest>.CreateFailure("No download URL found in map details");
            }

            if (!mapId.HasValue)
            {
                 logger.LogWarning("Invalid or missing map ID in resolver metadata for {Url}", discoveredItem.SourceUrl);
                 return OperationResult<ContentManifest>.CreateFailure("Invalid map ID in resolver metadata");
            }

            // The new site shows no title element in some detail pages; fall back to the
            // discovered item's name when the parser could not find one.
            if (string.IsNullOrEmpty(mapDetails.Name))
            {
                mapDetails = mapDetails with { Name = discoveredItem.Name ?? string.Empty };
            }

            // The redesigned detail page no longer exposes the breadcrumb the parser used for
            // game/content-type detection, so those come back as Unknown. The discoverer already
            // knows both from the list page the user browsed (e.g. Zero Hour Maps), so trust it.
            if (mapDetails.ContentType == ContentType.UnknownContentType && discoveredItem.ContentType != ContentType.UnknownContentType)
            {
                mapDetails = mapDetails with { ContentType = discoveredItem.ContentType };
            }

            if (mapDetails.TargetGame == GameType.Unknown &&
                discoveredItem.TargetGame != GameType.Unknown)
            {
                mapDetails = mapDetails with { TargetGame = discoveredItem.TargetGame };
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
    /// Reads a value from the detail page's definition list (2026 Bootstrap redesign), e.g.
    /// dt "Submitted" → dd "May 14, 2026".
    /// </summary>
    /// <param name="document">The parsed document.</param>
    /// <param name="label">The dt label to look for.</param>
    /// <returns>The dd text, or null when not present.</returns>
    private static string? ExtractDefinitionValue(IDocument document, string label)
    {
        var dt = document.QuerySelectorAll("dt")
            .FirstOrDefault(d => string.Equals(d.TextContent?.Trim(), label, StringComparison.OrdinalIgnoreCase));
        return dt?.NextElementSibling?.TextContent?.Trim();
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

        // 2026 site redesign: one tokenized download anchor per detail page. The token works with
        // a plain GET and no cookies (verified), so keep the href exactly as served.
        var downloadLink = document.QuerySelector("a[href*='/downloads/file/']");

        var downloadUrl = downloadLink?.GetAttribute(CNCLabsConstants.HrefAttribute) ?? string.Empty;

        // Ensure absolute URL
        if (!string.IsNullOrEmpty(downloadUrl) && !downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            downloadUrl = $"{CNCLabsConstants.PublisherWebsite.TrimEnd('/')}/{downloadUrl.TrimStart('/')}";
        }

        // Rewrite downloader.aspx to fetch.aspx to bypass JS redirect
        if (downloadUrl.Contains("downloader.aspx", StringComparison.OrdinalIgnoreCase))
        {
            downloadUrl = downloadUrl.Replace("downloader.aspx", "fetch.aspx", StringComparison.OrdinalIgnoreCase);
            logger.LogDebug("Rewrote downloader URL to direct fetch URL: {DownloadUrl}", downloadUrl);
        }

        logger.LogDebug("Parsed download URL: {DownloadUrl}", downloadUrl);

        // 6. File metadata (optional but useful). Prefer the 2026 redesign's <dl> definition list,
        // then fall back to the legacy <strong>-based extraction for older cached pages.
        var fileSizeText = ExtractDefinitionValue(document, "File Size") ?? ExtractMetadataValue(document, "File Size:");
        var fileSize = FileSizeFormatter.ParseToBytes(fileSizeText);

        var submittedText = ExtractDefinitionValue(document, "Submitted") ?? ExtractMetadataValue(document, "Submitted:");
        var submissionDate = DateTime.TryParse(submittedText, out var sd) ? sd : DateTime.MinValue;

        var downloadsText = ExtractDefinitionValue(document, "Downloads") ?? ExtractMetadataValue(document, "Downloads:");
        var downloadCount = int.TryParse(downloadsText?.Replace(",", string.Empty), out var dc) ? dc : 0;

        var ratingText = ExtractDefinitionValue(document, "Rating") ?? ExtractMetadataValue(document, "Rating:");
        var rating = float.TryParse(ratingText, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0f;

        // 7. Preview/screenshots (if available)
        var previewImage = document.QuerySelector("img.PreviewImage")?.GetAttribute("src") ?? string.Empty;
        if (!string.IsNullOrEmpty(previewImage) && !previewImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            previewImage = $"{CNCLabsConstants.PublisherWebsite}{previewImage}";
        }

        var screenshots = document.QuerySelectorAll("img.Screenshot")
            .Select(img => img.GetAttribute("src"))
            .Where(src => !string.IsNullOrEmpty(src))
            .Select(src => src.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? src
                : $"{CNCLabsConstants.PublisherWebsite}{src}")
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
