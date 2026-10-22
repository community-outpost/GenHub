using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for a bundle item.
/// </summary>
public partial class BundleItemViewModel : ObservableObject
{
    /// <summary>
    /// Gets or sets the name of the bundle.
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the bundle is selected for build.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Gets or sets a value indicating whether the bundle should be packaged as a .big archive.
    /// </summary>
    [ObservableProperty]
    private bool _isBig = true;

    /// <summary>
    /// Gets or sets the file count in this bundle.
    /// </summary>
    [ObservableProperty]
    private int _fileCount;

    /// <summary>
    /// Gets or sets the total size of files in this bundle.
    /// </summary>
    [ObservableProperty]
    private long _totalSize;
}
