using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Helpers;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

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
    /// A screenshot or artwork image referenced by catalog content.
    /// </summary>
    Screenshot,

    /// <summary>
    /// A trailer or preview video referenced by catalog content.
    /// </summary>
    Video,

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
    [NotifyPropertyChangedFor(nameof(IsDefinition))]
    [NotifyPropertyChangedFor(nameof(IsCatalog))]
    [NotifyPropertyChangedFor(nameof(IsArtifact))]
    [NotifyPropertyChangedFor(nameof(IsScreenshot))]
    [NotifyPropertyChangedFor(nameof(IsVideo))]
    [NotifyPropertyChangedFor(nameof(IsMedia))]
    private HostedAssetKind _assetKind = HostedAssetKind.Artifact;

    [ObservableProperty]
    private string? _catalogId;

    [ObservableProperty]
    private string? _catalogName;

    [ObservableProperty]
    private string? _definitionName;

    [ObservableProperty]
    private string? _contentId;

    [ObservableProperty]
    private string? _contentName;

    [ObservableProperty]
    private string _fileId = string.Empty;

    [ObservableProperty]
    private string? _releaseVersion;

    [ObservableProperty]
    private string? _localFilePath;

    [ObservableProperty]
    private bool _canUpload;

    [ObservableProperty]
    private bool _isUploading;

    [ObservableProperty]
    private bool _canLoadToProject;

    [ObservableProperty]
    private bool _canAddToCatalog;

    [ObservableProperty]
    private bool _canDelete;

    [ObservableProperty]
    private bool _isDeleting;

    [ObservableProperty]
    private string _loadButtonTooltip = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCatalog))]
    [NotifyPropertyChangedFor(nameof(IsArtifact))]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _category = string.Empty;

    [ObservableProperty]
    private string _location = string.Empty;

    [ObservableProperty]
    private long _fileSize;

    [ObservableProperty]
    private string _fileSizeFormatted = "0.0 B";

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    private bool _isOnline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    private bool _isExternalCdn;

    [ObservableProperty]
    private DateTime _lastUpdated;

    [ObservableProperty]
    private string _lastUpdatedFormatted = string.Empty;

    [ObservableProperty]
    private string? _sha256;

    [ObservableProperty]
    private string _linkedToText = string.Empty;

    [ObservableProperty]
    private string _linkedToTooltip = string.Empty;

    [ObservableProperty]
    private int _linkCount;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpandable))]
    private bool _hasChildren;

    [ObservableProperty]
    private string _childrenSummary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpandable))]
    private bool _isEmpty;

    [ObservableProperty]
    private string _emptyStateText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpandable))]
    private bool _needsRemotePreview;

    [ObservableProperty]
    private bool _remotePreviewLoaded;

    [ObservableProperty]
    private bool _isLoadingChildren;

    [ObservableProperty]
    private string? _childrenLoadError;

    /// <summary>
    /// Gets the child entries shown when this row is expanded.
    /// </summary>
    public ObservableCollection<HostedAssetChildViewModel> Children { get; } = new();

    /// <summary>
    /// Gets a value indicating whether this asset is a publisher definition.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI")]
    public bool IsDefinition => AssetKind == HostedAssetKind.Definition;

    /// <summary>
    /// Gets a value indicating whether this asset is a catalog manifest.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI")]
    public bool IsCatalog => AssetKind == HostedAssetKind.Catalog || (AssetKind == HostedAssetKind.CloudFile && Name.Contains("catalog", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets a value indicating whether this asset is an artifact or binary release file.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI")]
    public bool IsArtifact => AssetKind == HostedAssetKind.Artifact || (AssetKind == HostedAssetKind.CloudFile && !Name.Contains("catalog", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets a value indicating whether this asset is screenshot or artwork image media.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI")]
    public bool IsScreenshot => AssetKind == HostedAssetKind.Screenshot;

    /// <summary>
    /// Gets a value indicating whether this asset is video media.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI")]
    public bool IsVideo => AssetKind == HostedAssetKind.Video;

    /// <summary>
    /// Gets a value indicating whether this asset is hosted media (screenshot image or video).
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI")]
    public bool IsMedia => AssetKind is HostedAssetKind.Screenshot or HostedAssetKind.Video;

    /// <summary>
    /// Gets a value indicating whether this row can be expanded to reveal linked children.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI")]
    public bool IsExpandable => HasChildren || IsEmpty || NeedsRemotePreview;

    /// <summary>
    /// Gets a value indicating whether this asset is pending hosting (neither live online nor hosted on an external CDN).
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI")]
    public bool IsPending => !IsOnline && !IsExternalCdn;

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

    partial void OnAssetKindChanged(HostedAssetKind value)
    {
        CanLoadToProject = value is HostedAssetKind.Definition or HostedAssetKind.Catalog;
        CanAddToCatalog = value is HostedAssetKind.Artifact or HostedAssetKind.CloudFile;
    }
}
