using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for a content item displayed in the content grid.
/// </summary>
public partial class ContentGridItemViewModel : ObservableObject
{
    private static readonly HttpClient _imageClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private readonly ILogger<ContentGridItemViewModel>? _logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    [NotifyPropertyChangedFor(nameof(CanAddToProfile))]
    private bool _isDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    [NotifyPropertyChangedFor(nameof(CanAddToProfile))]
    private bool _isDownloaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownloadButton))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateButton))]
    [NotifyPropertyChangedFor(nameof(ShowAddToProfileButton))]
    private ContentState _currentState = ContentState.NotDownloaded;

    [ObservableProperty]
    private int _downloadProgress;

    [ObservableProperty]
    private string _downloadStatus = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Bitmap? _iconBitmap;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentGridItemViewModel"/> class.
    /// </summary>
    /// <param name="searchResult">The content search result.</param>
    /// <summary>
    /// Initializes a view model for the given content search result.
    /// </summary>
    /// <param name="searchResult">The content search result used to populate the view model; must not be null.</param>
    public ContentGridItemViewModel(ContentSearchResult searchResult, ILogger<ContentGridItemViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(searchResult);
        SearchResult = searchResult;
        _logger = logger;
        _ = LoadIconAsync();
    }

    /// <summary>
    /// Loads the content icon specified by <c>IconUrl</c> into <c>IconBitmap</c>, supporting local avares assets and remote HTTP URLs.
    /// </summary>
    /// <remarks>
    /// If loading fails for any reason, the method swallows the error and leaves <c>IconBitmap</c> unchanged so the UI can show a fallback.
    /// </remarks>
    private async Task LoadIconAsync()
    {
        if (string.IsNullOrEmpty(IconUrl)) return;

        try
        {
            if (IconUrl.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(IconUrl);
                if (AssetLoader.Exists(uri))
                {
                    using var asset = AssetLoader.Open(uri);
                    IconBitmap = new Bitmap(asset);
                }
            }
            else
            {
                var bytes = await _imageClient.GetByteArrayAsync(IconUrl);
                using var stream = new MemoryStream(bytes);
                IconBitmap = new Bitmap(stream);
            }
        }
        catch
        {
            // Ignore load failures, fallback will be shown
        }
    }

    /// <summary>
    /// Gets the underlying content search result.
    /// </summary>
    public ContentSearchResult SearchResult { get; }

    /// <summary>
    /// Gets the content ID.
    /// </summary>
    public string Id => SearchResult.Id ?? string.Empty;

    /// <summary>
    /// Gets the content name.
    /// </summary>
    public string Name => SearchResult.Name ?? "Unknown";

    /// <summary>
    /// Gets the content description.
    /// </summary>
    public string Description => SearchResult.Description ?? string.Empty;

    /// <summary>
    /// Gets the truncated description for card display.
    /// </summary>
    public string ShortDescription =>
        Description.Length > 150
            ? Description[..147] + "..."
            : Description;

    /// <summary>
    /// Gets the content version.
    /// </summary>
    public string Version => SearchResult.Version ?? string.Empty;

    /// <summary>
    /// Gets the author name.
    /// </summary>
    public string AuthorName => SearchResult.AuthorName ?? "Unknown";

    /// <summary>
    /// Gets a value indicating whether the author is known (not null/empty/Unknown).
    /// </summary>
    public bool HasAuthor => !string.IsNullOrEmpty(SearchResult.AuthorName) &&
                             !string.Equals(SearchResult.AuthorName, "Unknown", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the content type.
    /// </summary>
    public ContentType ContentType => SearchResult.ContentType;

    /// <summary>
    /// Gets the content type display name.
    /// </summary>
    public string ContentTypeDisplay => ContentType.ToString();

    /// <summary>
    /// Gets the target game.
    /// </summary>
    public GameType TargetGame => SearchResult.TargetGame;

    /// <summary>
    /// Gets the provider name.
    /// </summary>
    public string ProviderName => SearchResult.ProviderName ?? string.Empty;

    /// <summary>
    /// Gets the icon URL for the content.
    /// </summary>
    public string? IconUrl => SearchResult.IconUrl;

    /// <summary>
    /// Gets the source URL for viewing more details.
    /// </summary>
    public string? SourceUrl => SearchResult.SourceUrl;

    /// <summary>
    /// Gets the last updated date (optional).
    /// </summary>
    public DateTime? LastUpdated => SearchResult.LastUpdated;

    /// <summary>
    /// Gets the formatted last updated string.
    /// </summary>
    public string LastUpdatedDisplay => LastUpdated?.ToString("MMM dd, yyyy") ?? "Unknown Date";

    /// <summary>
    /// Gets a value indicating whether the last updated date is visible.
    /// </summary>
    public bool IsLastUpdatedVisible => LastUpdated.HasValue;

    /// <summary>
    /// Gets a value indicating whether both author and date are visible (for separator).
    /// </summary>
    public bool HasAuthorAndDate => HasAuthor && IsLastUpdatedVisible;

    /// <summary>
    /// Gets the download size in bytes.
    /// </summary>
    public long DownloadSize => SearchResult.DownloadSize;

    /// <summary>
    /// Gets a value indicating whether the download size should be displayed (non-zero).
    /// </summary>
    public bool IsDownloadSizeVisible => DownloadSize > 0;

    /// <summary>
    /// Gets a value indicating whether the content can be downloaded.
    /// </summary>
    public bool CanDownload => !IsDownloaded && !IsDownloading && CurrentState != ContentState.Downloaded;

    /// <summary>
    /// Gets a value indicating whether the content can be added to a profile.
    /// </summary>
    public bool CanAddToProfile => IsDownloaded && !IsDownloading;

    /// <summary>
    /// Gets a value indicating whether the Download button should be shown.
    /// </summary>
    public bool ShowDownloadButton => CurrentState == ContentState.NotDownloaded;

    /// <summary>
    /// Gets a value indicating whether the Update button should be shown.
    /// </summary>
    public bool ShowUpdateButton => CurrentState == ContentState.UpdateAvailable;

    /// <summary>
    /// Gets a value indicating whether the Add to Profile button should be shown.
    /// </summary>
    public bool ShowAddToProfileButton => CurrentState == ContentState.Downloaded;

    /// <summary>
    /// Gets the tags associated with this content.
    /// </summary>
    public IList<string> Tags => SearchResult.Tags;

    /// <summary>
    /// Gets or sets the command to view details.
    /// </summary>
    public System.Windows.Input.ICommand? ViewCommand { get; set; }

    /// <summary>
    /// Gets or sets the command to open the source URL.
    /// </summary>
    public System.Windows.Input.ICommand? OpenUrlCommand { get; set; }

    /// <summary>
    /// Gets or sets the command to download the content.
    /// </summary>
    public System.Windows.Input.ICommand? DownloadCommand { get; set; }

    /// <summary>
    /// Gets or sets the command to add content to a profile.
    /// </summary>
    public System.Windows.Input.ICommand? AddToProfileCommand { get; set; }

    /// <summary>
    /// Gets or sets the command to update the content (download newer version).
    /// </summary>
    public System.Windows.Input.ICommand? UpdateCommand { get; set; }

    /// <summary>
    /// Command to view content details.
    /// <summary>
    /// Executes the configured view command, passing this view model as the command parameter.
    /// </summary>
    [RelayCommand]
    private void ViewDetails()
    {
        ViewCommand?.Execute(this);
    }

    /// <summary>
    /// Command to open source URL in browser.
    /// <summary>
    /// Opens the item's source URL by invoking the bound OpenUrlCommand when a source URL is available.
    /// </summary>
    [RelayCommand]
    private void OpenSourceUrl()
    {
        if (!string.IsNullOrEmpty(SourceUrl))
        {
            OpenUrlCommand?.Execute(SourceUrl);
        }
    }

    /// <summary>
    /// Command to download content.
    /// <summary>
    /// Executes the configured DownloadCommand using this view model as the command parameter.
    /// </summary>
    [RelayCommand]
    private void DownloadContent()
    {
        DownloadCommand?.Execute(this);
    }

    /// <summary>
    /// Command to update content to newer version.
    /// <summary>
    /// Invokes the configured UpdateCommand, passing this view model as the command parameter if a command is set.
    /// </summary>
    [RelayCommand]
    private void UpdateContent()
    {
        UpdateCommand?.Execute(this);
    }

    /// <summary>
    /// Gets the collection of installable variants for this content.
    /// </summary>
    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<InstallableVariant> _variants = [];

    /// <summary>
    /// Gets a value indicating whether this content has multiple variants.
    /// </summary>
    public bool HasVariants => Variants.Count > 0;
}