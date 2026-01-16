using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for a publisher item in the sidebar.
/// </summary>
public partial class PublisherItemViewModel(
    string publisherId,
    string displayName,
    string? logoSource = null,
    string? publisherType = null,
    ILogger<PublisherItemViewModel>? logger = null) : ObservableObject
{
    private readonly ILogger<PublisherItemViewModel>? _logger = logger;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private int _contentCount;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Gets the catalog tabs for multi-catalog publishers.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<CatalogTabViewModel> _catalogTabs = new();

    /// <summary>
    /// Gets or sets the currently selected catalog tab (null means show all).
    /// </summary>
    [ObservableProperty]
    private CatalogTabViewModel? _selectedCatalogTab;

    /// <summary>
    /// Gets the publisher ID.
    /// </summary>
    public string PublisherId { get; } = publisherId;

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string DisplayName { get; } = displayName;

    /// <summary>
    /// Gets the logo source path or URL.
    /// </summary>
    public string? LogoSource { get; } = logoSource;

    /// <summary>
    /// Gets the publisher type (static for official publishers, dynamic for community).
    /// </summary>
    public string PublisherType { get; } = publisherType ?? "static";

    /// <summary>
    /// Gets a value indicating whether this is an official/static publisher.
    /// </summary>
    public bool IsStaticPublisher =>
        PublisherType.Equals("static", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets whether this publisher has multiple catalogs.
    /// </summary>
    public bool HasMultipleCatalogs => CatalogTabs.Count > 1;
}
