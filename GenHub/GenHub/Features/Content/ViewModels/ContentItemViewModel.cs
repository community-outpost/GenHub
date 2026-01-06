using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using System.Collections.ObjectModel;

namespace GenHub.Features.Content.ViewModels;

/// <summary>
/// ViewModel for a single content item in the discovery browser.
/// </summary>
public partial class ContentItemViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ContentItemViewModel"/> class.
    /// </summary>
    /// <param name="model">The underlying content search result model.</param>
    public ContentItemViewModel(ContentSearchResult model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));

        // Subscribe to AvailableVariants changes to notify HasVariants
        AvailableVariants.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasVariants));
    }

    /// <summary>
    /// Gets the underlying data model for the content item.
    /// </summary>
    public ContentSearchResult Model { get; }

    /// <summary>
    /// Gets the source result for installation.
    /// </summary>
    public ContentSearchResult SourceResult => Model;

    /// <summary>
    /// Gets the name of the content.
    /// </summary>
    public string Name => Model.Name ?? string.Empty;

    /// <summary>
    /// Gets the description of the content.
    /// </summary>
    public string Description => Model.Description ?? string.Empty;

    /// <summary>
    /// Gets the name of the content's author.
    /// </summary>
    public string AuthorName => Model.AuthorName ?? string.Empty;

    /// <summary>
    /// Gets the version of the content.
    /// </summary>
    public string Version => Model.Version ?? string.Empty;

    /// <summary>
    /// Gets the URL for the content's icon.
    /// </summary>
    public string IconUrl => Model.IconUrl ?? string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this content is already installed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalled;

    /// <summary>
    /// Gets or sets a value indicating whether this content is downloaded locally.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddToProfile))]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    private bool _isDownloaded;

    /// <summary>
    /// Gets or sets a value indicating whether a newer version is available for download.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddToProfile))]
    private bool _isUpdateAvailable;

    /// <summary>
    /// Gets or sets the version number of the available update (if any).
    /// </summary>
    [ObservableProperty]
    private string? _updateAvailableVersion;

    /// <summary>
    /// Gets a value indicating whether this content can be added to a profile (must be downloaded and up-to-date).
    /// </summary>
    public bool CanAddToProfile => IsDownloaded && !IsUpdateAvailable;

    /// <summary>
    /// Gets a value indicating whether this content can be installed (not already installed).
    /// </summary>
    public bool CanInstall => !IsInstalled && !IsDownloading;

    /// <summary>
    /// Gets a value indicating whether this content can be downloaded.
    /// Shows "Download" button when: not downloaded OR an update is available.
    /// </summary>
    public bool CanDownload => (!IsDownloaded || IsUpdateAvailable) && !IsDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    private bool _isDownloading;

    [ObservableProperty]
    private string _downloadStatus = string.Empty;

    [ObservableProperty]
    private int _downloadProgress;

    /// <summary>
    /// Gets or sets a value indicating whether the changelog view is expanded.
    /// </summary>
    [ObservableProperty]
    private bool _isChangelogExpanded;

    /// <summary>
    /// Gets a value indicating whether the changelog toggle button should be shown.
    /// Only show if the description is long enough to be truncated.
    /// </summary>
    public bool ShouldShowChangelogToggle => !string.IsNullOrEmpty(Description) && Description.Length > 150;

    [RelayCommand]
    private void ToggleChangelog()
    {
        IsChangelogExpanded = !IsChangelogExpanded;
    }

    /// <summary>
    /// Gets the collection of available content variants (e.g., Generals vs Zero Hour, or 30Hz vs 60Hz).
    /// </summary>
    public ObservableCollection<ContentManifest> AvailableVariants { get; } = [];

    /// <summary>
    /// Gets a value indicating whether this content has multiple variants to choose from.
    /// </summary>
    public bool HasVariants => AvailableVariants.Count > 0;
}
