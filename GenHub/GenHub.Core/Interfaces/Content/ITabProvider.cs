using GenHub.Core.Models.Content;
using GenHub.Core.Models.Results.Content;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Defines dynamic custom tab extensions for the downloads browser content detail view.
/// When users inspect a game mod, map, or patch in the downloads browser detail page, publishers can add extra custom tabs (such as documentation, changelogs, sub-addons, or external links) defined in their catalog json.
/// </summary>
public interface ITabProvider
{
    /// <summary>
    /// Gets the unique identifier for this tab provider instance (e.g. catalog-tabs).
    /// Provider ids uniquely identify tab sources in the tab provider registry.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Evaluates whether this tab provider can supply custom tabs for a specific content item selected in the downloads browser.
    /// </summary>
    /// <param name="searchResult">The content item search result being viewed in the downloads section.</param>
    /// <returns>True if this provider can build custom tabs for the specified downloads content item; otherwise false.</returns>
    bool CanProvideTabsFor(ContentSearchResult searchResult);

    /// <summary>
    /// Retrieves custom tab definitions to render as navigation tabs in the downloads browser detail view.
    /// </summary>
    /// <param name="searchResult">The downloads content item to retrieve custom tabs for.</param>
    /// <param name="cancellationToken">Cancellation token for asynchronous catalog fetching operations.</param>
    /// <returns>Read-only list of custom tab definitions to populate in the downloads detail view model.</returns>
    Task<IReadOnlyList<CustomTabDefinition>> GetTabsAsync(
        ContentSearchResult searchResult,
        CancellationToken cancellationToken = default);
}
