using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Hierarchy Tier 3: Content item containing versions and releases.
/// </summary>
public partial class UploadContentNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _contentType = string.Empty;

    [ObservableProperty]
    private string _targetGame = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>
    /// Gets the releases published for this content item.
    /// </summary>
    public ObservableCollection<UploadReleaseNodeViewModel> Releases { get; } = new();
}
