using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Parses Steam Workshop browse pages into structured items.
/// The browse UI is server-rendered React with hashed CSS classes, so parsing keys
/// off stable anchors: filedetails links, preview images, and author links.
/// </summary>
public static partial class SteamWorkshopBrowseParser
{
    /// <summary>
    /// Parses workshop browse HTML into items and a total match count.
    /// </summary>
    /// <param name="html">The raw browse page HTML.</param>
    /// <returns>The parsed items and total match count, when present.</returns>
    public static (IReadOnlyList<SteamWorkshopBrowseItem> Items, int? TotalMatches) Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return ([], null);
        }

        var (fileIdToCreator, creatorAvatars) = ParseEmbeddedCreatorData(html);
        var items = new List<SteamWorkshopBrowseItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var matches = FileDetailsLinkRegex().Matches(html).Cast<Match>().ToList();

        for (var i = 0; i < matches.Count; i++)
        {
            var id = matches[i].Groups[1].Value;
            if (!seen.Add(id))
            {
                continue;
            }

            var spanEnd = FindSpanEnd(matches, i, html.Length);
            var span = html.Substring(matches[i].Index, spanEnd - matches[i].Index);
            var item = ParseItemSpan(id, span, fileIdToCreator, creatorAvatars);
            if (item != null)
            {
                items.Add(item);
            }
        }

        return (items, ParseTotalMatches(html));
    }

    /// <summary>
    /// Determines whether more items exist beyond the current page.
    /// </summary>
    /// <param name="itemsOnPage">The number of items parsed on the current page.</param>
    /// <param name="page">The current 1-based page index.</param>
    /// <param name="totalMatches">The total match count, when the page reported one.</param>
    /// <param name="pageSize">The number of items per browse page.</param>
    /// <returns>True when more items are likely available.</returns>
    public static bool HasMoreItems(int itemsOnPage, int page, int? totalMatches, int pageSize)
    {
        if (totalMatches.HasValue)
        {
            return (long)page * pageSize < totalMatches.Value;
        }

        return itemsOnPage >= pageSize;
    }

    [GeneratedRegex(@"filedetails/\?id=(\d+)")]
    private static partial Regex FileDetailsLinkRegex();

    [GeneratedRegex(@"<img\b[^>]*?\bsrc=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex PreviewImageRegex();

    [GeneratedRegex(@"(?:href=""(?<url>https?://steamcommunity\.com/(?:profiles|id)/[^""/?#]+)/myworkshopfiles/\?appid=\d+[^""]*""[^>]*>|myworkshopfiles/\?appid=\d+[^""]*""[^>]*>)By\s+(?<name>[^<]+)</a>", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorLinkRegex();

    [GeneratedRegex(@"([\d,]+)\s+entries matching filters", RegexOptions.IgnoreCase)]
    private static partial Regex TotalMatchesRegex();

    [GeneratedRegex(@"\\*""publishedfileid\\*"":\s*\\*""(?<fileId>\d+)\\*""[^}]*?\\*""creator\\*"":\s*\\*""(?<creatorId>\d+)\\*""")]
    private static partial Regex FileIdToCreatorRegex();

    [GeneratedRegex(@"\\*""steamid\\*"":\s*\\*""(?<steamId>\d+)\\*""[^}]*?\\*""sha_digest_avatar\\*"":\s*\{(?:\s*\\*""_t\\*"":\s*0,)?\s*\\*""v\\*"":\s*\[(?<bytes>[0-9,\s]+)\]\}")]
    private static partial Regex SteamIdToAvatarDigestRegex();

    private static (Dictionary<string, string> FileIdToCreator, Dictionary<string, string> CreatorAvatars) ParseEmbeddedCreatorData(string html)
    {
        var fileIdToCreator = new Dictionary<string, string>(StringComparer.Ordinal);
        var creatorAvatars = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var groups in FileIdToCreatorRegex().Matches(html).Select(match => match.Groups))
        {
            var fileId = groups["fileId"].Value;
            var creatorId = groups["creatorId"].Value;
            if (!string.IsNullOrEmpty(fileId) && !string.IsNullOrEmpty(creatorId))
            {
                fileIdToCreator[fileId] = creatorId;
            }
        }

        foreach (var groups in SteamIdToAvatarDigestRegex().Matches(html).Select(match => match.Groups))
        {
            var steamId = groups["steamId"].Value;
            var bytesStr = groups["bytes"].Value;
            if (string.IsNullOrEmpty(steamId) || string.IsNullOrEmpty(bytesStr) || creatorAvatars.ContainsKey(steamId))
            {
                continue;
            }

            var avatarUrl = ConvertDigestToAvatarUrl(bytesStr);
            if (!string.IsNullOrEmpty(avatarUrl))
            {
                creatorAvatars[steamId] = avatarUrl;
            }
        }

        return (fileIdToCreator, creatorAvatars);
    }

    private static string? ConvertDigestToAvatarUrl(string bytesStr)
    {
        var parts = bytesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 20)
        {
            return null;
        }

        var bytes = new byte[20];
        var allZeros = true;
        for (var i = 0; i < 20; i++)
        {
            if (!byte.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var b))
            {
                return null;
            }

            bytes[i] = b;
            if (b != 0)
            {
                allZeros = false;
            }
        }

        if (allZeros)
        {
            return SteamWorkshopConstants.DefaultAvatarUrl;
        }

        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return string.Format(CultureInfo.InvariantCulture, SteamWorkshopConstants.AvatarUrlFormat, hex);
    }

    private static int FindSpanEnd(List<Match> matches, int index, int htmlLength)
    {
        for (var j = index + 1; j < matches.Count; j++)
        {
            if (!string.Equals(matches[j].Groups[1].Value, matches[index].Groups[1].Value, StringComparison.Ordinal))
            {
                return matches[j].Index;
            }
        }

        return htmlLength;
    }

    private static SteamWorkshopBrowseItem? ParseItemSpan(
        string id,
        string span,
        IReadOnlyDictionary<string, string> fileIdToCreator,
        IReadOnlyDictionary<string, string> creatorAvatars)
    {
        var title = ParseTitle(id, span);
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var (authorName, authorProfileUrl, creatorId) = ParseAuthorInfo(span, id, fileIdToCreator);

        string? creatorAvatarUrl = null;
        if (!string.IsNullOrEmpty(creatorId) && creatorAvatars.TryGetValue(creatorId, out var directAvatar))
        {
            creatorAvatarUrl = directAvatar;
        }

        return new SteamWorkshopBrowseItem(
            id,
            WebUtility.HtmlDecode(title.Trim()),
            authorName,
            ParsePreviewUrl(id, span),
            SteamWorkshopHelper.BuildFileDetailsUrl(id),
            authorProfileUrl,
            creatorId,
            creatorAvatarUrl);
    }

    private static (string? AuthorName, string? AuthorProfileUrl, string? CreatorId) ParseAuthorInfo(
        string span,
        string id,
        IReadOnlyDictionary<string, string> fileIdToCreator)
    {
        var (authorName, authorProfileUrl, creatorId) = ExtractAuthorFromAnchor(span);

        if (string.IsNullOrEmpty(creatorId) && fileIdToCreator.TryGetValue(id, out var embeddedCreatorId))
        {
            creatorId = embeddedCreatorId;
            authorProfileUrl ??= $"{SteamWorkshopConstants.CreatorProfileBaseUrl}{creatorId}";
        }

        return (authorName, authorProfileUrl, creatorId);
    }

    private static (string? AuthorName, string? AuthorProfileUrl, string? CreatorId) ExtractAuthorFromAnchor(string span)
    {
        var authorMatch = AuthorLinkRegex().Match(span);
        if (!authorMatch.Success)
        {
            return (null, null, null);
        }

        string? authorName = null;
        var nameGroup = authorMatch.Groups["name"];
        if (nameGroup.Success && !string.IsNullOrWhiteSpace(nameGroup.Value))
        {
            authorName = WebUtility.HtmlDecode(nameGroup.Value.Trim());
        }

        string? authorProfileUrl = null;
        string? creatorId = null;
        var urlGroup = authorMatch.Groups["url"];
        if (urlGroup.Success && !string.IsNullOrWhiteSpace(urlGroup.Value))
        {
            authorProfileUrl = urlGroup.Value.Trim();
            creatorId = TryExtractSteamIdFromProfileUrl(authorProfileUrl);
        }

        return (authorName, authorProfileUrl, creatorId);
    }

    private static string? TryExtractSteamIdFromProfileUrl(string profileUrl)
    {
        var profilesIdx = profileUrl.IndexOf("/profiles/", StringComparison.OrdinalIgnoreCase);
        if (profilesIdx < 0)
        {
            return null;
        }

        var candidateId = profileUrl[(profilesIdx + "/profiles/".Length)..].Trim('/');
        return ulong.TryParse(candidateId, NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? candidateId
            : null;
    }

    private static string? ParseTitle(string id, string span)
    {
        foreach (var (contentStart, contentEnd) in EnumerateItemAnchors(id, span))
        {
            var title = span[contentStart..contentEnd].Trim();
            if (title.Length > 0 && !title.Contains('<'))
            {
                return title;
            }
        }

        return null;
    }

    private static string? ParsePreviewUrl(string id, string span)
    {
        foreach (var (contentStart, contentEnd) in EnumerateItemAnchors(id, span))
        {
            var image = PreviewImageRegex().Match(span, contentStart, contentEnd - contentStart);
            if (image.Success)
            {
                return WebUtility.HtmlDecode(image.Groups[1].Value.Trim());
            }
        }

        return null;
    }

    private static IEnumerable<(int ContentStart, int ContentEnd)> EnumerateItemAnchors(string id, string span)
    {
        var anchorPrefix = $"filedetails/?id={id}\"";
        var searchFrom = 0;
        while (true)
        {
            var linkIndex = span.IndexOf(anchorPrefix, searchFrom, StringComparison.Ordinal);
            if (linkIndex < 0)
            {
                yield break;
            }

            var tagEnd = span.IndexOf('>', linkIndex + anchorPrefix.Length);
            if (tagEnd < 0)
            {
                yield break;
            }

            var anchorEnd = span.IndexOf("</a>", tagEnd, StringComparison.Ordinal);
            if (anchorEnd <= tagEnd)
            {
                searchFrom = tagEnd + 1;
                continue;
            }

            yield return (tagEnd + 1, anchorEnd);
            searchFrom = anchorEnd + 4;
        }
    }

    private static int? ParseTotalMatches(string html)
    {
        var match = TotalMatchesRegex().Match(html);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups[1].Value.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.None, CultureInfo.InvariantCulture, out var total)
            ? total
            : null;
    }
}
