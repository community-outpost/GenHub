using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Providers;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Represents the validation status of a release artifact's URL.
/// </summary>
public partial class ArtifactUrlStatus : ObservableObject
{
    private readonly ReleaseArtifact _artifact;

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
                Validate();
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtifactUrlStatus"/> class.
    /// </summary>
    /// <param name="artifact">The release artifact to validate.</param>
    /// <param name="contentName">The name of the content.</param>
    /// <param name="version">The release version.</param>
    public ArtifactUrlStatus(ReleaseArtifact artifact, string contentName, string version)
    {
        _artifact = artifact;
        ContentName = contentName;
        ReleaseVersion = version;
        ArtifactName = artifact.Filename;
        Validate();
    }

    /// <summary>
    /// Validates the download URL and updates the status.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DownloadUrl))
        {
            IsValid = false;
            StatusMessage = "Missing URL";
        }
        else if (System.Uri.TryCreate(DownloadUrl, System.UriKind.Absolute, out var uri)
                 && (uri.Scheme == System.Uri.UriSchemeHttp || uri.Scheme == System.Uri.UriSchemeHttps))
        {
            IsValid = true;
            StatusMessage = "Valid URL";
        }
        else
        {
            IsValid = false;
            StatusMessage = "Invalid URL format";
        }
    }
}
