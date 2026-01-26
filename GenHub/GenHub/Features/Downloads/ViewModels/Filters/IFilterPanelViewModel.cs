using System;
using System.Collections.Generic;
using System.ComponentModel;
using GenHub.Core.Models.Content;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Interface for publisher-specific filter panel view models.
/// Each publisher type (ModDB, CNCLabs, GitHub, etc.) implements this
/// to provide its own filtering UI and query building logic.
/// </summary>
public interface IFilterPanelViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// Gets the publisher ID this filter panel is associated with.
    /// </summary>
    string PublisherId { get; }

    /// <summary>
    /// Gets a value indicating whether any filters are currently active.
    /// </summary>
    bool HasActiveFilters { get; }

    /// <summary>
    /// Gets a value indicating whether the filter panel is loading data.
    /// </summary>
    bool IsLoading { get; }

    /// <summary>
    /// Event raised when filters are cleared.
    /// </summary>
    event EventHandler? FiltersCleared;

    /// <summary>
    /// Event raised when filters are applied.
    /// </summary>
    event EventHandler? FiltersApplied;

    /// <summary>
    /// Applies the filter panel's current filters to a base content search query.
    /// </summary>
    /// <param name="baseQuery">The base ContentSearchQuery to which filters will be applied.</param>
    /// <returns>The resulting ContentSearchQuery with the panel's active filters applied.</returns>
    ContentSearchQuery ApplyFilters(ContentSearchQuery baseQuery);

    /// <summary>
    /// Resets all filters to their default (cleared) state.
    /// </summary>
    /// <remarks>
    /// Implementations should update related state (for example <c>HasActiveFilters</c> and <c>IsLoading</c>) and raise the <c>FiltersCleared</c> event after clearing.
    /// </remarks>
    void ClearFilters();

    /// <summary>
    /// Provides a human-readable collection describing each currently active filter.
    /// </summary>
    /// <returns>Strings describing each active filter; empty collection if no filters are active.</returns>
    IEnumerable<string> GetActiveFilterSummary();
}