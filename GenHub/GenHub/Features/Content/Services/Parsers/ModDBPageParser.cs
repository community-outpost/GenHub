using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
/// Parser for ModDB pages that extracts rich content including files, videos, images, articles, reviews, and comments.
/// </summary>
public class ModDBPageParser(IPlaywrightService playwrightService, ILogger<ModDBPageParser> logger) : IWebPageParser
{
    /// <summary>
    /// Detects the page type based on URL patterns and DOM structure.
    /// <summary>
    /// Determines the ModDB page type by inspecting the document DOM and URL patterns.
    /// </summary>
    /// <param name="url">The page URL used to identify list-like paths (for example, "/addons" or "/images").</param>
    /// <param name="document">The parsed HTML document used to detect DOM-specific markers.</param>
    /// <returns>One of PageType.FileDetail, PageType.List, PageType.Summary, or PageType.Detail indicating the page category.</returns>
    private static PageType DetectPageType(string url, IDocument document)
    {
        // Check for file detail page
        if (document.QuerySelector(ModDBParserConstants.DownloadsInfoSelector) != null)
        {
            return PageType.FileDetail;
        }

        // Check for list pages (addons, images)
        if (url.Contains("/addons", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("/images", StringComparison.OrdinalIgnoreCase))
        {
            return PageType.List;
        }

        // Check for summary/news pages
        if (document.QuerySelector(ModDBParserConstants.ArticlesBrowseSelector) != null)
        {
            return PageType.Summary;
        }

        // Default to detail page
        return PageType.Detail;
    }

    /// <summary>
    /// Extracts content sections from summary/news pages.
    /// <summary>
    /// Extracts content sections from a summary (news) page.
    /// </summary>
    /// <param name="document">The parsed HTML document to extract articles from.</param>
    /// <returns>A list of ContentSection items representing articles found in the document.</returns>
    private static List<ContentSection> ExtractSummarySections(IDocument document)
    {
        var sections = new List<ContentSection>();

        // Extract articles
        sections.AddRange(ExtractArticles(document));

        return sections;
    }

    /// <summary>
    /// Extracts a file from a row element.
    /// </summary>
    /// <param name="row">The row element containing file information.</param>
    /// <summary>
    /// Builds a File model from a DOM row representing a file entry on a ModDB page.
    /// </summary>
    /// <param name="row">The HTML row element containing file entry fields (name, size, date, download link, comments).</param>
    /// <param name="sectionType">The file section category to assign to the created File (e.g., Downloads or Addons).</param>
    /// <returns>A <see cref="File"/> populated with the row's name, size, dates, download URL, comment count, and section type, or <c>null</c> if the row does not contain a valid file name.</returns>
    private static File? ExtractFileFromRow(IElement row, FileSectionType sectionType = FileSectionType.Downloads)
    {
        var nameEl = row.QuerySelector(ModDBParserConstants.FileNameSelector);
        var name = nameEl?.TextContent?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var sizeEl = row.QuerySelector(ModDBParserConstants.FileSizeSelector);
        var sizeText = sizeEl?.TextContent?.Trim();
        long? sizeBytes = null;
        if (!string.IsNullOrEmpty(sizeText))
        {
            sizeBytes = ParseFileSize(sizeText);
        }

        var dateEl = row.QuerySelector(ModDBParserConstants.FileDateSelector);
        DateTime? uploadDate = null;
        DateTime? releaseDate = null;
        if (dateEl != null)
        {
            var dateStr = dateEl.GetAttribute("datetime") ?? dateEl.TextContent?.Trim();
            if (!string.IsNullOrEmpty(dateStr))
            {
                // Try parsing the date string
                if (DateTime.TryParse(dateStr, out var standardDate))
                {
                    uploadDate = standardDate;
                    releaseDate = standardDate;
                }
                else
                {
                    // Try parsing common ModDB date formats like "Mar 15th, 2024"
                    var modDBDate = ParseModDBDate(dateStr);
                    if (modDBDate.HasValue)
                    {
                        uploadDate = modDBDate;
                        releaseDate = modDBDate;
                    }
                }
            }
        }

        var linkEl = row.QuerySelector(ModDBParserConstants.FileDownloadSelector);
        var downloadUrl = linkEl?.GetAttribute("href");
        if (!string.IsNullOrEmpty(downloadUrl) && !downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            downloadUrl = "https:" + downloadUrl;
        }

        var commentCountEl = row.QuerySelector(ModDBParserConstants.FileCommentCountSelector);
        int? commentCount = null;
        if (commentCountEl != null)
        {
            var countText = commentCountEl.TextContent?.Trim();
            if (!string.IsNullOrEmpty(countText) && int.TryParse(countText, out var count))
            {
                commentCount = count;
            }
        }

        return new File(
            Name: name,
            SizeBytes: sizeBytes,
            SizeDisplay: sizeText,
            UploadDate: uploadDate,
            DownloadUrl: downloadUrl,
            CommentCount: commentCount,
            FileSectionType: sectionType,
            ReleaseDate: releaseDate);
    }

    /// <summary>
    /// Extracts metadata from the sidebar profile section.
    /// <summary>
    /// Extracts metadata from the page's profile/sidebar section.
    /// </summary>
    /// <param name="document">The parsed HTML document to read the profile/sidebar from.</param>
    /// <returns>A ProfileMeta containing name, size in bytes (if parsed), size display string, release date, developer/uploader, and MD5 hash; fields are null when not present.</returns>
    private static ProfileMeta ExtractProfileMeta(IDocument document)
    {
        var sidebar = document.QuerySelector(ModDBParserConstants.ProfileSidebarSelector);
        if (sidebar == null)
        {
            return new ProfileMeta(null, null, null, null, null, null);
        }

        string? name = null; // Often the page title, but sometimes in sidebar
        long? sizeBytes = null;
        string? sizeDisplay = null;
        DateTime? releaseDate = null;
        string? developer = null;
        string? md5Hash = null;

        var rows = sidebar.QuerySelectorAll(ModDBParserConstants.ProfileRowSelector);
        foreach (var row in rows)
        {
            var labelEl = row.QuerySelector(ModDBParserConstants.ProfileLabelSelector);
            var contentEl = row.QuerySelector(ModDBParserConstants.ProfileContentSelector);

            if (labelEl == null || contentEl == null)
            {
                continue;
            }

            var label = labelEl.TextContent?.Trim().ToLowerInvariant().Replace(":", string.Empty);
            var content = contentEl.TextContent?.Trim();

            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(content))
            {
                continue;
            }

            switch (label)
            {
                case "filename":
                case "file":
                    name = content;
                    break;
                case "size":
                    sizeDisplay = content;

                    // Sometimes format is "134mb (140,505,749 bytes)"
                    // Try to parse the bytes part first if available
                    if (content.Contains("bytes") && content.Contains('(') && content.Contains(')'))
                    {
                         var bytesPart = content.Split('(').Last().Replace("bytes)", string.Empty).Replace(",", string.Empty).Trim();
                         if (long.TryParse(bytesPart, out var bytesVal))
                         {
                             sizeBytes = bytesVal;
                         }
                    }

                    sizeBytes ??= ParseFileSize(content);

                    break;
                case "uploader":
                case "author":
                    developer = content;
                    break;
                case "added":
                case "date":
                case "release date":
                    if (DateTime.TryParse(content, out var dt))
                    {
                         releaseDate = dt;
                    }

                    break;
                case "md5 hash":
                case "md5":
                    md5Hash = content;
                    break;
            }
        }

        return new ProfileMeta(name, sizeBytes, sizeDisplay, releaseDate, developer, md5Hash);
    }

    /// <summary>
    /// Extracts videos from the document.
    /// <summary>
    /// Extracts embedded video entries from the provided HTML document.
    /// </summary>
    /// <param name="document">The parsed HTML document to search for embedded videos.</param>
    /// <returns>A list of Video objects containing title, thumbnail URL, embed URL, and detected platform (e.g., "YouTube", "Vimeo", or "Unknown"); returns an empty list if no videos are found.</returns>
    private static List<Video> ExtractVideos(IDocument document)
    {
        var videos = new List<Video>();

        var videoElements = document.QuerySelectorAll(ModDBParserConstants.VideoSelector);
        foreach (var videoEl in videoElements)
        {
            var src = videoEl.GetAttribute("src");
            if (string.IsNullOrEmpty(src))
            {
                continue;
            }

            var titleEl = videoEl.QuerySelector(ModDBParserConstants.VideoTitleSelector);
            var title = titleEl?.TextContent?.Trim() ?? "Video";

            var thumbnailEl = videoEl.QuerySelector(ModDBParserConstants.VideoThumbnailSelector);
            var thumbnailUrl = thumbnailEl?.GetAttribute("src");
            if (!string.IsNullOrEmpty(thumbnailUrl) && !thumbnailUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                thumbnailUrl = "https:" + thumbnailUrl;
            }

            string? platform = "Unknown";
            if (src.Contains("youtube", StringComparison.OrdinalIgnoreCase) || src.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            {
                platform = "YouTube";
            }
            else if (src.Contains("vimeo", StringComparison.OrdinalIgnoreCase))
            {
                platform = "Vimeo";
            }

            if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                src = "https:" + src;
            }

            videos.Add(new Video(
                Title: title,
                ThumbnailUrl: thumbnailUrl,
                EmbedUrl: src,
                Platform: platform));
        }

        return videos;
    }

    /// <summary>
    /// Extracts images from the document.
    /// <summary>
    /// Extracts images from the page's image gallery and returns them as Image records.
    /// </summary>
    /// <returns>A list of Image objects representing each gallery image; empty if none found.</returns>
    private static List<Image> ExtractImages(IDocument document)
    {
        var images = new List<Image>();

        var gallery = document.QuerySelector(ModDBParserConstants.ImageGallerySelector);
        if (gallery == null)
        {
            return images;
        }

        var imgElements = gallery.QuerySelectorAll(ModDBParserConstants.ImageSelector);
        foreach (var img in imgElements)
        {
            var src = img.GetAttribute("src");
            if (string.IsNullOrEmpty(src))
            {
                continue;
            }

            var fullSizeUrl = src;
            if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                fullSizeUrl = "https:" + src;
            }

            var alt = img.GetAttribute("alt");

            images.Add(new Image(
                Title: alt ?? $"Image {images.Count + 1}",
                FullSizeUrl: fullSizeUrl,
                Description: alt));
        }

        return images;
    }

    /// <summary>
    /// Extracts an image from a row element.
    /// <summary>
    /// Extracts an image entry from a DOM row element representing a gallery/list item.
    /// </summary>
    /// <param name="row">The DOM element containing the image node to extract.</param>
    /// <returns>An Image with Title, FullSizeUrl, and Description if an image is found; otherwise <c>null</c>.</returns>
    private static Image? ExtractImageFromRow(IElement row)
    {
        var img = row.QuerySelector(ModDBParserConstants.ImageSelector);
        if (img == null)
        {
            return null;
        }

        var src = img.GetAttribute("src");
        if (string.IsNullOrEmpty(src))
        {
            return null;
        }

        var fullSizeUrl = src;
        if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            fullSizeUrl = "https:" + src;
        }

        var alt = img.GetAttribute("alt");

        return new Image(
            Title: alt ?? "Image",
            FullSizeUrl: fullSizeUrl,
            Description: alt);
    }

    /// <summary>
    /// Extracts articles from the document.
    /// <summary>
    /// Extracts article entries from the provided HTML document.
    /// </summary>
    /// <returns>A list of Article objects found in the document, each populated with Title, Author, PublishDate (when parsable), Content, and Url (normalized to an absolute URL when applicable); returns an empty list if no articles are found.</returns>
    private static List<Article> ExtractArticles(IDocument document)
    {
        var articles = new List<Article>();

        var articleRows = document.QuerySelectorAll(ModDBParserConstants.ArticlesSelector);
        foreach (var row in articleRows)
        {
            var titleEl = row.QuerySelector(ModDBParserConstants.ArticleTitleSelector);
            var title = titleEl?.TextContent?.Trim();
            if (string.IsNullOrEmpty(title))
            {
                continue;
            }

            var authorEl = row.QuerySelector(ModDBParserConstants.ArticleAuthorSelector);
            var author = authorEl?.TextContent?.Trim();

            var dateEl = row.QuerySelector(ModDBParserConstants.ArticleDateSelector);
            DateTime? publishDate = null;
            if (dateEl != null)
            {
                var dateStr = dateEl.GetAttribute("datetime") ?? dateEl.TextContent?.Trim();
                if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
                {
                    publishDate = parsedDate;
                }
            }

            var contentEl = row.QuerySelector(ModDBParserConstants.ArticleContentSelector);
            var content = contentEl?.TextContent?.Trim();

            var linkEl = row.QuerySelector(ModDBParserConstants.ArticleLinkSelector);
            var url = linkEl?.GetAttribute("href");
            if (!string.IsNullOrEmpty(url) && !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                url = "https:" + url;
            }

            articles.Add(new Article(
                Title: title,
                Author: author,
                PublishDate: publishDate,
                Content: content,
                Url: url));
        }

        return articles;
    }

    /// <summary>
    /// Extracts reviews from the document.
    /// <summary>
    /// Extracts user reviews from the provided HTML document and returns them as a list of Review objects.
    /// </summary>
    /// <param name="document">The parsed HTML document containing review elements to extract.</param>
    /// <returns>A list of extracted Review objects; empty if no reviews are present.</returns>
    private static List<Review> ExtractReviews(IDocument document)
    {
        var reviews = new List<Review>();

        var reviewRows = document.QuerySelectorAll(ModDBParserConstants.ReviewsSelector);
        foreach (var row in reviewRows)
        {
            var authorEl = row.QuerySelector(ModDBParserConstants.ReviewAuthorSelector);
            var author = authorEl?.TextContent?.Trim();

            var ratingEl = row.QuerySelector(ModDBParserConstants.ReviewRatingSelector);
            float? rating = null;
            if (ratingEl != null)
            {
                var ratingText = ratingEl.TextContent?.Trim();
                if (!string.IsNullOrEmpty(ratingText) && float.TryParse(ratingText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRating))
                {
                    rating = parsedRating;
                }
            }

            var contentEl = row.QuerySelector(ModDBParserConstants.ReviewContentSelector);
            var content = contentEl?.TextContent?.Trim();

            var dateEl = row.QuerySelector(ModDBParserConstants.ReviewDateSelector);
            DateTime? date = null;
            if (dateEl != null)
            {
                var dateStr = dateEl.GetAttribute("datetime") ?? dateEl.TextContent?.Trim();
                if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
                {
                    date = parsedDate;
                }
            }

            var helpfulEl = row.QuerySelector(ModDBParserConstants.ReviewHelpfulSelector);
            int? helpfulVotes = null;
            if (helpfulEl != null)
            {
                var votesText = helpfulEl.TextContent?.Trim();
                if (!string.IsNullOrEmpty(votesText) && int.TryParse(votesText, out var votes))
                {
                    helpfulVotes = votes;
                }
            }

            reviews.Add(new Review(
                Author: author,
                Rating: rating,
                Content: content,
                Date: date,
                HelpfulVotes: helpfulVotes));
        }

        return reviews;
    }

    /// <summary>
    /// Extracts comments from the document.
    /// <summary>
    /// Extracts user comments from the provided HTML document.
    /// </summary>
    /// <returns>A list of Comment objects representing each comment found in the document; an empty list if none are present.</returns>
    private static List<Comment> ExtractComments(IDocument document)
    {
        var comments = new List<Comment>();

        var commentRows = document.QuerySelectorAll(ModDBParserConstants.CommentRowSelector);
        foreach (var row in commentRows)
        {
            var authorEl = row.QuerySelector(ModDBParserConstants.CommentAuthorSelector);
            var author = authorEl?.TextContent?.Trim();

            var contentEl = row.QuerySelector(ModDBParserConstants.CommentContentSelector);
            var content = contentEl?.TextContent?.Trim();

            var dateEl = row.QuerySelector(ModDBParserConstants.CommentDateSelector);
            DateTime? date = null;
            if (dateEl != null)
            {
                var dateStr = dateEl.GetAttribute("datetime") ?? dateEl.TextContent?.Trim();
                if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
                {
                    date = parsedDate;
                }
            }

            var karmaEl = row.QuerySelector(ModDBParserConstants.CommentKarmaSelector);
            int? karma = null;
            if (karmaEl != null)
            {
                var karmaText = karmaEl.TextContent?.Trim();
                if (!string.IsNullOrEmpty(karmaText) && int.TryParse(karmaText, out var karmaValue))
                {
                    karma = karmaValue;
                }
            }

            var isCreator = row.QuerySelector(ModDBParserConstants.CommentCreatorSelector) != null;

            comments.Add(new Comment(
                Author: author,
                Content: content,
                Date: date,
                Karma: karma,
                IsCreator: isCreator));
        }

        return comments;
    }

    /// <summary>
    /// Parses a file size string (e.g., "15.5 MB") into bytes.
    /// <summary>
    /// Parses a human-readable file size (e.g., "15.5 MB") and converts it to bytes using 1024-based units.
    /// </summary>
    /// <param name="sizeText">The file size text to parse, typically a numeric value followed by a unit (GB, MB, KB, B).</param>
    /// <returns>`null` if the input cannot be parsed; otherwise the size in bytes as a long.</returns>
    private static long? ParseFileSize(string sizeText)
    {
        if (string.IsNullOrEmpty(sizeText))
        {
            return null;
        }

        var parts = sizeText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var unit = parts[1].ToUpperInvariant();

        return unit switch
        {
            "GB" => (long)(value * 1024 * 1024 * 1024),
            "MB" => (long)(value * 1024 * 1024),
            "KB" => (long)(value * 1024),
            "B" => (long)value,
            _ => null,
        };
    }

    /// <summary>
    /// Parses ModDB date formats like "Mar 15th, 2024" or "Added Mar 15th, 2024".
    /// <summary>
    /// Parses ModDB-style human-readable date strings (optionally prefixed with "Added", "Released", or "Updated") into a DateTime.
    /// </summary>
    /// <param name="dateStr">The input date string; supports month/day/year formats and ordinal day suffixes (e.g., "Mar 15th, 2024", "Mar 1st, 2024", "2024-03-15", "03/15/2024").</param>
    /// <returns>The parsed <see cref="DateTime"/> if parsing succeeds; otherwise <c>null</c>.</returns>
    private static DateTime? ParseModDBDate(string dateStr)
    {
        if (string.IsNullOrEmpty(dateStr))
        {
            return null;
        }

        // Remove common prefixes
        dateStr = dateStr.Replace("Added", string.Empty, StringComparison.OrdinalIgnoreCase)
                        .Replace("Released", string.Empty, StringComparison.OrdinalIgnoreCase)
                        .Replace("Updated", string.Empty, StringComparison.OrdinalIgnoreCase)
                        .Trim();

        // Try parsing common formats
        // Format: "Mar 15th, 2024"
        var format1 = "MMM d'th', yyyy";
        if (DateTime.TryParseExact(dateStr, format1, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result1))
        {
            return result1;
        }

        // Try with st, nd, rd, th suffixes
        var formats = new[]
        {
            "MMM d'st', yyyy",
            "MMM d'nd', yyyy",
            "MMM d'rd', yyyy",
            "MMM d'th', yyyy",
            "MMM dd, yyyy",
            "MMM d, yyyy",
            "MMMM d, yyyy",
            "yyyy-MM-dd",
            "MM/dd/yyyy",
            "dd/MM/yyyy",
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(dateStr, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Helper class for profile metadata.
    /// </summary>
    private record ProfileMeta(
        string? Name,
        long? SizeBytes,
        string? SizeDisplay,
        DateTime? ReleaseDate,
        string? Developer,
        string? Md5Hash);

    /// <summary>
    /// Extracts content sections from list pages (addons, images).
    /// </summary>
    /// <param name="document">The document to extract from.</param>
    /// <summary>
    /// Extracts images and files from a list-style page and returns them as content sections.
    /// </summary>
    /// <param name="sectionType">Specifies how extracted file entries should be classified (e.g., Downloads or Addons).</param>
    /// <returns>A list of ContentSection items containing extracted images and files from the document's list rows.</returns>
    private static List<ContentSection> ExtractListSections(IDocument document, FileSectionType sectionType = FileSectionType.Downloads)
    {
        var sections = new List<ContentSection>();

        var rows = document.QuerySelectorAll(ModDBParserConstants.RowContentSelector);
        foreach (var row in rows)
        {
            // Extract image
            var image = ExtractImageFromRow(row);
            if (image != null)
            {
                sections.Add(image);
            }

            // Extract file info if present
            var file = ExtractFileFromRow(row, sectionType);
            if (file != null)
            {
                sections.Add(file);
            }
        }

        return sections;
    }

    /// <summary>
    /// Extracts files from the document.
    /// </summary>
    /// <param name="document">The document to extract files from.</param>
    /// <summary>
    /// Extracts all file entries from the document and returns them as a list of File objects.
    /// </summary>
    /// <param name="sectionType">The file section type to assign to each extracted file (e.g., downloads or addons).</param>
    /// <returns>A list of extracted File objects; empty if no file rows are found.</returns>
    private static List<File> ExtractFiles(IDocument document, FileSectionType sectionType = FileSectionType.Downloads)
    {
        var files = new List<File>();

        var fileRows = document.QuerySelectorAll(ModDBParserConstants.FileRowSelector);
        foreach (var row in fileRows)
        {
            var file = ExtractFileFromRow(row, sectionType);
            if (file != null)
            {
                files.Add(file);
            }
        }

        return files;
    }

    /// <summary>
    /// Extracts content sections from detail pages.
    /// </summary>
    /// <param name="document">The document to extract from.</param>
    /// <summary>
    /// Extracts all detail-page content sections — files, videos, images, articles, reviews, and comments — from the provided document.
    /// </summary>
    /// <param name="sectionType">Specifies which file section type to assign to extracted files (e.g., Downloads or Addons).</param>
    /// <returns>A combined list of ContentSection items found on the detail page.</returns>
    private static List<ContentSection> ExtractDetailSections(IDocument document, FileSectionType sectionType = FileSectionType.Downloads)
    {
        var sections = new List<ContentSection>();

        // Extract files
        sections.AddRange(ExtractFiles(document, sectionType));

        // Extract videos
        sections.AddRange(ExtractVideos(document));

        // Extract images
        sections.AddRange(ExtractImages(document));

        // Extract articles
        sections.AddRange(ExtractArticles(document));

        // Extract reviews
        sections.AddRange(ExtractReviews(document));

        // Extract comments
        sections.AddRange(ExtractComments(document));

        return sections;
    }

    /// <inheritdoc />
    public string ParserId => "ModDB";

    /// <inheritdoc />
    public bool CanParse(string url) =>
        url.Contains("moddb.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses the ModDB page at the specified URL into a structured ParsedWebPage.
    /// </summary>
    /// <param name="url">The ModDB page URL to parse.</param>
    /// <returns>A ParsedWebPage containing the page's global context, detected page type, and extracted content sections.</returns>
    public async Task<ParsedWebPage> ParseAsync(string url, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Parsing ModDB page: {Url}", url);

        var document = await playwrightService.FetchAndParseAsync(url, cancellationToken);
        return await ParseInternalAsync(url, document, cancellationToken);
    }

    /// <summary>
    /// Parse the provided HTML for a ModDB page and produce a ParsedWebPage representing the page's context and content sections.
    /// </summary>
    /// <param name="url">The original page URL used to resolve relative links and to help determine page type.</param>
    /// <param name="html">The raw HTML content of the page to parse.</param>
    /// <param name="cancellationToken">A token to observe while parsing; may be used to cancel the operation.</param>
    /// <returns>A <see cref="ParsedWebPage"/> containing the extracted global context and a collection of content sections.</returns>
    public Task<ParsedWebPage> ParseAsync(string url, string html, CancellationToken cancellationToken = default)
    {
        var browsingContext = BrowsingContext.New(Configuration.Default);
        var document = browsingContext.OpenAsync(req => req.Content(html), cancellationToken).GetAwaiter().GetResult();
        return Task.FromResult(ParseInternal(url, document));
    }

    /// <summary>
    /// Determines if the URL is a mod detail page that should have both downloads and addons.
    /// <summary>
    /// Determines whether a URL refers to a mod's main detail page (the /mods/{name} page) excluding downloads, addons, images, and news subpages.
    /// </summary>
    /// <param name="url">The URL to evaluate.</param>
    /// <returns>`true` if the URL points to a mod detail page that should fetch both downloads and addons; `false` otherwise.</returns>
    private static bool IsModDetailPage(string url)
    {
        // Check if URL matches /mods/mod-name pattern but not /mods/mod-name/downloads or /mods/mod-name/addons
        return url.Contains("/mods/", StringComparison.OrdinalIgnoreCase) &&
            !url.Contains("/downloads/", StringComparison.OrdinalIgnoreCase) &&
            !url.Contains("/addons/", StringComparison.OrdinalIgnoreCase) &&
            !url.Contains("/images/", StringComparison.OrdinalIgnoreCase) &&
            !url.Contains("/news/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Internal parsing logic that works with a parsed AngleSharp document.
    /// <summary>
    /// Parses a ModDB page document, detects its page type, and builds a ParsedWebPage containing the extracted global context and content sections.
    /// </summary>
    /// <param name="url">The page URL used for page-type detection and assigned to the resulting ParsedWebPage.</param>
    /// <param name="document">The parsed HTML document to extract data from.</param>
    /// <returns>A ParsedWebPage containing the original URL, the extracted global context, the collected content sections, and the detected page type.</returns>
    private ParsedWebPage ParseInternal(string url, IDocument document)
    {
        var context = ExtractGlobalContext(document);
        var pageType = DetectPageType(url, document);

        logger.LogDebug("Detected page type: {PageType}", pageType);

        var sections = new List<ContentSection>();

        switch (pageType)
        {
            case PageType.List:
                // Determine if this is an addons list or generic list
                var sectionType = url.Contains("/addons", StringComparison.OrdinalIgnoreCase)
                    ? FileSectionType.Addons
                    : FileSectionType.Downloads;
                sections.AddRange(ExtractListSections(document, sectionType));
                break;

            case PageType.Summary:
                sections.AddRange(ExtractSummarySections(document));
                break;

            case PageType.Detail:
                sections.AddRange(ExtractDetailSections(document));
                break;

            case PageType.FileDetail:
                sections.AddRange(ExtractFileDetailSections(document));
                break;

            default:
                logger.LogWarning("Unknown page type for URL: {Url}", url);
                break;
        }

        logger.LogInformation(
            "Parsed ModDB page: {Url}, Type={PageType}, Sections={SectionCount}",
            url,
            pageType,
            sections.Count);

        return new ParsedWebPage(
            Url: new Uri(url),
            Context: context,
            Sections: sections,
            PageType: pageType);
    }

    /// <summary>
    /// Async internal parsing logic that can fetch additional pages for mod detail pages.
    /// <summary>
    /// Parse a ModDB page and produce a ParsedWebPage representing its global context and extracted content sections.
    /// </summary>
    /// <param name="url">The page URL to parse.</param>
    /// <param name="document">The parsed HTML document to extract content from.</param>
    /// <param name="cancellationToken">A token to observe while waiting for asynchronous operations to complete.</param>
    /// <returns>A ParsedWebPage containing the page URL, extracted global context, collected content sections, and detected page type.</returns>
    private async Task<ParsedWebPage> ParseInternalAsync(string url, IDocument document, CancellationToken cancellationToken)
    {
        var context = ExtractGlobalContext(document);
        var pageType = DetectPageType(url, document);

        logger.LogDebug("Detected page type: {PageType}", pageType);

        var sections = new List<ContentSection>();

        // Special handling for mod detail pages - fetch both downloads and addons
        if (IsModDetailPage(url) && pageType == PageType.Detail)
        {
            logger.LogInformation("Mod detail page detected, fetching both downloads and addons sections");

            // Extract non-file content from the main page
            sections.AddRange(ExtractVideos(document));
            sections.AddRange(ExtractImages(document));
            sections.AddRange(ExtractArticles(document));
            sections.AddRange(ExtractReviews(document));
            sections.AddRange(ExtractComments(document));

            // Try to fetch the downloads section
            try
            {
                var downloadsUrl = url.TrimEnd('/') + "/downloads";
                logger.LogDebug("Fetching downloads section from {Url}", downloadsUrl);
                var downloadsDoc = await playwrightService.FetchAndParseAsync(downloadsUrl, cancellationToken);
                var downloadsFiles = ExtractFiles(downloadsDoc, FileSectionType.Downloads);
                sections.AddRange(downloadsFiles);
                logger.LogInformation("Found {Count} files in downloads section", downloadsFiles.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch downloads section for {Url}", url);
            }

            // Try to fetch the addons section
            try
            {
                var addonsUrl = url.TrimEnd('/') + "/addons";
                logger.LogDebug("Fetching addons section from {Url}", addonsUrl);
                var addonsDoc = await playwrightService.FetchAndParseAsync(addonsUrl, cancellationToken);
                var addonsFiles = ExtractFiles(addonsDoc, FileSectionType.Addons);
                sections.AddRange(addonsFiles);
                logger.LogInformation("Found {Count} files in addons section", addonsFiles.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch addons section for {Url}", url);
            }
        }
        else
        {
            // Standard page handling
            switch (pageType)
            {
                case PageType.List:
                    // Determine if this is an addons list or generic list
                    var listSectionType = url.Contains("/addons", StringComparison.OrdinalIgnoreCase)
                        ? FileSectionType.Addons
                        : FileSectionType.Downloads;
                    sections.AddRange(ExtractListSections(document, listSectionType));
                    break;

                case PageType.Summary:
                    sections.AddRange(ExtractSummarySections(document));
                    break;

                case PageType.Detail:
                    sections.AddRange(ExtractDetailSections(document));
                    break;

                case PageType.FileDetail:
                    sections.AddRange(ExtractFileDetailSections(document));
                    break;

                default:
                    logger.LogWarning("Unknown page type for URL: {Url}", url);
                    break;
            }
        }

        logger.LogInformation(
            "Parsed ModDB page: {Url}, Type={PageType}, Sections={SectionCount}",
            url,
            pageType,
            sections.Count);

        return new ParsedWebPage(
            Url: new Uri(url),
            Context: context,
            Sections: sections,
            PageType: pageType);
    }

    /// <summary>
    /// Extracts global context from the page header and profile sidebar.
    /// <summary>
    /// Builds a GlobalContext by extracting page-level metadata (title, developer, release date, game name, icon URL, and description) from the provided HTML document.
    /// </summary>
    /// <param name="document">The parsed HTML document to extract context from.</param>
    /// <returns>A GlobalContext populated with Title, Developer, ReleaseDate, GameName, IconUrl, and Description (fields may be null or contain fallback values when not present in the document).</returns>
    private GlobalContext ExtractGlobalContext(IDocument document)
    {
        // 1. Extract title from main header
        var titleEl = document.QuerySelector("h1 a, h2 a, h1, h2, .title");
        var title = titleEl?.TextContent?.Trim() ?? "Unknown";

        // 2. Extract developer from profile sidebar or header
        string developer;
        var sidebarDeveloperEl = document.QuerySelector(ModDBParserConstants.ProfileSidebarSelector + " a[href*='/members/'], " +
            ModDBParserConstants.ProfileSidebarSelector + " a[href*='/company/']");
        if (sidebarDeveloperEl != null)
        {
            developer = sidebarDeveloperEl.TextContent?.Trim() ?? "Unknown";
        }
        else
        {
            var headerDeveloperEl = document.QuerySelector(ModDBParserConstants.DeveloperSelector);
            developer = headerDeveloperEl?.TextContent?.Trim() ?? "Unknown";
        }

        // 3. Extract release date
        DateTime? releaseDate = null;
        var releaseDateEl = document.QuerySelector(ModDBParserConstants.ReleaseDateSelector);
        if (releaseDateEl != null)
        {
            var dateStr = releaseDateEl.GetAttribute("datetime") ?? releaseDateEl.TextContent?.Trim();
            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
            {
                releaseDate = parsedDate;
            }
        }

        // 4. Extract icon from profile sidebar
        var iconEl = document.QuerySelector(ModDBParserConstants.ProfileIconSelector)
            ?? document.QuerySelector(ModDBParserConstants.IconSelector);
        var iconUrl = iconEl?.GetAttribute("src");
        if (!string.IsNullOrEmpty(iconUrl) && !iconUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            iconUrl = "https:" + iconUrl;
        }

        // 5. Extract FULL description (critical fix - try multiple sources)
        string? description = null;

        // Try the full description selector first (article content, summary content)
        var fullDescEl = document.QuerySelector(ModDBParserConstants.FullDescriptionSelector);
        if (fullDescEl != null)
        {
            description = fullDescEl.TextContent?.Trim();
        }

        // Fallback to summary paragraph
        if (string.IsNullOrEmpty(description))
        {
            var summaryEl = document.QuerySelector(ModDBParserConstants.SummarySelector);
            description = summaryEl?.TextContent?.Trim();
        }

        // Final fallback to description selector
        if (string.IsNullOrEmpty(description))
        {
            var descEl = document.QuerySelector(ModDBParserConstants.DescriptionSelector);
            description = descEl?.TextContent?.Trim();
        }

        // 6. Extract game name
        var gameNameEl = document.QuerySelector(ModDBParserConstants.GameNameSelector);
        var gameName = gameNameEl?.TextContent?.Trim();

        logger.LogDebug(
            "Extracted context: Title={Title}, Developer={Developer}, IconUrl={Icon}, DescriptionLength={DescLen}",
            title,
            developer,
            iconUrl,
            description?.Length ?? 0);

        return new GlobalContext(
            Title: title,
            Developer: developer,
            ReleaseDate: releaseDate,
            GameName: gameName,
            IconUrl: iconUrl,
            Description: description);
    }

    /// <summary>
    /// Extracts content sections from file detail pages.
    /// <summary>
    /// Extracts the detailed file section from a file-detail page.
    /// </summary>
    /// <returns>A list containing the detailed file content section when one is present; otherwise an empty list.</returns>
    private List<ContentSection> ExtractFileDetailSections(IDocument document)
    {
        var sections = new List<ContentSection>();

        // Extract detailed file info
        var file = ExtractDetailedFile(document);
        if (file != null)
        {
            sections.Add(file);
        }

        return sections;
    }

    /// <summary>
    /// Extracts detailed file information from a file detail page.
    /// Parses the metadata table with rows like: Filename, Category, Uploader, Size, MD5 Hash.
    /// <summary>
    /// Extracts detailed file metadata from a file-detail page document and constructs a File object.
    /// </summary>
    /// <returns>A File populated with name, size (bytes and display), uploader, category, upload/release dates, download URL, and MD5 hash; or null if the document does not contain file detail information.</returns>
    private File? ExtractDetailedFile(IDocument document)
    {
        // Initialize metadata dictionary
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1. Try to find and parse the file metadata table
        var metadataTable = document.QuerySelector(ModDBParserConstants.FileMetadataContainerSelector);
        if (metadataTable != null)
        {
            var rows = metadataTable.QuerySelectorAll(ModDBParserConstants.FileMetadataRowSelector);
            foreach (var row in rows)
            {
                var cells = row.QuerySelectorAll("td");
                if (cells.Length >= 2)
                {
                    var key = cells[0].TextContent?.Trim().ToLowerInvariant().Replace(":", string.Empty);
                    var value = cells[1].TextContent?.Trim();
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                    {
                        metadata[key] = value;
                    }
                }
            }

            logger.LogDebug("Parsed {Count} metadata rows from table", metadata.Count);
        }

        // 2. Extract values from metadata dictionary
        var name = metadata.GetValueOrDefault("filename")
            ?? document.QuerySelector("h2 a, h1 a, h2, h1")?.TextContent?.Trim()
            ?? "Unknown";

        string? sizeDisplay = metadata.GetValueOrDefault("size");
        long? sizeBytes = null;
        if (!string.IsNullOrEmpty(sizeDisplay))
        {
            // Parse size like "950mb (996,148,585 bytes)"
            if (sizeDisplay.Contains("bytes", StringComparison.OrdinalIgnoreCase) &&
                sizeDisplay.Contains('(') && sizeDisplay.Contains(')'))
            {
                var bytesPart = sizeDisplay.Split('(').LastOrDefault()?.Replace("bytes)", string.Empty).Replace(",", string.Empty).Trim();
                if (long.TryParse(bytesPart, out var bytesVal))
                {
                    sizeBytes = bytesVal;
                }
            }

            sizeBytes ??= ParseFileSize(sizeDisplay);
        }

        var uploader = metadata.GetValueOrDefault("uploader");
        var category = metadata.GetValueOrDefault("category");
        var md5Hash = metadata.GetValueOrDefault("md5 hash") ?? metadata.GetValueOrDefault("md5hash");

        DateTime? uploadDate = null;
        DateTime? releaseDate = null;
        var addedStr = metadata.GetValueOrDefault("added") ?? metadata.GetValueOrDefault("updated");
        if (!string.IsNullOrEmpty(addedStr))
        {
            if (DateTime.TryParse(addedStr, out var parsedDate))
            {
                uploadDate = parsedDate;
                releaseDate = parsedDate;
            }
            else
            {
                // Try parsing ModDB date format
                var modDBDate = ParseModDBDate(addedStr);
                if (modDBDate.HasValue)
                {
                    uploadDate = modDBDate;
                    releaseDate = modDBDate;
                }
            }
        }

        // 3. Extract Download URL from the main download button
        string? downloadUrl = null;
        var downloadButton = document.QuerySelector(ModDBParserConstants.MainDownloadButtonSelector);
        if (downloadButton != null)
        {
            var href = downloadButton.GetAttribute("href");
            if (!string.IsNullOrEmpty(href))
            {
                downloadUrl = href;
                if (!downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = ModDBConstants.BaseUrl.TrimEnd('/') + downloadUrl;
                }
            }

            // Try to get size from button if not in table
            if (string.IsNullOrEmpty(sizeDisplay))
            {
                sizeDisplay = downloadButton.TextContent?.Trim();
                if (!string.IsNullOrEmpty(sizeDisplay))
                {
                    sizeBytes = ParseFileSize(sizeDisplay);
                }
            }
        }

        logger.LogInformation(
            "Extracted file: Name={Name}, Size={Size}, Uploader={Uploader}, DownloadUrl={Url}",
            name,
            sizeDisplay,
            uploader,
            downloadUrl);

        return new File(
            Name: name,
            SizeBytes: sizeBytes,
            SizeDisplay: sizeDisplay,
            UploadDate: uploadDate,
            Category: category,
            Uploader: uploader,
            DownloadUrl: downloadUrl,
            Md5Hash: md5Hash,
            FileSectionType: FileSectionType.Downloads,
            ReleaseDate: releaseDate);
    }
}