using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Interfaces.Common;
using System;
using System.Collections.ObjectModel;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Hierarchy Tier 2: Catalog item containing content items.
/// </summary>
public partial class UploadCatalogNodeViewModel : ObservableObject
{
    private ILocalizationService? _localizationService;

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string? _directDownloadUrl;

    [ObservableProperty]
    private bool _isPublished;

    [ObservableProperty]
    private DateTime? _lastUpdated;

    /// <summary>
    /// Gets or sets the optional localization service used to resolve count text.
    /// </summary>
    public ILocalizationService? LocalizationService
    {
        get => _localizationService;
        set
        {
            _localizationService = value;
            OnPropertyChanged(nameof(ContentItemsCountText));
        }
    }

    /// <summary>
    /// Gets the number of content items in this catalog.
    /// </summary>
    public int ContentItemCount => ContentItems.Count;

    /// <summary>
    /// Gets the localized content-items count display text.
    /// </summary>
    public string ContentItemsCountText =>
        _localizationService != null && _localizationService.TryGetString("Tools.PublisherStudio.Publish.ContentItemsFormat", out var format)
            ? string.Format(format, ContentItemCount)
            : $"{ContentItemCount} items";

    /// <summary>
    /// Gets the content items contained within this catalog.
    /// </summary>
    public ObservableCollection<UploadContentNodeViewModel> ContentItems { get; } = new();
}
