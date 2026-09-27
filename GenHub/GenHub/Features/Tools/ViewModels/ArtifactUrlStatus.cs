using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Providers;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Represents the validation status of a release artifact's URL.
/// </summary>
public partial class ArtifactUrlStatus : ObservableObject
{
    private readonly ReleaseArtifact _artifact;
    private readonly ILocalizationService? _localizationService;

    [ObservableProperty]
    private string _artifactName = string.Empty;

    [ObservableProperty]
    private string _releaseVersion = string.Empty;

    [ObservableProperty]
    private string _contentName = string.Empty;

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasLocalFile;

    [ObservableProperty]
    private string _localFilePath = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtifactUrlStatus"/> class.
    /// </summary>
    /// <param name="artifact">The release artifact to validate.</param>
    /// <param name="contentName">The name of the content.</param>
    /// <param name="version">The release version.</param>
    /// <param name="localizationService">The optional localization service.</param>
    public ArtifactUrlStatus(ReleaseArtifact artifact, string contentName, string version, ILocalizationService? localizationService = null)
    {
        _artifact = artifact;
        _localizationService = localizationService;
        ContentName = contentName;
        ReleaseVersion = version;
        ArtifactName = artifact.Filename;
        LocalFilePath = artifact.LocalFilePath ?? string.Empty;
        HasLocalFile = !string.IsNullOrEmpty(artifact.LocalFilePath);
        Validate();
    }

    /// <summary>
    /// Gets a value indicating whether this artifact is an external CDN direct link (not pending upload).
    /// </summary>
    public bool IsExternalCdn => !string.IsNullOrWhiteSpace(DownloadUrl) && !HasLocalFile;

    /// <summary>
    /// Gets a value indicating whether this artifact is a local file pending cloud upload.
    /// </summary>
    public bool IsPendingUpload => string.IsNullOrWhiteSpace(DownloadUrl) && HasLocalFile;

    /// <summary>
    /// Gets or sets the download URL. Updates the underlying artifact.
    /// </summary>
    public string DownloadUrl
    {
        get => _artifact.DownloadUrl;
        set
        {
            if (_artifact.DownloadUrl != value)
            {
                _artifact.DownloadUrl = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsExternalCdn));
                OnPropertyChanged(nameof(IsPendingUpload));
                ValidateUrlOnly();
            }
        }
    }

    /// <summary>
    /// Validates the download URL and updates the status.
    /// </summary>
    public void Validate()
    {
        RefreshLocalFileState();

        if (!string.IsNullOrWhiteSpace(DownloadUrl))
        {
            ValidateUrlFormat();
        }
        else if (HasLocalFile)
        {
            if (System.IO.File.Exists(LocalFilePath) || System.IO.Directory.Exists(LocalFilePath))
            {
                // Has local file or folder but no URL - will be uploaded during publish
                IsValid = true;
                StatusMessage = GetStatusString("Tools.PublisherStudio.ArtifactStatus.PendingUpload", "Pending cloud upload");
            }
            else
            {
                IsValid = false;
                StatusMessage = GetStatusString("Tools.PublisherStudio.ArtifactStatus.LocalFileNotFound", "Local file or directory not found");
            }
        }
        else
        {
            IsValid = false;
            StatusMessage = GetStatusString("Tools.PublisherStudio.ArtifactStatus.NoFileOrUrl", "No file or URL configured");
        }

        OnPropertyChanged(nameof(IsExternalCdn));
        OnPropertyChanged(nameof(IsPendingUpload));
    }

    /// <summary>
    /// Revalidates only the URL format without touching the disk, for use on UI-thread binding updates.
    /// </summary>
    public void ValidateUrlOnly()
    {
        RefreshLocalFileState();

        if (!string.IsNullOrWhiteSpace(DownloadUrl))
        {
            ValidateUrlFormat();
        }
        else if (HasLocalFile)
        {
            IsValid = true;
            StatusMessage = GetStatusString("Tools.PublisherStudio.ArtifactStatus.PendingUpload", "Pending cloud upload");
        }
        else
        {
            IsValid = false;
            StatusMessage = GetStatusString("Tools.PublisherStudio.ArtifactStatus.NoFileOrUrl", "No file or URL configured");
        }

        OnPropertyChanged(nameof(IsExternalCdn));
        OnPropertyChanged(nameof(IsPendingUpload));
    }

    private void RefreshLocalFileState()
    {
        HasLocalFile = !string.IsNullOrEmpty(_artifact.LocalFilePath);
        LocalFilePath = _artifact.LocalFilePath ?? string.Empty;
    }

    private void ValidateUrlFormat()
    {
        if (System.Uri.TryCreate(DownloadUrl, System.UriKind.Absolute, out var uri)
            && (uri.Scheme == System.Uri.UriSchemeHttp || uri.Scheme == System.Uri.UriSchemeHttps))
        {
            IsValid = true;
            StatusMessage = HasLocalFile
                ? GetStatusString("Tools.PublisherStudio.ArtifactStatus.HostedWithLocal", "Hosted (local file available)")
                : GetStatusString("Tools.PublisherStudio.ArtifactStatus.HostedExternalCdn", "Hosted (External CDN)");
        }
        else
        {
            IsValid = false;
            StatusMessage = GetStatusString("Tools.PublisherStudio.ArtifactStatus.InvalidUrlFormat", "Invalid URL format");
        }
    }

    private string GetStatusString(string key, string fallback) =>
        _localizationService?.GetString(key) ?? fallback;
}
