using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Parsers;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Parsers;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;
using WebFile = GenHub.Core.Models.Parsers.File;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for the detailed content view.
/// </summary>
public partial class ContentDetailViewModel : ObservableObject, IDisposable
{
    private readonly System.Net.Http.HttpClient _imageClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private readonly ContentSearchResult _searchResult;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ContentDetailViewModel> _logger;
    private readonly IEnumerable<IWebPageParser> _parsers;
    private readonly IDownloadService _downloadService;
    private readonly Action? _closeAction;

    [ObservableProperty]
    private string _selectedScreenshotUrl;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _iconBitmap;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _isDownloaded;

    [ObservableProperty]
    private int _downloadProgress;

    [ObservableProperty]
    private ParsedWebPage? _parsedPage;

    [ObservableProperty]
    private string? _downloadStatusMessage;

    [ObservableProperty]
    private bool _isLoadingDetails;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentDetailViewModel"/> class.
    /// </summary>
    /// <param name="searchResult">The content search result.</param>
    /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
    /// <param name="parsers">The web page parsers.</param>
    /// <param name="downloadService">The download service instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="closeAction">The action to close the view.</param>
    public ContentDetailViewModel(
        ContentSearchResult searchResult,
        IServiceProvider serviceProvider,
        IEnumerable<IWebPageParser> parsers,
        IDownloadService downloadService,
        ILogger<ContentDetailViewModel> logger,
        Action? closeAction = null)
    {
        ArgumentNullException.ThrowIfNull(searchResult);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _searchResult = searchResult;
        _serviceProvider = serviceProvider;
        _parsers = parsers;
        _downloadService = downloadService;
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

        // Load rich content from parsed page if available
        LoadRichContent();

        // If rich content is missing, try to fetch it
        if (ParsedPage == null && !string.IsNullOrEmpty(_searchResult.SourceUrl))
        {
            _ = LoadFullDetailsAsync();
        }

        _ = LoadIconAsync();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _imageClient.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Command to close the detail view (navigate back).
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        _closeAction?.Invoke();
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

    private async Task LoadFullDetailsAsync()
    {
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

            _logger.LogInformation("Fetching full details from {Url} using {Parser}", url, parser.ParserId);

            var parsedPage = await parser.ParseAsync(url);

            // Update on UI thread
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _searchResult.ParsedPageData = parsedPage;
                LoadRichContent();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load full details for {Url}", _searchResult.SourceUrl);
        }
        finally
        {
            IsLoadingDetails = false;
        }
    }

    /// <summary>
    /// Loads rich content from the parsed web page data.
    /// </summary>
    private void LoadRichContent()
    {
        // Check both the new ParsedPageData property and the legacy Data property
        var parsedPage = _searchResult.ParsedPageData ?? _searchResult.GetData<ParsedWebPage>();
        if (parsedPage == null) return;

        ParsedPage = parsedPage;

        // Notify property changes for all parsed content collections
        OnPropertyChanged(nameof(Articles));
        OnPropertyChanged(nameof(Videos));
        OnPropertyChanged(nameof(Images));
        OnPropertyChanged(nameof(Files));
        OnPropertyChanged(nameof(Reviews));
        OnPropertyChanged(nameof(Comments));

        // Notify visibility properties
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(HasImages));
        OnPropertyChanged(nameof(HasVideos));
        OnPropertyChanged(nameof(HasComments));
        OnPropertyChanged(nameof(HasReviews));
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(HasCommunity));
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
    /// Gets the content name.
    /// </summary>
    public string Name => _searchResult.Name ?? "Unknown";

    /// <summary>
    /// Gets the content description (full) - prefers parsed page context description.
    /// </summary>
    public string Description =>
        ParsedPage?.Context.Description ?? _searchResult.Description ?? string.Empty;

    /// <summary>
    /// Gets the author name.
    /// </summary>
    public string AuthorName => _searchResult.AuthorName ?? "Unknown";

    /// <summary>
    /// Gets the version.
    /// </summary>
    public string Version => _searchResult.Version ?? string.Empty;

    /// <summary>
    /// Gets the last updated date (optional).
    /// </summary>
    public DateTime? LastUpdated => _searchResult.LastUpdated;

    /// <summary>
    /// Gets the formatted last updated string.
    /// </summary>
    public string LastUpdatedDisplay => LastUpdated?.ToString("MMM dd, yyyy") ?? "Unknown Date";

    /// <summary>
    /// Gets the download size.
    /// </summary>
    public long DownloadSize => _searchResult.DownloadSize;

    /// <summary>
    /// Gets the content type.
    /// </summary>
    public ContentType ContentType => _searchResult.ContentType;

    /// <summary>
    /// Gets the provider name.
    /// </summary>
    public string ProviderName => _searchResult.ProviderName ?? string.Empty;

    /// <summary>
    /// Gets the icon URL.
    /// </summary>
    public string? IconUrl => _searchResult.IconUrl;

    /// <summary>
    /// Gets the collection of screenshot URLs.
    /// </summary>
    public ObservableCollection<string> Screenshots { get; } = [];

    /// <summary>
    /// Gets the tags.
    /// </summary>
    public IList<string> Tags => _searchResult.Tags;

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
            DownloadStatusMessage = "Resolving content...";

            _logger.LogInformation("Starting download for content: {Name} ({Provider})", Name, ProviderName);

            // Get the content resolver for this provider
            if (_serviceProvider.GetService(typeof(IEnumerable<IContentResolver>)) is not IEnumerable<IContentResolver> resolvers)
            {
                _logger.LogError("IContentResolver services not available");
                DownloadStatusMessage = "Error: Content resolution service not available";
                return;
            }

            var resolverId = !string.IsNullOrEmpty(_searchResult.ResolverId)
                ? _searchResult.ResolverId
                : ProviderName;

            var resolver = resolvers.FirstOrDefault(r => r.ResolverId.Equals(resolverId, StringComparison.OrdinalIgnoreCase));

            if (resolver == null)
            {
                _logger.LogError("No resolver found for provider: {Provider} (ResolverId: {ResolverId})", ProviderName, resolverId);
                DownloadStatusMessage = $"Error: No resolver found for {resolverId}";
                return;
            }

            // Resolve the content to get a manifest
            DownloadStatusMessage = "Creating manifest...";
            var manifestResult = await resolver.ResolveAsync(_searchResult, cancellationToken);

            if (!manifestResult.Success || manifestResult.Data == null)
            {
                _logger.LogError("Failed to resolve content: {Error}", manifestResult.FirstError);
                DownloadStatusMessage = $"Error: {manifestResult.FirstError}";
                return;
            }

            var manifest = manifestResult.Data;
            _logger.LogInformation("Manifest created: {ManifestId}", manifest.Id.Value);

            // 2. Prepare Temp Directory
            var tempDir = Path.Combine(Path.GetTempPath(), "GenHub", "Downloads", manifest.Id.Value);
            Directory.CreateDirectory(tempDir);

            // 3. Download Files
            DownloadStatusMessage = "Downloading files...";
            var remoteFiles = manifest.Files.Where(f => f.SourceType == ContentSourceType.RemoteDownload).ToList();

            if (manifest.Files.Count == 0)
            {
                _logger.LogWarning("Manifest contains no files");
                DownloadStatusMessage = "Error: Manifest has no files";
                return;
            }

            if (remoteFiles.Count == 0)
            {
                _logger.LogInformation("No remote files to download in manifest (content might be pre-downloaded or in CAS)");
            }

            foreach (var file in remoteFiles)
            {
                if (string.IsNullOrEmpty(file.SourcePath)) continue;

                // Use RelativePath if available, otherwise extract from SourcePath
                var fileName = !string.IsNullOrEmpty(file.RelativePath)
                    ? Path.GetFileName(file.RelativePath)
                    : Path.GetFileName(file.SourcePath);
                var targetPath = Path.Combine(tempDir, fileName);

                DownloadStatusMessage = $"Downloading {fileName}...";

                var downloadResult = await _downloadService.DownloadFileAsync(
                    new Uri(file.SourcePath),
                    targetPath,
                    null,
                    new Progress<DownloadProgress>(p =>
                    {
                        // Map 0-100 progress
                         Avalonia.Threading.Dispatcher.UIThread.Post(() => DownloadProgress = (int)p.Percentage);
                    }),
                    cancellationToken);

                if (!downloadResult.Success)
                {
                    _logger.LogError("Failed to download file {Url}: {Error}", file.SourcePath, downloadResult.FirstError);
                    DownloadStatusMessage = $"Error downloading {fileName}";
                    return;
                }
            }

            // 4. Store Manifest
            // Store the manifest in the pool
            DownloadStatusMessage = "Storing manifest...";
            if (_serviceProvider.GetService(typeof(IContentManifestPool)) is not IContentManifestPool manifestPool)
            {
                _logger.LogError("IContentManifestPool service not available");
                DownloadStatusMessage = "Error: Manifest storage service not available";
                return;
            }

            // Pass the temp directory as the source directory
            var addResult = await manifestPool.AddManifestAsync(manifest, tempDir, null, cancellationToken);

            if (!addResult.Success)
            {
                _logger.LogError("Failed to store manifest: {Error}", addResult.FirstError);
                DownloadStatusMessage = $"Error: {addResult.FirstError}";
                return;
            }

            // 5. Cleanup (Optional, assuming pool copied files or moved them.
            // If pool copies, we delete. If moves, directory might be empty or gone.
            // Safe to try delete if exists.)
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup temp download directory {Dir}", tempDir);
            }

            DownloadProgress = 100;
            DownloadStatusMessage = "Download complete!";
            IsDownloaded = true;

            _logger.LogInformation("Successfully downloaded and stored content: {ManifestId}", manifest.Id.Value);
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
    /// Command to download an individual file from the Files list.
    /// </summary>
    /// <param name="file">The file to download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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
    /// <param name="url">The screenshot URL.</param>
    [RelayCommand]
    private void SetSelectedScreenshot(string url)
    {
        SelectedScreenshotUrl = url;
    }
}
