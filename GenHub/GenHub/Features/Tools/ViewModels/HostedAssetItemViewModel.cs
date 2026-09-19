using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Helpers;
using System;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Kinds of assets tracked in the hosted asset inventory.
/// </summary>
public enum HostedAssetKind
{
    /// <summary>
    /// The publisher definition manifest.
    /// </summary>
    Definition,

    /// <summary>
    /// A catalog manifest belonging to the project.
    /// </summary>
    Catalog,

    /// <summary>
    /// A release binary belonging to the project.
    /// </summary>
    Artifact,

    /// <summary>
    /// A file discovered in cloud storage that is not linked to the project.
    /// </summary>
    CloudFile,
}

/// <summary>
/// ViewModel representing an asset hosted on a cloud provider or linked via external CDN.
/// </summary>
public partial class HostedAssetItemViewModel : ObservableObject
{
    [ObservableProperty]
    private HostedAssetKind _assetKind = HostedAssetKind.Artifact;

    [ObservableProperty]
    private string? _catalogId;

    [ObservableProperty]
    private string? _contentId;

    [ObservableProperty]
    private string? _contentName;

    [ObservableProperty]
    private string? _releaseVersion;

    [ObservableProperty]
    private string? _localFilePath;

    [ObservableProperty]
    private bool _canUpload;

    [ObservableProperty]
    private bool _isUploading;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _category = string.Empty;

    [ObservableProperty]
    private string _location = string.Empty;

    [ObservableProperty]
    private long _fileSize;

    [ObservableProperty]
    private string _fileSizeFormatted = "0 B";

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private bool _isExternalCdn;

    [ObservableProperty]
    private DateTime _lastUpdated;

    [ObservableProperty]
    private string _lastUpdatedFormatted = string.Empty;

    [ObservableProperty]
    private string? _sha256;

    /// <summary>
    /// Updates the formatted file size whenever <see cref="FileSize"/> changes.
    /// </summary>
    partial void OnFileSizeChanged(long value)
    {
        FileSizeFormatted = FileSizeFormatter.Format(value);
    }

    /// <summary>
    /// Updates the formatted date whenever <see cref="LastUpdated"/> changes.
    /// </summary>
    partial void OnLastUpdatedChanged(DateTime value)
    {
        LastUpdatedFormatted = value == DateTime.MinValue
            ? "Never"
            : value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }
}
