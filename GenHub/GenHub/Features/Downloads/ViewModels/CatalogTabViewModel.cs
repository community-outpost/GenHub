using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for a catalog tab in the Downloads browser.
/// Used when a publisher has multiple catalogs.
/// </summary>
public partial class CatalogTabViewModel : ObservableObject
{
    /// <summary>
    /// Gets the catalog ID within the publisher's definition.
    /// </summary>
    public string CatalogId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the human-readable catalog name.
    /// </summary>
    public string CatalogName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the URL to the catalog JSON.
    /// </summary>
    public string CatalogUrl { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this tab is currently selected.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Gets or sets the number of content items in this catalog.
    /// </summary>
    [ObservableProperty]
    private int _contentCount;

    /// <summary>
    /// Gets or sets whether this catalog is currently loading.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Creates a special "All" tab that represents merged content from all catalogs.
    /// </summary>
    public static CatalogTabViewModel CreateAllTab() => new()
    {
        CatalogId = "_all",
        CatalogName = "All",
        CatalogUrl = string.Empty,
        IsSelected = true
    };

    /// <summary>
    /// Gets whether this is the "All" tab.
    /// </summary>
    public bool IsAllTab => CatalogId == "_all";
}