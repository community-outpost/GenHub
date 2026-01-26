using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Content;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Base class for filter panel view models providing common functionality.
/// </summary>
public abstract partial class FilterPanelViewModelBase : ObservableObject, IFilterPanelViewModel
{
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Event raised when filters are cleared.
    /// </summary>
    public event EventHandler? FiltersCleared;

    /// <summary>
    /// Event raised when filters are applied (user clicks Apply button).
    /// </summary>
    public event EventHandler? FiltersApplied;

    /// <inheritdoc />
    public abstract string PublisherId { get; }

    /// <inheritdoc />
    public abstract bool HasActiveFilters { get; }

    /// <summary>
/// Applies the filter panel's active filters to a provided content search query.
/// </summary>
/// <param name="baseQuery">The starting ContentSearchQuery to which this panel's active filters will be applied.</param>
/// <returns>A ContentSearchQuery representing the original query with this panel's filters applied.</returns>
    public abstract ContentSearchQuery ApplyFilters(ContentSearchQuery baseQuery);

    /// <summary>
/// Reset all filters to their default (no-filter) state.
/// </summary>
/// <remarks>
/// Implementations should clear any stored filter values and raise the <see cref="FiltersCleared"/> event to notify listeners that filters have been cleared.
/// </remarks>
    public abstract void ClearFilters();

    /// <summary>
/// Gets a human-readable summary of the currently active filters.
/// </summary>
/// <returns>An enumerable of strings describing each active filter; empty if no filters are active.</returns>
    public abstract IEnumerable<string> GetActiveFilterSummary();

    /// <summary>
    /// Raises property changed for HasActiveFilters when any filter changes.
    /// <summary>
    /// Raises a property-changed notification for the <c>HasActiveFilters</c> property.
    /// </summary>
    protected void NotifyFiltersChanged()
    {
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    /// <summary>
    /// Raises the FiltersCleared event.
    /// <summary>
    /// Notifies subscribers that filters have been cleared by raising the <see cref="FiltersCleared"/> event.
    /// </summary>
    protected void OnFiltersCleared()
    {
        FiltersCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Command to apply the current filters.
    /// <summary>
    /// Raises the <see cref="FiltersApplied"/> event to notify subscribers that the apply-filters action was triggered.
    /// </summary>
    [RelayCommand]
    private void ApplyFiltersAction()
    {
        FiltersApplied?.Invoke(this, EventArgs.Empty);
    }
}