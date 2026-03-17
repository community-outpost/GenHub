using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Windows.Input;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for an addon item in the Addons section.
/// </summary>
public partial class AddonItemViewModel : ObservableObject
{
    /// <summary>
    /// Gets the unique identifier for the addon.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the name of the addon.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the description of the addon.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the release date.
    /// </summary>
    public DateTime? ReleaseDate { get; init; }

    /// <summary>
    /// Gets the formatted release date display string.
    /// </summary>
    public string ReleaseDateDisplay => ReleaseDate?.ToString("MMM dd, yyyy") ?? "Unknown";

    /// <summary>
    /// Gets the file size in bytes.
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// Gets the download URL.
    /// </summary>
    public string? DownloadUrl { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the addon is downloaded.
    /// </summary>
    [ObservableProperty]
    private bool _isDownloaded;

    /// <summary>
    /// Gets or sets a value indicating whether the addon is currently downloading.
    /// </summary>
    [ObservableProperty]
    private bool _isDownloading;

    /// <summary>
    /// Gets or sets the download progress percentage.
    /// </summary>
    [ObservableProperty]
    private int _downloadProgress;

    /// <summary>
    /// Gets or sets the command to download the addon.
    /// </summary>
    public ICommand? DownloadCommand { get; set; }

    /// <summary>
    /// Gets or sets the command to add the addon to a profile.
    /// </summary>
    public ICommand? AddToProfileCommand { get; set; }
}
