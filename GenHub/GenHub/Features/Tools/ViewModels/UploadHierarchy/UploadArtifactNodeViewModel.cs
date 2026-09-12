using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Constants;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Artifact file with size, hash, and download link.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel properties and methods bound to MVVM UI and CommunityToolkit ObservableProperty generated properties.")]
public partial class UploadArtifactNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _fileSizeFormatted = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUrl))]
    [NotifyPropertyChangedFor(nameof(IsCloudHosted))]
    [NotifyPropertyChangedFor(nameof(IsPendingUpload))]
    [NotifyPropertyChangedFor(nameof(StorageBadgeText))]
    private string _downloadUrl = string.Empty;

    [ObservableProperty]
    private string _sha256 = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPendingUpload))]
    [NotifyPropertyChangedFor(nameof(StorageBadgeText))]
    private bool _hasLocalFile;

    [ObservableProperty]
    private string _localFilePath = string.Empty;

    [ObservableProperty]
    private bool _isHosted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCloudHosted))]
    [NotifyPropertyChangedFor(nameof(IsPendingUpload))]
    [NotifyPropertyChangedFor(nameof(StorageBadgeText))]
    private bool _isExternalCdn;

    /// <summary>
    /// Gets a value indicating whether a valid download URL exists.
    /// </summary>
    public bool HasUrl => !string.IsNullOrWhiteSpace(DownloadUrl);

    /// <summary>
    /// Gets a value indicating whether this artifact is hosted in the cloud.
    /// </summary>
    public bool IsCloudHosted => HasUrl && !IsExternalCdn;

    /// <summary>
    /// Gets a value indicating whether this artifact is a local file pending cloud upload.
    /// </summary>
    public bool IsPendingUpload => !HasUrl && HasLocalFile;

    /// <summary>
    /// Gets the human-readable storage badge text for this artifact.
    /// </summary>
    public string StorageBadgeText
    {
        get
        {
            if (IsExternalCdn)
            {
                return HostingConstants.StatusExternalCdn;
            }

            if (IsCloudHosted)
            {
                return HostingConstants.StatusCloudHosted;
            }

            if (IsPendingUpload)
            {
                return HostingConstants.StatusPendingUpload;
            }

            return HasUrl ? HostingConstants.StatusCloudHosted : HostingConstants.StatusNoFileOrUrl;
        }
    }
}
