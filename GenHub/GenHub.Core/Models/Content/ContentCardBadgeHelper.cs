using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.Content;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Shared helpers for promoting and reading download-card badge metadata across publishers.
/// </summary>
public static partial class ContentCardBadgeHelper
{
    /// <summary>
    /// Applies a player-count value to search-result metadata and tags.
    /// </summary>
    /// <param name="result">The search result to update.</param>
    /// <param name="playerCount">The player count to store.</param>
    public static void ApplyPlayerCount(ContentSearchResult result, int playerCount)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (playerCount <= 0)
        {
            return;
        }

        var value = playerCount.ToString(CultureInfo.InvariantCulture);
        result.ResolverMetadata[ContentConstants.PlayerCountMetadataKey] = value;
        result.Metadata[ContentConstants.PlayerCountMetadataKey] = value;

        var tag = playerCount == 1 ? "1 Player" : $"{playerCount} Players";
        if (!result.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            result.Tags.Add(tag);
        }
    }

    /// <summary>
    /// Applies a category label to search-result metadata and tags.
    /// </summary>
    /// <param name="result">The search result to update.</param>
    /// <param name="category">The category display label.</param>
    public static void ApplyCategory(ContentSearchResult result, string? category)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(category))
        {
            return;
        }

        var trimmed = category.Trim();
        result.ResolverMetadata[ContentConstants.CategoryMetadataKey] = trimmed;
        result.Metadata[ContentConstants.CategoryMetadataKey] = trimmed;

        if (!result.Tags.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            result.Tags.Add(trimmed);
        }
    }

    /// <summary>
    /// Promotes conventional tags (for example <c>4 Players</c> or <c>category:AOA</c>) into badge metadata.
    /// </summary>
    /// <param name="result">The search result to update.</param>
    public static void PromoteFromTags(ContentSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!HasMetadata(result, ContentConstants.PlayerCountMetadataKey))
        {
            PromotePlayerCountFromTags(result);
        }

        if (!HasMetadata(result, ContentConstants.CategoryMetadataKey))
        {
            PromoteCategoryFromTags(result);
        }
    }

    /// <summary>
    /// Resolves the player-count badge text for a download card, or an empty string when unavailable.
    /// </summary>
    /// <param name="result">The search result.</param>
    /// <returns>Badge text such as <c>4 players</c>.</returns>
    public static string GetPlayerCountBadge(ContentSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.ContentType is not (ContentType.Map or ContentType.MapPack or ContentType.UnknownContentType or ContentType.GameInstallation))
        {
            return string.Empty;
        }

        if (TryGetMetadata(result, ContentConstants.PlayerCountMetadataKey, out var raw) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) &&
            count > 0)
        {
            return count == 1 ? "1 player" : $"{count} players";
        }

        foreach (var tag in result.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var match = PlayerCountTagRegex().Match(tag);
            if (match.Success && int.TryParse(match.Groups["players"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var taggedCount) && taggedCount > 0)
            {
                return taggedCount == 1 ? "1 player" : $"{taggedCount} players";
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Resolves the category badge text for a download card, or an empty string when unavailable.
    /// </summary>
    /// <param name="result">The search result.</param>
    /// <returns>Category badge text.</returns>
    public static string GetCategoryBadge(ContentSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (TryGetMetadata(result, ContentConstants.CategoryMetadataKey, out var category) &&
            !string.IsNullOrWhiteSpace(category))
        {
            return category.Trim();
        }

        foreach (var tag in result.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var match = CategoryTagRegex().Match(tag);
            if (match.Success)
            {
                var val = match.Groups["category"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Resolves the best card/detail thumbnail URL: banner, then first screenshot, then icon.
    /// Returns null when no thumbnail is present so cards can fall back to publisher logo placeholders cleanly.
    /// </summary>
    /// <param name="result">The search result.</param>
    /// <returns>A thumbnail URL, or null when none is available.</returns>
    public static string? GetThumbnailUrl(ContentSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!string.IsNullOrWhiteSpace(result.BannerUrl))
        {
            return result.BannerUrl;
        }

        var screenshot = result.ScreenshotUrls.FirstOrDefault(static url => !string.IsNullOrWhiteSpace(url));
        if (!string.IsNullOrWhiteSpace(screenshot))
        {
            return screenshot;
        }

        if (!string.IsNullOrWhiteSpace(result.IconUrl))
        {
            return result.IconUrl;
        }

        return null;
    }

    /// <summary>
    /// Resolves the canonical publisher logo URI for a content search result.
    /// </summary>
    /// <param name="result">The search result.</param>
    /// <returns>A logo URI string, or null when unmapped.</returns>
    public static string? GetPublisherLogoUrl(ContentSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return PublisherInfoConstants.GetPublisherLogo(result.ProviderName, $"{result.AuthorName} {result.Id} {result.Name}");
    }

    /// <summary>
    /// Checks whether a category badge text is equivalent to the given content type display or manifest string,
    /// preventing duplicate badge display (for example showing both "Content Bundle" and "ContentBundle").
    /// </summary>
    /// <param name="category">The category string.</param>
    /// <param name="contentType">The content type.</param>
    /// <returns>True if the category is equivalent to the content type; otherwise false.</returns>
    public static bool IsCategoryDuplicateOfContentType(string? category, ContentType contentType)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return true;
        }

        static string Normalize(string input) =>
            new(input.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        var normCategory = Normalize(category);
        if (string.IsNullOrEmpty(normCategory))
        {
            return true;
        }

        var normDisplayName = Normalize(contentType.GetDisplayName());
        var normManifestName = Normalize(contentType.ToManifestIdString());
        var normEnumName = Normalize(contentType.ToString());

        return normCategory == normDisplayName ||
               normCategory == normManifestName ||
               normCategory == normEnumName;
    }

    /// <summary>
    /// Reads a precomputed includes/required-content summary from search-result metadata.
    /// </summary>
    /// <param name="result">The search result.</param>
    /// <returns>Comma-separated included content names, or empty when unavailable.</returns>
    public static string GetIncludesSummary(ContentSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (TryGetMetadata(result, ContentConstants.IncludesSummaryMetadataKey, out var summary) &&
            !string.IsNullOrWhiteSpace(summary))
        {
            return summary.Trim();
        }

        return string.Empty;
    }

    /// <summary>
    /// Applies an includes/required-content summary for glanceable card and detail display.
    /// </summary>
    /// <param name="result">The search result to update.</param>
    /// <param name="includedNames">Friendly names of included or required content.</param>
    public static void ApplyIncludesSummary(ContentSearchResult result, IEnumerable<string> includedNames)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(includedNames);

        var names = includedNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
        {
            return;
        }

        var summary = string.Join(", ", names);
        result.Metadata[ContentConstants.IncludesSummaryMetadataKey] = summary;
        result.ResolverMetadata[ContentConstants.IncludesSummaryMetadataKey] = summary;
    }

    /// <summary>
    /// Returns the result's tags that are <em>not</em> already surfaced by the dedicated
    /// player-count or category badges, for display as additional tag chips on a card.
    /// </summary>
    /// <param name="result">The search result.</param>
    /// <returns>
    /// A list of tag strings with promoted player-count ("N Players") and category
    /// ("category:X") tags removed. Tags are compared case-insensitively against the
    /// resolved badge values.
    /// </returns>
    public static List<string> GetCardTags(ContentSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var playerBadge = GetPlayerCountBadge(result);
        var categoryBadge = GetCategoryBadge(result);

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(playerBadge))
        {
            excluded.Add(playerBadge);
        }

        if (!string.IsNullOrWhiteSpace(categoryBadge))
        {
            excluded.Add(categoryBadge.Trim());
        }

        // Also drop the raw source tags that the badges were promoted from, so chips
        // do not duplicate a "3 Players" or "category:AOA" tag that already fed a badge.
        foreach (var tag in result.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var trimmedTag = tag.Trim();
            if (PlayerCountTagRegex().IsMatch(trimmedTag) || CategoryTagRegex().IsMatch(trimmedTag))
            {
                excluded.Add(trimmedTag);
            }
        }

        return [.. result.Tags
            .Select(t => t?.Trim() ?? string.Empty)
            .Where(t => !string.IsNullOrWhiteSpace(t) && !excluded.Contains(t))];
    }

    /// <summary>
    /// Extracts a trailing <c>YYYY-MM-DD</c> date from a build-stamp style version tag
    /// (e.g. <c>weekly-2026-07-03</c>), returning the date string for badge display.
    /// Returns null when the version is not a date-bearing build tag.
    /// </summary>
    /// <param name="version">The raw version/tag string.</param>
    /// <returns>The date portion, or null when no trailing date is present.</returns>
    public static string? ExtractDateFromTag(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var match = BuildStampDateRegex().Match(version);
        return match.Success ? match.Groups["date"].Value : null;
    }

    private static bool HasMetadata(ContentSearchResult result, string key) =>
        result.ResolverMetadata.ContainsKey(key) || result.Metadata.ContainsKey(key);

    private static bool TryGetMetadata(ContentSearchResult result, string key, out string value)
    {
        if (result.ResolverMetadata.TryGetValue(key, out var resolverValue) && !string.IsNullOrWhiteSpace(resolverValue))
        {
            value = resolverValue;
            return true;
        }

        if (result.Metadata.TryGetValue(key, out var metadataValue) && !string.IsNullOrWhiteSpace(metadataValue))
        {
            value = metadataValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static void PromotePlayerCountFromTags(ContentSearchResult result)
    {
        foreach (var tag in result.Tags)
        {
            var match = PlayerCountTagRegex().Match(tag);
            if (match.Success && int.TryParse(match.Groups["players"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            {
                ApplyPlayerCount(result, count);
                break;
            }
        }
    }

    private static void PromoteCategoryFromTags(ContentSearchResult result)
    {
        foreach (var tag in result.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var match = CategoryTagRegex().Match(tag);
            if (match.Success)
            {
                var cat = match.Groups["category"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(cat))
                {
                    ApplyCategory(result, cat);
                    break;
                }
            }
        }
    }

    [GeneratedRegex(@"^(?:players?:)?(?<players>[1-8])\s*players?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlayerCountTagRegex();

    [GeneratedRegex(@"^category:\s*(?<category>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CategoryTagRegex();

    [GeneratedRegex(@"^(?:(?:weekly|nightly|daily|build|dev|snapshot|stamp)[-_])?(?<date>\d{4}-\d{2}-\d{2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BuildStampDateRegex();
}
