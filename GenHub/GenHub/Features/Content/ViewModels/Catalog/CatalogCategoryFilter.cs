namespace GenHub.Features.Content.ViewModels.Catalog;

/// <summary>
/// Represents a category filter option for the catalog subscription confirmation dialog.
/// </summary>
/// <param name="key">The filter key (e.g. "All", "GameClient", "MapPack").</param>
/// <param name="label">The display label.</param>
/// <param name="count">The count of items in this category.</param>
/// <param name="isSelected">A value indicating whether this category filter is active.</param>
public class CatalogCategoryFilter(string key, string label, int count, bool isSelected)
{
    /// <summary>
    /// Gets the filter key (e.g. "All", "GameClient", "MapPack").
    /// </summary>
    public string Key { get; } = key;

    /// <summary>
    /// Gets the display label.
    /// </summary>
    public string Label { get; } = label;

    /// <summary>
    /// Gets the count of items in this category.
    /// </summary>
    public int Count { get; } = count;

    /// <summary>
    /// Gets a value indicating whether this category filter is active.
    /// </summary>
    public bool IsSelected { get; } = isSelected;

    /// <summary>
    /// Gets the formatted display text (e.g. "All (12)").
    /// </summary>
    public string DisplayText => $"{Label} ({Count})";
}
