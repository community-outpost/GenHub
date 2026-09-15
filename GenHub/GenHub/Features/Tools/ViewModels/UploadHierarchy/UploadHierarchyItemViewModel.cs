using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Hierarchy Tier 1: Provider Definition online metadata and subscription link.
/// </summary>
public partial class UploadHierarchyItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _publisherName = string.Empty;

    [ObservableProperty]
    private string _publisherId = string.Empty;

    [ObservableProperty]
    private string? _avatarUrl;

    [ObservableProperty]
    private string? _website;

    [ObservableProperty]
    private string? _definitionUrl;

    [ObservableProperty]
    private string? _subscriptionUrl;

    [ObservableProperty]
    private bool _isUploaded;

    [ObservableProperty]
    private DateTime? _lastUpdated;

    /// <summary>
    /// Gets the catalogs included in this provider definition.
    /// </summary>
    public ObservableCollection<UploadCatalogNodeViewModel> Catalogs { get; } = new();
}
