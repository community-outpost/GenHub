using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Hierarchy Tier 2: Catalog item containing content items.
/// </summary>
public partial class UploadCatalogNodeViewModel : ObservableObject
{
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
    /// Gets the number of content items in this catalog.
    /// </summary>
    public int ContentItemCount => ContentItems.Count;

    /// <summary>
    /// Gets the content items contained within this catalog.
    /// </summary>
    public ObservableCollection<UploadContentNodeViewModel> ContentItems { get; } = new();
}
