using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Parsers;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Parsers;
using Microsoft.Extensions.Logging;
using IDocument = AngleSharp.Dom.IDocument;

namespace GenHub.Features.Content.Services.Parsers;

/// <summary>
/// Parser for AODMaps pages that extracts map items from gallery pages.
/// </summary>
public partial class AODMapsPageParser(
    IPlaywrightService playwrightService,
    ILogger<AODMapsPageParser> logger) : IWebPageParser
{
    /// <summary>
    /// Regex to extract download count from script content.
    /// </summary>
    [GeneratedRegex(@"(\d+(?:,\d{3})*)\s*times\s*downloaded", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadCountRegex();

    /// <summary>
    /// Extracts global context from the page header.
    /// </summary>
    private static GlobalContext ExtractGlobalContext(IDocument document)
    {
        var headerEl = document.QuerySelector("header#header h1");
        var title = headerEl?.TextContent?.Trim() ?? "AODMaps";

        // Try to find author from map maker page context
        string? author = null;
        if (title.Contains("'s AOD Maps", StringComparison.OrdinalIgnoreCase))
        {
            author = title.Replace("'s AOD Maps", string.Empty).Trim();
        }

        return new GlobalContext(
            Title: title,
            Developer: author ?? "AODMaps Community",
            ReleaseDate: null);
    }

    private static int? ExtractDownloadCount(IElement item)
    {
        var spans = item.QuerySelectorAll("span");
        foreach (var span in spans)
        {
            var text = span.TextContent?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                var match = DownloadCountRegex().Match(text);
                if (match.Success)
                {
                    var countStr = match.Groups[1].Value.Replace(",", string.Empty);
                    if (int.TryParse(countStr, out var count))
                    {
                        return count;
                    }
                }
            }
        }

        return null;
    }

    private string MakeAbsoluteUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        // Fix for PashaCNC links - they are dead, replace with current domain
        if (url.Contains("pashacnc.com", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Detected dead pashacnc.com link, replacing with aodmaps.com: {Url}", url);
            url = url.Replace("pashacnc.com", "aodmaps.com", StringComparison.OrdinalIgnoreCase);
            url = url.Replace("www.pashacnc.com", "aodmaps.com", StringComparison.OrdinalIgnoreCase);
        }

        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return $"{AODMapsConstants.BaseUrl.TrimEnd('/')}/{url.TrimStart('/')}";
    }

    /// <summary>
    /// Extracts content sections (maps) from the document.
    /// </summary>
    private List<ContentSection> ExtractSections(IDocument document)
    {
        var sections = new List<ContentSection>();

        // 1. Try Gallery Items (Standard List)
        var galleryItems = document.QuerySelectorAll(AODMapsConstants.GalleryItemSelector);
        if (galleryItems.Length > 0)
        {
            foreach (var item in galleryItems)
            {
                var file = ExtractFileFromGalleryItem(item);
                if (file != null)
                {
                    sections.Add(file);
                }
            }
        }

        // 2. Try Map Maker Items (Vertical Layout)
        var mmItems = document.QuerySelectorAll(AODMapsConstants.MapMakerContainerSelector);
        if (mmItems.Length > 0)
        {
            foreach (var item in mmItems)
            {
                var contentDiv = item.QuerySelector(AODMapsConstants.MapMakerContentSelector);
                if (contentDiv != null)
                {
                    var file = ExtractFileFromMapMakerItem(contentDiv);
                    if (file != null)
                    {
                        sections.Add(file);
                    }
                }
            }
        }

        return sections;
    }

    /// <summary>
    /// Extracts a file from a gallery item element.
    /// </summary>
    private File? ExtractFileFromGalleryItem(IElement item)
    {
        var linkEl = item.QuerySelector(AODMapsConstants.GalleryDownloadLinkSelector);
        if (linkEl == null)
        {
            return null;
        }

        var downloadUrl = linkEl.GetAttribute(AODMapsConstants.HrefAttribute);
        if (string.IsNullOrEmpty(downloadUrl))
        {
            return null;
        }

        downloadUrl = MakeAbsoluteUrl(downloadUrl);

        var nameEl = item.QuerySelector(AODMapsConstants.GalleryMapNameSelector);
        var name = nameEl?.TextContent?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var downloadCount = ExtractDownloadCount(item);

        return new File(
            Name: name,
            Version: "0",
            SizeBytes: null,
            SizeDisplay: null,
            UploadDate: null,
            Category: "Map",
            Uploader: AODMapsConstants.DefaultAuthorName,
            DownloadUrl: downloadUrl,
            Md5Hash: null,
            CommentCount: downloadCount);
    }

    /// <summary>
    /// Extracts a file from a map maker item element.
    /// </summary>
    private File? ExtractFileFromMapMakerItem(IElement item)
    {
        // Download URL
        var downloadEl = item.QuerySelector(AODMapsConstants.MapMakerDownloadSelector) ?? item.QuerySelector("a[href*='ccount/click.php']");
        var downloadUrl = downloadEl?.GetAttribute(AODMapsConstants.HrefAttribute);
        if (string.IsNullOrEmpty(downloadUrl))
        {
            return null;
        }

        downloadUrl = MakeAbsoluteUrl(downloadUrl);

        // Name
        var titleEl = item.QuerySelector(AODMapsConstants.MapMakerTitleSelector);
        var name = titleEl?.TextContent?.Trim().TrimStart('-').Trim() ?? "Unknown Map";

        // Description
        var info = item.QuerySelector(AODMapsConstants.MapMakerInfoSelector)?.TextContent?.Trim();

        return new File(
             Name: name,
             Version: "0",
             SizeBytes: null,
             SizeDisplay: info,
             UploadDate: null,
             Category: "Map",
             Uploader: "MapMaker",
             DownloadUrl: downloadUrl,
             Md5Hash: null,
             CommentCount: null);
    }

    /// <inheritdoc />
    public string ParserId => "AODMaps";

    /// <inheritdoc />
    public bool CanParse(string url) =>
        url.Contains("aodmaps.com", StringComparison.OrdinalIgnoreCase) &&
        !url.Contains("moddb.com", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<ParsedWebPage> ParseAsync(string url, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Parsing AODMaps page: {Url}", url);

        var document = await playwrightService.FetchAndParseAsync(url, cancellationToken);
        return ParseInternal(url, document);
    }

    /// <inheritdoc />
    public async Task<ParsedWebPage> ParseAsync(string url, string html, CancellationToken cancellationToken = default)
    {
        var browsingContext = BrowsingContext.New(Configuration.Default);
        var document = await browsingContext.OpenAsync(req => req.Content(html), cancellationToken).ConfigureAwait(false);
        return ParseInternal(url, document);
    }

    /// <summary>
    /// Internal parsing logic that works with a parsed AngleSharp document.
    /// </summary>
    private ParsedWebPage ParseInternal(string url, IDocument document)
    {
        var context = ExtractGlobalContext(document);
        var pageType = PageType.List; // AODMaps pages are lists/galleries

        logger.LogDebug("Detected page type: {PageType}", pageType);

        var sections = ExtractSections(document);

        logger.LogInformation(
            "Parsed AODMaps page: {Url}, Sections={SectionCount}",
            url,
            sections.Count);

        return new ParsedWebPage(
            Url: new Uri(url),
            Context: context,
            Sections: sections,
            PageType: pageType);
    }
}
