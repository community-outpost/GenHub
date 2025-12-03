using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Enums;
using GenHub.Features.Content.ViewModels;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Represents a group of content items grouped by content type.
/// </summary>
public class ContentTypeGroup
{
    /// <summary>
    /// Gets or sets the content type for this group.
    /// </summary>
    public ContentType Type { get; set; }

    /// <summary>
    /// Gets or sets the display name for the content type.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of items in this group.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Gets or sets the collection of content items in this group.
    /// </summary>
    public ObservableCollection<ContentItemViewModel> Items { get; set; } = new();
}
