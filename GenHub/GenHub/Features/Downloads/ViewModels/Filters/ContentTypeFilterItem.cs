using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Enums;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Represents a content type filter toggle item.
/// </summary>
/// <param name="contentType">The content type.</param>
/// <param name="displayName">The display name.</param>
public partial class ContentTypeFilterItem(ContentType contentType, string displayName) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Gets the content type.
    /// </summary>
    public ContentType ContentType { get; } = contentType;

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string DisplayName { get; } = displayName;
}
