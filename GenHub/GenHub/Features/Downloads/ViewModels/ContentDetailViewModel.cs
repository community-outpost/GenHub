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
public partial class ContentDetailViewModel(
    ContentSearchResult searchResult,
    IServiceProvider serviceProvider,
    IEnumerable<IWebPageParser> parsers,
    IProfileContentService profileContentService,
    IGameProfileManager profileManager,
    INotificationService notificationService,
    ILogger<ContentDetailViewModel> logger,
    Action? closeAction = null) : ObservableObject
{
    private static readonly System.Net.Http.HttpClient _imageClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    // Lazy loading flags to track which sections have been loaded
    private bool _imagesLoaded;
    private bool _videosLoaded;
    private bool _releasesLoaded;
    private bool _addonsLoaded;
    private bool _basicContentLoaded;

    [ObservableProperty]
    private string _selectedScreenshotUrl = searchResult.ScreenshotUrls.FirstOrDefault() ?? string.Empty;

    /// <summary>
    /// Gets the collection of screenshot URLs.
    /// </summary>
    public ObservableCollection<string> Screenshots { get; } = new(searchResult.ScreenshotUrls);

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _iconBitmap;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownloadButton))]
    [NotifyPropertyChangedFor(nameof(ShowAddToProfileButton))]
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
    /// Performs asynchronous initialization of the view model content.
    /// </summary>
    public void Initialize()
    {
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
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        closeAction?.Invoke();
    }

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
    /// </summary>
    private async Task LoadBasicParsedDataAsync()
    {
        if (_basicContentLoaded || ParsedPage != null) return;

        try
        {
            IsLoadingDetails = true;
            if (string.IsNullOrEmpty(searchResult.SourceUrl)) return;

            var parser = parsers.FirstOrDefault(p => p.CanParse(searchResult.SourceUrl));
            if (parser == null)
            {
                // No parser found for this URL
                logger.LogDebug("No parser found for URL: {Url}", searchResult.SourceUrl);
                return;
            }

            logger.LogInformation("Parsing web page: {Url}", searchResult.SourceUrl);

            var parsedPage = await parser.ParseAsync(searchResult.SourceUrl);

            // Update on UI thread
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                searchResult.ParsedPageData = parsedPage;
                _basicContentLoaded = true;

                // Load basic overview data
                LoadRichContent();
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing web page data for {Url}", searchResult.SourceUrl);
        }
        finally
        {
            IsLoadingDetails = false;
        }
    }

    /// <summary>
    /// Ensures the basic parsed page data is loaded before accessing tab content.
    /// </summary>
    private async Task EnsureBasicDataLoadedAsync()
    {
        if (!_basicContentLoaded)
        {
            await LoadBasicParsedDataAsync();
        }
    }

    /// <summary>
    /// Loads rich content from the parsed web page data.
    /// </summary>
    private void LoadRichContent()
    {
        // Check both the new ParsedPageData property and the legacy Data property
        var parsedPage = searchResult.ParsedPageData ?? searchResult.GetData<ParsedWebPage>();
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
    /// </summary>
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
            logger.LogDebug("Images tab loaded for content: {Name}", Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load images for content: {Name}", Name);
        }
        finally
        {
            IsLoadingImages = false;
        }
    }

    /// <summary>
    /// Lazy loads videos when the Videos tab is accessed.
    /// </summary>
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
            logger.LogDebug("Videos tab loaded for content: {Name}", Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load videos for content: {Name}", Name);
        }
        finally
        {
            IsLoadingVideos = false;
        }
    }

    /// <summary>
    /// Lazy loads releases when the Releases tab is accessed.
    /// </summary>
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
            logger.LogDebug("Releases tab loaded for content: {Name}", Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load releases for content: {Name}", Name);
        }
        finally
        {
            IsLoadingReleases = false;
        }
    }

    /// <summary>
    /// Lazy loads addons when the Addons tab is accessed.
    /// </summary>
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
            logger.LogDebug("Addons tab loaded for content: {Name}", Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load addons for content: {Name}", Name);
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
    public string Id => searchResult.Id ?? string.Empty;

    /// <summary>
    /// Gets the content name - prefers parsed page context title.
    /// </summary>
    public string Name => ParsedPage?.Context.Title ?? searchResult.Name ?? "Unknown";

    /// <summary>
    /// Gets the content description (full) - prefers parsed page context description.
    /// </summary>
    public string Description =>
        ParsedPage?.Context.Description ?? searchResult.Description ?? string.Empty;

    /// <summary>
    /// Gets the author name - prefers parsed page context developer.
    /// </summary>
    public string AuthorName =>
        ParsedPage?.Context.Developer ?? searchResult.AuthorName ?? "Unknown";

    /// <summary>
    /// Gets the version.
    /// </summary>
    public string Version => searchResult.Version ?? string.Empty;

    /// <summary>
    /// Gets the last updated date (optional) - prefers parsed page context release date.
    /// </summary>
    public DateTime? LastUpdated => ParsedPage?.Context.ReleaseDate ?? searchResult.LastUpdated;

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

            return searchResult.DownloadSize;
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
    public ContentType ContentType => searchResult.ContentType;

    /// <summary>
    /// Gets the provider name.
    /// </summary>
    public string ProviderName => searchResult.ProviderName ?? string.Empty;

    /// <summary>
    /// Gets the icon URL - prefers parsed page context icon.
    /// </summary>
    public string? IconUrl => ParsedPage?.Context.IconUrl ?? searchResult.IconUrl;

    /// <summary>
    /// Gets the tags.
    /// </summary>
    public IList<string> Tags => searchResult.Tags;

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
    /// </summary>
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

            logger.LogInformation("Starting download for content: {Name} ({Provider})", Name, ProviderName);

            // Get the content orchestrator from service provider
            if (serviceProvider.GetService(typeof(IContentOrchestrator)) is not IContentOrchestrator contentOrchestrator)
            {
                logger.LogError("IContentOrchestrator service not available");
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
                    DownloadStatusMessage = p.FormatProgressStatus();
                });
            });

            var result = await contentOrchestrator.AcquireContentAsync(searchResult, progress, cancellationToken);

            if (result.Success && result.Data != null)
            {
                var manifest = result.Data;
                logger.LogInformation("Successfully downloaded and stored content: {ManifestId}", manifest.Id.Value);

                DownloadProgress = 100;
                DownloadStatusMessage = "Download complete!";
                IsDownloaded = true;

                // Update the SearchResult ID with the manifest ID for profile adding
                searchResult.UpdateId(manifest.Id.Value);

                // Notify other components that content was acquired
                try
                {
                    var message = new ContentAcquiredMessage(manifest);
                    WeakReferenceMessenger.Default.Send(message);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to send ContentAcquiredMessage");
                }

                notificationService.ShowSuccess("Download Complete", $"Downloaded {Name}");
            }
            else
            {
                var errorMsg = result.FirstError ?? "Unknown error";
                logger.LogError("Failed to download {ItemName}: {Error}", Name, errorMsg);
                DownloadStatusMessage = $"Error: {errorMsg}";
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Download cancelled for: {Name}", Name);
            DownloadStatusMessage = "Download cancelled";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error downloading content: {Name}", Name);
            DownloadStatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    /// <summary>
    /// Command to update the content (download newer version).
    /// </summary>
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
    /// <param name="cancellationToken">Cancellation token.</param>
    [RelayCommand]
    private async Task DownloadFileAsync(WebFile file, CancellationToken cancellationToken = default)
    {
        if (file == null || string.IsNullOrEmpty(file.DownloadUrl))
        {
            logger.LogWarning("Cannot download file: invalid file or missing download URL");
            return;
        }

        try
        {
            logger.LogInformation("Downloading individual file: {FileName} from {Url}", file.Name, file.DownloadUrl);

            // TODO: Implement individual file download
            // This would use IDownloadService to download the specific file
            // For now, we'll just trigger the main download
            await DownloadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error downloading file: {FileName}", file.Name);
        }
    }

    /// <summary>
    /// Command to set the selected screenshot.
    /// </summary>
    /// <param name="url">The screenshot URL.</param>
    [RelayCommand]
    private void SetSelectedScreenshot(string url)
    {
        SelectedScreenshotUrl = url;
    }

    /// <summary>
    /// Command to add the downloaded content to a game profile.
    /// </summary>
    [RelayCommand]
    private async Task AddToProfileAsync()
    {
        if (!IsDownloaded)
        {
            logger.LogWarning("Cannot add to profile: content not downloaded yet");
            notificationService.ShowWarning("Content Not Downloaded", "Please download the content before adding it to a profile.");
            return;
        }

        logger.LogInformation("Add to Profile clicked for content: {Name}", Name);

        // Show profile selection dialog
        await ShowProfileSelectionDialogAsync();
    }

    /// <summary>
    /// Downloads an individual file from the WebFile.
    /// </summary>
    /// <param name="file">The file to download.</param>
    private async Task DownloadFileAsync(WebFile file)
    {
        if (file == null || string.IsNullOrEmpty(file.DownloadUrl))
        {
            logger.LogWarning("Cannot download file: invalid file or missing download URL");
            notificationService.ShowError("Download Error", "Invalid file or missing download URL.");
            return;
        }

        try
        {
            logger.LogInformation("Downloading individual file: {FileName} from {Url}", file.Name, file.DownloadUrl);

            // For individual file downloads, we trigger the main download flow
            // The resolver will handle creating the appropriate manifest
            await DownloadAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during download of {Name}", Name);
            notificationService.ShowError("Download Error", $"An unexpected error occurred: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a specific file's manifest to a profile.
    /// </summary>
    /// <param name="file">The file whose manifest should be added to a profile.</param>
    private async Task AddFileToProfileAsync(WebFile file)
    {
        if (file == null)
        {
            logger.LogWarning("Cannot add file to profile: file is null");
            return;
        }

        logger.LogInformation("Add to Profile clicked for file: {FileName}", file.Name);

        // Show profile selection dialog for this specific file
        await ShowProfileSelectionDialogAsync();
    }

    /// <summary>
    /// Shows the profile selection dialog for adding content to a profile.
    /// </summary>
    private async Task ShowProfileSelectionDialogAsync()
    {
        try
        {
            // Determine the content manifest ID to add
            string? contentManifestId = null;
            string? contentName = null;
            GameType targetGame = searchResult.TargetGame;

            // First, check if the SearchResult has a valid manifest ID (set during download)
            if (!string.IsNullOrEmpty(searchResult.Id) && searchResult.Id.Contains('.'))
            {
                // Content was already downloaded, use the manifest ID from SearchResult
                contentManifestId = searchResult.Id;
                contentName = searchResult.Name;
            }
            else
            {
                // Content not yet downloaded - prompt user to download first
                notificationService.ShowWarning(
                    "Content Not Downloaded",
                    "Please download the content before adding it to a profile.");
                return;
            }

            // Create the profile selection view model
            var profileSelectionViewModel = new ProfileSelectionViewModel(
                serviceProvider.GetService(typeof(ILogger<ProfileSelectionViewModel>)) as ILogger<ProfileSelectionViewModel> ?? throw new InvalidOperationException("ILogger<ProfileSelectionViewModel> not available"),
                profileManager,
                profileContentService,
                serviceProvider.GetService(typeof(IContentManifestPool)) as IContentManifestPool ?? throw new InvalidOperationException("IContentManifestPool not available"),
                notificationService);

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
                logger.LogWarning("No main window found to show profile selection dialog");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error showing profile selection dialog: {Message}", ex.Message);
            notificationService.ShowError("Error", $"Failed to show profile selection dialog: {ex.Message}");
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
    /// <param name="files">The files to populate releases from.</param>
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
                AddToProfileCommand = new RelayCommand(async () => await AddFileToProfileAsync(file)),
            };

            Releases.Add(releaseItem);
        }
    }

    /// <summary>
    /// Populates the Addons collection from parsed page data.
    /// </summary>
    /// <param name="files">The files to populate addons from.</param>
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
