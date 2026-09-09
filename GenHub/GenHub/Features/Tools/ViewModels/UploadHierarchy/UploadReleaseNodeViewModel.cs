using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Release version containing download artifacts.
/// </summary>
public partial class UploadReleaseNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _version = string.Empty;

    [ObservableProperty]
    private string _releaseDate = string.Empty;

    [ObservableProperty]
    private string _releaseNotes = string.Empty;

    [ObservableProperty]
    private bool _isLatest;

    /// <summary>
    /// Gets the artifact files attached to this release.
    /// </summary>
    public ObservableCollection<UploadArtifactNodeViewModel> Artifacts { get; } = new();
}
