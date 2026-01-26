using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Parsers;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Parsers;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.Views;
using Microsoft.Extensions.Logging;
using WebFile = GenHub.Core.Models.Parsers.File;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for the detailed content view.
/// </summary>
public partial class ContentDetailViewModel : ObservableObject
{
    private static readonly System.Net.Http.HttpClient _imageClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private readonly ContentSearchResult _searchResult;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ContentDetailViewModel> _logger;
    private readonly IEnumerable<IWebPageParser> _parsers;
    private readonly IDownloadService _downloadService;
    private readonly IProfileContentService _profileContentService;
    private readonly IGameProfileManager _profileManager;
    private readonly INotificationService _notificationService;
    private readonly Action? _closeAction;

    // Lazy loading flags to track which sections have been loaded
    private bool _imagesLoaded;
    private bool _videosLoaded;
    private bool _releasesLoaded;
    private bool _addonsLoaded;
    private bool _basicContentLoaded;

    [ObservableProperty]
    private string _selectedScreenshotUrl;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _iconBitmap;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _isDownloaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownloadButton))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateButton))]
    [NotifyPropertyChangedFor(nameof(ShowAddToProfileButton))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private int _downloadProgress;

    [ObservableProperty]
    private ParsedWebPage? _parsedPage;

    [ObservableProperty]
    private string? _downloadStatusMessage;

    [ObservableProperty]
    private bool _isLoadingDetails;

    [ObservableProperty]
    private bool _isLoadingImages;

    [ObservableProperty]
    private bool _isLoadingVideos;

    [ObservableProperty]
    private bool _isLoadingReleases;

    [ObservableProperty]
    private bool _isLoadingAddons;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentDetailViewModel"/> class.
    /// </summary>
    /// <param name="searchResult">The content search result.</param>
    /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
    /// <param name="parsers">The web page parsers.</param>
    /// <param name="downloadService">The download service instance.</param>
    /// <param name="profileContentService">The profile content service instance.</param>
    /// <param name="profileManager">The profile manager instance.</param>
    /// <param name="notificationService">The notification service instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <summary>
    /// Creates a new ContentDetailViewModel, stores dependencies, initializes screenshots, and begins loading the icon and basic parsed page data.
    /// </summary>
    /// <param name="searchResult">Initial search result used to populate metadata and screenshot URLs.</param>
    /// <param name="serviceProvider">Service provider used to resolve runtime services.</param>
    /// <param name="parsers">Parsers used to extract detailed page data from the content URL.</param>
    /// <param name="downloadService">Service responsible for performing downloads.</param>
    /// <param name="profileContentService">Service for managing content associated with game profiles.</param>
    /// <param name="profileManager">Manager for game profiles.</param>
    /// <param name="notificationService">Service for showing user notifications.</param>
    /// <param name="logger">Logger instance for this view model.</param>
    /// <param name="closeAction">Optional action invoked to close the detail view.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="searchResult"/>, <paramref name="serviceProvider"/>, <paramref name="profileContentService"/>, <paramref name="profileManager"/>, <paramref name="notificationService"/>, or <paramref name="logger"/> is null.</exception>
    public ContentDetailViewModel(
        ContentSearchResult searchResult,
        IServiceProvider serviceProvider,
        IEnumerable<IWebPageParser> parsers,
        IDownloadService downloadService,
        IProfileContentService profileContentService,
        IGameProfileManager profileManager,
        INotificationService notificationService,
        ILogger<ContentDetailViewModel> logger,
        Action? closeAction = null)
    {
        ArgumentNullException.ThrowIfNull(searchResult);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(profileContentService);
        ArgumentNullException.ThrowIfNull(profileManager);
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(logger);

        _searchResult = searchResult;
        _serviceProvider = serviceProvider;
        _parsers = parsers;
        _downloadService = downloadService;
        _profileContentService = profileContentService;
        _profileManager = profileManager;
        _notificationService = notificationService;
        _logger = logger;
        _closeAction = closeAction;

        // Initialize screenshots
        foreach (var url in searchResult.ScreenshotUrls)
        {
            Screenshots.Add(url);
        }

        if (Screenshots.Count > 0)
        {
            SelectedScreenshotUrl = Screenshots[0];
        }
        else
        {
            SelectedScreenshotUrl = string.Empty;
        }

        // Load rich content from parsed page if already available
        LoadRichContent();

        // Load icon and parsed data asynchronously
        // Note: Full details are loaded eagerly for ModDB and similar content
        // that requires page parsing to show releases, addons, etc.
        _ = LoadIconAsync();
        _ = LoadBasicParsedDataAsync();
    }

    /// <summary>
    /// Command to close the detail view (navigate back).
    /// <summary>
    /// Invokes the configured close action to close or navigate away from the content detail view.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        _closeAction?.Invoke();
    }

    /// <summary>
    /// Loads the view model's IconBitmap from IconUrl when one is provided.
    /// </summary>
    /// <remarks>
    /// Supports Avalonia asset URIs (avares://) and remote image URLs. Failures are silently ignored and do not throw; the existing IconBitmap is left unchanged on error or when no IconUrl is present.
    /// </remarks>
    /// <returns>A task that completes when the icon load attempt has finished.</returns>
    private async Task LoadIconAsync()
    {
        if (string.IsNullOrEmpty(IconUrl)) return;

        try
        {
            if (IconUrl.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(IconUrl);
                if (Avalonia.Platform.AssetLoader.Exists(uri))
                {
                    using var asset = Avalonia.Platform.AssetLoader.Open(uri);
                    IconBitmap = new Avalonia.Media.Imaging.Bitmap(asset);
                }
            }
            else
            {
                var bytes = await _imageClient.GetByteArrayAsync(IconUrl);
                using var stream = new MemoryStream(bytes);
                IconBitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            }
        }
        catch
        {
            // Ignore load failures, fallback will be shown
        }
    }

    /// <summary>
    /// Loads the basic parsed page data (context and overview info) without loading all tab content.
    /// <summary>
    /// Loads basic parsed page data for the content's source URL and applies it to the view model.
    /// </summary>
    /// <remarks>
    /// If a suitable parser for the source URL is found, the parsed page is stored on the underlying search result, the view-model's basic-data flag is set, and bound properties are updated by invoking LoadRichContent on the UI thread. If no parser is available or the URL is empty, the method returns without side effects. The method also sets and clears the IsLoadingDetails flag during the operation and logs errors on failure.
    /// </remarks>
    /// <returns>A task that completes after the parsed data (if any) has been applied and the UI updates have finished.</returns>
    private async Task LoadBasicParsedDataAsync()
    {
        if (_basicContentLoaded || ParsedPage != null) return;

        try
        {
            IsLoadingDetails = true;
            var url = _searchResult.SourceUrl;
            if (string.IsNullOrEmpty(url)) return;

            var parser = _parsers.FirstOrDefault(p => p.CanParse(url));
            if (parser == null)
            {
                // No parser found for this URL
                return;
            }

            _logger.LogInformation("Fetching basic parsed data from {Url} using {Parser}", url, parser.ParserId);

            var parsedPage = await parser.ParseAsync(url);

            // Update on UI thread
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _searchResult.ParsedPageData = parsedPage;
                _basicContentLoaded = true;

                // Load basic overview data
                LoadRichContent();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load basic parsed data for {Url}", _searchResult.SourceUrl);
        }
        finally
        {
            IsLoadingDetails = false;
        }
    }

    /// <summary>
    /// Ensures the basic parsed page data is loaded before accessing tab content.
    /// <summary>
    /// Ensures that the basic parsed page data has been loaded into the view model.
    /// </summary>
    /// <returns>A task that completes once the basic parsed data has been loaded.</returns>
    private async Task EnsureBasicDataLoadedAsync()
    {
        if (!_basicContentLoaded)
        {
            await LoadBasicParsedDataAsync();
        }
    }

    /// <summary>
    /// Loads rich content from the parsed web page data.
    /// <summary>
    /// Populates view-model properties from the parsed web page and raises PropertyChanged notifications for all dependent metadata, collections, and visibility flags.
    /// </summary>
    /// <remarks>
    /// If the parsed page provides an icon URL and no icon is currently loaded, this method initiates an asynchronous icon load.
    /// </remarks>
    private void LoadRichContent()
    {
        // Check both the new ParsedPageData property and the legacy Data property
        var parsedPage = _searchResult.ParsedPageData ?? _searchResult.GetData<ParsedWebPage>();
        if (parsedPage == null) return;

        ParsedPage = parsedPage;

        // Notify property changes for context-dependent properties (from GlobalContext)
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(AuthorName));
        OnPropertyChanged(nameof(IconUrl));
        OnPropertyChanged(nameof(LastUpdated));
        OnPropertyChanged(nameof(LastUpdatedDisplay));
        OnPropertyChanged(nameof(DownloadSize));

        // Notify visibility properties for metadata display
        OnPropertyChanged(nameof(HasDownloadSize));
        OnPropertyChanged(nameof(HasLastUpdated));
        OnPropertyChanged(nameof(HasVersion));
        OnPropertyChanged(nameof(HasAuthor));

        // Reload icon if the URL changed from parsed context
        if (!string.IsNullOrEmpty(parsedPage.Context.IconUrl) && IconBitmap == null)
        {
            _ = LoadIconAsync();
        }

        // Notify property changes for all parsed content collections
        OnPropertyChanged(nameof(Articles));
        OnPropertyChanged(nameof(Videos));
        OnPropertyChanged(nameof(Images));
        OnPropertyChanged(nameof(Files));
        OnPropertyChanged(nameof(Reviews));
        OnPropertyChanged(nameof(Comments));

        // Notify visibility properties
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(ShowFilesTab));
        OnPropertyChanged(nameof(HasImages));
        OnPropertyChanged(nameof(HasVideos));
        OnPropertyChanged(nameof(HasComments));
        OnPropertyChanged(nameof(HasReviews));
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(HasCommunity));
    }

    /// <summary>
    /// Lazy loads images when the Images tab is accessed.
    /// <summary>
    /// Loads the Images tab content on first access and updates related UI properties.
    /// </summary>
    /// <remarks>
    /// Ensures basic parsed data is available, refreshes the Images and HasImages properties, and marks the images section as loaded so subsequent calls are no-ops.
    /// </remarks>
    [RelayCommand]
    private async Task LoadImagesAsync()
    {
        if (_imagesLoaded || IsLoadingImages) return;

        try
        {
            IsLoadingImages = true;

            // Ensure basic data is loaded first
            await EnsureBasicDataLoadedAsync();

            // Images are already loaded via LoadRichContent from the parsed page
            // We just mark it as loaded so we don't try to load again
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(Images));
                OnPropertyChanged(nameof(HasImages));
            });

            _imagesLoaded = true;
            _logger.LogDebug("Images tab loaded for content: {Name}", Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load images for content: {Name}", Name);
        }
        finally
        {
            IsLoadingImages = false;
        }
    }

    /// <summary>
    /// Lazy loads videos when the Videos tab is accessed.
    /// <summary>
    /// Lazily loads the Videos tab content for the view model, ensuring basic parsed data is available and updating UI-bound properties.
    /// </summary>
    /// <remarks>
    /// This operation is idempotent: it does nothing if videos have already been loaded or are currently loading. On success it raises change notifications for <c>Videos</c> and <c>HasVideos</c>.
    /// </remarks>
    [RelayCommand]
    private async Task LoadVideosAsync()
    {
        if (_videosLoaded || IsLoadingVideos) return;

        try
        {
            IsLoadingVideos = true;

            // Ensure basic data is loaded first
            await EnsureBasicDataLoadedAsync();

            // Videos are already loaded via LoadRichContent from the parsed page
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(Videos));
                OnPropertyChanged(nameof(HasVideos));
            });

            _videosLoaded = true;
            _logger.LogDebug("Videos tab loaded for content: {Name}", Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load videos for content: {Name}", Name);
        }
        finally
        {
            IsLoadingVideos = false;
        }
    }

    /// <summary>
    /// Lazy loads releases when the Releases tab is accessed.
    /// <summary>
    /// Loads release files from the parsed page into the Releases collection for the Releases tab.
    /// </summary>
    /// <remarks>
    /// No-op if releases are already loaded or currently loading. Ensures basic parsed data is available before populating and updates the HasReleases state.
    /// </remarks>
    [RelayCommand]
    private async Task LoadReleasesAsync()
    {
        if (_releasesLoaded || IsLoadingReleases) return;

        try
        {
            IsLoadingReleases = true;

            // Ensure basic data is loaded first
            await EnsureBasicDataLoadedAsync();

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Populate releases from the Files collection
                if (ParsedPage != null)
                {
                    var files = ParsedPage.Sections.OfType<WebFile>().ToList();
                    PopulateReleases(files);
                }

                OnPropertyChanged(nameof(HasReleases));
            });

            _releasesLoaded = true;
            _logger.LogDebug("Releases tab loaded for content: {Name}", Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load releases for content: {Name}", Name);
        }
        finally
        {
            IsLoadingReleases = false;
        }
    }

    /// <summary>
    /// Lazy loads addons when the Addons tab is accessed.
    /// <summary>
    /// Lazily loads addon file entries from the parsed page into the Addons collection and updates related UI state.
    /// </summary>
    /// <remarks>
    /// If addons have already been loaded or are currently loading, the method returns immediately.
    /// Ensures basic parsed data is available before populating addons, updates loading flags and counts, and logs failures.
    /// </remarks>
    [RelayCommand]
    private async Task LoadAddonsAsync()
    {
        if (_addonsLoaded || IsLoadingAddons) return;

        try
        {
            IsLoadingAddons = true;

            // Ensure basic data is loaded first
            await EnsureBasicDataLoadedAsync();

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Populate addons from the Files collection
                // Addons are typically marked differently in the parsed page
                // For now, we'll use all files that aren't main downloads
                if (ParsedPage != null)
                {
                    var files = ParsedPage.Sections.OfType<WebFile>().ToList();
                    PopulateAddons(files);
                }

                OnPropertyChanged(nameof(HasAddons));
                OnPropertyChanged(nameof(AddonsCount));
            });

            _addonsLoaded = true;
            _logger.LogDebug("Addons tab loaded for content: {Name}", Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load addons for content: {Name}", Name);
        }
        finally
        {
            IsLoadingAddons = false;
        }
    }

    /// <summary>
    /// Gets the articles from the parsed page.
    /// </summary>
    public ObservableCollection<Article> Articles => ParsedPage?.Sections.OfType<Article>().ToObservableCollection() ?? [];

    /// <summary>
    /// Gets the videos from the parsed page.
    /// </summary>
    public ObservableCollection<Video> Videos => ParsedPage?.Sections.OfType<Video>().ToObservableCollection() ?? [];

    /// <summary>
    /// Gets the images from the parsed page (excluding screenshots).
    /// </summary>
    public ObservableCollection<Image> Images => ParsedPage?.Sections.OfType<Image>().ToObservableCollection() ?? [];

    /// <summary>
    /// Gets the files from the parsed page.
    /// </summary>
    public ObservableCollection<WebFile> Files => ParsedPage?.Sections.OfType<WebFile>().ToObservableCollection() ?? [];

    /// <summary>
    /// Gets the reviews from the parsed page.
    /// </summary>
    public ObservableCollection<Review> Reviews => ParsedPage?.Sections.OfType<Review>().ToObservableCollection() ?? [];

    /// <summary>
    /// Gets the comments from the parsed page.
    /// </summary>
    public ObservableCollection<Comment> Comments => ParsedPage?.Sections.OfType<Comment>().ToObservableCollection() ?? [];

    /// <summary>
    /// Gets a value indicating whether files are available.
    /// </summary>
    public bool HasFiles => Files.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the Files tab should be shown.
    /// Only show if there are multiple files (more than 1).
    /// </summary>
    public bool ShowFilesTab => Files.Count > 1;

    /// <summary>
    /// Gets a value indicating whether images are available.
    /// </summary>
    public bool HasImages => Images.Count > 0;

    /// <summary>
    /// Gets a value indicating whether videos are available.
    /// </summary>
    public bool HasVideos => Videos.Count > 0;

    /// <summary>
    /// Gets a value indicating whether comments are available.
    /// </summary>
    public bool HasComments => Comments.Count > 0;

    /// <summary>
    /// Gets a value indicating whether reviews are available.
    /// </summary>
    public bool HasReviews => Reviews.Count > 0;

    /// <summary>
    /// Gets a value indicating whether media (images or videos) is available.
    /// </summary>
    public bool HasMedia => HasImages || HasVideos;

    /// <summary>
    /// Gets a value indicating whether community content (comments or reviews) is available.
    /// </summary>
    public bool HasCommunity => HasComments || HasReviews;

    /// <summary>
    /// Gets the content ID.
    /// </summary>
    public string Id => _searchResult.Id ?? string.Empty;

    /// <summary>
    /// Gets the content name - prefers parsed page context title.
    /// </summary>
    public string Name => ParsedPage?.Context.Title ?? _searchResult.Name ?? "Unknown";

    /// <summary>
    /// Gets the content description (full) - prefers parsed page context description.
    /// </summary>
    public string Description =>
        ParsedPage?.Context.Description ?? _searchResult.Description ?? string.Empty;

    /// <summary>
    /// Gets the author name - prefers parsed page context developer.
    /// </summary>
    public string AuthorName =>
        ParsedPage?.Context.Developer ?? _searchResult.AuthorName ?? "Unknown";

    /// <summary>
    /// Gets the version.
    /// </summary>
    public string Version => _searchResult.Version ?? string.Empty;

    /// <summary>
    /// Gets the last updated date (optional) - prefers parsed page context release date.
    /// </summary>
    public DateTime? LastUpdated => ParsedPage?.Context.ReleaseDate ?? _searchResult.LastUpdated;

    /// <summary>
    /// Gets the formatted last updated string.
    /// </summary>
    public string LastUpdatedDisplay => LastUpdated?.ToString("MMM dd, yyyy") ?? string.Empty;

    /// <summary>
    /// Gets the download size - prefers size from parsed files.
    /// </summary>
    public long DownloadSize
    {
        get
        {
            // Try to get size from parsed files first
            var parsedFile = Files?.FirstOrDefault();
            if (parsedFile?.SizeBytes > 0)
            {
                return parsedFile.SizeBytes.Value;
            }

            return _searchResult.DownloadSize;
        }
    }

    /// <summary>
    /// Gets a value indicating whether download size is available and greater than zero.
    /// </summary>
    public bool HasDownloadSize => DownloadSize > 0;

    /// <summary>
    /// Gets a value indicating whether a last updated date is available.
    /// </summary>
    public bool HasLastUpdated => LastUpdated.HasValue && LastUpdated.Value > DateTime.MinValue;

    /// <summary>
    /// Gets a value indicating whether a version is available.
    /// </summary>
    public bool HasVersion => !string.IsNullOrEmpty(Version);

    /// <summary>
    /// Gets a value indicating whether an author is available and not "Unknown".
    /// </summary>
    public bool HasAuthor => !string.IsNullOrEmpty(AuthorName) &&
                             !string.Equals(AuthorName, "Unknown", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the content type.
    /// </summary>
    public ContentType ContentType => _searchResult.ContentType;

    /// <summary>
    /// Gets the provider name.
    /// </summary>
    public string ProviderName => _searchResult.ProviderName ?? string.Empty;

    /// <summary>
    /// Gets the icon URL - prefers parsed page context icon.
    /// </summary>
    public string? IconUrl => ParsedPage?.Context.IconUrl ?? _searchResult.IconUrl;

    /// <summary>
    /// Gets the collection of screenshot URLs.
    /// </summary>
    public ObservableCollection<string> Screenshots { get; } = [];

    /// <summary>
    /// Gets the tags.
    /// </summary>
    public IList<string> Tags => _searchResult.Tags;

    /// <summary>
    /// Gets a value indicating whether the Download button should be shown.
    /// </summary>
    public bool ShowDownloadButton => !IsDownloaded && !IsUpdateAvailable;

    /// <summary>
    /// Gets a value indicating whether the Update button should be shown.
    /// </summary>
    public bool ShowUpdateButton => IsUpdateAvailable;

    /// <summary>
    /// Gets a value indicating whether the Add to Profile button should be shown.
    /// </summary>
    public bool ShowAddToProfileButton => IsDownloaded && !IsUpdateAvailable;

    /// <summary>
    /// Command to download the main content.
    /// <summary>
    /// Initiates download of the current content, updates the view-model's download state and progress, and notifies other components and the user on completion.
    /// </summary>
    /// <remarks>
    /// Reports progress to DownloadProgress and DownloadStatusMessage, sets IsDownloading while running, sets IsDownloaded when the download completes successfully, and updates the underlying search result with the acquired manifest ID. Handles cancellation and errors by updating DownloadStatusMessage and logging as appropriate.
    /// </remarks>
    [RelayCommand]
    private async Task DownloadAsync(CancellationToken cancellationToken = default)
    {
        if (IsDownloading)
        {
            return;
        }

        try
        {
            IsDownloading = true;
            DownloadProgress = 0;
            DownloadStatusMessage = "Starting download...";

            _logger.LogInformation("Starting download for content: {Name} ({Provider})", Name, ProviderName);

            // Get the content orchestrator from service provider
            if (_serviceProvider.GetService(typeof(IContentOrchestrator)) is not IContentOrchestrator contentOrchestrator)
            {
                _logger.LogError("IContentOrchestrator service not available");
                DownloadStatusMessage = "Error: Content orchestrator service not available";
                return;
            }

            // Use the ContentOrchestrator to properly acquire content
            // This handles ZIP extraction, manifest factory processing, and proper file storage
            var progress = new Progress<ContentAcquisitionProgress>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    DownloadProgress = (int)p.ProgressPercentage;
                    DownloadStatusMessage = FormatProgressStatus(p);
                });
            });

            var result = await contentOrchestrator.AcquireContentAsync(_searchResult, progress, cancellationToken);

            if (result.Success && result.Data != null)
            {
                var manifest = result.Data;
                _logger.LogInformation("Successfully downloaded and stored content: {ManifestId}", manifest.Id.Value);

                DownloadProgress = 100;
                DownloadStatusMessage = "Download complete!";
                IsDownloaded = true;

                // Update the SearchResult ID with the manifest ID for profile adding
                _searchResult.UpdateId(manifest.Id.Value);

                // Notify other components that content was acquired
                try
                {
                    var message = new ContentAcquiredMessage(manifest);
                    WeakReferenceMessenger.Default.Send(message);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send ContentAcquiredMessage");
                }

                _notificationService.ShowSuccess("Download Complete", $"Downloaded {Name}");
            }
            else
            {
                var errorMsg = result.FirstError ?? "Unknown error";
                _logger.LogError("Failed to download {ItemName}: {Error}", Name, errorMsg);
                DownloadStatusMessage = $"Error: {errorMsg}";
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Download cancelled for: {Name}", Name);
            DownloadStatusMessage = "Download cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading content: {Name}", Name);
            DownloadStatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    /// <summary>
    /// Formats a user-friendly progress status message.
    /// <summary>
    /// Builds a human-readable status message for the given content acquisition progress.
    /// </summary>
    /// <param name="progress">Progress details including phase, current operation, files processed, total files, and progress percentage.</param>
    /// <returns>A status string describing the current phase and, when available, the current operation; otherwise file counts with a computed percentage or an overall percentage.</returns>
    private static string FormatProgressStatus(ContentAcquisitionProgress progress)
    {
        var phaseName = progress.Phase switch
        {
            ContentAcquisitionPhase.Downloading => "Downloading",
            ContentAcquisitionPhase.Extracting => "Extracting",
            ContentAcquisitionPhase.Copying => "Copying",
            ContentAcquisitionPhase.ValidatingManifest => "Validating manifest",
            ContentAcquisitionPhase.ValidatingFiles => "Validating files",
            ContentAcquisitionPhase.Delivering => "Installing",
            ContentAcquisitionPhase.Completed => "Complete",
            _ => "Processing",
        };

        if (!string.IsNullOrEmpty(progress.CurrentOperation))
        {
            return $"{phaseName}: {progress.CurrentOperation}";
        }

        var percentText = progress.ProgressPercentage > 0 ? $"{progress.ProgressPercentage:F0}%" : string.Empty;

        if (progress.TotalFiles > 0)
        {
            var phasePercent = progress.TotalFiles > 0
                ? (int)((double)progress.FilesProcessed / progress.TotalFiles * 100)
                : 0;
            return $"{phaseName}: {progress.FilesProcessed}/{progress.TotalFiles} files ({phasePercent}%)";
        }

        return !string.IsNullOrEmpty(percentText) ? $"{phaseName}... {percentText}" : $"{phaseName}...";
    }

    /// <summary>
    /// Command to update the content (download newer version).
    /// <summary>
    /// Initiates an update for the current content by downloading and applying any available updates.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the update operation.</param>
    /// <returns>A task that completes when the update operation finishes.</returns>
    [RelayCommand]
    private async Task UpdateAsync(CancellationToken cancellationToken = default)
    {
        // Update uses the same download flow as initial download
        await DownloadAsync(cancellationToken);
    }

    /// <summary>
    /// Command to download an individual file from the Files list.
    /// </summary>
    /// <param name="file">The file to download.</param>
    /// <summary>
    /// Initiates downloading of the specified web file for this content item.
    /// </summary>
    /// <param name="file">The web file to download; must have a valid <see cref="WebFile.DownloadUrl"/>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    [RelayCommand]
    private async Task DownloadFileAsync(WebFile file, CancellationToken cancellationToken = default)
    {
        if (file == null || string.IsNullOrEmpty(file.DownloadUrl))
        {
            _logger.LogWarning("Cannot download file: invalid file or missing download URL");
            return;
        }

        try
        {
            _logger.LogInformation("Downloading individual file: {FileName} from {Url}", file.Name, file.DownloadUrl);

            // TODO: Implement individual file download
            // This would use IDownloadService to download the specific file
            // For now, we'll just trigger the main download
            await DownloadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file: {FileName}", file.Name);
        }
    }

    /// <summary>
    /// Command to set the selected screenshot.
    /// </summary>
    /// <summary>
    /// Selects the specified screenshot URL as the currently displayed screenshot.
    /// </summary>
    /// <param name="url">The screenshot URL to select.</param>
    [RelayCommand]
    private void SetSelectedScreenshot(string url)
    {
        SelectedScreenshotUrl = url;
    }

    /// <summary>
    /// Command to add the downloaded content to a game profile.
    /// <summary>
    /// Opens the profile selection dialog to add the current content to a profile.
    /// </summary>
    /// <remarks>
    /// If the content has not been downloaded, logs a warning and shows a user-facing warning instead of opening the dialog.
    /// </remarks>
    [RelayCommand]
    private async Task AddToProfileAsync()
    {
        if (!IsDownloaded)
        {
            _logger.LogWarning("Cannot add to profile: content not downloaded yet");
            _notificationService.ShowWarning("Content Not Downloaded", "Please download the content before adding it to a profile.");
            return;
        }

        _logger.LogInformation("Add to Profile clicked for content: {Name}", Name);

        // Show profile selection dialog
        await ShowProfileSelectionDialogAsync();
    }

    /// <summary>
    /// Downloads an individual file from the WebFile.
    /// </summary>
    /// <summary>
    /// Starts the download process for the specified WebFile by delegating to the primary download flow.
    /// </summary>
    /// <param name="file">The WebFile to download; must have a non-empty DownloadUrl. If null or missing a URL, the method logs a warning and shows an error notification.</param>
    private async Task DownloadFileAsync(WebFile file)
    {
        if (file == null || string.IsNullOrEmpty(file.DownloadUrl))
        {
            _logger.LogWarning("Cannot download file: invalid file or missing download URL");
            _notificationService.ShowError("Download Error", "Invalid file or missing download URL.");
            return;
        }

        try
        {
            _logger.LogInformation("Downloading individual file: {FileName} from {Url}", file.Name, file.DownloadUrl);

            // For individual file downloads, we trigger the main download flow
            // The resolver will handle creating the appropriate manifest
            await DownloadAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file: {FileName}", file.Name);
            _notificationService.ShowError("Download Error", $"Failed to download {file.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a specific file's manifest to a profile.
    /// </summary>
    /// <summary>
    /// Opens the profile selection dialog to add the specified file's manifest to a profile.
    /// </summary>
    /// <param name="file">The WebFile whose manifest should be added; if null the method logs a warning and does nothing.</param>
    private async Task AddFileToProfileAsync(WebFile file)
    {
        if (file == null)
        {
            _logger.LogWarning("Cannot add file to profile: file is null");
            return;
        }

        _logger.LogInformation("Add to Profile clicked for file: {FileName}", file.Name);

        // Show profile selection dialog for this specific file
        await ShowProfileSelectionDialogAsync();
    }

    /// <summary>
    /// Shows the profile selection dialog for adding content to a profile.
    /// <summary>
    /// Shows a profile selection dialog to add the currently selected content to a user profile.
    /// </summary>
    /// <remarks>
    /// If the content has not been downloaded (no manifest ID present) a warning is shown and the method returns.
    /// Otherwise this loads profiles for the content's target game and manifest ID, displays a profile selection dialog owned by the application's main window (when available), and applies logging and user-facing error notifications on failure.
    /// </remarks>
    private async Task ShowProfileSelectionDialogAsync()
    {
        try
        {
            // Determine the content manifest ID to add
            string? contentManifestId = null;
            string? contentName = null;
            GameType targetGame = _searchResult.TargetGame;

            // First, check if the SearchResult has a valid manifest ID (set during download)
            if (!string.IsNullOrEmpty(_searchResult.Id) && _searchResult.Id.Contains('.'))
            {
                // Content was already downloaded, use the manifest ID from SearchResult
                contentManifestId = _searchResult.Id;
                contentName = _searchResult.Name;
            }
            else
            {
                // Content not yet downloaded - prompt user to download first
                _notificationService.ShowWarning(
                    "Content Not Downloaded",
                    "Please download the content before adding it to a profile.");
                return;
            }

            // Create the profile selection view model
            var profileSelectionViewModel = new ProfileSelectionViewModel(
                _serviceProvider.GetService(typeof(ILogger<ProfileSelectionViewModel>)) as ILogger<ProfileSelectionViewModel> ?? throw new InvalidOperationException("ILogger<ProfileSelectionViewModel> not available"),
                _profileManager,
                _profileContentService);

            // Load profiles into the view model
            await profileSelectionViewModel.LoadProfilesAsync(targetGame, contentManifestId, contentName);

            // Create the profile selection dialog
            var dialog = new ProfileSelectionView(profileSelectionViewModel);

            // Get the current visual window to use as owner
            var currentWindow = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (currentWindow != null)
            {
                await dialog.ShowDialog(currentWindow);
            }
            else
            {
                _logger.LogWarning("No main window found to show profile selection dialog");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing profile selection dialog");
            _notificationService.ShowError("Error", $"Failed to show profile selection dialog: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the collection of releases (from /downloads section for mods).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReleases))]
    private ObservableCollection<ReleaseItemViewModel> _releases = [];

    /// <summary>
    /// Gets a value indicating whether there are releases to display.
    /// </summary>
    public bool HasReleases => Releases?.Count > 0;

    /// <summary>
    /// Gets the collection of addons (from /addons section for mods).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddons))]
    [NotifyPropertyChangedFor(nameof(AddonsCount))]
    private ObservableCollection<AddonItemViewModel> _addons = [];

    /// <summary>
    /// Gets a value indicating whether there are addons to display.
    /// </summary>
    public bool HasAddons => Addons?.Count > 0;

    /// <summary>
    /// Gets the count of addons for display.
    /// </summary>
    public int AddonsCount => Addons?.Count ?? 0;

    /// <summary>
    /// Populates the Releases collection from parsed page data.
    /// </summary>
    /// <summary>
    /// Populate the Releases collection with ReleaseItemViewModel instances created from the provided files that belong to the Downloads section.
    /// </summary>
    /// <param name="files">Sequence of WebFile items to convert; only files with <see cref="FileSectionType.Downloads"/> are used. Each created ReleaseItemViewModel is given a unique Id, mapped metadata, and a DownloadCommand bound to download that file.</param>
    public void PopulateReleases(IEnumerable<WebFile> files)
    {
        Releases.Clear();
        foreach (var file in files.Where(f => f.FileSectionType == FileSectionType.Downloads))
        {
            ReleaseItemViewModel releaseItem = new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = file.Name ?? "Unknown Release",
                Version = file.Version,
                ReleaseDate = file.UploadDate,
                FileSize = file.SizeBytes ?? 0,
                DownloadUrl = file.DownloadUrl,

                // Wire up commands
                DownloadCommand = new RelayCommand(async () => await DownloadFileAsync(file)),
            };

            Releases.Add(releaseItem);
        }
    }

    /// <summary>
    /// Populates the Addons collection from parsed page data.
    /// </summary>
    /// <summary>
    /// Replaces the Addons collection with view models created from the provided files that are marked as addons.
    /// </summary>
    /// <param name="files">Sequence of WebFile objects; those with FileSectionType.Addons are converted to AddonItemViewModel instances and added to the Addons collection (existing items are cleared first).</param>
    public void PopulateAddons(IEnumerable<WebFile> files)
    {
        Addons.Clear();
        foreach (var file in files.Where(f => f.FileSectionType == FileSectionType.Addons))
        {
            AddonItemViewModel addonItem = new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = file.Name ?? "Unknown Addon",
                ReleaseDate = file.UploadDate,
                FileSize = file.SizeBytes ?? 0,
                DownloadUrl = file.DownloadUrl,

                // Wire up commands
                DownloadCommand = new RelayCommand(async () => await DownloadFileAsync(file)),
                AddToProfileCommand = new RelayCommand(async () => await AddFileToProfileAsync(file)),
            };

            Addons.Add(addonItem);
        }
    }
}