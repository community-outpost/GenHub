using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.Content;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Helper utilities for building Steam Workshop URLs and mapping query options.
/// </summary>
public static class SteamWorkshopHelper
{
    private const string EnabledFlagSuffix = "=true";

    /// <summary>
    /// Resolves the workshop AppID for the requested game.
    /// </summary>
    /// <param name="game">The target game.</param>
    /// <returns>The Generals or Zero Hour workshop AppID.</returns>
    public static int AppIdForGame(GameType? game)
    {
        return game == GameType.Generals
            ? SteamWorkshopConstants.GeneralsAppId
            : SteamWorkshopConstants.ZeroHourAppId;
    }

    /// <summary>
    /// Resolves the target game for a workshop AppID.
    /// </summary>
    /// <param name="appId">The workshop AppID.</param>
    /// <returns>The matching game type, or Zero Hour when unknown.</returns>
    public static GameType GameForAppId(int appId)
    {
        return appId == SteamWorkshopConstants.GeneralsAppId
            ? GameType.Generals
            : GameType.ZeroHour;
    }

    /// <summary>
    /// Resolves the browse sort value for a search query. Free-text searches use
    /// text search unless the user picked an explicit sort order.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <returns>A workshop browse sort value.</returns>
    public static string ResolveBrowseSort(ContentSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!string.IsNullOrWhiteSpace(query.SteamWorkshopSort))
        {
            return NormalizeSort(query.SteamWorkshopSort);
        }

        return string.IsNullOrWhiteSpace(query.SearchTerm)
            ? SteamWorkshopConstants.DefaultSort
            : SteamWorkshopConstants.SortTextSearch;
    }

    /// <summary>
    /// Builds a workshop browse page URL for the given query.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="pageOverride">The optional page number override for browsing.</param>
    /// <returns>A fully-formed absolute browse URL.</returns>
    public static string BuildBrowseUrl(ContentSearchQuery query, int? pageOverride = null)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = pageOverride ?? (query.Page is > 0 ? query.Page.Value : 1);
        var sort = ResolveBrowseSort(query);
        var builder = new StringBuilder(SteamWorkshopConstants.BrowseBaseUrl);
        builder.Append('?').Append(SteamWorkshopConstants.AppIdQueryParam).Append('=').Append(AppIdForGame(query.TargetGame).ToString(CultureInfo.InvariantCulture));
        builder.Append('&').Append(SteamWorkshopConstants.SearchTextQueryParam).Append('=').Append(Uri.EscapeDataString(query.SearchTerm ?? string.Empty));
        builder.Append('&').Append(SteamWorkshopConstants.ChildFileIdQueryParam).Append('=').Append('0');
        builder.Append('&').Append(SteamWorkshopConstants.BrowseSortQueryParam).Append('=').Append(sort);
        builder.Append('&').Append(SteamWorkshopConstants.SectionQueryParam).Append('=').Append(SteamWorkshopConstants.BrowseSection);
        builder.Append('&').Append(SteamWorkshopConstants.ActualSortQueryParam).Append('=').Append(sort);
        builder.Append('&').Append(SteamWorkshopConstants.PageQueryParam).Append('=').Append(page.ToString(CultureInfo.InvariantCulture));
        foreach (var tag in GetNormalizedRequiredTags(query))
        {
            builder.Append('&').Append(SteamWorkshopConstants.RequiredTagsQueryParam).Append('=').Append(Uri.EscapeDataString(tag));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds a workshop file details page URL for a published file ID.
    /// </summary>
    /// <param name="publishedFileId">The published file ID.</param>
    /// <returns>A fully-formed absolute details URL.</returns>
    public static string BuildFileDetailsUrl(string publishedFileId)
    {
        return $"{SteamWorkshopConstants.FileDetailsBaseUrl}?{SteamWorkshopConstants.FileIdQueryParam}={Uri.EscapeDataString(publishedFileId)}";
    }

    /// <summary>
    /// Tries to extract a published file ID from a workshop details URL.
    /// </summary>
    /// <param name="url">The URL string to inspect.</param>
    /// <param name="publishedFileId">When this method returns, contains the parsed ID if successful.</param>
    /// <returns>True if an ID value was found and parsed; otherwise, false.</returns>
    public static bool TryExtractPublishedFileId(string? url, out string publishedFileId)
    {
        publishedFileId = string.Empty;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var id = query[SteamWorkshopConstants.FileIdQueryParam];
        if (string.IsNullOrWhiteSpace(id) || !ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        publishedFileId = id;
        return true;
    }

    /// <summary>
    /// Tries to extract a published file ID from a workshop content ID (steamworkshop.{id}).
    /// </summary>
    /// <param name="contentId">The content ID to inspect.</param>
    /// <param name="publishedFileId">When this method returns, contains the parsed ID if successful.</param>
    /// <returns>True if an ID value was found and parsed; otherwise, false.</returns>
    public static bool TryExtractPublishedFileIdFromContentId(string? contentId, out string publishedFileId)
    {
        publishedFileId = string.Empty;
        var prefix = $"{SteamWorkshopConstants.PublisherPrefix}.";
        if (string.IsNullOrWhiteSpace(contentId) || !contentId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = contentId[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(candidate) || !ulong.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        publishedFileId = candidate;
        return true;
    }

    /// <summary>
    /// Infers a content type from workshop item title and description text.
    /// Workshop feeds mix maps, missions, and packs without reliable tags.
    /// </summary>
    /// <param name="title">The item title.</param>
    /// <param name="description">The item description.</param>
    /// <returns>The inferred content type and whether it is a guess.</returns>
    public static (ContentType Type, bool IsInferred) InferContentType(string? title, string? description)
    {
        return InferContentType(title, description, tags: null);
    }

    /// <summary>
    /// Infers a content type from workshop item title, description text, and tags.
    /// Explicit workshop tags (such as Co-op) take precedence over title keywords.
    /// </summary>
    /// <param name="title">The item title.</param>
    /// <param name="description">The item description.</param>
    /// <param name="tags">The workshop tags, when available.</param>
    /// <returns>The inferred content type and whether it is a guess.</returns>
    public static (ContentType Type, bool IsInferred) InferContentType(string? title, string? description, IEnumerable<string>? tags)
    {
        if (tags != null)
        {
            var normalizedTags = tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(NormalizeWorkshopTag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .ToList();
            if (normalizedTags.Any(tag => SteamWorkshopConstants.MissionTags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            {
                return (ContentType.Mission, true);
            }
        }

        return InferFromKeywords(title, description);
    }

    /// <summary>
    /// Normalizes a workshop tag to its canonical filter value.
    /// </summary>
    /// <param name="tag">The raw tag value.</param>
    /// <returns>The canonical tag, or the trimmed input when no alias applies.</returns>
    public static string NormalizeWorkshopTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        var trimmed = tag.Trim();
        return SteamWorkshopConstants.WorkshopTagAliases.TryGetValue(trimmed, out var canonical)
            ? canonical
            : trimmed;
    }

    /// <summary>
    /// Promotes workshop tags into player-count badges on a search result.
    /// </summary>
    /// <param name="result">The search result to update.</param>
    /// <param name="tags">The workshop tags.</param>
    public static void ApplyWorkshopTagBadges(ContentSearchResult result, IEnumerable<string>? tags)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (tags == null)
        {
            return;
        }

        foreach (var tag in tags)
        {
            var normalized = NormalizeWorkshopTag(tag);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (SteamWorkshopConstants.WorkshopTagPlayerCounts.TryGetValue(normalized, out var playerCount))
            {
                ContentCardBadgeHelper.ApplyPlayerCount(result, playerCount);
                break;
            }
        }
    }

    /// <summary>
    /// Upgrades a Steam user-content image URL to its full-resolution original by
    /// stripping the CDN resize parameters. Workshop pages embed tiny variants
    /// (116x65 gallery thumbs, 268x268 previews) that blur when upscaled, while
    /// the bare URL serves the original file. Non-Steam URLs pass through untouched.
    /// Browse card thumbnails intentionally keep their sized variants.
    /// </summary>
    /// <param name="url">The image URL to upgrade.</param>
    /// <returns>The full-resolution image URL.</returns>
    public static string? ToFullResolutionImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var trimmed = url.Trim();
        if (!trimmed.Contains(SteamWorkshopConstants.GalleryImageHostFragment, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var queryIndex = trimmed.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0)
        {
            return trimmed;
        }

        var kept = trimmed[(queryIndex + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(parameter => !IsResizeParameter(parameter))
            .ToList();
        return kept.Count == 0
            ? trimmed[..queryIndex]
            : $"{trimmed[..queryIndex]}?{string.Join("&", kept)}";
    }

    /// <summary>
    /// Gets the normalized required tag filters for a search query.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <returns>The distinct canonical tag values.</returns>
    public static IReadOnlyList<string> GetNormalizedRequiredTags(ContentSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return query.SteamWorkshopRequiredTags
            .Select(NormalizeWorkshopTag)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Normalizes an HTML description fragment into plain text.
    /// </summary>
    /// <param name="htmlFragment">The HTML fragment to normalize.</param>
    /// <returns>Readable, normalized plain text.</returns>
    public static string NormalizeHtmlDescription(string? htmlFragment)
    {
        return HtmlTextHelper.NormalizeHtml(htmlFragment);
    }

    /// <summary>
    /// Parses a Steam details-page date such as "26 Aug @ 4:25am" or "26 Aug, 2024 @ 4:25am".
    /// </summary>
    /// <param name="text">The raw date text.</param>
    /// <returns>The parsed date, or null when parsing fails.</returns>
    public static DateTime? ParseDetailsDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Trim();
        string[] formatsWithYear = ["d MMM, yyyy @ h:mmtt", "d MMM, yyyy @ hh:mmtt"];
        if (DateTime.TryParseExact(normalized, formatsWithYear, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var withYear))
        {
            return withYear;
        }

        string[] formatsWithoutYear = ["d MMM @ h:mmtt", "d MMM @ hh:mmtt"];
        if (DateTime.TryParseExact(normalized, formatsWithoutYear, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withoutYear))
        {
            var candidate = new DateTime(DateTime.UtcNow.Year, withoutYear.Month, withoutYear.Day, withoutYear.Hour, withoutYear.Minute, 0, DateTimeKind.Utc);
            return candidate > DateTime.UtcNow ? candidate.AddYears(-1) : candidate;
        }

        return null;
    }

    /// <summary>
    /// Converts a Unix timestamp to a UTC date, or null when out of range.
    /// </summary>
    /// <param name="unixSeconds">Seconds since the Unix epoch.</param>
    /// <returns>The UTC date, or null when invalid.</returns>
    public static DateTime? FromUnixTime(long unixSeconds)
    {
        if (unixSeconds <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Attempts to extract the 64-bit Steam ID from a JWT access or refresh token's subject claim.
    /// </summary>
    /// <param name="token">The JWT token string.</param>
    /// <returns>The 64-bit Steam ID, or 0 if unparseable.</returns>
    public static ulong ExtractSteamIdFromToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return 0UL;
        }

        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return 0UL;
        }

        try
        {
            var base64 = parts[1].Replace('-', '+').Replace('_', '/');
            var padding = (base64.Length % 4) switch
            {
                2 => "==",
                3 => "=",
                _ => string.Empty,
            };
            base64 += padding;

            var bytes = Convert.FromBase64String(base64);
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.TryGetProperty("sub", out var subProp) &&
                ulong.TryParse(subProp.GetString(), CultureInfo.InvariantCulture, out var steamId))
            {
                return steamId;
            }
        }
        catch (FormatException)
        {
            // Fall through on any malformed token or parsing issue.
        }
        catch (JsonException)
        {
            // Fall through on any malformed token or parsing issue.
        }
        catch (InvalidOperationException)
        {
            // Fall through on any malformed token or parsing issue.
        }

        return 0UL;
    }

    private static string NormalizeSort(string sort)
    {
        return sort.Trim().ToLowerInvariant() switch
        {
            SteamWorkshopConstants.SortMostRecent => SteamWorkshopConstants.SortMostRecent,
            SteamWorkshopConstants.SortLastUpdated => SteamWorkshopConstants.SortLastUpdated,
            SteamWorkshopConstants.SortMostSubscribed => SteamWorkshopConstants.SortMostSubscribed,
            SteamWorkshopConstants.SortTopRated => SteamWorkshopConstants.SortTopRated,
            SteamWorkshopConstants.SortTextSearch => SteamWorkshopConstants.SortTextSearch,
            _ => SteamWorkshopConstants.SortTrend,
        };
    }

    private static bool ContainsKeyword(string text, IReadOnlyList<string> keywords)
    {
        return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static (ContentType Type, bool IsInferred) InferFromKeywords(string? title, string? description)
    {
        var text = $"{title} {description}";
        if (ContainsKeyword(text, SteamWorkshopConstants.MapPackKeywords))
        {
            return (ContentType.MapPack, true);
        }

        if (ContainsKeyword(text, SteamWorkshopConstants.MissionKeywords))
        {
            return (ContentType.Mission, true);
        }

        return (ContentType.Map, true);
    }

    private static bool IsResizeParameter(string parameter)
    {
        var name = parameter.Split('=', 2)[0].Trim();
        return SteamWorkshopConstants.SteamImageResizeParams.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}
