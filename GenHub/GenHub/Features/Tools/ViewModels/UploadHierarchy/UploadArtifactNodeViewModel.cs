using CommunityToolkit.Mvvm.ComponentModel;

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
