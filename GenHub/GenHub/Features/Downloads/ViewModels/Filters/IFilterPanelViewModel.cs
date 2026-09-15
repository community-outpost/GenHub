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
    /// Applies the current filter state to a base query and returns the modified query.
    /// </summary>
    /// <param name="baseQuery">The base query to apply filters to.</param>
    /// <returns>A new query with filters applied.</returns>
    ContentSearchQuery ApplyFilters(ContentSearchQuery baseQuery);

    /// <summary>
    /// Clears all active filters to their default state.
    /// </summary>
    void ClearFilters();

    /// <summary>
    /// Gets a human-readable summary of active filters for display.
    /// </summary>
    /// <returns>Collection of filter description strings.</returns>
    IEnumerable<string> GetActiveFilterSummary();
}
