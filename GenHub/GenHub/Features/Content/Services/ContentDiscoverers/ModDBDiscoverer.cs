using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.ModDB;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers content from ModDB website using Playwright to bypass WAF/Bot protections.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S3776:Cognitive Complexity of methods should not be too high", Justification = "ModDB discovery handles pagination, challenge detection, and scraping across multiple sections.")]
public partial class ModDBDiscoverer(
    ILogger<ModDBDiscoverer> logger,
    IPlaywrightService playwrightService,
    IHttpClientFactory httpClientFactory) : IContentDiscoverer
{
    private const string UnknownValue = "Unknown";

    /// <inheritdoc />
    public string SourceName => ModDBConstants.DiscovererSourceName;

    /// <inheritdoc />
    public string Description => ModDBConstants.DiscovererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresDiscovery;

    /// <inheritdoc />
    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var gameType = query.TargetGame ?? GameType.ZeroHour;
            logger.LogInformation("Discovering ModDB content for {Game} using Playwright", gameType);

            List<ContentSearchResult> results = [];
            bool hasMoreItems = false;
            bool keepPageOpenForVerification = false;
            bool challengeDetected = false;

            // Determine which sections to search based on query filters
            var sectionsToSearch = DetermineSectionsToSearch(query);

            IPage? page = null;
            try
            {
                page = await playwrightService.CreatePersistentPageAsync(ModDBConstants.BrowserProfileName, cancellationToken);

                if (TryNormalizeModDBUrl(query.SearchTerm, out var directUrl))
                {
                    logger.LogInformation("[ModDB] Search term is a direct ModDB URL: {Url}", directUrl);
                    var (directResults, directHasMore, directKeepOpen, directChallenge) = await DiscoverFromDirectUrlAsync(page, directUrl, gameType, cancellationToken);
                    results.AddRange(directResults);
                    hasMoreItems = directHasMore;
                    keepPageOpenForVerification = directKeepOpen;
                    challengeDetected = directChallenge;
                }
                else
                {
                    foreach (var section in sectionsToSearch)
                    {
                        var (sectionResults, sectionHasMore, sectionKeepOpen, sectionChallenge) = await DiscoverFromSectionAsync(page, section, gameType, query, cancellationToken);
                        results.AddRange(sectionResults);
                        if (sectionHasMore)
                        {
                            hasMoreItems = true;
                        }

                        if (sectionKeepOpen)
                        {
                            keepPageOpenForVerification = true;
                        }

                        if (sectionChallenge)
                        {
                            challengeDetected = true;
                        }
                    }
                }
            }
            finally
            {
                if (page != null)
                {
                    await playwrightService.ClosePersistentPageAsync(page, keepPageOpenForVerification);
                }
            }

            var orderedResults = OrderDiscoveredResults(results, query);

            logger.LogInformation(
                "Discovered {Count} ModDB items across {Sections} sections",
                orderedResults.Count,
                sectionsToSearch.Count);

            return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = orderedResults,
                HasMoreItems = hasMoreItems,
                ChallengeDetected = challengeDetected,
            });
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "ModDB discovery cancelled");
            return OperationResult<ContentDiscoveryResult>.CreateFailure(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover ModDB content");
            return OperationResult<ContentDiscoveryResult>.CreateFailure($"Discovery failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts the ModDB identifier slug from a ModDB URL.
    /// </summary>
    /// <param name="url">The ModDB page or download URL.</param>
    /// <returns>The extracted slug identifier or a generated fallback GUID string.</returns>
    internal static string ExtractModDBIdFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);

            // http://.../mods/contra
            // http://.../downloads/contra-009
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 0 ? segments[^1] : Guid.NewGuid().ToString();
        }
        catch
        {
            return Guid.NewGuid().ToString();
        }
    }

    /// <summary>
    /// Checks if an input search term is a ModDB URL and normalizes it to an absolute URL.
    /// </summary>
    /// <param name="input">The raw search term or URL string.</param>
    /// <param name="normalizedUrl">The normalized absolute ModDB URL if valid.</param>
    /// <returns><c>true</c> if input represents a ModDB URL; otherwise, <c>false</c>.</returns>
    internal static bool TryNormalizeModDBUrl(string? input, [NotNullWhen(true)] out string? normalizedUrl)
    {
        normalizedUrl = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        if (TryNormalizeHttpModDBUrl(trimmed, out normalizedUrl) ||
            TryNormalizeDomainModDBUrl(trimmed, out normalizedUrl))
        {
            return true;
        }

        return TryNormalizeRelativeModDBUrl(trimmed, out normalizedUrl);
    }

    /// <summary>
    /// Determines whether a ModDB URL is a detail page for a single mod, file download, or addon.
    /// </summary>
    /// <param name="url">The absolute ModDB URL.</param>
    /// <returns><see langword="true"/> if the URL represents a single content detail page; otherwise, <see langword="false"/>.</returns>
    internal static bool IsDetailPage(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (IsNonContentUrl(uri.AbsolutePath))
        {
            return false;
        }

        var segments = uri.AbsolutePath.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        // 1. Mod detail: /mods/{slug}
        if (segments.Length == 2 && segments[0].Equals("mods", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 2. Direct download detail: /downloads/{slug}
        if (segments.Length == 2 && segments[0].Equals("downloads", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 3. Direct addon detail: /addons/{slug}
        if (segments.Length == 2 && segments[0].Equals("addons", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 4. Mod-scoped download detail: /mods/{mod-slug}/downloads/{slug}
        if (segments.Length == 4 &&
            segments[0].Equals("mods", StringComparison.OrdinalIgnoreCase) &&
            segments[2].Equals("downloads", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 5. Mod-scoped addon detail: /mods/{mod-slug}/addons/{slug}
        if (segments.Length == 4 &&
            segments[0].Equals("mods", StringComparison.OrdinalIgnoreCase) &&
            segments[2].Equals("addons", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 6. Game-scoped download detail: /games/{game-slug}/downloads/{slug}
        if (segments.Length == 4 &&
            segments[0].Equals("games", StringComparison.OrdinalIgnoreCase) &&
            segments[2].Equals("downloads", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 7. Game-scoped addon detail: /games/{game-slug}/addons/{slug}
        if (segments.Length == 4 &&
            segments[0].Equals("games", StringComparison.OrdinalIgnoreCase) &&
            segments[2].Equals("addons", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 8. Game-scoped mod detail: /games/{game-slug}/mods/{slug}
        if (segments.Length == 4 &&
            segments[0].Equals("games", StringComparison.OrdinalIgnoreCase) &&
            segments[2].Equals("mods", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether a URL or path points to a non-content section such as news, articles, tutorials, media, or profiles.
    /// </summary>
    /// <param name="urlOrPath">The relative or absolute URL string.</param>
    /// <returns><see langword="true"/> if the URL contains non-downloadable sections; otherwise, <see langword="false"/>.</returns>
    internal static bool IsNonContentUrl(string urlOrPath)
    {
        if (string.IsNullOrWhiteSpace(urlOrPath))
        {
            return true;
        }

        var path = urlOrPath;
        if (Uri.TryCreate(urlOrPath, UriKind.Absolute, out var uri))
        {
            path = uri.AbsolutePath;
        }

        var segments = path.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var nonContentKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "news",
            "articles",
            "tutorials",
            "videos",
            "images",
            "reviews",
            "forum",
            "threads",
            "features",
            "blogs",
            "members",
            "company",
            "groups",
            "events",
            "comments",
            "polls",
            "engines",
            "jobs",
            "hardware",
            "register",
            "login",
        };

        return segments.Any(seg => nonContentKeywords.Contains(seg));
    }

    /// <summary>
    /// Checks if a relative or absolute URL extracted from a link points to a valid mod, download, or addon item.
    /// </summary>
    /// <param name="href">The href attribute value.</param>
    /// <returns><see langword="true"/> if the link points to a valid mod, download, or addon; otherwise, <see langword="false"/>.</returns>
    internal static bool IsValidContentDetailUrl(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        if (IsNonContentUrl(href))
        {
            return false;
        }

        var path = href;
        if (Uri.TryCreate(href, UriKind.Absolute, out var uri))
        {
            path = uri.AbsolutePath;
        }

        var segments = path.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        // Mod detail: /mods/{slug}
        if (segments.Length == 2 && segments[0].Equals("mods", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Download detail: /downloads/{slug} or /mods/{mod}/downloads/{slug} or /games/{game}/downloads/{slug}
        if ((segments.Length == 2 && segments[0].Equals("downloads", StringComparison.OrdinalIgnoreCase)) ||
            (segments.Length == 4 && segments[2].Equals("downloads", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Addon detail: /addons/{slug} or /mods/{mod}/addons/{slug} or /games/{game}/addons/{slug}
        if ((segments.Length == 2 && segments[0].Equals("addons", StringComparison.OrdinalIgnoreCase)) ||
            (segments.Length == 4 && segments[2].Equals("addons", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Game mod detail: /games/{game}/mods/{slug}
        if (segments.Length == 4 &&
            segments[0].Equals("games", StringComparison.OrdinalIgnoreCase) &&
            segments[2].Equals("mods", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool TryNormalizeHttpModDBUrl(string trimmed, [NotNullWhen(true)] out string? normalizedUrl)
    {
        normalizedUrl = null;
        if ((trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
            Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            IsModDBHost(uri.Host))
        {
            normalizedUrl = uri.AbsoluteUri;
            return true;
        }

        return false;
    }

    private static bool TryNormalizeDomainModDBUrl(string trimmed, [NotNullWhen(true)] out string? normalizedUrl)
    {
        normalizedUrl = null;
        if ((trimmed.StartsWith("moddb.com", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("www.moddb.com", StringComparison.OrdinalIgnoreCase)) &&
            Uri.TryCreate("https://" + trimmed, UriKind.Absolute, out var uri) &&
            IsModDBHost(uri.Host))
        {
            normalizedUrl = uri.AbsoluteUri;
            return true;
        }

        return false;
    }

    private static bool TryNormalizeRelativeModDBUrl(string trimmed, [NotNullWhen(true)] out string? normalizedUrl)
    {
        normalizedUrl = null;
        var validPrefixes = new[] { "/mods/", "/games/", "/downloads/", "/addons/" };
        if (validPrefixes.Any(p => trimmed.StartsWith(p, StringComparison.OrdinalIgnoreCase)) &&
            Uri.TryCreate(ModDBConstants.BaseUrl.TrimEnd('/') + trimmed, UriKind.Absolute, out var uri))
        {
            normalizedUrl = uri.AbsoluteUri;
            return true;
        }

        return false;
    }

    private static bool IsModDBHost(string host)
    {
        return host.Equals("moddb.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".moddb.com", StringComparison.OrdinalIgnoreCase);
    }

    private static List<ContentSearchResult> OrderDiscoveredResults(List<ContentSearchResult> results, ContentSearchQuery query)
    {
        var sortParam = query.Sort;
        if (string.Equals(sortParam, ModDBConstants.SortNameAsc, StringComparison.OrdinalIgnoreCase) ||
            query.SortOrder == ContentSortField.Name)
        {
            return results.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        if (string.Equals(sortParam, ModDBConstants.SortNameDesc, StringComparison.OrdinalIgnoreCase))
        {
            return results.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        if (string.Equals(sortParam, ModDBConstants.SortDateAsc, StringComparison.OrdinalIgnoreCase))
        {
            return results.OrderBy(r => r.LastUpdated ?? DateTime.MaxValue).ToList();
        }

        // Default: newest first (date-desc)
        return results.OrderByDescending(r => r.LastUpdated ?? DateTime.MinValue).ToList();
    }

    private static List<string> DetermineSectionsToSearch(ContentSearchQuery query)
    {
        // Use explicit section from query if provided
        if (!string.IsNullOrEmpty(query.ModDBSection))
        {
            return [query.ModDBSection];
        }

        // Map ContentType to section if possible
        if (query.ContentType.HasValue)
        {
            return query.ContentType.Value switch
            {
                ContentType.Mod or ContentType.Patch or ContentType.Video => [ModDBConstants.DownloadsSection],
                ContentType.Map or ContentType.Skin or ContentType.LanguagePack => [ModDBConstants.AddonsSection],
                _ => [ModDBConstants.DownloadsSection, ModDBConstants.AddonsSection],
            };
        }

        // Default: browse both sections so the grid has more content.
        return [ModDBConstants.DownloadsSection, ModDBConstants.AddonsSection];
    }

    private static ModDBFilter BuildFilterFromQuery(ContentSearchQuery query)
    {
        var filter = new ModDBFilter
        {
            Keyword = query.SearchTerm,
            Page = query.Page ?? 1,
            Sort = ResolveSort(query),
        };

        // Apply Category filter (for downloads section)
        if (!string.IsNullOrWhiteSpace(query.ModDBCategory))
        {
            filter.Category = query.ModDBCategory;
        }

        // Apply AddonCategory filter (for categoryaddon param)
        if (!string.IsNullOrWhiteSpace(query.ModDBAddonCategory))
        {
            filter.AddonCategory = query.ModDBAddonCategory;
        }

        // Apply License filter
        if (!string.IsNullOrWhiteSpace(query.ModDBLicense))
        {
            filter.Licence = query.ModDBLicense;
        }

        // Apply Timeframe filter
        if (!string.IsNullOrWhiteSpace(query.ModDBTimeframe))
        {
            filter.Timeframe = query.ModDBTimeframe;
        }

        return filter;
    }

    private static string ResolveSort(ContentSearchQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Sort))
        {
            return query.Sort;
        }

        return query.SortOrder switch
        {
            ContentSortField.Name => ModDBConstants.SortNameAsc,
            ContentSortField.DownloadCount => ModDBConstants.SortVisitDesc,
            ContentSortField.Rating => ModDBConstants.SortRatingDesc,
            ContentSortField.DateCreated => ModDBConstants.SortDateDesc,
            _ => ModDBConstants.DefaultSort,
        };
    }

    private static string? MapContentTypeToCategory(ContentType contentType, string section)
    {
        if (section == ModDBConstants.DownloadsSection)
        {
            return contentType switch
            {
                ContentType.Mod => ModDBConstants.CategoryFullVersion,
                ContentType.Patch => ModDBConstants.CategoryPatch,
                ContentType.Video => ModDBConstants.CategoryMovie,
                ContentType.ModdingTool => ModDBConstants.CategoryMappingTool,
                ContentType.LanguagePack => ModDBConstants.CategoryLanguagePack,
                _ => null,
            };
        }

        if (section == "addons")
        {
            return contentType switch
            {
                ContentType.Map => ModDBConstants.AddonMultiplayerMap,
                ContentType.Skin => ModDBConstants.AddonPlayerSkin,
                ContentType.LanguagePack => ModDBConstants.AddonLanguageSounds,
                _ => null,
            };
        }

        return null;
    }

    private static ContentSearchResult? ParseContentItem(AngleSharp.Dom.IElement item, GameType gameType, string section)
    {
        var titleAndUrl = ExtractTitleAndDetailUrl(item);
        if (titleAndUrl == null) return null;

        var (title, detailUrl) = titleAndUrl.Value;
        var author = ExtractAuthor(item);
        var iconUrl = ExtractIconUrl(item);

        var descEl = item.QuerySelector("p, div.summary, span.summary, td.content.name span.summary");
        var description = HtmlTextHelper.NormalizeHtml(descEl?.TextContent?.Trim());

        var categoryEl = item.QuerySelector(".category, .type, span.category");
        var category = categoryEl?.TextContent?.Trim();

        var lastUpdated = ExtractLastUpdatedDate(item);
        var contentType = DetermineContentType(section, category, detailUrl, title);
        var moddbId = ExtractModDBIdFromUrl(detailUrl);
        var prospectiveId = lastUpdated.HasValue && lastUpdated.Value > DateTime.MinValue
            ? ManifestIdGenerator.GeneratePublisherContentId(ModDBConstants.PublisherPrefix, contentType, title, lastUpdated.Value)
            : ManifestIdGenerator.GeneratePublisherContentId(ModDBConstants.PublisherPrefix, contentType, title, 0);

        var result = new ContentSearchResult
        {
            Id = prospectiveId,
            Name = title,
            Description = description,
            AuthorName = author,
            ContentType = contentType,
            TargetGame = gameType,
            ProviderName = ModDBConstants.DiscovererSourceName,
            IconUrl = iconUrl,
            RequiresResolution = true,
            ResolverId = ModDBConstants.ResolverId,
            SourceUrl = detailUrl,
            LastUpdated = lastUpdated,
        };

        result.ResolverMetadata[ModDBConstants.ContentIdMetadataKey] = moddbId;
        result.ResolverMetadata[ModDBConstants.SectionMetadataKey] = section;
        ContentCardBadgeHelper.ApplyCategory(result, category);

        ApplyParentModMetadata(result, detailUrl);

        return result;
    }

    private static (string Title, string DetailUrl)? ExtractTitleAndDetailUrl(AngleSharp.Dom.IElement item)
    {
        var titleLink = item.QuerySelector("h4 a, h3 a, a.title") ?? item.QuerySelector("td.content.name a");
        if (titleLink == null) return null;

        var title = titleLink.TextContent?.Trim();
        var href = titleLink.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href)) return null;

        if (!IsValidContentDetailUrl(href)) return null;

        var detailUrl = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? href
            : $"{ModDBConstants.BaseUrl.TrimEnd('/')}/{(href.StartsWith('/') ? href.TrimStart('/') : href)}";
        return (title, detailUrl);
    }

    private static string ExtractAuthor(AngleSharp.Dom.IElement item)
    {
        var authorLink = item.QuerySelector("a[href*='/members/']") ??
                        item.QuerySelector("span.by a") ??
                        item.QuerySelector("span.author a");
        var author = authorLink?.TextContent?.Trim();
        return string.IsNullOrWhiteSpace(author) ? UnknownValue : author;
    }

    private static string ExtractIconUrl(AngleSharp.Dom.IElement item)
    {
        var img = item.QuerySelector("img.image, img.screenshot, div.image img, td.content.image img") ?? item.QuerySelector("img");
        if (img == null)
        {
            return string.Empty;
        }

        var iconUrl = img.GetAttribute("data-src")
            ?? img.GetAttribute("data-original")
            ?? img.GetAttribute("data-lazy-src")
            ?? img.GetAttribute("src")
            ?? string.Empty;

        if (!string.IsNullOrEmpty(iconUrl) && iconUrl.Contains("blank.gif", StringComparison.OrdinalIgnoreCase))
        {
            iconUrl = img.GetAttribute("data-src")
                ?? img.GetAttribute("data-original")
                ?? img.GetAttribute("data-lazy-src")
                ?? string.Empty;
        }

        if (!string.IsNullOrEmpty(iconUrl))
        {
            if (iconUrl.Contains("blank.gif", StringComparison.OrdinalIgnoreCase))
            {
                iconUrl = string.Empty;
            }
            else if (!iconUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                iconUrl = $"{ModDBConstants.BaseUrl.TrimEnd('/')}/{(iconUrl.StartsWith('/') ? iconUrl.TrimStart('/') : iconUrl)}";
            }
        }

        return iconUrl;
    }

    private static DateTime? ExtractLastUpdatedDate(AngleSharp.Dom.IElement item)
    {
        var dateEl = item.QuerySelector("time[datetime]") ?? item.QuerySelector("abbr.timeago");
        var dateStr = dateEl?.GetAttribute("datetime") ?? dateEl?.GetAttribute("title");
        if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            return parsedDate;
        }

        return null;
    }

    private static void ApplyParentModMetadata(ContentSearchResult result, string detailUrl)
    {
        var isMod = detailUrl.Contains(ModDBConstants.ModsSegment) && !detailUrl.Contains(ModDBConstants.AddonsSegment);
        result.ResolverMetadata[ModDBConstants.IsModMetadataKey] = isMod.ToString();

        if (detailUrl.Contains(ModDBConstants.ModsSegment) && detailUrl.Contains(ModDBConstants.AddonsSegment))
        {
            var modMatch = ParentModUrlRegex().Match(detailUrl);
            if (modMatch.Success)
            {
                result.ResolverMetadata[ModDBConstants.ParentModUrlMetadataKey] = modMatch.Groups[1].Value;
            }
        }
    }

    private static ContentType? InferContentTypeFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        if (title.Contains("patch", StringComparison.OrdinalIgnoreCase) || title.Contains("hotfix", StringComparison.OrdinalIgnoreCase))
        {
            return ContentType.Patch;
        }

        if (title.Contains("tool", StringComparison.OrdinalIgnoreCase) || title.Contains("editor", StringComparison.OrdinalIgnoreCase) || title.Contains("genpatcher", StringComparison.OrdinalIgnoreCase))
        {
            return ContentType.ModdingTool;
        }

        if (title.Contains("multiplayer map", StringComparison.OrdinalIgnoreCase) || title.Contains("singleplayer map", StringComparison.OrdinalIgnoreCase) || title.Contains("map pack", StringComparison.OrdinalIgnoreCase))
        {
            return ContentType.Map;
        }

        return null;
    }

    private static ContentType DetermineContentType(string section, string? category, string url, string? title = null)
    {
        if (!string.IsNullOrEmpty(category))
        {
            var mapped = ModDBCategoryMapper.MapCategoryByName(category);
            if (mapped != ContentType.Addon)
            {
                return mapped;
            }
        }

        var fromTitle = InferContentTypeFromTitle(title);
        if (fromTitle.HasValue)
        {
            return fromTitle.Value;
        }

        var isModUrl = url.Contains(ModDBConstants.ModsSegment, StringComparison.OrdinalIgnoreCase);
        var isAddonUrl = url.Contains(ModDBConstants.AddonsSegment, StringComparison.OrdinalIgnoreCase);
        var isDownloadUrl = url.Contains(ModDBConstants.DownloadsSegment, StringComparison.OrdinalIgnoreCase);

        return section switch
        {
            "mods" when !isDownloadUrl => ContentType.Mod,
            ModDBConstants.DownloadsSection when url.Contains("/maps/", StringComparison.OrdinalIgnoreCase) => ContentType.Map,
            ModDBConstants.DownloadsSection when url.Contains("/tools/", StringComparison.OrdinalIgnoreCase) => ContentType.ModdingTool,
            ModDBConstants.DownloadsSection when url.Contains("/patches/", StringComparison.OrdinalIgnoreCase) => ContentType.Patch,
            ModDBConstants.DownloadsSection when isModUrl && !isAddonUrl => ContentType.Mod,
            ModDBConstants.DownloadsSection => ContentType.Addon,
            ModDBConstants.AddonsSection => url.Contains("/maps/", StringComparison.OrdinalIgnoreCase) ? ContentType.Map : ContentType.Addon,
            _ => isModUrl && !isAddonUrl ? ContentType.Mod : ContentType.Addon,
        };
    }

    /// <summary>
    /// Determines whether a page title indicates a bot-protection interstitial rather than real
    /// ModDB content. Cloudflare's challenge ("Just a moment...") and the legacy "Attention Required"
    /// page never resolve unattended.
    /// </summary>
    /// <param name="title">The browser page title to inspect.</param>
    /// <returns><see langword="true"/> if the title looks like a bot-protection challenge.</returns>
    private static bool IsChallengePage(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Attention Required", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"(https?://[^/]+/mods/[^/]+)")]
    private static partial Regex ParentModUrlRegex();

    private static string BuildSectionUrl(string section, GameType gameType, ModDBFilter filter)
    {
        var baseUrl = gameType == GameType.Generals
            ? $"{ModDBConstants.GeneralsBaseUrl}/{section}"
            : $"{ModDBConstants.ZeroHourBaseUrl}/{section}";
        var pageSuffix = filter.Page > 1 ? $"/page/{filter.Page}" : string.Empty;
        return baseUrl + pageSuffix + filter.ToQueryString();
    }

    private static List<ContentSearchResult> ParseDocumentSearchResults(
        IDocument document,
        GameType gameType,
        string section)
    {
        List<ContentSearchResult> results = [];
        var contentItems = document.QuerySelectorAll(ModDBConstants.DefaultListItemSelector);

        foreach (var item in contentItems)
        {
            try
            {
                var searchResult = ParseContentItem(item, gameType, section);
                if (searchResult != null)
                {
                    results.Add(searchResult);
                }
            }
            catch
            {
                // Ignore parse errors for individual items
            }
        }

        return results;
    }

    private static bool HasMorePages(IDocument document)
    {
        var nextLink = document.QuerySelector("div.pages a.next") ?? document.QuerySelector("a.next");
        return nextLink != null;
    }

    private static string DetermineSectionFromUrl(string url)
    {
        if (url.Contains(ModDBConstants.AddonsSegment, StringComparison.OrdinalIgnoreCase))
        {
            return "addons";
        }

        if (url.Contains(ModDBConstants.DownloadsSegment, StringComparison.OrdinalIgnoreCase))
        {
            return ModDBConstants.DownloadsSection;
        }

        return ModDBConstants.ModsSection;
    }

    private static string NormalizeRelativeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Contains("blank.gif", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return $"{ModDBConstants.BaseUrl.TrimEnd('/')}/{(url.StartsWith('/') ? url.TrimStart('/') : url)}";
    }

    private static ContentSearchResult? ParseSinglePageItem(
        IDocument document,
        string url,
        GameType gameType,
        string section)
    {
        var title = document.QuerySelector("h1 a, h1, #profiletitle")?.TextContent?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            var docTitle = document.Title?.Trim();
            if (!string.IsNullOrWhiteSpace(docTitle))
            {
                var titleParts = docTitle.Split([" - Mod DB", " - Command & Conquer"], StringSplitOptions.RemoveEmptyEntries);
                title = titleParts.Length > 0 ? titleParts[0].Trim() : docTitle;
            }
        }

        if (!string.IsNullOrWhiteSpace(title) && title.EndsWith(" file", StringComparison.OrdinalIgnoreCase))
        {
            title = title[..^5].Trim();
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            title = ExtractModDBIdFromUrl(url);
        }

        var authorLink = document.QuerySelector("a[href*='/members/'], a[href*='/company/'], span.by a, span.author a, .subheading a, td.content.name span.author a");
        var author = authorLink?.TextContent?.Trim() ?? UnknownValue;

        var descEl = document.QuerySelector("meta[name='description']");
        var descriptionText = descEl?.GetAttribute("content")
            ?? document.QuerySelector("p, div.summary, span.summary, #profiledescription, .description")?.TextContent?.Trim();
        var description = HtmlTextHelper.NormalizeHtml(descriptionText);

        var iconUrl = ExtractIconUrl(document.DocumentElement);
        if (string.IsNullOrWhiteSpace(iconUrl))
        {
            var iconEl = document.QuerySelector(".imagebox img, img.image, img.screenshot, div.image img, #profilemain img, meta[property='og:image']");
            var rawIcon = iconEl?.GetAttribute("src") ?? iconEl?.GetAttribute("content") ?? string.Empty;
            iconUrl = !string.IsNullOrWhiteSpace(rawIcon) ? NormalizeRelativeUrl(rawIcon) : string.Empty;
        }

        var categoryEl = document.QuerySelector(".category, .type, span.category, span.subheading");
        var category = categoryEl?.TextContent?.Trim();

        var lastUpdated = ExtractLastUpdatedDate(document.DocumentElement);
        var contentType = DetermineContentType(section, category, url, title);
        var moddbId = ExtractModDBIdFromUrl(url);
        var prospectiveId = lastUpdated.HasValue && lastUpdated.Value > DateTime.MinValue
            ? ManifestIdGenerator.GeneratePublisherContentId(ModDBConstants.PublisherPrefix, contentType, title, lastUpdated.Value)
            : ManifestIdGenerator.GeneratePublisherContentId(ModDBConstants.PublisherPrefix, contentType, title, 0);

        var result = new ContentSearchResult
        {
            Id = prospectiveId,
            Name = title,
            Description = description,
            AuthorName = author,
            ContentType = contentType,
            TargetGame = gameType,
            ProviderName = ModDBConstants.DiscovererSourceName,
            IconUrl = iconUrl,
            RequiresResolution = true,
            ResolverId = ModDBConstants.ResolverId,
            SourceUrl = url,
            LastUpdated = lastUpdated,
        };

        result.ResolverMetadata[ModDBConstants.ContentIdMetadataKey] = moddbId;
        result.ResolverMetadata[ModDBConstants.SectionMetadataKey] = section;
        ContentCardBadgeHelper.ApplyCategory(result, category);
        ApplyParentModMetadata(result, url);

        return result;
    }

    private async Task<(List<ContentSearchResult> Items, bool HasMoreItems, bool KeepPageOpen, bool ChallengeDetected)> DiscoverFromSectionAsync(
        IPage page,
        string section,
        GameType gameType,
        ContentSearchQuery query,
        CancellationToken cancellationToken)
    {
        // Playwright scrape is the primary source: it paginates and exposes the full catalog.
        // The headed browser persistent context in PlaywrightService preserves the Cloudflare
        // clearance cookie after user verification so real listing markup loads.
        var (scrapeResults, hasMore, keepOpen, challengeDetected) = await DiscoverFromScrapeAsync(page, section, gameType, query, cancellationToken);
        if (challengeDetected)
        {
            // Do not replace a blocked, interactive browser flow with RSS. RSS is intentionally
            // capped at ten items and made the verified catalogue appear to regress. The browser
            // page stays open for the user to complete Cloudflare, then a refresh loads the real
            // paginated list from the persisted clearance profile.
            return ([], true, keepOpen, true);
        }

        if (scrapeResults.Count > 0)
        {
            return (scrapeResults, hasMore, keepOpen, false);
        }

        // Scrape returned 0 items.
        // If the user performed a search or applied a filter, respect the 0 results.
        // Do not fall back to RSS on custom queries as that would return random unrelated downloads.
        var hasSearchOrFilters = !string.IsNullOrWhiteSpace(query.SearchTerm) ||
            !string.IsNullOrWhiteSpace(query.ModDBCategory) ||
            !string.IsNullOrWhiteSpace(query.ModDBAddonCategory) ||
            !string.IsNullOrWhiteSpace(query.ModDBLicense) ||
            !string.IsNullOrWhiteSpace(query.ModDBTimeframe) ||
            (query.Page ?? 1) > 1;

        if (hasSearchOrFilters)
        {
            return ([], false, keepOpen, false);
        }

        // Scrape returned nothing (transient WAF block, outage, or the browser failed to launch).
        // Fall back to the public RSS feed so the grid is never empty on default browse. RSS cannot paginate, so
        // HasMoreItems is false regardless of what the scrape thought.
        logger.LogWarning("[ModDB] Scrape returned no items for '{Section}', falling back to RSS", section);
        var rssSection = string.Equals(section, "mods", StringComparison.OrdinalIgnoreCase)
            ? ModDBConstants.DownloadsSection
            : section;
        var rssResults = await DiscoverFromRssFeedAsync(rssSection, gameType, cancellationToken);
        return (rssResults, false, keepOpen, false);
    }

    private async Task<(List<ContentSearchResult> Items, bool HasMoreItems, bool KeepPageOpen, bool ChallengeDetected)> DiscoverFromScrapeAsync(
        IPage page,
        string section,
        GameType gameType,
        ContentSearchQuery query,
        CancellationToken cancellationToken)
    {
        var keepPageOpenForVerification = false;
        try
        {
            var filter = BuildFilterFromQuery(query);
            var url = BuildSectionUrl(section, gameType, filter);

            logger.LogInformation(
                "[ModDB] Fetching page {Page} from section '{Section}': {Url}",
                filter.Page,
                section,
                url);

            await page.GotoAsync(url, new PageGotoOptions { Timeout = ModDBConstants.DefaultGotoTimeout, WaitUntil = WaitUntilState.Commit });

            var (listingReady, challengeObserved) = await WaitForListingOrChallengeAsync(page, url, cancellationToken);
            if (!listingReady)
            {
                var isChallenge = await HandleVerificationFailureAsync(page, url, challengeObserved);
                if (isChallenge)
                {
                    keepPageOpenForVerification = !page.IsClosed;
                    return ([], false, keepPageOpenForVerification, true);
                }
            }

            if (page.IsClosed)
            {
                return ([], false, false, challengeObserved);
            }

            var html = await page.ContentAsync();
            var browsingContext = BrowsingContext.New(Configuration.Default);
            var document = await browsingContext.OpenAsync(req => req.Content(html), cancellationToken);

            var results = ParseDocumentSearchResults(document, gameType, section);
            if (results.Count == 0)
            {
                logger.LogWarning("[ModDB] Scrape returned no items for section '{Section}'", section);
            }

            var hasMoreItems = HasMorePages(document);
            if (hasMoreItems)
            {
                logger.LogInformation("[ModDB] More items available for {Section}", section);
            }

            return (results, hasMoreItems, keepPageOpenForVerification, false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover from {Section} with Playwright", section);
            return ([], false, keepPageOpenForVerification, false);
        }
    }

    private async Task<(bool ListingReady, bool ChallengeObserved)> WaitForListingOrChallengeAsync(
        IPage page,
        string url,
        CancellationToken cancellationToken)
    {
        var challengeObserved = false;
        var deadline = DateTime.UtcNow.AddMilliseconds(ModDBConstants.VerificationWaitTimeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (page.IsClosed)
            {
                logger.LogInformation("[ModDB] Browser page was closed; ending verification wait for {Url}", url);
                return (false, challengeObserved);
            }

            try
            {
                var title = await page.TitleAsync();
                if (IsChallengePage(title))
                {
                    if (!challengeObserved)
                    {
                        challengeObserved = true;
                        logger.LogWarning(
                            "[ModDB] Cloudflare challenge is blocking {Url} (title: '{Title}'). Waiting for the user to solve it in the browser window.",
                            url,
                            title);
                        try
                        {
                            await page.BringToFrontAsync();
                        }
                        catch (PlaywrightException ex)
                        {
                            logger.LogDebug(ex, "Failed to bring browser page to front");
                        }
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                    continue;
                }

                if (await page.QuerySelectorAsync(ModDBConstants.DefaultListItemSelector) != null)
                {
                    if (challengeObserved)
                    {
                        logger.LogInformation("[ModDB] Cloudflare challenge cleared for {Url}; parsing the listing.", url);
                    }

                    return (true, challengeObserved);
                }

                // If not a challenge page, check if the real page DOM container has fully loaded.
                // When a query produces 0 results, the page is complete but DefaultListItemSelector is absent.
                var readyState = await page.EvaluateAsync<string>("() => document.readyState");
                if (string.Equals(readyState, "complete", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(readyState, "interactive", StringComparison.OrdinalIgnoreCase))
                {
                    var hasPageContainer = await page.QuerySelectorAsync("div#sitecontainer, div#body, div.panes, div.column, div.main, footer, form") != null;
                    if (hasPageContainer)
                    {
                        // Page is fully rendered with 0 matching items. Do not spin polling for items.
                        return (true, challengeObserved);
                    }
                }
            }
            catch (PlaywrightException ex) when (Tools.PlaywrightService.IsContextClosedError(ex))
            {
                if (page.IsClosed)
                {
                    logger.LogInformation("[ModDB] Browser page was closed; ending verification wait for {Url}", url);
                    return (false, challengeObserved);
                }

                logger.LogDebug(ex, "[ModDB] Transient navigation while waiting for listing {Url}; retrying", url);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        return (false, challengeObserved);
    }

    private async Task<bool> HandleVerificationFailureAsync(IPage page, string url, bool challengeObserved)
    {
        string? pageTitle = null;
        try
        {
            if (!page.IsClosed)
            {
                pageTitle = await page.TitleAsync();
            }
        }
        catch (PlaywrightException ex)
        {
            logger.LogDebug(ex, "Failed to retrieve page title for {Url}", url);
        }

        if (IsChallengePage(pageTitle) || challengeObserved)
        {
            logger.LogWarning(
                "[ModDB] Verification was not completed within {Timeout} ms for {Url}. The page stays open; the user can retry after solving it.",
                ModDBConstants.VerificationWaitTimeoutMs,
                url);
            return true;
        }

        logger.LogWarning(
            "ModDB did not expose a listing selector within {Timeout} ms for {Url} (page title: '{Title}'), parsing the current document...",
            ModDBConstants.VerificationWaitTimeoutMs,
            url,
            pageTitle ?? UnknownValue);
        return false;
    }

    /// <summary>
    /// Fallback discovery via ModDB's public RSS feeds (https://rss.moddb.com/...), which are not
    /// behind the site's bot protection. Returns up to the feed size (currently 10 items).
    /// </summary>
    /// <param name="section">The ModDB section (e.g. "downloads" or "addons").</param>
    /// <param name="gameType">The target game.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed search results (empty on any failure).</returns>
    private async Task<List<ContentSearchResult>> DiscoverFromRssFeedAsync(
        string section,
        GameType gameType,
        CancellationToken cancellationToken)
    {
        var results = new List<ContentSearchResult>();

        try
        {
            var gameSlug = gameType == GameType.Generals ? "cc-generals" : "cc-generals-zero-hour";
            var feedUrl = $"https://rss.moddb.com/games/{gameSlug}/{section}/feed/rss.xml";

            using var client = httpClientFactory.CreateClient(ModDBConstants.PublisherPrefix);
            if (client.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            }

            var xml = await client.GetStringAsync(feedUrl, cancellationToken);
            var feed = System.Xml.Linq.XDocument.Parse(xml);

            foreach (var item in feed.Descendants("item"))
            {
                var title = item.Element("title")?.Value?.Trim();
                var link = item.Element("link")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                {
                    continue;
                }

                var rawDescription = item.Element("description")?.Value?.Trim() ?? string.Empty;
                var description = HtmlTextHelper.NormalizeHtml(rawDescription);
                DateTime? published = null;
                if (DateTime.TryParse(item.Element("pubDate")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    published = parsedDate;
                }

                System.Xml.Linq.XNamespace media = ModDBConstants.MediaRssNamespace;
                var fullImage = item.Descendants(media + "content").FirstOrDefault()?.Attribute("url")?.Value;
                var thumbnail = item.Descendants(media + "thumbnail").FirstOrDefault()?.Attribute("url")?.Value
                    ?? fullImage
                    ?? string.Empty;

                var moddbId = ExtractModDBIdFromUrl(link);
                var contentType = DetermineContentType(section, null, link, title);
                var prospectiveId = published.HasValue && published.Value > DateTime.MinValue
                    ? ManifestIdGenerator.GeneratePublisherContentId(ModDBConstants.PublisherPrefix, contentType, title, published.Value)
                    : ManifestIdGenerator.GeneratePublisherContentId(ModDBConstants.PublisherPrefix, contentType, title, 0);

                var result = new ContentSearchResult
                {
                    Id = prospectiveId,
                    Name = title,
                    Description = description,
                    AuthorName = UnknownValue,
                    ContentType = contentType,
                    TargetGame = gameType,
                    ProviderName = ModDBConstants.DiscovererSourceName,
                    IconUrl = thumbnail,
                    RequiresResolution = true,
                    ResolverId = ModDBConstants.ResolverId,
                    SourceUrl = link,
                    LastUpdated = published,
                };

                if (!string.IsNullOrEmpty(fullImage))
                {
                    result.ScreenshotUrls.Add(fullImage);
                }

                result.ResolverMetadata[ModDBConstants.ContentIdMetadataKey] = moddbId;
                result.ResolverMetadata[ModDBConstants.SectionMetadataKey] = section;
                result.ResolverMetadata[ModDBConstants.IsModMetadataKey] = (link.Contains(ModDBConstants.ModsSegment) && !link.Contains(ModDBConstants.AddonsSegment)).ToString();

                results.Add(result);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[ModDB] RSS fallback failed for section '{Section}'", section);
        }

        return results;
    }

    private async Task<(List<ContentSearchResult> Items, bool HasMoreItems, bool KeepPageOpen, bool ChallengeDetected)> DiscoverFromDirectUrlAsync(
        IPage page,
        string url,
        GameType gameType,
        CancellationToken cancellationToken)
    {
        var keepPageOpenForVerification = false;
        try
        {
            logger.LogInformation("[ModDB] Navigating directly to requested URL: {Url}", url);
            await page.GotoAsync(url, new PageGotoOptions { Timeout = ModDBConstants.DefaultGotoTimeout, WaitUntil = WaitUntilState.Commit });

            var (listingReady, challengeObserved) = await WaitForListingOrChallengeAsync(page, url, cancellationToken);
            if (!listingReady)
            {
                var isChallenge = await HandleVerificationFailureAsync(page, url, challengeObserved);
                if (isChallenge)
                {
                    keepPageOpenForVerification = !page.IsClosed;
                    return ([], false, keepPageOpenForVerification, true);
                }
            }

            if (page.IsClosed)
            {
                return ([], false, false, challengeObserved);
            }

            var html = await page.ContentAsync();
            var browsingContext = BrowsingContext.New(Configuration.Default);
            var document = await browsingContext.OpenAsync(req => req.Content(html), cancellationToken);

            var section = DetermineSectionFromUrl(url);

            // If the URL directly points to a single mod / addon / download detail page, parse it as a single item
            if (IsDetailPage(url))
            {
                var singleItem = ParseSinglePageItem(document, url, gameType, section);
                if (singleItem != null)
                {
                    logger.LogInformation("[ModDB] Discovered single item '{Name}' from direct detail URL: {Url}", singleItem.Name, url);
                    return ([singleItem], false, keepPageOpenForVerification, false);
                }
            }

            // If not a detail page or if it's a listing page (e.g. /downloads, /addons, /mods/{slug}/downloads), parse listing items
            var listingItems = ParseDocumentSearchResults(document, gameType, section);
            if (listingItems.Count > 0)
            {
                var hasMore = HasMorePages(document);
                logger.LogInformation("[ModDB] Discovered {Count} items from listing URL: {Url}", listingItems.Count, url);
                return (listingItems, hasMore, keepPageOpenForVerification, false);
            }

            // Fallback: try parsing as single page item if not yet attempted
            if (!IsDetailPage(url))
            {
                var singleFallback = ParseSinglePageItem(document, url, gameType, section);
                if (singleFallback != null)
                {
                    logger.LogInformation("[ModDB] Discovered single item '{Name}' from direct URL fallback: {Url}", singleFallback.Name, url);
                    return ([singleFallback], false, keepPageOpenForVerification, false);
                }
            }

            logger.LogWarning("[ModDB] Could not parse content item from direct URL: {Url}", url);
            return ([], false, keepPageOpenForVerification, false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover from direct ModDB URL {Url}", url);
            return ([], false, keepPageOpenForVerification, false);
        }
    }
}
