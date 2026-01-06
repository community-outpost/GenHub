using System;
using System.Globalization;
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
using GenHub.Core.Models.ModDB;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging;
using MapDetails = GenHub.Core.Models.ModDB.MapDetails;

namespace GenHub.Features.Content.Services.ContentResolvers;

/// <summary>
/// Resolves ModDB content details from discovered items.
/// Parses HTML detail pages and generates content manifests.
/// </summary>
public class ModDBResolver(
    HttpClient httpClient,
    ModDBManifestFactory manifestFactory,
    ILogger<ModDBResolver> logger) : IContentResolver
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ModDBManifestFactory _manifestFactory = manifestFactory;
    private readonly ILogger<ModDBResolver> _logger = logger;

    /// <summary>
    /// Extracts submission/release date from the page.
    /// Critical for manifest ID generation which requires YYYYMMDD format.
    /// </summary>
    private static DateTime ExtractSubmissionDate(IDocument document)
    {
        // Try various selectors for date
        // ModDB often uses <time> elements or "Added" / "Posted" labels

        // Try <time datetime="..."> first
        var timeEl = document.QuerySelector("time[datetime]");
        if (timeEl != null)
        {
            var dateTimeAttr = timeEl.GetAttribute("datetime");
            if (DateTime.TryParse(dateTimeAttr, out var dt))
            {
                return dt;
            }
        }

        // Try finding "Added:" or "Posted:" labels
        var addedText = ExtractMetadataValue(document, "Added:", "Posted:", "Released:");
        if (!string.IsNullOrEmpty(addedText))
        {
            // Try to parse various date formats
            if (DateTime.TryParse(addedText, out var dt))
            {
                return dt;
            }
        }

        // Fallback: use current date
        return DateTime.UtcNow;
    }

    /// <summary>
    /// Extracts a metadata value by searching for labels.
    /// </summary>
    private static string? ExtractMetadataValue(IDocument document, params string[] labels)
    {
        foreach (var label in labels)
        {
            // Try finding in headers/labels
            var labelEl = document.QuerySelectorAll("strong, label, dt, th, span.label")
                .FirstOrDefault(el => el.TextContent?.Contains(label, StringComparison.OrdinalIgnoreCase) == true);

            if (labelEl != null)
            {
                // Try next sibling
                var value = labelEl.NextSibling?.TextContent?.Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }

                // Try parent -> next sibling
                value = labelEl.ParentElement?.NextElementSibling?.TextContent?.Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public string ResolverId => ModDBConstants.ResolverId;

    /// <inheritdoc />
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
            _logger.LogInformation("Resolving ModDB content from {Url}", discoveredItem.SourceUrl);

            // Fetch HTML
            var html = await _httpClient.GetStringAsync(discoveredItem.SourceUrl, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Parse details from HTML
            var mapDetails = await ParseModDetailPageAsync(html, discoveredItem, cancellationToken);

            if (string.IsNullOrEmpty(mapDetails.DownloadUrl))
            {
                return OperationResult<ContentManifest>.CreateFailure("No download URL found in mod details");
            }

            // Use factory to create manifest
            var manifest = await _manifestFactory.CreateManifestAsync(mapDetails, discoveredItem.SourceUrl);

            _logger.LogInformation(
                "Successfully resolved ModDB content: {ManifestId} - {Name}",
                manifest.Id.Value,
                manifest.Name);

            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while resolving mod details from {Url}", discoveredItem.SourceUrl);
            return OperationResult<ContentManifest>.CreateFailure($"Failed to fetch content: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve mod details from {Url}", discoveredItem.SourceUrl);
            return OperationResult<ContentManifest>.CreateFailure($"Resolution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses the HTML detail page for a ModDB mod and extracts all relevant details.
    /// </summary>
    private async Task<MapDetails> ParseModDetailPageAsync(
        string html,
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken)
    {
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), cancellationToken);

        // 1. Name
        var name = discoveredItem.Name; // Already have from discovery
        var nameFromPage = document.QuerySelector("h1[itemprop='name'], div.heading h4")?.TextContent?.Trim();
        if (!string.IsNullOrEmpty(nameFromPage))
        {
            name = nameFromPage;
        }

        _logger.LogDebug("Parsed name: {Name}", name);

        // 2. Description
        var descEl = document.QuerySelector("div#profiledescription, div.description, p[itemprop='description']");
        var description = descEl?.TextContent?.Trim() ?? discoveredItem.Description ?? string.Empty;

        // 3. Author
        var authorLink = document.QuerySelector("a[href*='/members/'], span.author, div.creator a");
        var author = authorLink?.TextContent?.Trim() ?? discoveredItem.AuthorName ?? ModDBConstants.DefaultAuthor;

        _logger.LogDebug("Parsed author: {Author}", author);

        // 4. Download URL
        var downloadLink = document.QuerySelector("a.buttondownload, a[href*='/downloads/start/'], a.downloadslink");
        var downloadUrl = downloadLink?.GetAttribute("href") ?? string.Empty;

        // Ensure absolute URL
        if (!string.IsNullOrEmpty(downloadUrl) && !downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            downloadUrl = ModDBConstants.BaseUrl + downloadUrl;
        }

        _logger.LogDebug("Parsed download URL: {DownloadUrl}", downloadUrl);

        // 5. File size
        var fileSizeText = ExtractMetadataValue(document, "Size:", "File Size:");
        var fileSize = FileSizeFormatter.ParseToBytes(fileSizeText);

        // 6. Submission date (for manifest ID)
        var submissionDate = ExtractSubmissionDate(document);

        _logger.LogDebug("Parsed submission date: {Date}", submissionDate);

        // 7. Download count
        var downloadsText = ExtractMetadataValue(document, "Downloads:");
        var downloadCount = int.TryParse(downloadsText?.Replace(",", string.Empty).Replace(".", string.Empty).Trim(), out var dc) ? dc : 0;

        // 8. Preview image
        var previewImage = discoveredItem.IconUrl;
        var imgFromPage = document.QuerySelector("img.image, div.imagebox img")?.GetAttribute("src");
        if (!string.IsNullOrEmpty(imgFromPage))
        {
            previewImage = imgFromPage.StartsWith("http") ? imgFromPage : ModDBConstants.BaseUrl + imgFromPage;
        }

        // 9. Screenshots
        var screenshots = document.QuerySelectorAll("div.mediarow img, div.screenshot img, a[href*='/images'] img")
            .Select(img => img.GetAttribute("src"))
            .Where(src => !string.IsNullOrEmpty(src))
            .Select(src => src!.StartsWith("http") ? src : ModDBConstants.BaseUrl + src)
            .ToList();

        // 10. Determine content type from discoveredItem or category on page
        var contentType = discoveredItem.ContentType;
        var categoryEl = document.QuerySelector("span.category, dd.category");
        if (categoryEl != null)
        {
            var mappedType = ModDBCategoryMapper.MapCategoryByName(categoryEl.TextContent);

            // Only overwrite if the new type is not generic Addon, or if we currently have generic Addon
            if (mappedType != ContentType.Addon || contentType == ContentType.Addon)
            {
                contentType = mappedType;
            }
        }

        // 11. Target game from discoveredItem
        var targetGame = discoveredItem.TargetGame;

        return new MapDetails(
            Name: name,
            Description: description,
            Author: author,
            PreviewImage: previewImage ?? string.Empty,
            Screenshots: screenshots,
            FileSize: fileSize,
            DownloadCount: downloadCount,
            SubmissionDate: submissionDate,
            DownloadUrl: downloadUrl,
            TargetGame: targetGame,
            ContentType: contentType);
    }
}