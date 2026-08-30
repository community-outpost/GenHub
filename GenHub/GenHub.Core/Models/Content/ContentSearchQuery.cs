using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents a query for searching content across providers.
/// </summary>
public class ContentSearchQuery
{
    private string? _language;

    /// <summary>
    /// Gets or sets the primary search term.
    /// </summary>
    public string SearchTerm { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of content to filter by.
    /// </summary>
    public ContentType? ContentType { get; set; }

    /// <summary>
    /// Gets or sets optional provider name to restrict the search to a specific provider.
    /// </summary>
    public string? ProviderName { get; set; }

    /// <summary>
    /// Gets or sets the target game to filter by.
    /// </summary>
    public GameType? TargetGame { get; set; }

    /// <summary>
    /// Gets a list of tags to filter by.
    /// </summary>
    public Collection<string> Tags { get; } = [];

    /// <summary>
    /// Gets or sets the author's name to filter by.
    /// </summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the minimum last updated date.
    /// </summary>
    public DateTime? MinDate { get; set; }

    /// <summary>
    /// Gets or sets the maximum last updated date.
    /// </summary>
    public DateTime? MaxDate { get; set; }

    /// <summary>
    /// Gets the number of results to skip for pagination.
    /// </summary>
    public int Skip { get; init; }

    /// <summary>
    /// Gets the number of results to take for pagination.
    /// </summary>
    public int Take { get; init; } = 50;

    /// <summary>
    /// Gets or sets the order to sort the results by.
    /// </summary>
    public ContentSortField SortOrder { get; set; } = ContentSortField.Relevance;

    /// <summary>
    /// Gets or sets a value indicating whether to include already installed content in the results.
    /// </summary>
    public bool IncludeInstalled { get; set; } = true;

    /// <summary>
    /// Gets or sets number of players filter by.
    /// </summary>
    public int? NumberOfPlayers { get; set; }

    /// <summary>
    /// Gets or sets page number.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to include older versions of content in results.
    /// Default is false (show only latest stable version).
    /// </summary>
    public bool IncludeOlderVersions { get; set; } = false;

    /// <summary>
    /// Gets or sets the sort order.
    /// </summary>
    public string Sort { get; set; } = string.Empty;

    // ===== ModDB-specific filters =====

    /// <summary>
    /// Gets or sets the ModDB category filter (full-version, patch, movie, etc.).
    /// </summary>
    public string? ModDBCategory { get; set; }

    /// <summary>
    /// Gets or sets the ModDB addon category filter (multiplayer-map, skin, etc.).
    /// </summary>
    public string? ModDBAddonCategory { get; set; }

    /// <summary>
    /// Gets or sets the ModDB license filter.
    /// </summary>
    public string? ModDBLicense { get; set; }

    /// <summary>
    /// Gets or sets the ModDB timeframe filter (24h, week, month, etc.).
    /// </summary>
    public string? ModDBTimeframe { get; set; }

    /// <summary>
    /// Gets or sets the ModDB section to search (mods, downloads, addons).
    /// </summary>
    public string? ModDBSection { get; set; }

    // ===== CNCLabs-specific filters =====

    /// <summary>
    /// Gets the CNCLabs map tag filters (Cramped, Spacious, Well-balanced, etc.).
    /// </summary>
    public Collection<string> CNCLabsMapTags { get; } = [];

    // ===== AODMaps-specific filters =====

    /// <summary>
    /// Gets or sets the AODMaps player count filter (2, 3, 4, 6, 8 players).
    /// </summary>
    public string? AODMapsPlayerCount { get; set; }

    /// <summary>
    /// Gets or sets the AODMaps category filter (Compstomp, Air, Race, Map Packs).
    /// </summary>
    public string? AODMapsCategory { get; set; }

    /// <summary>
    /// Gets or sets the AODMaps map type filter (1v1, 2v2, FFA).
    /// </summary>
    public string? AODMapsMapType { get; set; }

    // ===== GitHub-specific filters =====

    /// <summary>
    /// Gets or sets the GitHub topic filter.
    /// </summary>
    public string? GitHubTopic { get; set; }

    /// <summary>
    /// Gets or sets the GitHub author/owner filter.
    /// </summary>
    public string? GitHubAuthor { get; set; }

    /// <summary>
    /// Gets or sets the optional language filter used by CSV content pipeline.
    /// </summary>
    /// <remarks>
    /// Accepts case-insensitive input and is normalized to uppercase to match CSV schema values.
    /// Values: "All", "EN", "DE", "FR", "ES", "IT", "KO", "PL", "PT-BR", "ZH-CN", "ZH-TW".
    /// </remarks>
    public string? Language
    {
        get => _language;
        set => _language = NormalizeLanguage(value);
    }

    /// <summary>
    /// Generates a deterministic cache key representing the query and all its active filters.
    /// </summary>
    /// <returns>A string cache key.</returns>
    public string ToCacheKey()
    {
        static string Escape(string? value) => System.Uri.EscapeDataString(value ?? string.Empty);
        var tags = Tags.Count > 0 ? string.Join(",", Tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).Select(Escape)) : string.Empty;
        var cncTags = CNCLabsMapTags.Count > 0 ? string.Join(",", CNCLabsMapTags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).Select(Escape)) : string.Empty;
        var minDate = MinDate?.ToString("o", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        var maxDate = MaxDate?.ToString("o", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        return $"search::{Escape(ProviderName)}::{Escape(SearchTerm)}::{Escape(AuthorName)}::{ContentType}::{TargetGame}::{Skip}::{Take}::{SortOrder}::{Escape(Sort)}::{IncludeInstalled}::{IncludeOlderVersions}::{NumberOfPlayers}::{Page}::{minDate}::{maxDate}::{Escape(ModDBCategory)}::{Escape(ModDBAddonCategory)}::{Escape(ModDBLicense)}::{Escape(ModDBTimeframe)}::{Escape(ModDBSection)}::{Escape(AODMapsPlayerCount)}::{Escape(AODMapsCategory)}::{Escape(AODMapsMapType)}::{Escape(GitHubTopic)}::{Escape(GitHubAuthor)}::{Escape(Language)}::{tags}::{cncTags}";
    }

    private static readonly Dictionary<string, string> LanguageMap =
    new(StringComparer.OrdinalIgnoreCase)
    {
        [CsvConstants.AllLanguagesFilter] = CsvConstants.AllLanguagesFilter,
        [CsvConstants.LanguageEn] = CsvConstants.LanguageEn,
        [CsvConstants.LanguageDe] = CsvConstants.LanguageDe,
        [CsvConstants.LanguageFr] = CsvConstants.LanguageFr,
        [CsvConstants.LanguagePl] = CsvConstants.LanguagePl,
        [CsvConstants.LanguageEs] = CsvConstants.LanguageEs,
        [CsvConstants.LanguageIt] = CsvConstants.LanguageIt,
        [CsvConstants.LanguageKo] = CsvConstants.LanguageKo,
        [CsvConstants.LanguagePtBr] = CsvConstants.LanguagePtBr,
        ["BR"] = CsvConstants.LanguagePtBr,
        ["PT"] = CsvConstants.LanguagePtBr,
        [CsvConstants.LanguageZhCn] = CsvConstants.LanguageZhCn,
        ["CN"] = CsvConstants.LanguageZhCn,
        ["ZH"] = CsvConstants.LanguageZhCn,
        [CsvConstants.LanguageZhTw] = CsvConstants.LanguageZhTw,
        ["TW"] = CsvConstants.LanguageZhTw,
    };

    /// <summary>
    /// Normalizes a language code to the canonical casing and alias mapping.
    /// </summary>
    /// <param name="language">The raw language string.</param>
    /// <returns>The normalized language string, defaulting to <see cref="CsvConstants.AllLanguagesFilter"/> when null or empty.</returns>
    public static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return CsvConstants.AllLanguagesFilter;
        }

        var key = language.Trim();

        return LanguageMap.TryGetValue(key, out var normalized) ? normalized : key.ToUpperInvariant();
    }
}
