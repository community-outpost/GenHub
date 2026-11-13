using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Parses Steam Workshop file details pages into structured data.
/// Details pages use the classic Steam Community layout with stable markers.
/// </summary>
public static partial class SteamWorkshopDetailsParser
{
    /// <summary>
    /// Parses workshop file details HTML into structured data.
    /// </summary>
    /// <param name="html">The raw details page HTML.</param>
    /// <returns>The parsed details.</returns>
    public static SteamWorkshopFileDetails Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return new SteamWorkshopFileDetails(null, null, null, null, null, null, null, []);
        }

        var stats = ParseDetailsStats(html);
        var descriptionHtml = ParseDescriptionHtml(html);
        var previewUrl = ParsePreviewUrl(html);
        return new SteamWorkshopFileDetails(
            ParseTitle(html),
            descriptionHtml,
            ParseAuthor(html),
            previewUrl,
            stats.TryGetValue(SteamWorkshopConstants.DetailsStatFileSize, out var fileSize) ? fileSize : null,
            stats.TryGetValue(SteamWorkshopConstants.DetailsStatPosted, out var posted) ? SteamWorkshopHelper.ParseDetailsDate(posted) : null,
            stats.TryGetValue(SteamWorkshopConstants.DetailsStatUpdated, out var updated) ? SteamWorkshopHelper.ParseDetailsDate(updated) : null,
            ParseTags(html),
            ParseAppId(html),
            ParseScreenshots(html, previewUrl, descriptionHtml),
            ParseCreatorAvatarUrl(html),
            stats);
    }

    [GeneratedRegex(@"<title>Steam Workshop::([^<]+)</title>", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"friendBlockContent[^>]*>\s*([^<]+?)\s*<br", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorRegex();

    [GeneratedRegex(@"<img[^>]*\sid=""previewImageMain""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex PreviewImageTagRegex();

    [GeneratedRegex(@"(?<![\w-])src=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ImageSrcRegex();

    [GeneratedRegex(@"<img\b[^>]*?(?<![\w-])src=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex GalleryImageRegex();

    [GeneratedRegex(@"<div class=""detailsStatLeft"">([^<]+)</div>", RegexOptions.IgnoreCase)]
    private static partial Regex StatLabelRegex();

    [GeneratedRegex(@"<div class=""detailsStatRight"">([^<]+)</div>", RegexOptions.IgnoreCase)]
    private static partial Regex StatValueRegex();

    [GeneratedRegex(@"<div[^>]*class=""workshopTags""[^>]*>([\s\S]*?)</div>", RegexOptions.IgnoreCase)]
    private static partial Regex TagsBlockRegex();

    [GeneratedRegex(@"<a [^>]*>([^<]+)</a>", RegexOptions.IgnoreCase)]
    private static partial Regex TagLinkRegex();

    [GeneratedRegex(@"myworkshopfiles/\?appid=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex AppIdRegex();

    private static string? ParseTitle(string html)
    {
        var match = TitleRegex().Match(html);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value.Trim()) : null;
    }

    private static string? ParseDescriptionHtml(string html)
    {
        var markerIndex = html.IndexOf("id=\"highlightContent\"", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var contentStart = html.IndexOf('>', markerIndex);
        if (contentStart < 0)
        {
            return null;
        }

        var contentEnd = FindMatchingDivEnd(html, contentStart + 1);
        return contentEnd < 0 ? null : html[(contentStart + 1)..contentEnd].Trim();
    }

    private static string? ParseAuthor(string html)
    {
        var match = AuthorRegex().Match(html);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value.Trim()) : null;
    }

    private static string? ParsePreviewUrl(string html)
    {
        var tagMatch = PreviewImageTagRegex().Match(html);
        if (!tagMatch.Success)
        {
            return null;
        }

        var srcMatch = ImageSrcRegex().Match(tagMatch.Value);
        return srcMatch.Success
            ? SteamWorkshopHelper.ToFullResolutionImageUrl(WebUtility.HtmlDecode(srcMatch.Groups[1].Value.Trim()))
            : null;
    }

    private static IReadOnlyList<string> ParseScreenshots(string html, string? previewUrl, string? descriptionHtml)
    {
        var screenshots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(previewUrl))
        {
            seen.Add(previewUrl);
        }

        var decodedDescriptionHtml = string.IsNullOrEmpty(descriptionHtml) ? null : WebUtility.HtmlDecode(descriptionHtml);

        var matches = GalleryImageRegex().Matches(html).Cast<Match>();
        foreach (var match in matches)
        {
            if (screenshots.Count >= SteamWorkshopConstants.MaxGalleryScreenshots)
            {
                break;
            }

            var raw = WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
            if (decodedDescriptionHtml != null &&
                !string.IsNullOrWhiteSpace(raw) &&
                decodedDescriptionHtml.Contains(raw, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = SteamWorkshopHelper.ToFullResolutionImageUrl(raw);
            if (string.IsNullOrWhiteSpace(url) || !IsGalleryImageUrl(url) || !seen.Add(url))
            {
                continue;
            }

            screenshots.Add(url);
        }

        return screenshots;
    }

    private static bool IsGalleryImageUrl(string url)
    {
        // Gallery images are served from Steam user-content hosts. Restricting to
        // those hosts keeps Steam chrome (header logos, backgrounds on steamstatic
        // hosts) and creator avatars out of the screenshot strip.
        return url.Contains(SteamWorkshopConstants.GalleryImageHostFragment, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ParseCreatorAvatarUrl(string html)
    {
        var blockIndex = html.IndexOf("creatorsBlock", StringComparison.OrdinalIgnoreCase);
        if (blockIndex < 0)
        {
            blockIndex = html.IndexOf(SteamWorkshopConstants.CreatorAvatarBlockMarker, StringComparison.OrdinalIgnoreCase);
        }

        if (blockIndex < 0)
        {
            blockIndex = html.IndexOf("friendBlockContent", StringComparison.OrdinalIgnoreCase);
        }

        if (blockIndex < 0)
        {
            blockIndex = html.IndexOf("playerAvatar", StringComparison.OrdinalIgnoreCase);
        }

        if (blockIndex < 0)
        {
            return null;
        }

        var windowEnd = Math.Min(html.Length, blockIndex + SteamWorkshopConstants.CreatorAvatarSearchWindow);
        var match = ImageSrcRegex().Match(html, blockIndex, windowEnd - blockIndex);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value.Trim()) : null;
    }

    private static int? ParseAppId(string html)
    {
        var match = AppIdRegex().Match(html);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var appId)
            ? appId
            : null;
    }

    private static Dictionary<string, string> ParseDetailsStats(string html)
    {
        var labels = StatLabelRegex().Matches(html).Cast<Match>().Select(m => m.Groups[1].Value.Trim()).ToList();
        var values = StatValueRegex().Matches(html).Cast<Match>().Select(m => m.Groups[1].Value.Trim()).ToList();
        var stats = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (labels.Count != values.Count)
        {
            return stats;
        }

        for (var i = 0; i < labels.Count; i++)
        {
            stats[labels[i]] = values[i];
        }

        return stats;
    }

    private static int FindMatchingDivEnd(string html, int startIndex)
    {
        var depth = 1;
        var index = startIndex;
        while (depth > 0)
        {
            var openIndex = IndexOfDivOpen(html, index);
            var closeIndex = html.IndexOf("</div", index, StringComparison.OrdinalIgnoreCase);
            if (closeIndex < 0)
            {
                return -1;
            }

            if (openIndex >= 0 && openIndex < closeIndex)
            {
                depth++;
                index = openIndex + 4;
            }
            else
            {
                depth--;
                if (depth == 0)
                {
                    return closeIndex;
                }

                index = closeIndex + 5;
            }
        }

        return -1;
    }

    private static int IndexOfDivOpen(string html, int startIndex)
    {
        var index = startIndex;
        while (true)
        {
            index = html.IndexOf("<div", index, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return -1;
            }

            var after = index + 4;
            if (after >= html.Length || html[after] is ' ' or '\t' or '\r' or '\n' or '>' or '/')
            {
                return index;
            }

            index = after;
        }
    }

    private static IReadOnlyList<string> ParseTags(string html)
    {
        var tags = new List<string>();
        var blocks = TagsBlockRegex().Matches(html).Cast<Match>();
        foreach (var block in blocks)
        {
            var links = TagLinkRegex().Matches(block.Groups[1].Value).Cast<Match>();
            foreach (var link in links)
            {
                var tag = WebUtility.HtmlDecode(link.Groups[1].Value.Trim());
                if (!string.IsNullOrWhiteSpace(tag) && !tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    tags.Add(tag);
                }
            }
        }

        return tags;
    }
}
