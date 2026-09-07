using System;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Publishers;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Represents the publish status of a catalog.
/// </summary>
public partial class CatalogPublishStatus : ObservableObject
{
    [ObservableProperty]
    private NamedCatalog _catalog;

    [ObservableProperty]
    private bool _isPublished;

    [ObservableProperty]
    private string? _publishedUrl;

    [ObservableProperty]
    private DateTime? _lastPublished;

    [ObservableProperty]
    private bool _hasChanges;

    /// <summary>
    /// Gets the display status text.
    /// </summary>
    public string StatusText
    {
        get
        {
            if (!IsPublished)
            {
                return "Not Published";
            }

            if (HasChanges)
            {
                return "Changes Pending";
            }

            return LastPublished.HasValue
                ? $"Published {LastPublished.Value:MMM d, yyyy}"
                : "Published";
        }
    }

    /// <summary>
    /// Gets the status color.
    /// </summary>
    public string StatusColor
    {
        get
        {
            if (!IsPublished)
            {
                return "#6B7280";
            }

            if (HasChanges)
            {
                return "#F59E0B";
            }

            return "#10B981";
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogPublishStatus"/> class.
    /// </summary>
    /// <param name="catalog">The catalog.</param>
    public CatalogPublishStatus(NamedCatalog catalog)
    {
        _catalog = catalog;
    }

    partial void OnIsPublishedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
    }

    partial void OnHasChangesChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
    }

    partial void OnLastPublishedChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(StatusText));
    }
}
