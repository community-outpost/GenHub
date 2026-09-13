using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;

namespace GenHub.Features.Content.Services.Helpers;

/// <summary>
/// Helper utilities for building CNC Labs URLs and parsing document fragments.
/// </summary>
public static partial class CNCLabsHelper
{
    /// <summary>
    /// Tries to extract a numeric content identifier from a CNC Labs details URL.
    /// The method first ensures the URL is absolute and that its path contains the expected
    /// marker (e.g., <c>details</c>). It then checks for an <c>id</c> query parameter or a
    /// path segment following <c>details/</c>.
    /// </summary>
    /// <param name="url">The URL string to inspect.</param>
    /// <param name="pathMarker">A substring expected in the absolute path (e.g., "details").</param>
    /// <param name="id">When this method returns, contains the parsed ID if successful; otherwise, <c>0</c>.</param>
    /// <returns><see langword="true"/> if an <c>id</c> value was found and parsed; otherwise, <see langword="false"/>.</returns>
    public static bool TryExtractMapIdFromUrl(string? url, string pathMarker, out int id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!uri.AbsolutePath.Contains(pathMarker, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var idStr = query[CNCLabsConstants.QueryStringIdParameter];
        if (int.TryParse(idStr, out id))
        {
            return true;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "details", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(segments[i + 1], out id))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds a CNC Labs list URL based on the structured <paramref name="query"/> (game, content type, etc.).
    /// The result is a fully composed absolute URL including applicable query parameters.
    /// </summary>
    /// <param name="query">Search input containing <see cref="ContentSearchQuery.TargetGame"/>, <see cref="ContentSearchQuery.ContentType"/> and optional filters.</param>
    /// <returns>A fully-formed absolute URL string pointing to the relevant CNC Labs listing page.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="UriFormatException">Thrown if the resulting URL is not a valid absolute URI.</exception>
    public static string BuildSearchUrl(ContentSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Determine base URL based on content type
        var baseUrl = DetermineBaseUrl(query.ContentType);
        var sb = new StringBuilder(baseUrl);
        sb.Append(ResolveListingPath(query.TargetGame, query.ContentType));

        // Query parameters
        var pageIndex = (query.Page.HasValue && query.Page.Value > 0) ? query.Page.Value : 1;

        sb.Append('?')
          .Append(CNCLabsConstants.PageQueryParam)
          .Append('=')
          .Append(pageIndex.ToString(CultureInfo.InvariantCulture));

        // Only add tags/players filters for Maps & Missions as other sections don't support them
        if (SupportsFilters(query.ContentType))
        {
            if (query.NumberOfPlayers.HasValue && query.NumberOfPlayers.Value > 0)
            {
                sb.Append('&')
                  .Append(CNCLabsConstants.PlayersQueryParam)
                  .Append('=')
                  .Append(query.NumberOfPlayers.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (query.CNCLabsMapTags is { Count: > 0 })
            {
                sb.Append('&')
                  .Append(CNCLabsConstants.TagsQueryParam)
                  .Append('=')
                  .Append(string.Join(CNCLabsConstants.CommaSeparator, query.CNCLabsMapTags));
            }
        }

        if (!string.IsNullOrEmpty(query.Sort))
        {
            sb.Append('&')
              .Append(CNCLabsConstants.Sort)
              .Append('=')
              .Append(query.Sort);
        }

        var url = sb.ToString();

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            throw new UriFormatException(CNCLabsConstants.InvalidAbsoluteUri);
        }

        return url;
    }

    /// <summary>
    /// Returns the next sibling text node that has non-whitespace content for the specified <paramref name="node"/>.
    /// Useful when labels like <c>&lt;strong&gt;Author:&lt;/strong&gt;</c> are followed by text nodes.
    /// </summary>
    /// <param name="node">The starting node whose siblings are examined.</param>
    /// <returns>The trimmed text content of the next non-empty text sibling, or <see langword="null"/> if none exist.</returns>
    public static string? GetNextNonEmptyTextSibling(INode? node)
    {
        for (var n = node?.NextSibling; n is not null; n = n.NextSibling)
        {
            // Text node
            if (n is IText t)
            {
                var text = t.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            // If another element appears (like <br>), keep scanning in case of whitespace-only nodes.
        }

        return null;
    }

    /// <summary>
    /// Converts a small HTML description fragment into normalized plain text:
    /// - Converts &lt;br&gt; tags to line breaks
    /// - HTML-decodes entities (e.g., &amp;gt; → &gt;, &amp;nbsp; → non-breaking space)
    /// - Replaces non-breaking spaces with regular spaces
    /// - Normalizes newlines and trims messy whitespace
    /// - Collapses excessive blank lines.
    /// </summary>
    /// <param name="htmlFragment">The HTML fragment to normalize (e.g., the InnerHtml of the description span).</param>
    /// <returns>Readable, normalized plain text using <see cref="Environment.NewLine"/> line separators.</returns>
    public static string NormalizeHtmlDescription(string? htmlFragment)
    {
        return HtmlTextHelper.NormalizeHtml(htmlFragment);
    }

    /// <summary>
    /// Extracts and canonicalizes the category from the breadcrumb navigation of a document.
    /// </summary>
    /// <param name="document">
    /// The <see cref="IDocument"/> instance to parse.
    /// </param>
    /// <returns>
    /// A tuple of (<see cref="GameType"/>, <see cref="ContentType"/>).
    /// </returns>
    public static (GameType GameType, ContentType ContentType) ExtractBreadcrumbCategory(IDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var fromNav = ExtractFromNavElements(document);
        if (fromNav.ContentType != ContentType.UnknownContentType)
        {
            return fromNav;
        }

        var fromTitle = ExtractFromTitle(document.Title);
        if (fromTitle.ContentType != ContentType.UnknownContentType)
        {
            return fromTitle;
        }

        return ExtractFromLegacyHeader(document);
    }

    /// <summary>
    /// Canonicalizes raw category text into (<see cref="GameType"/>, <see cref="ContentType"/>).
    /// </summary>
    /// <param name="raw">The raw category string.</param>
    /// <returns>The resolved game and content type.</returns>
    public static (GameType GameType, ContentType ContentType) ParseCategoryString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (GameType.Unknown, ContentType.UnknownContentType);
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "generals maps" => (GameType.Generals, ContentType.Map),
            "generals missions" => (GameType.Generals, ContentType.Mission),
            "zero hour maps" => (GameType.ZeroHour, ContentType.Map),
            "zero hour missions" => (GameType.ZeroHour, ContentType.Mission),
            "generals winamp skins" => (GameType.Generals, ContentType.Skin),
            "modding and mapping" => (GameType.Unknown, ContentType.ModdingTool),
            "mods (generals and zero hour)" => (GameType.Unknown, ContentType.Mod),
            "patches (generals and zero hour)" => (GameType.Unknown, ContentType.Patch),
            "screensavers" => (GameType.Unknown, ContentType.Screensaver),
            "videos (generals and zero hour)" => (GameType.Unknown, ContentType.Video),
            "zero hour replays" => (GameType.ZeroHour, ContentType.Replay),
            _ => (GameType.Unknown, ContentType.UnknownContentType),
        };
    }

    /// <summary>
    /// Resolves the correct list page path for the given game and content type.
    /// </summary>
    /// <param name="game">The target game.</param>
    /// <param name="contentType">The type of content to list.</param>
    /// <returns>A relative page path (e.g., <c>maps.aspx</c>).</returns>
    private static string ResolveListingPath(GameType? game, ContentType? contentType)
    {
        return (game, contentType) switch
        {
            // Maps & Missions (with filters)
            (GameType.Generals, ContentType.Map) => CNCLabsConstants.MapsPagePath,
            (GameType.Generals, ContentType.Mission) => CNCLabsConstants.MissionsPagePath,
            (GameType.ZeroHour, ContentType.Map) => CNCLabsConstants.ZeroHourMapsPagePath,
            (GameType.ZeroHour, ContentType.Mission) => CNCLabsConstants.ZeroHourMissionsPagePath,

            // Skins (Generals only, no filters)
            (GameType.Generals, ContentType.Skin) => CNCLabsConstants.WinampSkinsPagePath,

            // Modding tools (shared, no filters)
            (_, ContentType.ModdingTool) => CNCLabsConstants.ModdingMappingPagePath,

            // Mods (shared between Generals & ZH, no filters)
            (GameType.Generals, ContentType.Mod) => CNCLabsConstants.DownloadsPagePath,
            (GameType.ZeroHour, ContentType.Mod) => CNCLabsConstants.DownloadsPagePath,
            (_, ContentType.Mod) => CNCLabsConstants.DownloadsPagePath,

            // Patches (shared, no filters)
            (GameType.Generals, ContentType.Patch) => CNCLabsConstants.PatchesPagePath,
            (GameType.ZeroHour, ContentType.Patch) => CNCLabsConstants.PatchesPagePath,
            (_, ContentType.Patch) => CNCLabsConstants.PatchesPagePath,

            // Screensavers (shared, no filters)
            (_, ContentType.Screensaver) => CNCLabsConstants.ScreensaversPagePath,

            // Videos (shared, no filters)
            (GameType.Generals, ContentType.Video) => CNCLabsConstants.VideosPagePath,
            (GameType.ZeroHour, ContentType.Video) => CNCLabsConstants.VideosPagePath,
            (_, ContentType.Video) => CNCLabsConstants.VideosPagePath,

            // Replays (Zero Hour only, no filters)
            (GameType.ZeroHour, ContentType.Replay) => CNCLabsConstants.ZeroHourReplaysPagePath,

            // Default fallback
            _ => CNCLabsConstants.MapsPagePath,
        };
    }

    private static (GameType GameType, ContentType ContentType) ExtractFromNavElements(IDocument document)
    {
        var items = document.QuerySelectorAll(CNCLabsConstants.BreadcrumbItemsSelector);
        foreach (var item in items)
        {
            var category = ParseCategoryString(item.TextContent);
            if (category.ContentType != ContentType.UnknownContentType)
            {
                return category;
            }

            var href = item.QuerySelector("a")?.GetAttribute("href");
            if (!string.IsNullOrWhiteSpace(href))
            {
                var fromHref = ParseCategoryFromHref(href);
                if (fromHref.ContentType != ContentType.UnknownContentType)
                {
                    return fromHref;
                }
            }
        }

        return (GameType.Unknown, ContentType.UnknownContentType);
    }

    private static (GameType GameType, ContentType ContentType) ExtractFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return (GameType.Unknown, ContentType.UnknownContentType);
        }

        var parts = title.Split('-');
        foreach (var part in parts)
        {
            var category = ParseCategoryString(part);
            if (category.ContentType != ContentType.UnknownContentType)
            {
                return category;
            }
        }

        return (GameType.Unknown, ContentType.UnknownContentType);
    }

    private static (GameType GameType, ContentType ContentType) ExtractFromLegacyHeader(IDocument document)
    {
        var header = document.QuerySelector(CNCLabsConstants.BreadcrumbHeaderSelector);
        if (header == null)
        {
            return (GameType.Unknown, ContentType.UnknownContentType);
        }

        var parts = header.TextContent
            .Split(CNCLabsConstants.BreadcrumbSeparator)
            .Select(s => s.Replace('\u00A0', ' ').Trim())
            .Where(s => s.Length > 0)
            .ToArray();

        if (parts.Length <= CNCLabsConstants.BreadcrumbCategoryIndex)
        {
            return (GameType.Unknown, ContentType.UnknownContentType);
        }

        return ParseCategoryString(parts[CNCLabsConstants.BreadcrumbCategoryIndex]);
    }

    private static (GameType GameType, ContentType ContentType) ParseCategoryFromHref(string href)
    {
        var lower = href.ToLowerInvariant();
        if (lower.Contains("zerohour-maps") || lower.Contains("zerohour_maps"))
        {
            return (GameType.ZeroHour, ContentType.Map);
        }

        if (lower.Contains("generals-maps") || lower.Contains("generals_maps") || (lower.Contains("maps/generals") && !lower.Contains("zerohour")))
        {
            return (GameType.Generals, ContentType.Map);
        }

        if (lower.Contains("zerohour-missions") || lower.Contains("zerohour_missions"))
        {
            return (GameType.ZeroHour, ContentType.Mission);
        }

        if (lower.Contains("generals-missions") || lower.Contains("generals_missions"))
        {
            return (GameType.Generals, ContentType.Mission);
        }

        return (GameType.Unknown, ContentType.UnknownContentType);
    }

    private static string DetermineBaseUrl(ContentType? contentType)
    {
        return contentType switch
        {
            ContentType.Mod => CNCLabsConstants.SearchModsUrlBase,
            ContentType.Patch or ContentType.Skin or ContentType.Screensaver or ContentType.Video or ContentType.ModdingTool => CNCLabsConstants.SearchDownloadsUrlBase,
            _ => CNCLabsConstants.SearchMapsUrlBase,
        };
    }

    private static bool SupportsFilters(ContentType? contentType)
    {
        // Only Maps and Missions support tag/player filtering
        return contentType == ContentType.Map || contentType == ContentType.Mission;
    }
}
