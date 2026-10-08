using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Common;
using System;
using System.Collections.ObjectModel;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Hierarchy Tier 2: Catalog item containing content items.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Computed properties read CommunityToolkit generated instance properties.")]
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
    private bool _hasChanges;

    [ObservableProperty]
    private DateTime? _lastUpdated;

    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>
    /// Toggles the collapsed / expanded display of this catalog.
    /// </summary>
    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

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
    /// Gets a value indicating whether this catalog needs to be published.
    /// True when never published or when changes are pending since the last publish.
    /// </summary>
    public bool NeedsPublish => !IsPublished || HasChanges;

    /// <summary>
    /// Gets a value indicating whether this catalog is published with no pending changes.
    /// </summary>
    public bool IsUpToDate => IsPublished && !HasChanges;

    /// <summary>
    /// Gets a value indicating whether this catalog is published but has pending changes.
    /// </summary>
    public bool HasPendingChanges => IsPublished && HasChanges;

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

    partial void OnIsPublishedChanged(bool value) => NotifyPublishStateChanged();

    partial void OnHasChangesChanged(bool value) => NotifyPublishStateChanged();

    private void NotifyPublishStateChanged()
    {
        OnPropertyChanged(nameof(NeedsPublish));
        OnPropertyChanged(nameof(IsUpToDate));
        OnPropertyChanged(nameof(HasPendingChanges));
    }
}
