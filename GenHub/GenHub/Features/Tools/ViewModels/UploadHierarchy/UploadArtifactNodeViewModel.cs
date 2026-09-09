using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Artifact file with size, hash, and download link.
/// </summary>
public partial class UploadArtifactNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _fileSizeFormatted = string.Empty;

    [ObservableProperty]
    private string _downloadUrl = string.Empty;

    [ObservableProperty]
    private string _sha256 = string.Empty;

    [ObservableProperty]
    private bool _hasLocalFile;

    [ObservableProperty]
    private string _localFilePath = string.Empty;

    [ObservableProperty]
    private bool _isHosted;

    /// <summary>
    /// Gets a value indicating whether a valid download URL exists.
    /// </summary>
    public bool HasUrl => !string.IsNullOrWhiteSpace(DownloadUrl);
}
