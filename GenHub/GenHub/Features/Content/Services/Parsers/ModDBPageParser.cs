using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
/// Parser for ModDB pages that extracts rich content including files, videos, images, articles, reviews, and comments.
/// </summary>
public partial class ModDBPageParser(IPlaywrightService playwrightService, ILogger<ModDBPageParser> logger) : IWebPageParser
{
    [GeneratedRegex(ModDBParserConstants.ParentModPathRegex, RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex MyRegex();

    [GeneratedRegex(@"/(?:crop|thumb)_[^/]+/", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex ModDBImageCropRegex();

    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.None, "en-US")]
    private static partial Regex CamelCaseSplitRegex();

    [GeneratedRegex(@"(?:\b|\()(\d+(?:[.,]\d+)?\s*(?:GB|MB|KB|B|bytes|byte|gigabytes|megabytes|kilobytes)(?:\s*\([^)]+\))?)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex FileSizeRegex();

    [GeneratedRegex(@"(?:\(|\b)(\d[\d,\s]*)\s*bytes", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex ExactBytesRegex();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*([a-zA-Z]+)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex NumericSizeWithUnitRegex();

    [GeneratedRegex(@"(?:(?:v|embed|shorts|vi)/|(?:\?|&)v=|youtu\.be/)([a-zA-Z0-9_-]{11})", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex YouTubeVideoIdRegex();

    [GeneratedRegex(@"vimeo\.com/(?:video/)?(\d+)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex VimeoVideoIdRegex();

    [GeneratedRegex(@"^(?<title>.+?)\s+(?:file|addon|patch|demo|mod)\s+-", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex ModDBPageTitleRegex();

    /// <summary>
    /// Extracts parent mod URL from a FileDetail page URL.
    /// Example: /mods/mod-name/downloads/file-name -> /mods/mod-name.
    /// </summary>
    private static string? ExtractParentModUrl(string url)
    {
        // Remove query string and fragment
        var cleanUrl = url.Split('?')[0].Split('#')[0];

        // Match pattern: /mods/mod-name/downloads/file-name or /mods/mod-name/addons/file-name
        var match = MyRegex().Match(cleanUrl);

        if (match.Success)
        {
            var parentPath = match.Groups[1].Value;

            // Ensure it's a full URL
            if (!parentPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return ModDBConstants.BaseUrl.TrimEnd('/') + parentPath;
            }

            return parentPath;
        }

        return null;
    }

    /// <summary>
    /// Merges primary and fallback global contexts, taking fallback values when primary is missing.
    /// </summary>
    private static GlobalContext MergeContext(GlobalContext primary, GlobalContext fallback)
    {
        return new GlobalContext(
            Title: string.IsNullOrWhiteSpace(primary.Title) || primary.Title == "Unknown" ? fallback.Title : primary.Title,
            Developer: string.IsNullOrWhiteSpace(primary.Developer) ? fallback.Developer : primary.Developer,
            ReleaseDate: primary.ReleaseDate ?? fallback.ReleaseDate,
            GameName: string.IsNullOrWhiteSpace(primary.GameName) ? fallback.GameName : primary.GameName,
            IconUrl: string.IsNullOrWhiteSpace(primary.IconUrl) ? fallback.IconUrl : primary.IconUrl,
            Description: string.IsNullOrWhiteSpace(primary.Description) ? fallback.Description : primary.Description);
    }

    /// <summary>
    /// Detects the page type based on URL patterns and DOM structure.
    /// </summary>
    private static PageType DetectPageType(string url, IDocument document)
    {
        // Check for file detail page
        if (document.QuerySelector(ModDBParserConstants.DownloadsInfoSelector) != null)
        {
            return PageType.FileDetail;
        }

        // Addons listings are row tables. A mod's /images tab is a gallery (#imagebox), not a list.
        if (url.Contains("/addons", StringComparison.OrdinalIgnoreCase))
        {
            return PageType.List;
        }

        if (url.Contains("/images", StringComparison.OrdinalIgnoreCase) &&
            document.QuerySelector(ModDBParserConstants.ImageGallerySelector) == null)
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
    /// </summary>
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
    /// <param name="sectionType">The type of file section (Release or Addon).</param>
    private static DownloadableFile? ExtractFileFromRow(IElement row, FileSectionType sectionType = FileSectionType.Downloads)
    {
        var nameEl = row.QuerySelector(ModDBParserConstants.FileNameSelector);
        var name = nameEl?.TextContent?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var (sizeBytes, sizeText) = ExtractRowFileSize(row);
        var (uploadDate, releaseDate) = ExtractRowDates(row);
        var category = ExtractRowCategory(row);
        var uploader = ExtractRowUploader(row);
        var (downloadUrl, detailsUrl) = ExtractRowUrls(row, nameEl);
        var thumbnailUrl = ExtractRowThumbnail(row);
        var summary = ExtractRowSummary(row);
        var commentCount = ExtractRowCommentCount(row);

        return new DownloadableFile(
            Name: name,
            SizeBytes: sizeBytes,
            SizeDisplay: sizeText,
            UploadDate: uploadDate,
            Category: category,
            Uploader: uploader,
            DownloadUrl: downloadUrl,
            CommentCount: commentCount,
            ThumbnailUrl: thumbnailUrl,
            FileSectionType: sectionType,
            ReleaseDate: releaseDate,
            DetailsUrl: detailsUrl,
            Description: summary);
    }

    private static (long? SizeBytes, string? SizeText) ExtractRowFileSize(IElement row)
    {
        var sizeEl = row.QuerySelector(ModDBParserConstants.FileSizeSelector);
        var sizeText = sizeEl?.TextContent?.Trim();
        long? sizeBytes = null;
        if (!string.IsNullOrEmpty(sizeText))
        {
            sizeBytes = ParseFileSize(sizeText);
        }

        if (sizeBytes == null || string.IsNullOrEmpty(sizeText))
        {
            var subHeadingEl = row.QuerySelector(ModDBParserConstants.FileSubheadingSelector);
            var textToSearch = subHeadingEl?.TextContent ?? row.TextContent;
            if (!string.IsNullOrWhiteSpace(textToSearch))
            {
                var sizeMatch = FileSizeRegex().Match(textToSearch);
                if (sizeMatch.Success)
                {
                    sizeText = sizeMatch.Groups[1].Value.Trim();
                    sizeBytes = ParseFileSize(sizeText);
                }
            }
        }

        return (sizeBytes, sizeText);
    }

    private static (DateTime? UploadDate, DateTime? ReleaseDate) ExtractRowDates(IElement row)
    {
        var dateEl = row.QuerySelector(ModDBParserConstants.FileDateSelector);
        DateTime? uploadDate = null;
        DateTime? releaseDate = null;
        if (dateEl != null)
        {
            var dateStr = dateEl.GetAttribute("datetime") ?? dateEl.GetAttribute("title") ?? dateEl.TextContent?.Trim();
            if (!string.IsNullOrEmpty(dateStr))
            {
                if (DateTime.TryParse(dateStr, out var standardDate))
                {
                    uploadDate = standardDate;
                    releaseDate = standardDate;
                }
                else
                {
                    var modDBDate = ParseModDBDate(dateStr);
                    if (modDBDate.HasValue)
                    {
                        uploadDate = modDBDate;
                        releaseDate = modDBDate;
                    }
                }
            }
        }

        return (uploadDate, releaseDate);
    }

    private static string? ExtractRowCategory(IElement row)
    {
        var category = row.QuerySelector(ModDBParserConstants.FileCategorySelector)?.TextContent?.Trim();
        if (string.IsNullOrEmpty(category))
        {
            var subHeadingEl = row.QuerySelector(ModDBParserConstants.FileSubheadingSelector);
            var subHeadingText = subHeadingEl?.TextContent?.Trim();
            if (!string.IsNullOrEmpty(subHeadingText))
            {
                var categoryKeywords = new[] { "Full Version", "Patch", "Demo", "Tool", "Addon", "Map", "Skin" };
                foreach (var keyword in categoryKeywords)
                {
                    if (subHeadingText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return keyword;
                    }
                }
            }
        }

        return category;
    }

    private static string? ExtractRowUploader(IElement row)
    {
        var uploaderEl = row.QuerySelector(ModDBParserConstants.FileUploaderSelector);
        var uploader = uploaderEl?.TextContent?.Trim();
        return IsJunkDeveloperName(uploader) ? null : uploader;
    }

    private static (string? DownloadUrl, string? DetailsUrl) ExtractRowUrls(IElement row, IElement? nameEl)
    {
        string? downloadUrl = null;
        string? detailsUrl = null;

        var titleLinkEl = row.QuerySelector("h4 a, h5 a, h3 a, .heading a, .title a, a.title, .name a, a[href*='/downloads/'], a[href*='/addons/']")
            ?? (string.Equals(nameEl?.TagName, "A", StringComparison.OrdinalIgnoreCase) ? nameEl : nameEl?.QuerySelector("a"));
        var titleHref = titleLinkEl?.GetAttribute("href");
        if (!string.IsNullOrEmpty(titleHref))
        {
            titleHref = ToAbsoluteUrl(titleHref);
            if (!IsDirectDownloadUrl(titleHref))
            {
                detailsUrl = titleHref;
            }
            else
            {
                downloadUrl = titleHref;
            }
        }

        var linkEl = row.QuerySelector(ModDBParserConstants.FileDownloadSelector);
        var buttonHref = linkEl?.GetAttribute("href");
        if (!string.IsNullOrEmpty(buttonHref))
        {
            if (!buttonHref.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                buttonHref = ModDBConstants.BaseUrl.TrimEnd('/') + "/" + buttonHref.TrimStart('/');
            }

            if (IsDirectDownloadUrl(buttonHref))
            {
                downloadUrl = buttonHref;
            }
            else if (string.IsNullOrEmpty(detailsUrl))
            {
                detailsUrl = buttonHref;
            }
        }

        downloadUrl ??= detailsUrl;
        return (downloadUrl, detailsUrl);
    }

    private static string? ExtractRowThumbnail(IElement row)
    {
        var imgEl = row.QuerySelector("img");
        var thumbnailUrl = imgEl?.GetAttribute("src") ?? imgEl?.GetAttribute("data-src");
        if (!string.IsNullOrEmpty(thumbnailUrl))
        {
            return (thumbnailUrl.Contains("blank.gif", StringComparison.OrdinalIgnoreCase) ||
                    thumbnailUrl.Contains("clear.gif", StringComparison.OrdinalIgnoreCase))
                ? null
                : ToAbsoluteUrl(thumbnailUrl);
        }

        return null;
    }

    private static string? ExtractRowSummary(IElement row)
    {
        var summaryEl = row.QuerySelector("p.summary, .summary, p, div.summary");
        var summary = summaryEl?.TextContent?.Trim();
        return IsBreadcrumbOrLocationText(summary) ? null : summary;
    }

    private static int? ExtractRowCommentCount(IElement row)
    {
        var commentCountEl = row.QuerySelector(ModDBParserConstants.FileCommentCountSelector);
        if (commentCountEl != null)
        {
            var countText = commentCountEl.TextContent?.Trim();
            if (!string.IsNullOrEmpty(countText) && int.TryParse(countText, out var count))
            {
                return count;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts metadata from the sidebar profile section.
    /// </summary>
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
                default:
                    // Ignore other metadata labels
                    break;
            }
        }

        return new ProfileMeta(name, sizeBytes, sizeDisplay, releaseDate, developer, md5Hash);
    }

    /// <summary>
    /// Extracts videos from the document, including embedded iframes, HTML5 video elements,
    /// and video gallery cards (e.g., from the /videos tab or media widgets).
    /// </summary>
    private static List<Video> ExtractVideos(IDocument document)
    {
        var videos = new List<Video>();
        videos.AddRange(ExtractIFrameVideos(document));
        videos.AddRange(ExtractHtml5Videos(document));
        videos.AddRange(ExtractGalleryCardVideos(document));
        return DeduplicateVideoList(videos);
    }

    private static List<Video> ExtractIFrameVideos(IDocument document)
    {
        var videos = new List<Video>();
        var videoElements = document.QuerySelectorAll(ModDBParserConstants.VideoSelector);
        foreach (var videoEl in videoElements)
        {
            var src = videoEl.GetAttribute("src")
                ?? videoEl.GetAttribute("data-src")
                ?? videoEl.GetAttribute("data-video");

            if (string.IsNullOrWhiteSpace(src))
            {
                continue;
            }

            if (src.StartsWith("//", StringComparison.Ordinal))
            {
                src = "https:" + src;
            }

            if (IsNonVideoIframe(src))
            {
                continue;
            }

            var title = ResolveIFrameVideoTitle(videoEl);
            if (!IsUsableVideoTitle(title))
            {
                continue;
            }

            var (platform, thumbnailUrl, embedUrl) = ResolveVideoPlatformDetails(src, videoEl);

            if (!embedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                embedUrl = ToAbsoluteUrl(embedUrl);
            }

            videos.Add(new Video(
                Title: title,
                ThumbnailUrl: thumbnailUrl,
                EmbedUrl: embedUrl,
                Platform: platform));
        }

        return videos;
    }

    private static (string Platform, string? ThumbnailUrl, string EmbedUrl) ResolveVideoPlatformDetails(string src, IElement videoEl)
    {
        string? thumbnailUrl = null;
        var platform = "Unknown";
        var embedUrl = src;

        var ytMatch = YouTubeVideoIdRegex().Match(src);
        if (ytMatch.Success)
        {
            var videoId = ytMatch.Groups[1].Value;
            platform = "YouTube";
            thumbnailUrl = $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg";
            embedUrl = $"https://www.youtube.com/embed/{videoId}";
        }
        else
        {
            var vimeoMatch = VimeoVideoIdRegex().Match(src);
            if (vimeoMatch.Success)
            {
                var videoId = vimeoMatch.Groups[1].Value;
                platform = "Vimeo";
                embedUrl = $"https://player.vimeo.com/video/{videoId}";
            }
            else if (src.Contains("dailymotion", StringComparison.OrdinalIgnoreCase))
            {
                platform = "Dailymotion";
            }
            else if (src.Contains("moddb.com", StringComparison.OrdinalIgnoreCase))
            {
                platform = "ModDB";
            }
        }

        if (string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            var container = videoEl.Closest(".video, .media, .mediabox, .holder, .row, .embed, figure, .video-container");
            var thumbEl = container?.QuerySelector(ModDBParserConstants.VideoThumbnailSelector);
            var rawThumb = thumbEl?.GetAttribute("src") ?? thumbEl?.GetAttribute("data-src");
            if (!string.IsNullOrWhiteSpace(rawThumb))
            {
                thumbnailUrl = ToAbsoluteUrl(rawThumb);
            }
        }

        return (platform, thumbnailUrl, embedUrl);
    }

    private static List<Video> ExtractHtml5Videos(IDocument document)
    {
        var videos = new List<Video>();
        var html5VideoElements = document.QuerySelectorAll("video");
        foreach (var videoEl in html5VideoElements)
        {
            var src = videoEl.GetAttribute("src")
                ?? videoEl.QuerySelector("source")?.GetAttribute("src");

            if (string.IsNullOrWhiteSpace(src))
            {
                continue;
            }

            var poster = videoEl.GetAttribute("poster");
            var thumbnailUrl = !string.IsNullOrWhiteSpace(poster) ? ToAbsoluteUrl(poster) : null;
            var title = ResolveIFrameVideoTitle(videoEl);
            if (!IsUsableVideoTitle(title))
            {
                continue;
            }

            videos.Add(new Video(
                Title: title,
                ThumbnailUrl: thumbnailUrl,
                EmbedUrl: ToAbsoluteUrl(src),
                Platform: "ModDB"));
        }

        return videos;
    }

    private static List<Video> ExtractGalleryCardVideos(IDocument document)
    {
        var videos = new List<Video>();
        var videoLinks = document.QuerySelectorAll(ModDBParserConstants.VideoLinkSelector);
        foreach (var linkEl in videoLinks)
        {
            var href = linkEl.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            if (IsNavigationOrPaginationLink(href, linkEl))
            {
                continue;
            }

            if (IsInRecommendationsOrAds(linkEl))
            {
                continue;
            }

            var absoluteHref = ToAbsoluteUrl(href);
            var (video, isVideo) = ExtractVideoFromGalleryLink(linkEl, absoluteHref);
            if (isVideo && video != null)
            {
                videos.Add(video);
            }
        }

        return videos;
    }

    private static bool IsNonVideoIframe(string src)
    {
        if (string.IsNullOrWhiteSpace(src))
        {
            return true;
        }

        if (src.Contains("/widget", StringComparison.OrdinalIgnoreCase) ||
            src.Contains("/widgets/", StringComparison.OrdinalIgnoreCase) ||
            src.Contains("doubleclick", StringComparison.OrdinalIgnoreCase) ||
            src.Contains("googlesyndication", StringComparison.OrdinalIgnoreCase) ||
            src.Contains("adnxs", StringComparison.OrdinalIgnoreCase) ||
            src.Contains("amazon-adsystem", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (src.Contains("moddb.com", StringComparison.OrdinalIgnoreCase) &&
            !src.Contains("/media/iframe/", StringComparison.OrdinalIgnoreCase) &&
            !src.Contains("/media/embed/", StringComparison.OrdinalIgnoreCase) &&
            !src.Contains("/videos/iframe/", StringComparison.OrdinalIgnoreCase) &&
            !src.Contains("/videos/embed/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsInRecommendationsOrAds(IElement el)
    {
        return el.Closest(ModDBParserConstants.RecommendationsSelector) != null ||
               el.Closest("aside, #sidecolumn, .sidecolumn") != null;
    }

    private static string ResolveIFrameVideoTitle(IElement element)
    {
        var title = element.GetAttribute("title");
        if (IsUsableVideoTitle(title))
        {
            return FormatVideoTitle(title!);
        }

        var ariaLabel = element.GetAttribute("aria-label");
        if (IsUsableVideoTitle(ariaLabel))
        {
            return FormatVideoTitle(ariaLabel!);
        }

        var container = element.Closest(".video, .media, .mediabox, .holder, .row, .embed, figure, .video-container, [class*='video'], [class*='media']");
        var containerTitleEl = container?.QuerySelector(".title, .caption, figcaption, h1, h2, h3, h4, h5, strong");
        var containerTitle = containerTitleEl?.TextContent?.Trim();
        if (IsUsableVideoTitle(containerTitle))
        {
            return FormatVideoTitle(containerTitle!);
        }

        var prev = element.PreviousElementSibling;
        if (prev != null && (prev.TagName.StartsWith('H') || prev.ClassList.Contains("title") || prev.ClassList.Contains("caption")))
        {
            var prevTitle = prev.TextContent?.Trim();
            if (IsUsableVideoTitle(prevTitle))
            {
                return FormatVideoTitle(prevTitle!);
            }
        }

        var next = element.NextElementSibling;
        if (next != null && (next.ClassList.Contains("title") || next.ClassList.Contains("caption") || next.TagName.Equals("FIGCAPTION", StringComparison.OrdinalIgnoreCase)))
        {
            var nextTitle = next.TextContent?.Trim();
            if (IsUsableVideoTitle(nextTitle))
            {
                return FormatVideoTitle(nextTitle!);
            }
        }

        return "Video";
    }

    private static bool IsUsableVideoTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var clean = title.Trim();
        return !clean.Equals("Video", StringComparison.OrdinalIgnoreCase) &&
               !clean.Equals("YouTube video player", StringComparison.OrdinalIgnoreCase) &&
               !clean.Equals("YouTube player", StringComparison.OrdinalIgnoreCase) &&
               !clean.Equals("Play", StringComparison.OrdinalIgnoreCase) &&
               !clean.Equals("View media", StringComparison.OrdinalIgnoreCase) &&
               !clean.Equals("Image", StringComparison.OrdinalIgnoreCase) &&
               !clean.Equals("Next Media", StringComparison.OrdinalIgnoreCase) &&
               !clean.Equals("Previous Media", StringComparison.OrdinalIgnoreCase) &&
               !clean.Equals("Next", StringComparison.OrdinalIgnoreCase) &&
               !clean.Equals("Previous", StringComparison.OrdinalIgnoreCase) &&
               !clean.Equals("Prev", StringComparison.OrdinalIgnoreCase) &&
               !clean.Equals("RSS", StringComparison.OrdinalIgnoreCase) &&
               !clean.Equals("RSS Feed", StringComparison.OrdinalIgnoreCase) &&
               !clean.Equals("Feed", StringComparison.OrdinalIgnoreCase) &&
               !clean.Equals("Subscribe", StringComparison.OrdinalIgnoreCase) &&
               !clean.StartsWith("You may also like", StringComparison.OrdinalIgnoreCase) &&
               !clean.StartsWith("Recommended", StringComparison.OrdinalIgnoreCase) &&
               !clean.StartsWith("Related", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatVideoTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Video";
        }

        var formatted = title.Trim('*', ' ', '\t', '\r', '\n').Replace('_', ' ').Replace('-', ' ');

        if (!formatted.Contains(' '))
        {
            formatted = CamelCaseSplitRegex().Replace(formatted, " ");
        }

        formatted = Regex.Replace(formatted, @"\s+", " ").Trim();

        return string.IsNullOrWhiteSpace(formatted) ? "Video" : formatted;
    }

    private static (Video? Video, bool IsVideo) ExtractVideoFromGalleryLink(IElement linkEl, string absoluteHref)
    {
        if (absoluteHref.Contains("/widget", StringComparison.OrdinalIgnoreCase) ||
            absoluteHref.Contains("/downloads/", StringComparison.OrdinalIgnoreCase) ||
            absoluteHref.Contains("/addons/", StringComparison.OrdinalIgnoreCase))
        {
            return (null, false);
        }

        if (IsNavigationOrPaginationLink(absoluteHref, linkEl))
        {
            return (null, false);
        }

        var container = linkEl.Closest(".holder, .mediabox, .mediarow, .row, .rowcontent, .media, .videobox") ?? linkEl;

        var img = linkEl.QuerySelector("img")
            ?? container.QuerySelector("img");

        var rawThumb = img?.GetAttribute("src") ?? img?.GetAttribute("data-src");
        string? thumbnailUrl = null;
        if (!string.IsNullOrWhiteSpace(rawThumb))
        {
            thumbnailUrl = ToAbsoluteUrl(rawThumb);
        }

        var platform = "ModDB";
        var embedUrl = absoluteHref;

        var ytMatch = YouTubeVideoIdRegex().Match(absoluteHref);
        if (!ytMatch.Success && thumbnailUrl != null)
        {
            ytMatch = YouTubeVideoIdRegex().Match(thumbnailUrl);
        }

        if (ytMatch.Success)
        {
            var videoId = ytMatch.Groups[1].Value;
            platform = "YouTube";
            thumbnailUrl = $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg";
            embedUrl = $"https://www.youtube.com/embed/{videoId}";
        }
        else
        {
            var vimeoMatch = VimeoVideoIdRegex().Match(absoluteHref);
            if (vimeoMatch.Success)
            {
                var videoId = vimeoMatch.Groups[1].Value;
                platform = "Vimeo";
                embedUrl = $"https://player.vimeo.com/video/{videoId}";
            }
        }

        var title = ResolveGalleryVideoTitle(linkEl, container, img, absoluteHref);
        if (!IsUsableVideoTitle(title))
        {
            return (null, false);
        }

        if (string.IsNullOrWhiteSpace(thumbnailUrl) && platform == "ModDB")
        {
            return (null, false);
        }

        return (new Video(
            Title: title,
            ThumbnailUrl: thumbnailUrl,
            EmbedUrl: embedUrl,
            Platform: platform), true);
    }

    private static string ResolveGalleryVideoTitle(IElement linkEl, IElement container, IElement? img, string absoluteHref)
    {
        var linkTitle = linkEl.GetAttribute("title");
        if (IsUsableVideoTitle(linkTitle))
        {
            return FormatVideoTitle(linkTitle!);
        }

        var imgAlt = img?.GetAttribute("alt") ?? img?.GetAttribute("title");
        if (IsUsableVideoTitle(imgAlt))
        {
            return FormatVideoTitle(imgAlt!);
        }

        var containerTitleEl = container.QuerySelector(".title a, .title, h4 a, h4, h3 a, h3, h5, .caption");
        var containerTitle = containerTitleEl?.TextContent?.Trim();
        if (IsUsableVideoTitle(containerTitle))
        {
            return FormatVideoTitle(containerTitle!);
        }

        var slug = GalleryImageKey(absoluteHref);
        var dot = slug.LastIndexOf('.');
        if (dot > 0)
        {
            slug = slug[..dot];
        }

        if (IsUsableVideoTitle(slug))
        {
            return FormatVideoTitle(slug);
        }

        return "Video";
    }

    private static bool IsNavigationOrPaginationLink(string href, IElement linkEl)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return true;
        }

        var cleanHref = href.Split('?')[0].Split('#')[0].TrimEnd('/');
        if (cleanHref.EndsWith("/videos", StringComparison.OrdinalIgnoreCase) ||
            cleanHref.EndsWith("/media", StringComparison.OrdinalIgnoreCase) ||
            cleanHref.EndsWith("/images", StringComparison.OrdinalIgnoreCase) ||
            cleanHref.EndsWith("/downloads", StringComparison.OrdinalIgnoreCase) ||
            cleanHref.EndsWith("/addons", StringComparison.OrdinalIgnoreCase) ||
            cleanHref.EndsWith("/reviews", StringComparison.OrdinalIgnoreCase) ||
            cleanHref.EndsWith("/articles", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (cleanHref.Contains("/page/", StringComparison.OrdinalIgnoreCase) ||
            cleanHref.Contains("/rss", StringComparison.OrdinalIgnoreCase) ||
            cleanHref.Contains("/feed", StringComparison.OrdinalIgnoreCase) ||
            cleanHref.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
            href.Contains("?page=", StringComparison.OrdinalIgnoreCase) ||
            href.Contains("&page=", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var text = linkEl.TextContent?.Trim();
        if (!string.IsNullOrEmpty(text) &&
            (text.Equals("Next Media", StringComparison.OrdinalIgnoreCase) ||
             text.Equals("Previous Media", StringComparison.OrdinalIgnoreCase) ||
             text.Equals("Next", StringComparison.OrdinalIgnoreCase) ||
             text.Equals("Previous", StringComparison.OrdinalIgnoreCase) ||
             text.Equals("Prev", StringComparison.OrdinalIgnoreCase) ||
             text.Equals("RSS", StringComparison.OrdinalIgnoreCase) ||
             text.Equals("RSS Feed", StringComparison.OrdinalIgnoreCase) ||
             text.Equals("Feed", StringComparison.OrdinalIgnoreCase) ||
             text.Equals("Subscribe", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var inNav = linkEl.Closest("nav, .tabs, .navigation, .pagination, .pages, .heading, #nav, .feed, .rss, .subheading, .actions") != null;
        return inNav;
    }

    /// <summary>
    /// Extracts gallery images from the document. Game icons, member avatars, file-page chrome,
    /// and social-share glyphs are excluded — those previously flooded the Media tab.
    /// </summary>
    private static List<Image> ExtractImages(IDocument document)
    {
        var images = new List<Image>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var elements = document.QuerySelectorAll(ModDBParserConstants.GalleryImageSelector);
        foreach (var img in elements)
        {
            var src = img.GetAttribute("src") ?? img.GetAttribute("data-src");
            if (!IsUsableModGalleryImage(src, img))
            {
                continue;
            }

            var thumbnailUrl = ToAbsoluteUrl(src!);
            var key = GalleryImageKey(thumbnailUrl);
            if (!seenKeys.Add(key))
            {
                continue;
            }

            var parentAnchor = img.Closest("a");
            var anchorHref = parentAnchor?.GetAttribute("href");
            var fullSizeUrl = GetFullSizeModDBImageSource(thumbnailUrl);

            if (!string.IsNullOrWhiteSpace(anchorHref))
            {
                var absoluteHref = ToAbsoluteUrl(anchorHref);
                if (absoluteHref.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    absoluteHref.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    absoluteHref.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                    absoluteHref.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                {
                    fullSizeUrl = absoluteHref;
                }
            }

            var title = ResolveGalleryImageTitle(img, thumbnailUrl, anchorHref);

            images.Add(new Image(
                Title: title,
                ThumbnailUrl: fullSizeUrl,
                FullSizeUrl: fullSizeUrl,
                Description: title));
        }

        return images;
    }

    private static bool IsUsableModGalleryImage(string? src, IElement img)
    {
        if (IsDisallowedImageSrc(src))
        {
            return false;
        }

        var alt = img.GetAttribute("alt") ?? string.Empty;
        if (IsDisallowedAltText(alt))
        {
            return false;
        }

        var inSidebar = img.Closest(ModDBParserConstants.ImageSidebarSelector) != null;
        var inGallery = img.Closest(ModDBParserConstants.ImageGallerySelector) != null;
        if (inSidebar && !inGallery)
        {
            return false;
        }

        var href = img.Closest("a")?.GetAttribute("href") ?? string.Empty;
        if (IsDisallowedVideoLink(href))
        {
            return false;
        }

        var isModImagePage = href.Contains("/mods/", StringComparison.OrdinalIgnoreCase) &&
                             (href.Contains("/images/", StringComparison.OrdinalIgnoreCase) ||
                              href.Contains("/downloads/", StringComparison.OrdinalIgnoreCase) ||
                              href.Contains("/addons/", StringComparison.OrdinalIgnoreCase));
        var isModMediaFile = src != null && (src.Contains("/images/mods/", StringComparison.OrdinalIgnoreCase) ||
                             src.Contains("/cache/images/mods/", StringComparison.OrdinalIgnoreCase) ||
                             src.Contains("/images/downloads/", StringComparison.OrdinalIgnoreCase) ||
                             src.Contains("/cache/images/downloads/", StringComparison.OrdinalIgnoreCase));

        return inGallery || isModImagePage || isModMediaFile;
    }

    private static bool IsDisallowedImageSrc(string? src)
    {
        if (string.IsNullOrWhiteSpace(src))
        {
            return true;
        }

        var disallowedTokens = new[]
        {
            "data:", "clear.gif", "blank.gif", "/avatar/", "/button",
            "/guest/", "/default/error", "error_50x50", "/images/games/",
            "/images/groups/", "/images/members/", "/cache/images/games/",
            "/cache/images/groups/", "/cache/images/members/", "icon.gif",
        };

        foreach (var token in disallowedTokens)
        {
            if (src.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDisallowedAltText(string alt)
    {
        return alt.Contains("Share on", StringComparison.OrdinalIgnoreCase) ||
               alt.Equals("Post", StringComparison.OrdinalIgnoreCase) ||
               alt.Contains("Email a friend", StringComparison.OrdinalIgnoreCase) ||
               alt.Contains("Tweeeeeet", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDisallowedVideoLink(string href)
    {
        return href.Contains("/videos/", StringComparison.OrdinalIgnoreCase) ||
               href.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
               href.Contains("youtu.be", StringComparison.OrdinalIgnoreCase) ||
               href.Contains("vimeo.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string GalleryImageKey(string url)
    {
        var path = url.Split('?')[0].Split('#')[0];
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    /// <summary>
    /// Converts a ModDB cached/cropped thumbnail image source to a high-resolution full-size image source.
    /// </summary>
    private static string GetFullSizeModDBImageSource(string imageSource)
    {
        if (string.IsNullOrWhiteSpace(imageSource))
        {
            return imageSource;
        }

        var result = imageSource;
        if (result.Contains("/cache/images/", StringComparison.OrdinalIgnoreCase))
        {
            result = result.Replace("/cache/images/", "/images/", StringComparison.OrdinalIgnoreCase);
        }

        result = ModDBImageCropRegex().Replace(result, "/");
        return result;
    }

    private static string ResolveGalleryImageTitle(IElement img, string thumbnailUrl, string? anchorHref)
    {
        string? rawTitle = null;

        var imgAlt = img.GetAttribute("alt") ?? img.GetAttribute("title");
        if (!string.IsNullOrWhiteSpace(imgAlt) &&
            !imgAlt.Equals("View media", StringComparison.OrdinalIgnoreCase) &&
            !imgAlt.Equals("Image", StringComparison.OrdinalIgnoreCase) &&
            !imgAlt.StartsWith("Share on", StringComparison.OrdinalIgnoreCase))
        {
            rawTitle = imgAlt.Trim();
        }

        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            var parentAnchor = img.Closest("a");
            if (parentAnchor != null)
            {
                var anchorTitle = parentAnchor.GetAttribute("title");
                if (!string.IsNullOrWhiteSpace(anchorTitle) &&
                    !anchorTitle.Equals("View media", StringComparison.OrdinalIgnoreCase) &&
                    !anchorTitle.Equals("Image", StringComparison.OrdinalIgnoreCase))
                {
                    rawTitle = anchorTitle.Trim();
                }
            }
        }

        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            var container = img.Closest(".imagebox, .mediabox, .mediarow, .holder");
            var titleEl = container?.QuerySelector(".title, .caption");
            var containerTitle = titleEl?.TextContent?.Trim();
            if (!string.IsNullOrWhiteSpace(containerTitle) &&
                !containerTitle.Equals("View media", StringComparison.OrdinalIgnoreCase) &&
                !containerTitle.Equals("Image", StringComparison.OrdinalIgnoreCase))
            {
                rawTitle = containerTitle;
            }
        }

        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            var fileName = GalleryImageKey(thumbnailUrl);
            var dot = fileName.LastIndexOf('.');
            if (dot > 0)
            {
                fileName = fileName[..dot];
            }

            if (!string.IsNullOrWhiteSpace(fileName) &&
                !fileName.Equals("image", StringComparison.OrdinalIgnoreCase))
            {
                rawTitle = fileName;
            }
        }

        if (string.IsNullOrWhiteSpace(rawTitle) && !string.IsNullOrWhiteSpace(anchorHref))
        {
            var slug = GalleryImageKey(anchorHref.Split('#')[0]);
            if (!string.IsNullOrWhiteSpace(slug))
            {
                rawTitle = slug;
            }
        }

        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return "Image";
        }

        return FormatImageTitle(rawTitle);
    }

    private static string FormatImageTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Image";
        }

        var formatted = title.Trim('*', ' ', '\t', '\r', '\n').Replace('_', ' ').Replace('-', ' ');

        if (!formatted.Contains(' '))
        {
            formatted = CamelCaseSplitRegex().Replace(formatted, " ");
        }

        formatted = Regex.Replace(formatted, @"\s+", " ").Trim();

        return string.IsNullOrWhiteSpace(formatted) ? "Image" : formatted;
    }

    private static string ToAbsoluteUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + url;
        }

        if (url.StartsWith('/'))
        {
            return ModDBConstants.BaseUrl.TrimEnd('/') + url;
        }

        return ModDBConstants.BaseUrl.TrimEnd('/') + "/" + url;
    }

    /// <summary>
    /// Extracts an image from a row element.
    /// </summary>
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

        var absUrl = ToAbsoluteUrl(src);
        var fullSizeUrl = GetFullSizeModDBImageSource(absUrl);
        var alt = img.GetAttribute("alt");
        var title = !string.IsNullOrWhiteSpace(alt) ? FormatImageTitle(alt) : "Image";

        return new Image(
            Title: title,
            ThumbnailUrl: fullSizeUrl,
            FullSizeUrl: fullSizeUrl,
            Description: alt);
    }

    /// <summary>
    /// Extracts articles from the document.
    /// </summary>
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
    /// </summary>
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
                if (!string.IsNullOrEmpty(votesText) && int.TryParse(votesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var votes))
                {
                    helpfulVotes = votes;
                }
                else if (!string.IsNullOrEmpty(votesText))
                {
                    // "12 people found this helpful" — take the leading integer when present.
                    var digits = new string(votesText.TakeWhile(char.IsDigit).ToArray());
                    if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out votes))
                    {
                        helpfulVotes = votes;
                    }
                }
            }

            // Broad selectors also match bare rating widgets; skip shells with no review body.
            if (string.IsNullOrWhiteSpace(author) && string.IsNullOrWhiteSpace(content))
            {
                continue;
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
    /// Extracts comments from the document with hierarchical thread structure.
    /// Only posted threads inside <c>#commentsbrowse</c> are considered — the composer
    /// (<c>#commentform</c>, guest/email rows, injected CSS) is not a comment.
    /// </summary>
    private static List<Comment> ExtractComments(IDocument document)
    {
        var comments = new List<Comment>();

        var browse = document.QuerySelector(ModDBParserConstants.CommentsSelector);
        if (browse == null)
        {
            return comments;
        }

        var allCommentRows = browse.QuerySelectorAll(ModDBParserConstants.CommentRowSelector);
        var seenRows = new HashSet<IElement>();
        var rootRows = new List<IElement>();

        foreach (var row in allCommentRows)
        {
            if (!seenRows.Add(row))
            {
                continue;
            }

            // Nested replies live under .children / another .rowcomment. Do not treat
            // #commentsbrowse (id starts with "comment") as a nesting ancestor.
            var isNested = row.Ancestors<IElement>().Any(a =>
                a.ClassList.Contains("children") ||
                a.ClassList.Contains("rowcomment") ||
                (a.Id?.StartsWith("comment", StringComparison.OrdinalIgnoreCase) == true &&
                 !a.Id.StartsWith("comments", StringComparison.OrdinalIgnoreCase)));

            if (!isNested)
            {
                rootRows.Add(row);
            }
        }

        foreach (var rootRow in rootRows)
        {
            var comment = ParseCommentElement(rootRow, indentLevel: 0);
            if (comment != null)
            {
                comments.Add(comment);
            }
        }

        return comments;
    }

    private static Comment? ParseCommentElement(IElement row, int indentLevel)
    {
        if (row.ClassList.Contains("rowcommentguest") ||
            row.ClassList.Contains("rowcommentsummary") ||
            row.ClassList.Contains("rowcommentemail"))
        {
            return null;
        }

        var author = ExtractCommentAuthor(row);
        var content = ExtractCommentContent(row);

        if (string.IsNullOrWhiteSpace(author) && string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var date = ExtractCommentDate(row);
        var karma = ExtractCommentKarma(row);
        var isCreator = row.QuerySelector(ModDBParserConstants.CommentCreatorSelector) != null;
        var childReplies = ExtractChildReplies(row, indentLevel);

        return new Comment(
            Author: author ?? "Anonymous",
            Content: content,
            Date: date,
            Karma: karma,
            IsCreator: isCreator,
            IndentLevel: indentLevel,
            Replies: childReplies.Count > 0 ? childReplies : null);
    }

    private static string? ExtractCommentAuthor(IElement row)
    {
        var authorEl = row.QuerySelector(ModDBParserConstants.CommentAuthorSelector)
            ?? row.QuerySelector(".username")
            ?? row.QuerySelector("a[href*='/members/']:not([href*='/register']):not([href*='/login'])");
        var author = authorEl?.TextContent?.Trim();
        return IsJunkDeveloperName(author) ? null : author;
    }

    private static string? ExtractCommentContent(IElement row)
    {
        var contentEl = row.QuerySelector(":scope > .commentbody")
            ?? row.QuerySelector(".commentbody")
            ?? row.QuerySelector(ModDBParserConstants.CommentContentSelector)
            ?? row.QuerySelector("p.comment");

        string? content = null;
        if (contentEl != null)
        {
            content = ExtractCleanCommentBody(contentEl);
        }
        else
        {
            var pEl = row.QuerySelector(":scope > p") ?? row.QuerySelector("p");
            if (pEl != null)
            {
                content = ExtractCleanCommentBody(pEl);
            }
        }

        return IsJunkCommentContent(content) ? null : content;
    }

    private static DateTime? ExtractCommentDate(IElement row)
    {
        var dateEl = row.QuerySelector(ModDBParserConstants.CommentDateSelector);
        if (dateEl != null)
        {
            var dateStr = dateEl.GetAttribute("datetime") ?? dateEl.TextContent?.Trim();
            if (!string.IsNullOrEmpty(dateStr))
            {
                return DateTime.TryParse(dateStr, out var parsedDate)
                    ? parsedDate
                    : ParseModDBDate(dateStr);
            }
        }

        return null;
    }

    private static int? ExtractCommentKarma(IElement row)
    {
        var karmaEl = row.QuerySelector(ModDBParserConstants.CommentKarmaSelector);
        if (karmaEl != null)
        {
            var karmaText = karmaEl.TextContent?.Trim();
            if (!string.IsNullOrEmpty(karmaText) && int.TryParse(karmaText, out var karmaValue))
            {
                return karmaValue;
            }
        }

        return null;
    }

    private static List<Comment> ExtractChildReplies(IElement row, int indentLevel)
    {
        var childReplies = new List<Comment>();
        var childrenContainer = row.QuerySelector(".children");
        if (childrenContainer == null)
        {
            return childReplies;
        }

        var directChildRows = childrenContainer.QuerySelectorAll(".rowcomment, div[id^='comment']:not([id^='comments'])");
        var seenChildren = new HashSet<IElement>();
        foreach (var childRow in directChildRows)
        {
            if (!seenChildren.Add(childRow))
            {
                continue;
            }

            var intermediateParent = childRow.Ancestors<IElement>()
                .TakeWhile(a => a != childrenContainer)
                .FirstOrDefault(a =>
                    a.ClassList.Contains("rowcomment") ||
                    (a.Id?.StartsWith("comment", StringComparison.OrdinalIgnoreCase) == true &&
                     !a.Id.StartsWith("comments", StringComparison.OrdinalIgnoreCase)));

            if (intermediateParent == null)
            {
                var childComment = ParseCommentElement(childRow, indentLevel + 1);
                if (childComment != null)
                {
                    childReplies.Add(childComment);
                }
            }
        }

        return childReplies;
    }

    /// <summary>
    /// Reads comment body text without nested reply threads or ModDB action chrome, which would
    /// otherwise inflate the comment content into a huge empty-looking block in the UI.
    /// </summary>
    private static string? ExtractCleanCommentBody(IElement contentEl)
    {
        var clone = contentEl.Clone(deep: true) as IElement;
        if (clone == null)
        {
            return CleanCommentContent(contentEl.TextContent);
        }

        foreach (var junk in clone.QuerySelectorAll(".children, .actions, .reply, .commentoptions, .toolbar"))
        {
            junk.Remove();
        }

        // Nested .rowcomment nodes sometimes sit inside .commentbody on live ModDB markup.
        foreach (var nested in clone.QuerySelectorAll(".rowcomment, div[id^='comment']"))
        {
            nested.Remove();
        }

        return CleanCommentContent(clone.TextContent);
    }

    private static string? CleanCommentContent(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        var lines = rawText.Split(['\r', '\n']);
        var cleanLines = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) ||
                trimmed.Equals("Reply", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Good karma", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Bad karma", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("vote", StringComparison.OrdinalIgnoreCase) ||
                IsJunkCommentContent(trimmed))
            {
                continue;
            }

            cleanLines.Add(trimmed);
        }

        return cleanLines.Count > 0 ? string.Join("\n", cleanLines) : null;
    }

    private static bool IsJunkCommentContent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (text.Contains("Your comment will be anonymous", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("join the community", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Post a comment", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Save comment", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("sign in with your social", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Cloudflare/ModDB inject obfuscated CSS into the composer (span.abc { display: none; }).
        return text.Contains('{') &&
               (text.Contains("display:", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("formouter", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsJunkDeveloperName(string? name) =>
        string.IsNullOrWhiteSpace(name) ||
        name.Equals("register", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("sign in", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("login", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("guest", StringComparison.OrdinalIgnoreCase);

    private static bool IsBreadcrumbOrLocationText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var trimmed = text.Trim();
        return trimmed.StartsWith("Games :", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("Mods :", StringComparison.OrdinalIgnoreCase) ||
               (trimmed.Contains(" : Mods : ", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains(" : Files", StringComparison.OrdinalIgnoreCase)) ||
               trimmed.Equals("Location", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeArchiveFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot >= name.Length - 1)
        {
            return false;
        }

        var ext = name[(dot + 1)..];
        return ext.Length is >= 2 and <= 4 && ext.All(char.IsLetterOrDigit);
    }

    private static string? GetCanonicalModDBFileKey(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            !Uri.TryCreate(new Uri(ModDBConstants.BaseUrl), url, out uri))
        {
            return url.Trim().TrimEnd('/').ToLowerInvariant();
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        if (segments.Length >= 3 && (segments[^2].Equals("start", StringComparison.OrdinalIgnoreCase) || segments[^2].Equals("mirror", StringComparison.OrdinalIgnoreCase)))
        {
            return $"start:{segments[^1].ToLowerInvariant()}";
        }

        var lastSegment = segments[^1].ToLowerInvariant();
        if (lastSegment.Equals("downloads", StringComparison.OrdinalIgnoreCase) ||
            lastSegment.Equals("addons", StringComparison.OrdinalIgnoreCase) ||
            lastSegment.Equals("files", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return lastSegment;
    }

    private static string? NormalizeDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = url.Trim().TrimEnd('/');
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/')
            : trimmed;
    }

    /// <summary>
    /// Collapses FileDetail filename rows with the parent /downloads listing of the same binary
    /// so the Releases tab does not show both "GeneralsUndone_v1.0.zip" and "C&amp;C Generals Undone".
    /// </summary>
    private static List<ContentSection> DeduplicateDownloadableFiles(List<ContentSection> sections)
    {
        var files = sections.OfType<DownloadableFile>().ToList();
        if (files.Count <= 1)
        {
            return sections;
        }

        var merged = new List<DownloadableFile>();
        var indexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var existingIndex = FindExistingFileIndex(file, merged, indexByKey);

            if (existingIndex >= 0)
            {
                var updated = MergeDownloadableFiles(merged[existingIndex], file);
                merged[existingIndex] = updated;
                UpdateFileIndexKeys(updated, existingIndex, indexByKey);
                continue;
            }

            var newIndex = merged.Count;
            UpdateFileIndexKeys(file, newIndex, indexByKey);
            merged.Add(file);
        }

        var sortedMerged = merged
            .OrderByDescending(f => f.ReleaseDate ?? f.UploadDate ?? DateTime.MinValue)
            .ThenByDescending(f => f.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<ContentSection>(sections.Count);
        var filesEmitted = false;
        foreach (var section in sections)
        {
            if (section is DownloadableFile)
            {
                if (!filesEmitted)
                {
                    result.AddRange(sortedMerged);
                    filesEmitted = true;
                }

                continue;
            }

            result.Add(section);
        }

        return result;
    }

    private static int FindExistingFileIndex(
        DownloadableFile file,
        List<DownloadableFile> merged,
        Dictionary<string, int> indexByKey)
    {
        var downloadKey = NormalizeDownloadUrl(file.DownloadUrl);
        var detailsKey = NormalizeDownloadUrl(file.DetailsUrl);
        var canonicalDownloadKey = GetCanonicalModDBFileKey(file.DownloadUrl);
        var canonicalDetailsKey = GetCanonicalModDBFileKey(file.DetailsUrl);
        var nameKey = !string.IsNullOrWhiteSpace(file.Name) ? file.Name.Trim().ToLowerInvariant() : null;
        var filenameKey = !string.IsNullOrWhiteSpace(file.Filename) ? file.Filename.Trim().ToLowerInvariant() : null;

        var keysToTry = new[] { downloadKey, detailsKey, canonicalDownloadKey, canonicalDetailsKey, filenameKey, nameKey };
        int existingIndex = -1;

        foreach (var key in keysToTry)
        {
            if (key != null && indexByKey.TryGetValue(key, out var idx))
            {
                existingIndex = idx;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            var existing = merged[existingIndex];
            var existingCanonDetails = GetCanonicalModDBFileKey(existing.DetailsUrl);
            var existingCanonDownload = GetCanonicalModDBFileKey(existing.DownloadUrl);

            var detailsConflict = canonicalDetailsKey != null && existingCanonDetails != null &&
                                  !string.Equals(canonicalDetailsKey, existingCanonDetails, StringComparison.OrdinalIgnoreCase);
            var downloadConflict = IsDirectDownloadUrl(file.DownloadUrl) && IsDirectDownloadUrl(existing.DownloadUrl) &&
                                   canonicalDownloadKey != null && existingCanonDownload != null &&
                                   !string.Equals(canonicalDownloadKey, existingCanonDownload, StringComparison.OrdinalIgnoreCase);

            if (detailsConflict || downloadConflict)
            {
                return -1;
            }
        }

        return existingIndex;
    }

    private static void UpdateFileIndexKeys(
        DownloadableFile file,
        int targetIndex,
        Dictionary<string, int> indexByKey)
    {
        var downloadKey = NormalizeDownloadUrl(file.DownloadUrl);
        var detailsKey = NormalizeDownloadUrl(file.DetailsUrl);
        var canonDownload = GetCanonicalModDBFileKey(file.DownloadUrl);
        var canonDetails = GetCanonicalModDBFileKey(file.DetailsUrl);
        var nameKey = !string.IsNullOrWhiteSpace(file.Name) ? file.Name.Trim().ToLowerInvariant() : null;
        var filenameKey = !string.IsNullOrWhiteSpace(file.Filename) ? file.Filename.Trim().ToLowerInvariant() : null;

        if (downloadKey != null) indexByKey[downloadKey] = targetIndex;
        if (detailsKey != null) indexByKey[detailsKey] = targetIndex;
        if (canonDownload != null) indexByKey[canonDownload] = targetIndex;
        if (canonDetails != null) indexByKey[canonDetails] = targetIndex;
        if (nameKey != null) indexByKey[nameKey] = targetIndex;
        if (filenameKey != null) indexByKey[filenameKey] = targetIndex;
    }

    private static DownloadableFile MergeDownloadableFiles(DownloadableFile left, DownloadableFile right)
    {
        var finalName = ResolveMergedName(left.Name, right.Name);
        var finalFilename = ResolveMergedFilename(left, right);
        var bestDownloadUrl = ResolveBestDownloadUrl(left.DownloadUrl, right.DownloadUrl);
        var bestDetailsUrl = ResolveBestDetailsUrl(left, right);

        FileSectionType bestSectionType = left.FileSectionType != FileSectionType.Downloads
            ? left.FileSectionType
            : right.FileSectionType;

        return left with
        {
            Name = finalName ?? left.Name ?? string.Empty,
            Filename = finalFilename,
            DownloadUrl = bestDownloadUrl,
            DetailsUrl = bestDetailsUrl,
            FileSectionType = bestSectionType,
            ReleaseDate = left.ReleaseDate ?? right.ReleaseDate,
            UploadDate = left.UploadDate ?? right.UploadDate,
            SizeBytes = left.SizeBytes ?? right.SizeBytes,
            SizeDisplay = left.SizeDisplay ?? right.SizeDisplay,
            Version = left.Version ?? right.Version,
            Category = left.Category ?? right.Category,
            Uploader = left.Uploader ?? right.Uploader,
            Description = left.Description ?? right.Description,
            ThumbnailUrl = left.ThumbnailUrl ?? right.ThumbnailUrl,
            Md5Hash = left.Md5Hash ?? right.Md5Hash,
            DownloadCount = left.DownloadCount ?? right.DownloadCount,
            CommentCount = left.CommentCount ?? right.CommentCount,
        };
    }

    private static string? ResolveMergedName(string? leftName, string? rightName)
    {
        var preferRightName = LooksLikeArchiveFileName(leftName) && !LooksLikeArchiveFileName(rightName);
        var preferLeftName = !LooksLikeArchiveFileName(leftName) && LooksLikeArchiveFileName(rightName);

        if (preferRightName)
        {
            return rightName;
        }

        if (preferLeftName)
        {
            return leftName;
        }

        return leftName ?? rightName;
    }

    private static string? ResolveMergedFilename(DownloadableFile left, DownloadableFile right)
    {
        var finalFilename = left.Filename ?? right.Filename;
        if (string.IsNullOrEmpty(finalFilename))
        {
            if (LooksLikeArchiveFileName(left.Name))
            {
                finalFilename = left.Name;
            }
            else if (LooksLikeArchiveFileName(right.Name))
            {
                finalFilename = right.Name;
            }
        }

        return finalFilename;
    }

    private static string? ResolveBestDownloadUrl(string? leftDownloadUrl, string? rightDownloadUrl)
    {
        if (IsDirectDownloadUrl(leftDownloadUrl))
        {
            return leftDownloadUrl;
        }

        if (IsDirectDownloadUrl(rightDownloadUrl))
        {
            return rightDownloadUrl;
        }

        return leftDownloadUrl ?? rightDownloadUrl;
    }

    private static string? ResolveBestDetailsUrl(DownloadableFile left, DownloadableFile right)
    {
        string? bestDetailsUrl = left.DetailsUrl ?? right.DetailsUrl;
        if (bestDetailsUrl == null && !IsDirectDownloadUrl(left.DownloadUrl))
        {
            bestDetailsUrl = left.DownloadUrl;
        }

        if (bestDetailsUrl == null && !IsDirectDownloadUrl(right.DownloadUrl))
        {
            bestDetailsUrl = right.DownloadUrl;
        }

        return bestDetailsUrl;
    }

    private static List<ContentSection> DeduplicateImages(List<ContentSection> sections)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ContentSection>(sections.Count);
        foreach (var section in sections)
        {
            if (section is Image image)
            {
                var key = GalleryImageKey(image.ThumbnailUrl ?? image.FullSizeUrl ?? image.Title);
                if (!seen.Add(key))
                {
                    continue;
                }
            }

            result.Add(section);
        }

        return result;
    }

    private static List<ContentSection> DeduplicateVideos(List<ContentSection> sections)
    {
        var videoList = sections.OfType<Video>().ToList();
        if (videoList.Count == 0)
        {
            return sections;
        }

        var deduplicated = DeduplicateVideoList(videoList);
        var result = new List<ContentSection>(sections.Count);
        var videoEnumerator = deduplicated.GetEnumerator();

        foreach (var section in sections)
        {
            if (section is Video)
            {
                if (videoEnumerator.MoveNext())
                {
                    result.Add(videoEnumerator.Current);
                }
            }
            else
            {
                result.Add(section);
            }
        }

        while (videoEnumerator.MoveNext())
        {
            result.Add(videoEnumerator.Current);
        }

        return result;
    }

    private static List<Video> DeduplicateVideoList(List<Video> videos)
    {
        var dict = new Dictionary<string, Video>(StringComparer.OrdinalIgnoreCase);

        foreach (var video in videos)
        {
            var key = GetVideoKey(video);
            if (!dict.TryGetValue(key, out var existing))
            {
                dict[key] = video;
            }
            else
            {
                var betterTitle = existing.Title;
                if (!IsUsableVideoTitle(betterTitle) && IsUsableVideoTitle(video.Title))
                {
                    betterTitle = video.Title;
                }

                var betterThumb = !string.IsNullOrWhiteSpace(existing.ThumbnailUrl)
                    ? existing.ThumbnailUrl
                    : video.ThumbnailUrl;
                var betterEmbed = !string.IsNullOrWhiteSpace(existing.EmbedUrl)
                    ? existing.EmbedUrl
                    : video.EmbedUrl;
                var betterPlatform = !string.Equals(existing.Platform, "Unknown", StringComparison.OrdinalIgnoreCase)
                    ? existing.Platform
                    : video.Platform;

                dict[key] = new Video(
                    Title: betterTitle,
                    ThumbnailUrl: betterThumb,
                    EmbedUrl: betterEmbed,
                    Platform: betterPlatform);
            }
        }

        return [.. dict.Values];
    }

    private static string GetVideoKey(Video video)
    {
        if (!string.IsNullOrWhiteSpace(video.EmbedUrl))
        {
            var ytMatch = YouTubeVideoIdRegex().Match(video.EmbedUrl);
            if (ytMatch.Success)
            {
                return "yt:" + ytMatch.Groups[1].Value.ToLowerInvariant();
            }

            var vimeoMatch = VimeoVideoIdRegex().Match(video.EmbedUrl);
            if (vimeoMatch.Success)
            {
                return "vimeo:" + vimeoMatch.Groups[1].Value.ToLowerInvariant();
            }

            return "url:" + GalleryImageKey(video.EmbedUrl.Split('?')[0]).ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(video.ThumbnailUrl))
        {
            var ytMatch = YouTubeVideoIdRegex().Match(video.ThumbnailUrl);
            if (ytMatch.Success)
            {
                return "yt:" + ytMatch.Groups[1].Value.ToLowerInvariant();
            }

            return "thumb:" + GalleryImageKey(video.ThumbnailUrl.Split('?')[0]).ToLowerInvariant();
        }

        return "title:" + video.Title.ToLowerInvariant();
    }

    /// <summary>
    /// Parses a file size string into bytes (e.g., "15.5 MB", "188.3kb (192,819 bytes)", "9,72 MB").
    /// </summary>
    private static long? ParseFileSize(string sizeText)
    {
        if (string.IsNullOrWhiteSpace(sizeText))
        {
            return null;
        }

        // Check for exact byte count in parentheses or text, e.g. "(192,819 bytes)"
        var bytesMatch = ExactBytesRegex().Match(sizeText);
        if (bytesMatch.Success)
        {
            var rawDigits = bytesMatch.Groups[1].Value.Replace(",", string.Empty).Replace(" ", string.Empty).Trim();
            if (long.TryParse(rawDigits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exactBytesVal))
            {
                return exactBytesVal;
            }
        }

        // Parse floating point size with unit, supporting comma or dot decimals and optional spacing
        var match = NumericSizeWithUnitRegex().Match(sizeText);
        if (!match.Success)
        {
            return null;
        }

        var numStr = match.Groups[1].Value.Replace(',', '.');
        if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var unit = match.Groups[2].Value.ToUpperInvariant();

        if (unit.StartsWith("G", StringComparison.OrdinalIgnoreCase))
        {
            return (long)(value * 1024 * 1024 * 1024);
        }

        if (unit.StartsWith("M", StringComparison.OrdinalIgnoreCase))
        {
            return (long)(value * 1024 * 1024);
        }

        if (unit.StartsWith("K", StringComparison.OrdinalIgnoreCase))
        {
            return (long)(value * 1024);
        }

        if (unit.StartsWith("B", StringComparison.OrdinalIgnoreCase))
        {
            return (long)value;
        }

        return null;
    }

    /// <summary>
    /// Parses ModDB date formats like "Mar 15th, 2024" or "Added Mar 15th, 2024".
    /// </summary>
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
    /// <param name="sectionType">The type of file section (Release or Addon).</param>
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
    /// <param name="sectionType">The type of file section (Release or Addon).</param>
    private static List<DownloadableFile> ExtractFiles(IDocument document, FileSectionType sectionType = FileSectionType.Downloads)
    {
        var files = new List<DownloadableFile>();

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
    /// <param name="sectionType">The type of file section (Release or Addon).</param>
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
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        (uri.Host.Equals("moddb.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".moddb.com", StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public async Task<ParsedWebPage> ParseAsync(string url, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Parsing ModDB page: {Url}", url);

        return await playwrightService.ExecuteInPersistentContextAsync(
            ModDBConstants.BrowserProfileName,
            async () =>
            {
                var urlsToFetch = GetUrlsToFetch(url);

                // fetch all required URLs on one persistent Chromium page (profile) in a single batch.
                var fetched = await playwrightService.FetchAndParsePersistentManyAsync(
                    ModDBConstants.BrowserProfileName,
                    urlsToFetch,
                    cancellationToken);

                if (!fetched.TryGetValue(url, out var document))
                {
                    throw new InvalidOperationException($"Failed to fetch document for {url}");
                }

                var parsedPage = ParseInternalWithFetched(url, document, fetched);

                // check whether there are releases and addons to be loaded before closing the browser window
                var releaseFilesToEnrich = parsedPage.Sections.OfType<DownloadableFile>()
                    .Where(f => f.FileSectionType == FileSectionType.Downloads && !string.IsNullOrEmpty(f.DetailsUrl ?? f.DownloadUrl))
                    .OrderByDescending(f => f.ReleaseDate ?? f.UploadDate ?? DateTime.MinValue)
                    .ThenByDescending(f => f.Version, StringComparer.OrdinalIgnoreCase)
                    .Take(ContentConstants.PreloadRecentItemsLimit);

                var addonFilesToEnrich = parsedPage.Sections.OfType<DownloadableFile>()
                    .Where(f => f.FileSectionType == FileSectionType.Addons && !string.IsNullOrEmpty(f.DetailsUrl ?? f.DownloadUrl))
                    .OrderByDescending(f => f.ReleaseDate ?? f.UploadDate ?? DateTime.MinValue)
                    .ThenByDescending(f => f.Version, StringComparer.OrdinalIgnoreCase)
                    .Take(ContentConstants.PreloadRecentItemsLimit);

                var filesToEnrich = releaseFilesToEnrich.Concat(addonFilesToEnrich).ToList();

                var urlsToEnrich = filesToEnrich
                    .Select(f => f.DetailsUrl ?? f.DownloadUrl!)
                    .Where(u => !fetched.ContainsKey(u))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (urlsToEnrich.Count > 0)
                {
                    logger.LogInformation("Enriching {Count} recent releases/addons in same browser window", urlsToEnrich.Count);
                    var enrichedFetched = await playwrightService.FetchAndParsePersistentManyAsync(
                        ModDBConstants.BrowserProfileName,
                        urlsToEnrich,
                        cancellationToken);

                    if (enrichedFetched.Count > 0)
                    {
                        var enrichedFiles = new Dictionary<string, DownloadableFile>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kvp in enrichedFetched)
                        {
                            var detailUrl = kvp.Key;
                            var detailDoc = kvp.Value;
                            var detailed = ExtractDetailedFile(detailDoc, detailUrl);
                            if (detailed != null)
                            {
                                enrichedFiles[detailUrl] = detailed;
                            }
                        }

                        if (enrichedFiles.Count > 0)
                        {
                            var updatedSections = new List<ContentSection>();
                            foreach (var section in parsedPage.Sections)
                            {
                                if (section is DownloadableFile file)
                                {
                                    var key = file.DetailsUrl ?? file.DownloadUrl;
                                    if (key != null && enrichedFiles.TryGetValue(key, out var detailed))
                                    {
                                        var merged = detailed with
                                        {
                                            Category = !string.IsNullOrEmpty(detailed.Category) ? detailed.Category : file.Category,
                                            FileSectionType = file.FileSectionType,
                                            UploadDate = detailed.UploadDate ?? file.UploadDate,
                                            ReleaseDate = detailed.ReleaseDate ?? file.ReleaseDate,
                                            ThumbnailUrl = !string.IsNullOrEmpty(detailed.ThumbnailUrl) ? detailed.ThumbnailUrl : file.ThumbnailUrl,
                                            DetailsUrl = !string.IsNullOrEmpty(detailed.DetailsUrl) ? detailed.DetailsUrl : file.DetailsUrl,
                                            DownloadUrl = !string.IsNullOrEmpty(detailed.DownloadUrl) ? detailed.DownloadUrl : file.DownloadUrl,
                                            SizeBytes = detailed.SizeBytes ?? file.SizeBytes,
                                            SizeDisplay = !string.IsNullOrEmpty(detailed.SizeDisplay) ? detailed.SizeDisplay : file.SizeDisplay,
                                            Name = !string.IsNullOrEmpty(detailed.Name) && !string.Equals(detailed.Name, "Unknown", StringComparison.OrdinalIgnoreCase) ? detailed.Name : file.Name,
                                        };
                                        updatedSections.Add(merged);
                                        continue;
                                    }
                                }

                                updatedSections.Add(section);
                            }

                            parsedPage = parsedPage with { Sections = updatedSections };
                        }
                    }
                }

                return parsedPage;
            },
            cancellationToken);
    }

    /// <summary>
    /// File-only parse for acquisition: returns the single <see cref="DownloadableFile"/> from a
    /// FileDetail page and the page's own metadata, without fetching the parent mod's downloads,
    /// addons, videos, images, reviews, and articles sections. The download path needs only the
    /// file; the FileDetail page already carries title/developer/icon/description via its profile
    /// sidebar. Use <see cref="ParseAsync(string, CancellationToken)"/> when the full rich page
    /// (Media/Community/Releases/Addons) is required for display.
    /// </summary>
    /// <param name="url">A FileDetail URL (<c>/mods/.../downloads/file-name</c> or <c>/addons/...</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A parsed page containing the single file and the FileDetail page's context.</returns>
    public async Task<ParsedWebPage> ParseFileDetailAsync(string url, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Parsing ModDB file detail (acquisition path): {Url}", url);

        var document = await playwrightService.FetchAndParsePersistentAsync(
            ModDBConstants.BrowserProfileName, url, cancellationToken);

        var sections = new List<ContentSection>();
        var file = ExtractDetailedFile(document, url);
        if (file != null)
        {
            sections.Add(file);
        }

        var context = ExtractGlobalContext(document);

        return new ParsedWebPage(
            Url: new Uri(url),
            Context: context,
            Sections: sections,
            PageType: PageType.FileDetail);
    }

    /// <summary>
    /// Parses multiple file detail pages in a single parallel batch using persistent Chromium tabs.
    /// </summary>
    /// <param name="urls">The file detail URLs to parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary of URL -> ParsedWebPage results.</returns>
    public async Task<IReadOnlyDictionary<string, ParsedWebPage>> ParseFileDetailsManyAsync(
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken = default)
    {
        if (urls == null || urls.Count == 0)
        {
            return new Dictionary<string, ParsedWebPage>();
        }

        logger.LogInformation("Parsing ModDB file details in parallel batch ({Count} URLs)", urls.Count);

        var fetched = await playwrightService.FetchAndParsePersistentManyAsync(
            ModDBConstants.BrowserProfileName,
            urls,
            cancellationToken);

        var results = new Dictionary<string, ParsedWebPage>(StringComparer.OrdinalIgnoreCase);
        foreach (var (url, document) in fetched)
        {
            try
            {
                var sections = new List<ContentSection>();
                var file = ExtractDetailedFile(document, url);
                if (file != null)
                {
                    sections.Add(file);
                }

                var context = ExtractGlobalContext(document);
                results[url] = new ParsedWebPage(
                    Url: new Uri(url),
                    Context: context,
                    Sections: sections,
                    PageType: PageType.FileDetail);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse file detail for {Url}", url);
            }
        }

        return results;
    }

    /// <inheritdoc />
    public Task<ParsedWebPage> ParseAsync(string url, string html, CancellationToken cancellationToken = default)
    {
        var browsingContext = BrowsingContext.New(Configuration.Default);
        var document = browsingContext.OpenAsync(req => req.Content(html), cancellationToken).GetAwaiter().GetResult();
        return Task.FromResult(ParseInternal(url, document));
    }

    /// <summary>
    /// Determines whether a ModDB URL points directly to an acquisition endpoint (/start/ or /mirror/)
    /// rather than an HTML content or detail listing page.
    /// </summary>
    /// <param name="url">The URL to test.</param>
    /// <returns><see langword="true"/> if the URL is a direct download link; otherwise <see langword="false"/>.</returns>
    internal static bool IsDirectDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (url.Contains("/downloads/start/", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("/addons/start/", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("/downloads/mirror/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if ((url.Contains("/mods/", StringComparison.OrdinalIgnoreCase) || url.Contains("/games/", StringComparison.OrdinalIgnoreCase)) &&
            (url.Contains("/downloads/", StringComparison.OrdinalIgnoreCase) || url.Contains("/addons/", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Computes the complete list of URLs needed to parse a ModDB page and its related sections
    /// in a single Playwright batch, avoiding multiple browser launches.
    /// </summary>
    private static IReadOnlyList<string> GetUrlsToFetch(string url)
    {
        if (IsModDetailPage(url))
        {
            var baseUrl = Uri.TryCreate(url, UriKind.Absolute, out var uri)
                ? $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}"
                : url.Split('?')[0].Split('#')[0].TrimEnd('/');

            return
            [
                url,
                baseUrl + "/downloads",
                baseUrl + "/addons",
                baseUrl + "/videos",
                baseUrl + "/images",
                baseUrl + "/reviews",
                baseUrl + "/articles",
            ];
        }

        var parentModUrl = ExtractParentModUrl(url);
        if (!string.IsNullOrEmpty(parentModUrl))
        {
            var baseUrl = Uri.TryCreate(parentModUrl, UriKind.Absolute, out var parentUri)
                ? $"{parentUri.Scheme}://{parentUri.Authority}{parentUri.AbsolutePath.TrimEnd('/')}"
                : parentModUrl.Split('?')[0].Split('#')[0].TrimEnd('/');

            return
            [
                url,
                parentModUrl,
                baseUrl + "/downloads",
                baseUrl + "/addons",
                baseUrl + "/videos",
                baseUrl + "/images",
                baseUrl + "/reviews",
                baseUrl + "/articles",
            ];
        }

        return [url];
    }

    /// <summary>
    /// Determines if the URL is a mod detail page that should have all sections fetched.
    /// Only the mod root (<c>/mods/{slug}</c>) qualifies — subpaths like comments, downloads,
    /// or tutorials must not trigger the six-section Chromium sweep.
    /// </summary>
    private static bool IsModDetailPage(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // /mods/{slug} optionally with a trailing slash — nothing after the slug.
        var segments = uri.AbsolutePath.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2 &&
               segments[0].Equals("mods", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractDeveloper(IDocument document)
    {
        foreach (var el in document.QuerySelectorAll(ModDBParserConstants.DeveloperProfileSelector))
        {
            var href = el.GetAttribute("href") ?? string.Empty;
            if (href.Contains("/register", StringComparison.OrdinalIgnoreCase) ||
                href.Contains("/login", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = el.TextContent?.Trim();
            if (!IsJunkDeveloperName(text))
            {
                return text!;
            }
        }

        foreach (var el in document.QuerySelectorAll(ModDBParserConstants.DeveloperSelector))
        {
            var href = el.GetAttribute("href") ?? string.Empty;
            if (href.Contains("/register", StringComparison.OrdinalIgnoreCase) ||
                href.Contains("/login", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = el.TextContent?.Trim();
            if (!IsJunkDeveloperName(text))
            {
                return text!;
            }
        }

        return "Unknown";
    }

    private static string? ExtractDescription(IDocument document)
    {
        var fileDesc = ReadDescriptionFrom(document.QuerySelector(ModDBParserConstants.FileDescriptionSelector)
            ?? document.QuerySelector("#downloaddescription")
            ?? document.QuerySelector("#downloadsummary"));
        if (!IsBreadcrumbOrLocationText(fileDesc))
        {
            var extra = document.QuerySelector("#downloaddescription");
            var summary = document.QuerySelector("#downloadsummary");
            var parts = new List<string>();
            var summaryText = ReadDescriptionFrom(summary);
            var fullText = extra != null && extra != summary ? ReadDescriptionFrom(extra) : null;
            if (!IsBreadcrumbOrLocationText(summaryText))
            {
                parts.Add(summaryText!);
            }

            if (!IsBreadcrumbOrLocationText(fullText) &&
                (summaryText == null || fullText!.IndexOf(summaryText, StringComparison.OrdinalIgnoreCase) < 0))
            {
                parts.Add(fullText!);
            }

            if (parts.Count > 0)
            {
                return string.Join("\n\n", parts);
            }

            return fileDesc;
        }

        var fullDescEl = document.QuerySelector(ModDBParserConstants.FullDescriptionSelector);
        var fromFull = ReadDescriptionFrom(fullDescEl);
        if (!IsBreadcrumbOrLocationText(fromFull))
        {
            return fromFull;
        }

        var summaryEl = document.QuerySelector(ModDBParserConstants.SummarySelector);
        var fromSummary = ReadDescriptionFrom(summaryEl);
        if (!IsBreadcrumbOrLocationText(fromSummary))
        {
            return fromSummary;
        }

        var metaDesc = document.QuerySelector("meta[name='description'], meta[property='og:description']");
        var meta = metaDesc?.GetAttribute("content")?.Trim();
        return IsBreadcrumbOrLocationText(meta) ? null : meta;
    }

    private static string? ReadDescriptionFrom(IElement? element)
    {
        if (element == null)
        {
            return null;
        }

        var paragraphs = element.QuerySelectorAll("p")
            .Select(p => p.TextContent?.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t) && !IsBreadcrumbOrLocationText(t))
            .ToList();

        if (paragraphs.Count > 0)
        {
            return string.Join("\n\n", paragraphs);
        }

        var text = element.TextContent?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// Internal parsing logic that works with a parsed AngleSharp document.
    /// </summary>
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

        sections = DeduplicateDownloadableFiles(sections);
        sections = DeduplicateImages(sections);
        sections = DeduplicateVideos(sections);

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
    /// Synchronous internal parsing logic that extracts content from the primary document
    /// and any pre-fetched section documents (from the single Playwright batch).
    /// </summary>
    private ParsedWebPage ParseInternalWithFetched(
        string url,
        IDocument document,
        IReadOnlyDictionary<string, IDocument> fetched)
    {
        var context = ExtractGlobalContext(document);
        var pageType = DetectPageType(url, document);

        logger.LogDebug("Detected page type: {PageType}", pageType);

        var sections = new List<ContentSection>();

        // Special handling for mod detail pages - populate ALL sections
        if (IsModDetailPage(url) && pageType == PageType.Detail)
        {
            logger.LogInformation("Mod detail page detected, parsing all sections (downloads, addons, videos, images, reviews, articles)");

            // Extract non-file content from the main page
            sections.AddRange(ExtractVideos(document));
            sections.AddRange(ExtractImages(document));
            sections.AddRange(ExtractArticles(document));
            sections.AddRange(ExtractReviews(document));
            sections.AddRange(ExtractComments(document));

            var baseUrl = url.TrimEnd('/');
            AppendFetchedSections(sections, baseUrl, fetched);
        }
        else if (pageType == PageType.FileDetail)
        {
            var file = ExtractDetailedFile(document, url);
            if (file != null)
            {
                sections.Add(file);
            }

            // Game downloads/addons are discovered as FileDetail URLs under /games/... and never enter
            // the /mods/ parent sweep below. Pull comments and any inline media from this page so the
            // detail view is not left with only a single Releases row.
            AppendOnPageFileDetailSections(sections, document);

            var parentModUrl = ExtractParentModUrl(url);
            if (!string.IsNullOrEmpty(parentModUrl) && fetched.TryGetValue(parentModUrl, out var parentDoc))
            {
                logger.LogInformation("FileDetail page detected, extracted parent mod sections from: {ParentUrl}", parentModUrl);

                var parentContext = ExtractGlobalContext(parentDoc);
                if (!string.IsNullOrEmpty(parentContext.IconUrl))
                {
                    logger.LogInformation("Extracted icon from parent mod: {IconUrl}", parentContext.IconUrl);
                }

                context = MergeContext(context, parentContext);

                sections.AddRange(ExtractVideos(parentDoc));
                sections.AddRange(ExtractImages(parentDoc));
                sections.AddRange(ExtractArticles(parentDoc));
                sections.AddRange(ExtractReviews(parentDoc));
                sections.AddRange(ExtractComments(parentDoc));

                var baseUrl = parentModUrl.TrimEnd('/');
                AppendFetchedSections(sections, baseUrl, fetched);
            }
        }
        else
        {
            // Standard page handling
            switch (pageType)
            {
                case PageType.List:
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

                default:
                    logger.LogWarning("Unknown page type for URL: {Url}", url);
                    break;
            }
        }

        sections = DeduplicateDownloadableFiles(sections);
        sections = DeduplicateImages(sections);
        sections = DeduplicateVideos(sections);

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
    /// </summary>
    private GlobalContext ExtractGlobalContext(IDocument document)
    {
        // 1. Extract title. File pages use h1 "... file" and h2 for the real content name.
        var h2 = document.QuerySelector("h2 a, h2")?.TextContent?.Trim();
        var h1 = document.QuerySelector("h1 a, h1")?.TextContent?.Trim();
        var title = h1 ?? "Unknown";
        if (!string.IsNullOrWhiteSpace(h2) &&
            (string.IsNullOrWhiteSpace(h1) ||
             h1.EndsWith(" file", StringComparison.OrdinalIgnoreCase)))
        {
            title = h2;
        }

        if (string.IsNullOrWhiteSpace(title) || title == "Unknown")
        {
            title = document.QuerySelector(".title")?.TextContent?.Trim() ?? "Unknown";
        }

        // 2. Extract developer — never the header "register" / "sign in" links.
        var developer = ExtractDeveloper(document);

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
        if (!string.IsNullOrEmpty(iconUrl))
        {
            if (!iconUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                iconUrl = "https:" + iconUrl;
            }

            if (iconUrl.Contains("error_50x50", StringComparison.OrdinalIgnoreCase) ||
                iconUrl.Contains("default/error", StringComparison.OrdinalIgnoreCase) ||
                iconUrl.Contains("blank.gif", StringComparison.OrdinalIgnoreCase) ||
                iconUrl.Contains("clear.gif", StringComparison.OrdinalIgnoreCase))
            {
                iconUrl = null;
            }
        }

        // 5. Extract description. File pages put copy in #downloadsummary / #downloaddescription;
        // the first .summary on those pages is the breadcrumb "Games : ... : Files".
        var description = ExtractDescription(document);

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
    /// Extracts the downloadable file plus any Media/Community content already rendered on a
    /// FileDetail page (comments, inline images, etc.). Game-scoped FileDetail URLs
    /// (<c>/games/.../downloads/...</c>) never have a parent mod to sweep, so this on-page pass is
    /// the only way those detail views get Community/Media tabs.
    /// </summary>
    private List<ContentSection> ExtractFileDetailSections(IDocument document)
    {
        var sections = new List<ContentSection>();

        var file = ExtractDetailedFile(document, document.Url);
        if (file != null)
        {
            sections.Add(file);
        }

        AppendOnPageFileDetailSections(sections, document);
        return sections;
    }

    /// <summary>
    /// Appends videos, images, articles, reviews, and comments already present on the current
    /// FileDetail document. Does not navigate to sibling section URLs.
    /// </summary>
    private void AppendOnPageFileDetailSections(List<ContentSection> sections, IDocument document)
    {
        sections.AddRange(ExtractVideos(document));
        sections.AddRange(ExtractImages(document));
        sections.AddRange(ExtractArticles(document));
        sections.AddRange(ExtractReviews(document));
        sections.AddRange(ExtractComments(document));
    }

    /// <summary>
    /// Maps already-fetched section documents into content sections. Missing keys are soft-failures
    /// (logged by the Playwright batch fetch); this method only extracts what loaded.
    /// </summary>
    private void AppendFetchedSections(
        List<ContentSection> sections,
        string baseUrl,
        IReadOnlyDictionary<string, IDocument> fetched)
    {
        TryAppendSection(sections, fetched, baseUrl + "/downloads", "downloads", doc => ExtractFiles(doc, FileSectionType.Downloads));
        TryAppendSection(sections, fetched, baseUrl + "/addons", "addons", doc => ExtractFiles(doc, FileSectionType.Addons));
        TryAppendSection(sections, fetched, baseUrl + "/videos", "videos", ExtractVideos);
        TryAppendSection(sections, fetched, baseUrl + "/images", "images", ExtractImages);
        TryAppendSection(sections, fetched, baseUrl + "/reviews", "reviews", ExtractReviews);
        TryAppendSection(sections, fetched, baseUrl + "/articles", "articles", ExtractArticles);
    }

    private void TryAppendSection(
        List<ContentSection> sections,
        IReadOnlyDictionary<string, IDocument> fetched,
        string sectionUrl,
        string sectionName,
        Func<IDocument, IEnumerable<ContentSection>> extract)
    {
        if (!fetched.TryGetValue(sectionUrl, out var sectionDoc))
        {
            logger.LogWarning("Failed to fetch {Section} section for {Url}", sectionName, sectionUrl);
            return;
        }

        var sectionItems = extract(sectionDoc).ToList();
        sections.AddRange(sectionItems);
        logger.LogInformation("Found {Count} items in {Section} section", sectionItems.Count, sectionName);
    }

    /// <summary>
    /// Extracts detailed file information from a file detail page.
    /// Parses the metadata table with rows like: Filename, Category, Uploader, Size, MD5 Hash.
    /// </summary>
    private DownloadableFile? ExtractDetailedFile(IDocument document, string? pageUrl = null)
    {
        var detailsUrl = pageUrl;
        if (string.IsNullOrEmpty(detailsUrl) && document.Url != null && document.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            detailsUrl = document.Url;
        }

        var metadata = CollectDetailedFileMetadata(document);
        var filename = metadata.GetValueOrDefault(ModDBParserConstants.MetadataFilename)
            ?? metadata.GetValueOrDefault(ModDBParserConstants.MetadataFileNameAlt)
            ?? metadata.GetValueOrDefault(ModDBParserConstants.MetadataFileAlt);

        var name = ResolveDetailedFileName(document, filename);
        var (sizeBytes, sizeDisplay) = ExtractDetailedFileSize(document, metadata);
        var uploader = metadata.GetValueOrDefault(ModDBParserConstants.MetadataUploader)
            ?? metadata.GetValueOrDefault(ModDBParserConstants.MetadataUploadedBy)
            ?? metadata.GetValueOrDefault(ModDBParserConstants.MetadataAuthor);

        var category = metadata.GetValueOrDefault(ModDBParserConstants.MetadataCategory)
            ?? metadata.GetValueOrDefault(ModDBParserConstants.MetadataFileCategory)
            ?? metadata.GetValueOrDefault(ModDBParserConstants.MetadataType);

        var md5Hash = metadata.GetValueOrDefault(ModDBParserConstants.MetadataMd5Hash)
            ?? metadata.GetValueOrDefault(ModDBParserConstants.MetadataMd5HashAlt)
            ?? metadata.GetValueOrDefault(ModDBParserConstants.MetadataMd5Checksum)
            ?? metadata.GetValueOrDefault(ModDBParserConstants.MetadataMd5)
            ?? metadata.GetValueOrDefault(ModDBParserConstants.MetadataHash)
            ?? metadata.GetValueOrDefault(ModDBParserConstants.MetadataChecksum);

        var (uploadDate, releaseDate) = ExtractDetailedFileDates(metadata);
        var downloadUrl = ExtractDetailedDownloadUrl(document);
        var downloadCount = ExtractDetailedDownloadCount(metadata);
        var description = ExtractDescription(document)
            ?? document.QuerySelector(ModDBParserConstants.FileDescriptionContainerSelector)?.TextContent?.Trim();
        var previewImages = ExtractDetailedPreviewImages(document);

        logger.LogInformation(
            "Extracted file: Name={Name}, Size={Size}, Uploader={Uploader}, DownloadUrl={Url}",
            name,
            sizeDisplay,
            uploader,
            downloadUrl);

        var sectionType = detailsUrl?.Contains("/addons/", StringComparison.OrdinalIgnoreCase) == true ||
                          category?.Contains("addon", StringComparison.OrdinalIgnoreCase) == true
            ? FileSectionType.Addons
            : FileSectionType.Downloads;

        return new DownloadableFile(
            Name: name,
            SizeBytes: sizeBytes,
            SizeDisplay: sizeDisplay,
            UploadDate: uploadDate,
            Category: category,
            Uploader: uploader,
            DownloadUrl: downloadUrl,
            Md5Hash: md5Hash,
            DownloadCount: downloadCount,
            FileSectionType: sectionType,
            ReleaseDate: releaseDate,
            DetailsUrl: detailsUrl,
            Description: description,
            PreviewImages: previewImages.Count > 0 ? previewImages : null,
            Filename: filename);
    }

    private Dictionary<string, string> CollectDetailedFileMetadata(IDocument document)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var containers = document.QuerySelectorAll("#downloadsinfo, .table, table.table, #downloadsfiles, .sidecolumn, #modsinfo, #profile");
        foreach (var container in containers)
        {
            var rows = container.QuerySelectorAll("tr");
            foreach (var row in rows)
            {
                var cells = row.QuerySelectorAll("td, th");
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

            var flexRows = container.QuerySelectorAll(".row.clear, .row, div.heading, dl");
            foreach (var row in flexRows)
            {
                var labelEl = row.QuerySelector("h5, h4, dt, strong, .label, .rowlabel");
                var label = labelEl?.TextContent?.Trim().ToLowerInvariant().Replace(":", string.Empty);
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                var valueElement = labelEl?.NextElementSibling
                    ?? row.QuerySelector("span.summary, dd, time, a, .content, span");
                var value = valueElement?.TextContent?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    metadata[label] = value;
                }
            }
        }

        return metadata;
    }

    private string ResolveDetailedFileName(IDocument document, string? filename)
    {
        string? fileHeading = null;
        var headingCandidates = document.QuerySelectorAll(ModDBParserConstants.FilePageTitleSelector);
        foreach (var cand in headingCandidates)
        {
            if (cand.Closest(ModDBParserConstants.HeaderBoxSelector) != null)
            {
                continue;
            }

            var text = cand.TextContent?.Trim();
            if (!string.IsNullOrWhiteSpace(text) && !text.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                fileHeading = text;
                break;
            }
        }

        string? titleTagCandidate = null;
        var docTitle = document.Title?.Trim();
        if (!string.IsNullOrWhiteSpace(docTitle))
        {
            var match = ModDBPageTitleRegex().Match(docTitle);
            if (match.Success)
            {
                titleTagCandidate = match.Groups["title"].Value.Trim();
            }
        }

        var h1Title = document.QuerySelector("h1 a, h1")?.TextContent?.Trim();
        if (!string.IsNullOrWhiteSpace(h1Title) && h1Title.EndsWith(" file", StringComparison.OrdinalIgnoreCase))
        {
            h1Title = h1Title[..^5].Trim();
        }

        var humanName = fileHeading
            ?? titleTagCandidate
            ?? (!string.IsNullOrWhiteSpace(h1Title) && !h1Title.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? h1Title : null)
            ?? document.QuerySelector(ModDBParserConstants.FallbackTitleSelector)?.TextContent?.Trim();

        return humanName ?? filename ?? "Unknown";
    }

    private (long? SizeBytes, string? SizeDisplay) ExtractDetailedFileSize(IDocument document, Dictionary<string, string> metadata)
    {
        string? sizeDisplay = metadata.GetValueOrDefault(ModDBParserConstants.MetadataSize)
            ?? metadata.GetValueOrDefault(ModDBParserConstants.MetadataFileSizeAlt);
        long? sizeBytes = null;

        if (!string.IsNullOrEmpty(sizeDisplay))
        {
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

        if (string.IsNullOrEmpty(sizeDisplay))
        {
            var downloadButton = document.QuerySelector(ModDBParserConstants.MainDownloadButtonSelector);
            sizeDisplay = downloadButton?.TextContent?.Trim();
            if (!string.IsNullOrEmpty(sizeDisplay))
            {
                sizeBytes = ParseFileSize(sizeDisplay);
            }
        }

        return (sizeBytes, sizeDisplay);
    }

    private (DateTime? UploadDate, DateTime? ReleaseDate) ExtractDetailedFileDates(Dictionary<string, string> metadata)
    {
        DateTime? uploadDate = null;
        DateTime? releaseDate = null;
        var addedStr = metadata.GetValueOrDefault(ModDBParserConstants.MetadataAdded)
            ?? metadata.GetValueOrDefault(ModDBParserConstants.MetadataUpdated);

        if (!string.IsNullOrEmpty(addedStr))
        {
            if (DateTime.TryParse(addedStr, out var parsedDate))
            {
                uploadDate = parsedDate;
                releaseDate = parsedDate;
            }
            else
            {
                var modDBDate = ParseModDBDate(addedStr);
                if (modDBDate.HasValue)
                {
                    uploadDate = modDBDate;
                    releaseDate = modDBDate;
                }
            }
        }

        return (uploadDate, releaseDate);
    }

    private string? ExtractDetailedDownloadUrl(IDocument document)
    {
        var downloadButton = document.QuerySelector(ModDBParserConstants.MainDownloadButtonSelector);
        if (downloadButton != null)
        {
            var href = downloadButton.GetAttribute("href");
            if (!string.IsNullOrEmpty(href))
            {
                return ToAbsoluteUrl(href);
            }
        }

        return null;
    }

    private int? ExtractDetailedDownloadCount(Dictionary<string, string> metadata)
    {
        var downloadsStr = metadata.GetValueOrDefault(ModDBParserConstants.MetadataTotalDownloads)
            ?? metadata.GetValueOrDefault(ModDBParserConstants.MetadataDownloadCount)
            ?? metadata.GetValueOrDefault("downloads");

        if (!string.IsNullOrEmpty(downloadsStr))
        {
            var numberMatch = System.Text.RegularExpressions.Regex.Match(downloadsStr, @"[\d,]+");
            if (numberMatch.Success && int.TryParse(numberMatch.Value.Replace(",", string.Empty), out var parsedDl))
            {
                return parsedDl;
            }
        }

        return null;
    }

    private List<string> ExtractDetailedPreviewImages(IDocument document)
    {
        var previewImages = new List<string>();
        var imageEls = document.QuerySelectorAll(ModDBParserConstants.FilePreviewImagesSelector);
        foreach (var img in imageEls)
        {
            var src = img.GetAttribute("src") ?? img.GetAttribute("data-src");
            if (string.IsNullOrWhiteSpace(src) ||
                src.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("blank.gif", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("clear.gif", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("guest", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("/avatar/", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("button", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("icon.gif", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fullUrl = ToAbsoluteUrl(src);
            var parentAnchor = img.Closest("a");
            var anchorHref = parentAnchor?.GetAttribute("href");
            if (!string.IsNullOrWhiteSpace(anchorHref))
            {
                var absAnchor = ToAbsoluteUrl(anchorHref);
                var isDirectImage = absAnchor.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                    absAnchor.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                    absAnchor.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                    absAnchor.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

                fullUrl = isDirectImage ? absAnchor : GetFullSizeModDBImageSource(fullUrl);
            }
            else
            {
                fullUrl = GetFullSizeModDBImageSource(fullUrl);
            }

            if (!previewImages.Contains(fullUrl))
            {
                previewImages.Add(fullUrl);
            }
        }

        return previewImages;
    }
}
