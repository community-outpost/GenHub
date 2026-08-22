using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Parsers;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Generic base view model for downloadable rows (releases, addons, custom publisher content) with expandable details.
/// </summary>
public abstract partial class DownloadableItemViewModel : ObservableObject, IDownloadableRowViewModel
{
    /// <summary>
    /// Gets the unique identifier for the downloadable item.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the display name of the item.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the target game for this item.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTargetGame))]
    private string? _targetGame;

    /// <summary>
    /// Gets a value indicating whether a target game is specified.
    /// </summary>
    public bool HasTargetGame => !string.IsNullOrWhiteSpace(TargetGame);

    /// <summary>
    /// Gets or sets the version of the item.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVersion))]
    private string? _version;

    /// <summary>
    /// Gets a value indicating whether a valid version is present.
    /// </summary>
    public bool HasVersion => !string.IsNullOrWhiteSpace(Version) && !string.Equals(Version, "Unknown", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the category of the item.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCategory))]
    private string? _category;

    /// <summary>
    /// Gets a value indicating whether a category is present.
    /// </summary>
    public bool HasCategory => !string.IsNullOrWhiteSpace(Category);

    /// <summary>
    /// Gets or sets the uploader or author of the item.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUploader))]
    [NotifyPropertyChangedFor(nameof(ShowSizeDot))]
    [NotifyPropertyChangedFor(nameof(ShowDateDot))]
    private string? _uploader;

    /// <summary>
    /// Gets a value indicating whether a valid uploader is present.
    /// </summary>
    public bool HasUploader => !string.IsNullOrWhiteSpace(Uploader) && !string.Equals(Uploader, "Unknown", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReleaseDateDisplay))]
    [NotifyPropertyChangedFor(nameof(HasReleaseDate))]
    [NotifyPropertyChangedFor(nameof(ShowSizeDot))]
    [NotifyPropertyChangedFor(nameof(ShowDateDot))]
    private DateTime? _releaseDate;

    /// <summary>
    /// Gets the formatted release date display string.
    /// </summary>
    public string ReleaseDateDisplay => ReleaseDate.HasValue && ReleaseDate.Value != DateTime.MinValue
        ? ReleaseDate.Value.ToString("MMM dd, yyyy")
        : string.Empty;

    /// <summary>
    /// Gets a value indicating whether a release date is present.
    /// </summary>
    public bool HasReleaseDate => !string.IsNullOrEmpty(ReleaseDateDisplay);

    /// <summary>
    /// Gets a value indicating whether the separator dot after size should be displayed.
    /// </summary>
    public bool ShowSizeDot => HasFormattedSize && (HasReleaseDate || HasUploader);

    /// <summary>
    /// Gets a value indicating whether the separator dot after date should be displayed.
    /// </summary>
    public bool ShowDateDot => HasReleaseDate && HasUploader;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedSize))]
    [NotifyPropertyChangedFor(nameof(HasFormattedSize))]
    [NotifyPropertyChangedFor(nameof(ShowSizeDot))]
    private long _fileSize;

    /// <summary>
    /// Gets or sets the human readable size display string if provided.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedSize))]
    [NotifyPropertyChangedFor(nameof(HasFormattedSize))]
    [NotifyPropertyChangedFor(nameof(ShowSizeDot))]
    private string? _sizeDisplay;

    /// <summary>
    /// Gets the formatted size string for UI display.
    /// </summary>
    public string FormattedSize
    {
        get
        {
            if (!string.IsNullOrEmpty(SizeDisplay))
            {
                return SizeDisplay;
            }

            if (ContentType == ContentType.ContentBundle)
            {
                return "Bundle";
            }

            return FileSize > 0 ? FormatBytes(FileSize) : string.Empty;
        }
    }

    /// <summary>
    /// Gets a value indicating whether formatted size information is available.
    /// </summary>
    public bool HasFormattedSize => !string.IsNullOrEmpty(FormattedSize);

    /// <summary>
    /// Gets or sets the direct download URL.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDownloadUrl))]
    private string? _downloadUrl;

    /// <summary>
    /// Gets a value indicating whether a download URL is present.
    /// </summary>
    public bool HasDownloadUrl => !string.IsNullOrWhiteSpace(DownloadUrl);

    /// <summary>
    /// Gets or sets the web page details URL for fetching extended info.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetailsUrl))]
    private string? _detailsUrl;

    /// <summary>
    /// Gets a value indicating whether a details URL is present.
    /// </summary>
    public bool HasDetailsUrl => !string.IsNullOrWhiteSpace(DetailsUrl);

    /// <summary>
    /// Gets or sets the thumbnail image URL.
    /// </summary>
    [ObservableProperty]
    private string? _thumbnailUrl;

    /// <summary>
    /// Gets or sets the actual filename archive name.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilename))]
    private string? _filename;

    /// <summary>
    /// Gets a value indicating whether a filename is present.
    /// </summary>
    public bool HasFilename => !string.IsNullOrWhiteSpace(Filename);

    /// <summary>
    /// Gets or sets the MD5 checksum hash.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMd5Hash))]
    private string? _md5Hash;

    /// <summary>
    /// Gets a value indicating whether an MD5 checksum hash is present.
    /// </summary>
    public bool HasMd5Hash => !string.IsNullOrWhiteSpace(Md5Hash);

    /// <summary>
    /// Gets or sets the download count.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDownloadCount))]
    private int? _downloadCount;

    /// <summary>
    /// Gets a value indicating whether download count information is available.
    /// </summary>
    public bool HasDownloadCount => DownloadCount.HasValue && DownloadCount.Value > 0;

    /// <summary>
    /// Gets or sets the comment count.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCommentCount))]
    private int? _commentCount;

    /// <summary>
    /// Gets a value indicating whether comment count information is available.
    /// </summary>
    public bool HasCommentCount => CommentCount.HasValue && CommentCount.Value > 0;

    /// <summary>
    /// Gets or sets the full description or release notes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    [NotifyPropertyChangedFor(nameof(Description))]
    private string? _fullDescription;

    /// <summary>
    /// Gets or sets the short summary or description of the item.
    /// Maps to <see cref="FullDescription"/>.
    /// </summary>
    public string? Description
    {
        get => FullDescription;
        set => FullDescription = value;
    }

    /// <summary>
    /// Gets a value indicating whether a description is present.
    /// </summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(FullDescription);

    /// <summary>
    /// Gets the collection of preview images for this item.
    /// </summary>
    public ObservableCollection<string> PreviewImages { get; } = [];

    /// <summary>
    /// Gets a value indicating whether preview images are available.
    /// </summary>
    public bool HasPreviewImages => PreviewImages.Count > 0;

    /// <summary>
    /// Gets or sets a value indicating whether the item is downloaded.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    private bool _isDownloaded;

    /// <summary>
    /// Gets or sets a value indicating whether an update is available for this item.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    private bool _isUpdateAvailable;

    /// <summary>
    /// Gets or sets the manifest ID produced when this item was acquired.
    /// </summary>
    [ObservableProperty]
    private string? _downloadedManifestId;

    /// <summary>
    /// Gets or sets a value indicating whether the item is currently downloading.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    private bool _isDownloading;

    /// <summary>
    /// Gets or sets the download progress percentage.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    private int _downloadProgress;

    /// <summary>
    /// Gets the human-readable status text for this release.
    /// </summary>
    public string StatusDisplay
    {
        get
        {
            if (IsUpdateAvailable)
            {
                return "Update Available";
            }

            if (IsDownloaded)
            {
                return "Downloaded";
            }

            return IsDownloading ? $"Downloading ({DownloadProgress}%)" : "Available for Download";
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the row is currently expanded to show details.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Gets or sets a value indicating whether extended details are currently being fetched.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingDetails;

    /// <summary>
    /// Gets or sets a value indicating whether extended details have been loaded.
    /// </summary>
    [ObservableProperty]
    private bool _isDetailsLoaded;

    /// <summary>
    /// Gets or sets a value indicating whether an error occurred fetching details.
    /// </summary>
    [ObservableProperty]
    private bool _hasDetailsError;

    /// <summary>
    /// Gets or sets the detail error message if fetching failed.
    /// </summary>
    [ObservableProperty]
    private string? _detailsErrorMessage;

    /// <summary>
    /// Gets or sets a value indicating whether this item is currently selected as the active target.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Gets or sets the content type of this downloadable item.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedSize))]
    [NotifyPropertyChangedFor(nameof(HasFormattedSize))]
    private ContentType _contentType = ContentType.Addon;

    /// <summary>
    /// Gets or sets the underlying downloadable file model.
    /// </summary>
    public DownloadableFile? File { get; set; }

    /// <summary>
    /// Gets or sets the command to select this item.
    /// </summary>
    public ICommand? SelectCommand { get; set; }

    /// <summary>
    /// Gets or sets the command to download the item.
    /// </summary>
    public ICommand? DownloadCommand { get; set; }

    /// <summary>
    /// Gets or sets the command to add the item to a profile.
    /// </summary>
    public ICommand? AddToProfileCommand { get; set; }

    /// <summary>
    /// Gets or sets the delegate function to fetch extended details on demand.
    /// </summary>
    public Func<DownloadableItemViewModel, CancellationToken, Task>? FetchDetailsAsync { get; set; }

    /// <summary>
    /// Toggles the row expansion and loads details on demand if not yet loaded.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task ToggleExpandAsync()
    {
        IsExpanded = !IsExpanded;

        if (IsExpanded && !IsDetailsLoaded && FetchDetailsAsync != null)
        {
            try
            {
                IsLoadingDetails = true;
                HasDetailsError = false;
                DetailsErrorMessage = null;

                await FetchDetailsAsync(this, CancellationToken.None);

                IsDetailsLoaded = true;
            }
            catch (Exception ex)
            {
                HasDetailsError = true;
                DetailsErrorMessage = ex.Message;
            }
            finally
            {
                IsLoadingDetails = false;
            }
        }
    }

    /// <summary>
    /// Copies the MD5 hash to the system clipboard.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task CopyMd5Async()
    {
        if (string.IsNullOrEmpty(Md5Hash))
        {
            return;
        }

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
                if (topLevel?.Clipboard != null)
                {
                    await topLevel.Clipboard.SetTextAsync(Md5Hash);
                }
            }
        }
        catch
        {
            // Clipboard access fallback ignored
        }
    }

    /// <summary>
    /// Copies the download or details URL to the system clipboard.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task CopyLinkAsync()
    {
        var targetUrl = DownloadUrl ?? DetailsUrl;
        if (string.IsNullOrEmpty(targetUrl))
        {
            return;
        }

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
                if (topLevel?.Clipboard != null)
                {
                    await topLevel.Clipboard.SetTextAsync(targetUrl);
                }
            }
        }
        catch
        {
            // Clipboard access fallback ignored
        }
    }

    private static string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;

        if (bytes >= GB)
        {
            return $"{bytes / (double)GB:F2} GB";
        }

        if (bytes >= MB)
        {
            return $"{bytes / (double)MB:F2} MB";
        }

        if (bytes >= KB)
        {
            return $"{bytes / (double)KB:F2} KB";
        }

        return $"{bytes} B";
    }
}
