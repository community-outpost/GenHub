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

    /// <inheritdoc />
    public abstract ContentSearchQuery ApplyFilters(ContentSearchQuery baseQuery);

    /// <inheritdoc />
    public abstract void ClearFilters();

    /// <inheritdoc />
    public abstract IEnumerable<string> GetActiveFilterSummary();

    /// <summary>
    /// Raises property changed for HasActiveFilters when any filter changes.
    /// </summary>
    protected void NotifyFiltersChanged()
    {
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    /// <summary>
    /// Raises the FiltersCleared event.
    /// </summary>
    protected void OnFiltersCleared()
    {
        FiltersCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Command to apply the current filters.
    /// </summary>
    [RelayCommand]
    private void ApplyFiltersAction()
    {
        FiltersApplied?.Invoke(this, EventArgs.Empty);
    }
}
