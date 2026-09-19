using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Represents a map tag filter toggle item.
/// </summary>
public partial class MapTagFilterItem : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapTagFilterItem"/> class.
    /// </summary>
    /// <param name="displayName">The display name.</param>
    /// <param name="tag">The tag value.</param>
    /// <param name="category">The tag category.</param>
    public MapTagFilterItem(string displayName, string tag, string category)
    {
        DisplayName = displayName;
        Tag = tag;
        Category = category;
    }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the tag value used in queries.
    /// </summary>
    public string Tag { get; }

    /// <summary>
    /// Gets the tag category for grouping in UI.
    /// </summary>
    public string Category { get; }
}
