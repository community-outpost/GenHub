using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Providers;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Represents an artifact upload task with progress tracking.
/// </summary>
public partial class ArtifactUploadTask : ObservableObject
{
    /// <summary>
    /// Gets or sets the content ID this artifact belongs to.
    /// </summary>
    public string ContentId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release version this artifact belongs to.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the artifact being uploaded.
    /// </summary>
    public ReleaseArtifact Artifact { get; set; } = null!;

    [ObservableProperty]
    private UploadStatus _status = UploadStatus.Pending;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _statusText = "Pending";

    partial void OnStatusChanged(UploadStatus value)
    {
        StatusText = value switch
        {
            UploadStatus.Pending => "Pending",
            UploadStatus.Uploading => "Uploading...",
            UploadStatus.Uploaded => "Uploaded",
            UploadStatus.Failed => "Failed",
            _ => "Unknown",
        };
    }
}

/// <summary>
/// Represents the status of an artifact upload.
/// </summary>
public enum UploadStatus
{
    /// <summary>
    /// Upload is pending.
    /// </summary>
    Pending,

    /// <summary>
    /// Upload is in progress.
    /// </summary>
    Uploading,

    /// <summary>
    /// Upload completed successfully.
    /// </summary>
    Uploaded,

    /// <summary>
    /// Upload failed.
    /// </summary>
    Failed,
}
