using System.Collections.ObjectModel;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Encapsulates provider-specific filter state for the Downloads browser.
/// Used to pass filter context between UI and discoverers.
/// </summary>
public class PublisherFilterContext
{
    // ===== Common filters (all publishers) =====

    /// <summary>
    /// Gets or sets the content type filter.
    /// </summary>
    public ContentType? ContentTypeFilter { get; set; }

    /// <summary>
    /// Gets or sets the search term.
    /// </summary>
    public string? SearchTerm { get; set; }

    /// <summary>
    /// Gets or sets the target game filter.
    /// </summary>
    public GameType? TargetGame { get; set; }

    // ===== ModDB-specific filters =====

    /// <summary>
    /// Gets or sets the ModDB category filter (Releases, Media, Tools, Miscellaneous).
    /// </summary>
    public string? ModDBCategory { get; set; }

    /// <summary>
    /// Gets or sets the ModDB addon category filter (Maps, Models, Skins, Audio, Graphics).
    /// </summary>
    public string? ModDBAddonCategory { get; set; }

    /// <summary>
    /// Gets or sets the ModDB license filter (BSD, Commercial, GPL, etc.).
    /// </summary>
    public string? ModDBLicense { get; set; }

    /// <summary>
    /// Gets or sets the ModDB timeframe filter (Past 24 hours, Past week, etc.).
    /// </summary>
    public string? ModDBTimeframe { get; set; }

    // ===== CNCLabs-specific filters =====

    /// <summary>
    /// Gets the CNCLabs map tag filters (Cramped, Spacious, Well-balanced, etc.).
    /// Multiple tags can be selected simultaneously.
    /// </summary>
    public Collection<string> CNCLabsMapTags { get; } = [];

    // ===== GitHub-specific filters =====

    /// <summary>
    /// Gets or sets the GitHub topic filter (genhub, generals-mod, zero-hour-mod).
    /// </summary>
    public string? GitHubTopic { get; set; }

    /// <summary>
    /// Gets or sets the GitHub author/owner filter.
    /// </summary>
    public string? GitHubAuthor { get; set; }

    /// <summary>
    /// Gets a value indicating whether any filters are active.
    /// </summary>
    public bool HasActiveFilters =>
        ContentTypeFilter.HasValue ||
        !string.IsNullOrWhiteSpace(SearchTerm) ||
        TargetGame.HasValue ||
        !string.IsNullOrWhiteSpace(ModDBCategory) ||
        !string.IsNullOrWhiteSpace(ModDBAddonCategory) ||
        !string.IsNullOrWhiteSpace(ModDBLicense) ||
        !string.IsNullOrWhiteSpace(ModDBTimeframe) ||
        CNCLabsMapTags.Count > 0 ||
        !string.IsNullOrWhiteSpace(GitHubTopic) ||
        !string.IsNullOrWhiteSpace(GitHubAuthor);

    /// <summary>
    /// Clears all filters to their default state.
    /// </summary>
    public void Clear()
    {
        ContentTypeFilter = null;
        SearchTerm = null;
        TargetGame = null;
        ModDBCategory = null;
        ModDBAddonCategory = null;
        ModDBLicense = null;
        ModDBTimeframe = null;
        CNCLabsMapTags.Clear();
        GitHubTopic = null;
        GitHubAuthor = null;
    }

    /// <summary>
    /// Applies this filter context to a content search query.
    /// </summary>
    /// <param name="query">The query to apply filters to.</param>
    /// <returns>The modified query.</returns>
    public ContentSearchQuery ApplyTo(ContentSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Common filters
        if (ContentTypeFilter.HasValue)
        {
            query.ContentType = ContentTypeFilter;
        }

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            query.SearchTerm = SearchTerm;
        }

        if (TargetGame.HasValue)
        {
            query.TargetGame = TargetGame;
        }

        // ModDB filters
        if (!string.IsNullOrWhiteSpace(ModDBCategory))
        {
            query.ModDBCategory = ModDBCategory;
        }

        if (!string.IsNullOrWhiteSpace(ModDBAddonCategory))
        {
            query.ModDBAddonCategory = ModDBAddonCategory;
        }

        if (!string.IsNullOrWhiteSpace(ModDBLicense))
        {
            query.ModDBLicense = ModDBLicense;
        }

        if (!string.IsNullOrWhiteSpace(ModDBTimeframe))
        {
            query.ModDBTimeframe = ModDBTimeframe;
        }

        // CNCLabs filters
        foreach (var tag in CNCLabsMapTags)
        {
            query.CNCLabsMapTags.Add(tag);
        }

        // GitHub filters
        if (!string.IsNullOrWhiteSpace(GitHubTopic))
        {
            query.GitHubTopic = GitHubTopic;
        }

        if (!string.IsNullOrWhiteSpace(GitHubAuthor))
        {
            query.GitHubAuthor = GitHubAuthor;
        }

        return query;
    }
}
