using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.Helpers;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Utilities;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Tools.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for the Content Library tab in Publisher Studio.
/// Scoped to the currently active catalog.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Make member static", Justification = "ViewModel properties and methods mutate CommunityToolkit generated instance properties.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "ContentLibraryViewModel requires catalog context, parent coordination, and optional cross-cutting services for library management.")]
public partial class ContentLibraryViewModel(
    PublisherStudioProject project,
    NamedCatalog activeCatalog,
    PublisherStudioViewModel parentViewModel,
    ILogger logger,
    IPublisherStudioDialogService dialogService,
    INotificationService? notificationService = null,
    ILocalizationService? localizationService = null,
    ICatalogUpstreamIngestionService? upstreamIngestionService = null) : ObservableObject
{
    private const int DetailTabReleases = 1;
    private const int DetailTabAddons = 2;
    private const int DetailTabMedia = 3;

    [ObservableProperty]
    private ObservableCollection<CatalogContentItem> _contentItems = InitializeContentItems(activeCatalog, parentViewModel);

    private static bool AreUpstreamSyncsEqual(CatalogUpstreamSync? a, CatalogUpstreamSync? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a == null || b == null)
        {
            return false;
        }

        if (!string.Equals(a.Provider, b.Provider, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(a.Repository, b.Repository, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(a.Channel, b.Channel, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(a.VariantAxis, b.VariantAxis, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(a.ContentCode, b.ContentCode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (a.AssetRules.Count != b.AssetRules.Count)
        {
            return false;
        }

        for (var i = 0; i < a.AssetRules.Count; i++)
        {
            var ruleA = a.AssetRules[i];
            var ruleB = b.AssetRules[i];
            if (!string.Equals(ruleA.Pattern, ruleB.Pattern, StringComparison.Ordinal) ||
                !string.Equals(ruleA.Variant, ruleB.Variant, StringComparison.Ordinal) ||
                ruleA.IsDefault != ruleB.IsDefault ||
                ruleA.TargetGame != ruleB.TargetGame)
            {
                return false;
            }
        }

        return true;
    }

    private static CatalogContentItem? CloneForPreview(CatalogContentItem item)
    {
        try
        {
            return JsonSerializer.Deserialize<CatalogContentItem>(JsonSerializer.Serialize(item));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (string? CatalogIconUrl, string? PublisherAvatarUrl) ResolveCatalogPresentationUrls(
        NamedCatalog? catalog,
        PublisherStudioViewModel? parentViewModel)
    {
        var catalogIcon = catalog?.IconUrl
            ?? catalog?.Catalog?.IconUrl
            ?? catalog?.Catalog?.AvatarUrl
            ?? catalog?.Catalog?.Publisher?.AvatarUrl;
        var publisherAvatar = parentViewModel?.CurrentProject?.Catalog?.Publisher?.AvatarUrl
            ?? catalog?.Catalog?.Publisher?.AvatarUrl
            ?? catalogIcon;

        return (catalogIcon, publisherAvatar);
    }

    private static ObservableCollection<CatalogContentItem> InitializeContentItems(
        NamedCatalog? activeCatalog,
        PublisherStudioViewModel? parentViewModel)
    {
        var items = new ObservableCollection<CatalogContentItem>();
        var catalog = activeCatalog;
        var content = catalog?.Catalog?.Content;
        if (catalog != null && content != null)
        {
            var (catalogIcon, publisherAvatar) = ResolveCatalogPresentationUrls(catalog, parentViewModel);

            foreach (var item in content)
            {
                item.CatalogIconUrl ??= catalogIcon;
                item.PublisherAvatarUrl ??= publisherAvatar;
                items.Add(item);
            }
        }

        return items;
    }

    [ObservableProperty]
    private CatalogContentItem? _selectedContent;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _selectedDetailTabIndex;

    [ObservableProperty]
    private ObservableCollection<ContentRelease> _upstreamPreviewReleases = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpstreamSyncFailedWarning))]
    private bool _isUpstreamPreviewLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpstreamSyncFailedWarning))]
    private bool _upstreamPreviewFailed;

    private CancellationTokenSource? _upstreamPreviewCts;
    private bool _suppressUpstreamPreviewReload;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentLibraryViewModel"/> class with default catalog.
    /// </summary>
    /// <param name="project">The publisher studio project.</param>
    /// <param name="parentViewModel">The parent view model.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="dialogService">The dialog service.</param>
    public ContentLibraryViewModel(
        PublisherStudioProject project,
        PublisherStudioViewModel parentViewModel,
        ILogger logger,
        IPublisherStudioDialogService dialogService)
        : this(project, project?.Catalogs.FirstOrDefault() ?? new NamedCatalog { Id = "default", Name = "Content", Catalog = project?.Catalog ?? new() }, parentViewModel, logger, dialogService, null, null, null)
    {
    }

    /// <summary>
    /// Gets the publisher studio project.
    /// </summary>
    public PublisherStudioProject Project => project;

    /// <summary>
    /// Gets the name of the active catalog.
    /// </summary>
    public string ActiveCatalogName => activeCatalog?.Name ?? string.Empty;

    /// <summary>
    /// Gets a value indicating whether an upstream release preview is available for the selected content item.
    /// </summary>
    public bool HasUpstreamPreview => UpstreamPreviewReleases.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the selected content item tracks a live upstream provider.
    /// Static releases of tracked items are fallback releases: the live upstream releases below supersede them.
    /// </summary>
    public bool SelectedContentTracksUpstream => IsSelectedContentUpstream();

    /// <summary>
    /// Gets the normalized versions of the live upstream preview releases for duplicate detection.
    /// </summary>
    public IReadOnlyList<string> UpstreamPreviewVersions { get; private set; } = [];

    /// <summary>
    /// Gets a value indicating whether the fallback-releases note should be shown above the static list.
    /// </summary>
    public bool ShowFallbackReleasesNote => SelectedContentTracksUpstream && HasUpstreamPreview;

    /// <summary>
    /// Gets a value indicating whether the manual fallback releases expander should be shown.
    /// </summary>
    public bool ShowManualFallbackReleases => ShowFallbackReleasesNote && (SelectedContent?.Releases?.Count ?? 0) > 0;

    /// <summary>
    /// Gets a value indicating whether the static releases list should be shown as the main list.
    /// When live upstream releases are available they take over the main list and the
    /// static releases collapse into the fallback expander instead.
    /// </summary>
    public bool ShowStaticReleasesList => !HasUpstreamPreview;

    /// <summary>
    /// Gets a value indicating whether a manual release can be added to the selected content item.
    /// Upstream-tracked items receive their releases from the provider, and content bundles
    /// version their dependency graph through the bundle editor, so manual adds are hidden.
    /// </summary>
    public bool CanAddManualRelease => !SelectedContentTracksUpstream && SelectedContent?.ContentType != ContentType.ContentBundle;

    /// <summary>
    /// Gets a value indicating whether the releases drop zone should be shown.
    /// Hidden for upstream-tracked items, which receive releases from the provider,
    /// and for content bundles, which have no file artifacts.
    /// </summary>
    public bool ShowReleasesDropZone => CanAddManualRelease && (SelectedContent?.Releases.Count ?? 0) == 0;

    /// <summary>
    /// Gets a value indicating whether the upstream sync failure warning should be shown.
    /// </summary>
    public bool ShowUpstreamSyncFailedWarning => SelectedContentTracksUpstream && UpstreamPreviewFailed && !IsUpstreamPreviewLoading;

    /// <summary>
    /// Gets a value indicating whether the selected content is a content bundle.
    /// Bundle releases carry only dependencies, never file artifacts.
    /// </summary>
    public bool IsSelectedContentBundle => SelectedContent?.ContentType == ContentType.ContentBundle;

    /// <summary>
    /// Gets a value indicating whether release artifact UI should be shown.
    /// Hidden for content bundles, which version a dependency graph instead of files.
    /// </summary>
    public bool ShowReleaseArtifacts => !IsSelectedContentBundle;

    /// <summary>
    /// Gets the effective release count for the selected content item (upstream live count when tracked, or static count).
    /// </summary>
    public int EffectiveSelectedContentReleasesCount
    {
        get
        {
            if (SelectedContentTracksUpstream && HasUpstreamPreview)
            {
                return UpstreamPreviewReleases.Count;
            }

            return SelectedContent?.Releases?.Count ?? 0;
        }
    }

    /// <summary>
    /// Gets the localized catalog item count summary for the footer.
    /// </summary>
    public string CatalogSummaryText => string.Format(
        localizationService?.GetString("Tools.PublisherStudio.Library.ItemsInCatalog") ?? "{0} items in catalog",
        ContentItems.Count);

    /// <summary>
    /// Gets filtered content items based on the search query.
    /// </summary>
    public ObservableCollection<CatalogContentItem> FilteredContent
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return ContentItems;
            }

            var query = SearchText.Trim();
            return new ObservableCollection<CatalogContentItem>(
                ContentItems.Where(item =>
                    (item.Name is { } name && name.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (item.Id is { } id && id.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (item.Description is { } desc && desc.Contains(query, StringComparison.OrdinalIgnoreCase))));
        }
    }

    /// <summary>
    /// Gets the list of catalogs in the project.
    /// </summary>
    public ObservableCollection<NamedCatalog>? Catalogs => parentViewModel?.Catalogs;

    /// <summary>
    /// Gets or sets the selected catalog.
    /// </summary>
    public NamedCatalog? SelectedCatalog
    {
        get => parentViewModel?.SelectedCatalog;
        set
        {
            if (parentViewModel != null && parentViewModel.SelectedCatalog != value)
            {
                parentViewModel.SelectedCatalog = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the current catalog can be removed.
    /// </summary>
    public bool CanRemoveCatalog => parentViewModel?.CanRemoveCatalog ?? false;

    /// <summary>
    /// Gets the command to add a new catalog.
    /// </summary>
    public IRelayCommand? AddCatalogCommand => parentViewModel?.AddCatalogCommand;

    /// <summary>
    /// Gets the command to import a catalog JSON file.
    /// </summary>
    public IAsyncRelayCommand? ImportCatalogCommand => parentViewModel?.ImportCatalogCommand;

    /// <summary>
    /// Gets the command to remove a catalog.
    /// </summary>
    public IAsyncRelayCommand<NamedCatalog>? RemoveCatalogCommand => parentViewModel?.RemoveCatalogCommand;

    private int? _cachedPendingUploadCount;

    /// <summary>
    /// Gets the number of local artifacts and gallery media files in the selected content that are still waiting for cloud upload.
    /// </summary>
    public int PendingUploadCount
    {
        get
        {
            if (_cachedPendingUploadCount.HasValue)
            {
                return _cachedPendingUploadCount.Value;
            }

            var projectDirectory = Path.GetDirectoryName(Project.ProjectPath);
            var count = CountPendingLocalArtifacts(SelectedContent) + CountPendingLocalMedia(SelectedContent, projectDirectory);
            _cachedPendingUploadCount = count;
            return count;
        }
    }

    private static int CountPendingLocalArtifacts(CatalogContentItem? item) =>
        (item?.Releases ?? [])
            .SelectMany(r => r.Artifacts)
            .Count(a => !string.IsNullOrEmpty(a.LocalFilePath) && string.IsNullOrEmpty(a.DownloadUrl));

    private static int CountPendingLocalMedia(CatalogContentItem? item, string? projectDirectory)
    {
        if (item?.Metadata == null)
        {
            return 0;
        }

        var count = CountPendingReleaseMedia(item.Metadata.ScreenshotUrls, projectDirectory);
        count += CountPendingReleaseMedia(item.Metadata.VideoUrls, projectDirectory);
        if (IsUploadableMedia(projectDirectory, item.Metadata.VideoUrl))
        {
            count++;
        }

        count += (item.Releases ?? []).Sum(release => CountPendingReleaseMedia(release.ImageUrls, projectDirectory) + CountPendingReleaseMedia(release.VideoUrls, projectDirectory));
        count += (item.AddonReleases ?? []).Sum(addon => CountPendingReleaseMedia(addon.ImageUrls, projectDirectory) + CountPendingReleaseMedia(addon.VideoUrls, projectDirectory));
        return count;
    }

    private static int CountPendingReleaseMedia(List<string>? urls, string? projectDirectory) =>
        urls?.Count(url => IsUploadableMedia(projectDirectory, url)) ?? 0;

    private static bool IsUploadableMedia(string? projectDirectory, string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !MediaFileHelper.IsRemoteHttpUrl(value)
        && MediaFileHelper.TryResolveLocalMediaPath(projectDirectory, value) != null;

    /// <summary>
    /// Gets a value indicating whether a hosting provider is currently connected.
    /// </summary>
    public bool IsHostingConnected => parentViewModel?.PublishShareViewModel?.IsProviderAuthenticated ?? false;

    /// <summary>
    /// Gets a value indicating whether the hosting hint banner should be shown.
    /// Shown when local files are pending upload but no hosting provider is connected.
    /// </summary>
    public bool ShowHostingHint => PendingUploadCount > 0 && !IsHostingConnected;

    /// <summary>
    /// Gets the localized hosting hint message for pending uploads.
    /// </summary>
    public string HostingHintMessage => string.Format(
        localizationService?.GetString("Tools.PublisherStudio.Library.HostingHintMessage")
            ?? "{0} file(s) are waiting for upload. Connect a hosting provider to publish them to the cloud.",
        PendingUploadCount);

    /// <summary>
    /// Refreshes hosting-related hint bindings, for example after a provider was connected on another tab.
    /// </summary>
    public void RefreshHostingHint()
    {
        _cachedPendingUploadCount = null;
        OnPropertyChanged(nameof(PendingUploadCount));
        OnPropertyChanged(nameof(IsHostingConnected));
        OnPropertyChanged(nameof(ShowHostingHint));
        OnPropertyChanged(nameof(HostingHintMessage));
    }

    /// <summary>
    /// Adds a new content item to the active catalog with an optional initial folder/file path.
    /// </summary>
    /// <param name="initialPath">Optional initial path of dropped or selected folder/file.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task AddContentWithPathAsync(string? initialPath) =>
        AddContentWithPathsAsync(initialPath != null ? [initialPath] : null);

    /// <summary>
    /// Attempts to import a catalog from a dropped file path.
    /// </summary>
    /// <param name="filePath">The path of the dropped file.</param>
    /// <param name="announceFailures">True to announce parse failures; false otherwise.</param>
    /// <returns>True if the file was recognized as a catalog and processed; false otherwise.</returns>
    public Task<bool> TryImportCatalogFileAsync(string filePath, bool announceFailures = false) =>
        parentViewModel != null ? parentViewModel.ImportCatalogFromFileAsync(filePath, announceFailures) : Task.FromResult(false);

    /// <summary>
    /// Shows an error notification when drag-and-drop import fails unexpectedly.
    /// </summary>
    public void NotifyDropFailed()
    {
        var title = GetLocalizedString("Tools.PublisherStudio.Library.DropErrorTitle", "Import Failed");
        var message = GetLocalizedString("Tools.PublisherStudio.Library.DropErrorMessage", "Could not import the dropped files. See logs for details.");
        notificationService?.ShowError(title, message);
    }

    /// <summary>
    /// Adds a new content item to the active catalog with optional initial folder/file paths.
    /// </summary>
    /// <param name="initialPaths">Optional initial paths of dropped or selected folders/files.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task AddContentWithPathsAsync(IEnumerable<string>? initialPaths)
    {
        var pathsList = initialPaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (pathsList is { Count: > 0 } && !pathsList.Any(p => System.IO.File.Exists(p) || System.IO.Directory.Exists(p)))
        {
            var title = GetLocalizedString("Tools.PublisherStudio.Content.InvalidPathTitle", "Invalid Path");
            var message = GetLocalizedString("Tools.PublisherStudio.Content.InvalidPathMessage", "The specified file or folder does not exist.");
            notificationService?.ShowWarning(title, message);
            return;
        }

        var newContent = await dialogService.ShowAddContentDialogAsync(pathsList, activeCatalog.Catalog);
        if (newContent != null)
        {
            if (activeCatalog.Catalog.Content.Any(c => string.Equals(c.Id, newContent.Id, StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogWarning("Content with ID '{ContentId}' already exists in catalog '{CatalogId}'", newContent.Id, activeCatalog.Id);
                var title = GetLocalizedString("Tools.PublisherStudio.Content.DuplicateIdTitle", "Duplicate Content ID");
                var message = string.Format(
                    GetLocalizedString(
                        "Tools.PublisherStudio.Content.DuplicateIdMessageFormat",
                        "A content item with ID '{0}' already exists in this catalog."),
                    newContent.Id);
                notificationService?.ShowWarning(title, message);
                return;
            }

            var (catalogIcon, publisherAvatar) = ResolveCatalogPresentationUrls(activeCatalog, parentViewModel);
            newContent.CatalogIconUrl = catalogIcon;
            newContent.PublisherAvatarUrl ??= publisherAvatar;
            activeCatalog.Catalog?.Content.Add(newContent);
            ContentItems.Add(newContent);
            OnPropertyChanged(nameof(FilteredContent));
            OnPropertyChanged(nameof(CatalogSummaryText));
            SelectedContent = newContent;

            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Added new content item: {ContentId} to catalog: {CatalogId}", newContent.Id, activeCatalog.Id);
        }
    }

    /// <summary>
    /// Batch imports multiple archive or folder paths as individual content items (one release each).
    /// </summary>
    /// <param name="paths">The collection of file or folder paths to import.</param>
    /// <param name="cancellationToken">A token to cancel the import and hashing work.</param>
    /// <returns>A task representing the asynchronous operation, returning the count of items imported.</returns>
    public async Task<int> BatchImportContentItemsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var validPaths = paths
            .Where(p => !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p)))
            .ToList();

        if (validPaths.Count == 0)
        {
            ShowInvalidPathWarning();
            return 0;
        }

        var importedCount = 0;
        CatalogContentItem? lastCreated = null;

        try
        {
            foreach (var path in validPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = await CreateBatchContentItemAsync(path, cancellationToken);
                if (item == null)
                {
                    continue;
                }

                activeCatalog.Catalog.Content.Add(item);
                ContentItems.Add(item);
                lastCreated = item;
                importedCount++;
                logger.LogInformation("Batch imported content item '{ContentId}' from '{Path}'", item.Id, path);
            }
        }
        finally
        {
            if (importedCount > 0)
            {
                await FinalizeBatchImportAsync(importedCount, lastCreated);
            }
        }

        return importedCount;
    }

    /// <summary>
    /// Refreshes localized display text after a culture change.
    /// </summary>
    public void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(CatalogSummaryText));
    }

    /// <summary>
    /// Gets a value indicating whether the active catalog needs publishing.
    /// </summary>
    public bool ActiveCatalogNeedsPublish =>
        parentViewModel?.PublishShareViewModel?.CatalogNeedsPublish(activeCatalog.Id) ?? true;

    /// <summary>
    /// Rebuilds the content detail display after artifact uploads mutate models in place.
    /// Invoked from the publish pipeline so pending badges and hints update without switching tabs.
    /// </summary>
    public void RefreshContentDisplay()
    {
        RefreshSelectedContent();
        OnPropertyChanged(nameof(CatalogSummaryText));
        OnPropertyChanged(nameof(ActiveCatalogNeedsPublish));
    }

    /// <summary>
    /// Handles dropped files onto the Addon section.
    /// </summary>
    /// <param name="paths">The dropped file or directory paths.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task AddAddonWithPathsAsync(IEnumerable<string> paths)
    {
        if (SelectedContent == null)
        {
            return;
        }

        var created = await dialogService.ShowAddAddonDialogAsync(SelectedContent, activeCatalog.Catalog, paths);
        if (created != null)
        {
            SelectedContent.AddonReleases.Add(created);
            RefreshSelectedContent();
            SelectedDetailTabIndex = DetailTabAddons;
            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }
        }
    }

    /// <summary>
    /// Handles dropped files onto the Release section.
    /// </summary>
    /// <param name="paths">The dropped file or directory paths.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task AddReleaseWithPathsAsync(IEnumerable<string> paths)
    {
        if (SelectedContent == null || !CanAddManualRelease)
        {
            return;
        }

        var created = await dialogService.ShowAddReleaseDialogAsync(SelectedContent, activeCatalog.Catalog, paths);
        if (created != null)
        {
            if (created.IsLatest)
            {
                foreach (var rel in SelectedContent.Releases)
                {
                    rel.IsLatest = false;
                }
            }

            SelectedContent.Releases.Add(created);
            RefreshSelectedContent();
            SelectedDetailTabIndex = DetailTabReleases;
            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }
        }
    }

    /// <summary>
    /// Adds dropped screenshots and videos to the selected content item's metadata.
    /// </summary>
    /// <param name="paths">The dropped file paths.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task AddMediaToSelectedContentAsync(IEnumerable<string> paths)
    {
        if (SelectedContent == null) return;

        SelectedContent.Metadata ??= new();
        var added = false;
        foreach (var raw in paths)
        {
            var path = raw?.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var mediaPath = NormalizeMediaPath(path);
            if (TryAppendMediaToMetadata(SelectedContent.Metadata, path, mediaPath))
            {
                added = true;
            }
        }

        if (!added)
        {
            var title = GetLocalizedString("Tools.PublisherStudio.Library.DropErrorTitle", "Import Failed");
            var message = GetLocalizedString("Tools.PublisherStudio.Library.UnsupportedMediaDropMessage", "No supported image or video files found.");
            notificationService?.ShowWarning(title, message);
            return;
        }

        var contentId = SelectedContent.Id;
        RefreshSelectedContent();
        SelectedDetailTabIndex = DetailTabMedia;
        MarkProjectAndCatalogDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectAsync();
        }

        logger.LogInformation("Added media to {ContentId}", contentId);
    }

    /// <summary>
    /// Updates the active catalog icon and propagates it immediately to all content items.
    /// </summary>
    /// <param name="iconUrl">The new icon URL or path.</param>
    public void UpdateActiveCatalogIcon(string? iconUrl)
    {
        if (activeCatalog != null)
        {
            activeCatalog.IconUrl = iconUrl;
            if (activeCatalog.Catalog != null)
            {
                activeCatalog.Catalog.IconUrl = iconUrl;
                activeCatalog.Catalog.AvatarUrl = iconUrl;
            }
        }

        foreach (var item in ContentItems)
        {
            item.CatalogIconUrl = iconUrl;
        }

        var content = activeCatalog?.Catalog?.Content;
        if (content != null)
        {
            foreach (var item in content)
            {
                item.CatalogIconUrl = iconUrl;
            }
        }

        OnPropertyChanged(nameof(CatalogSummaryText));
    }

    private static string NormalizeMediaPath(string path)
    {
        if (Path.IsPathRooted(path) && !path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var escapedPath = path.Replace("%", "%25", StringComparison.Ordinal).Replace("#", "%23", StringComparison.Ordinal);
                return new Uri(escapedPath).AbsoluteUri;
            }
            catch (UriFormatException)
            {
                return path;
            }
        }

        return path;
    }

    private static bool TryAppendMediaToMetadata(ContentRichMetadata metadata, string path, string mediaPath)
    {
        if (MediaFileHelper.IsImageFile(path)
            && !metadata.ScreenshotUrls.Contains(mediaPath, StringComparer.OrdinalIgnoreCase))
        {
            metadata.ScreenshotUrls.Add(mediaPath);
            return true;
        }

        if (MediaFileHelper.IsVideoFile(path)
            && !metadata.VideoUrls.Contains(mediaPath, StringComparer.OrdinalIgnoreCase))
        {
            metadata.VideoUrls.Add(mediaPath);
            return true;
        }

        return false;
    }

    private static ContentType ClassifyBatchContentType(string rawName, string path)
    {
        // A lone .map file is always a single map regardless of its file name.
        if (File.Exists(path)
            && string.Equals(Path.GetExtension(path), ".map", StringComparison.OrdinalIgnoreCase))
        {
            return ContentType.Map;
        }

        // Match whole-word tokens so names like "models" or "modern" are not misclassified.
        var tokens = rawName.Split(
            [' ', '-', '_', '.', '(', ')', '[', ']', '{', '}'],
            StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Any(t => t.Equals("map", StringComparison.OrdinalIgnoreCase)
            || t.Equals("maps", StringComparison.OrdinalIgnoreCase)
            || t.Equals("mappack", StringComparison.OrdinalIgnoreCase)
            || t.Equals("mappacks", StringComparison.OrdinalIgnoreCase)))
        {
            return ContentType.MapPack;
        }

        if (tokens.Any(t => t.Equals("mod", StringComparison.OrdinalIgnoreCase)
            || t.Equals("mods", StringComparison.OrdinalIgnoreCase)))
        {
            return ContentType.Mod;
        }

        if (tokens.Any(t => t.Equals("patch", StringComparison.OrdinalIgnoreCase)
            || t.Equals("patches", StringComparison.OrdinalIgnoreCase)))
        {
            return ContentType.Patch;
        }

        return ContentType.MapPack;
    }

    private static List<string> BuildBatchContentTags(bool isMap)
    {
        if (isMap)
        {
            return ["maps", "mappack", CatalogConstants.ZeroHourContentId];
        }

        return [CatalogConstants.ZeroHourContentId];
    }

    [GeneratedRegex(@"[_\-]+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UnderscoreDashRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^a-z0-9\-]+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NonSlugCharRegex();

    [GeneratedRegex(@"-+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DashRunRegex();

    private void MarkProjectAndCatalogDirty()
    {
        parentViewModel?.MarkDirty();
        parentViewModel?.PublishShareViewModel?.MarkCatalogChanged(activeCatalog.Id);
        OnPropertyChanged(nameof(ActiveCatalogNeedsPublish));
    }

    /// <summary>
    /// Uploads a single artifact directly from the Content Library pending list.
    /// </summary>
    /// <param name="artifact">The artifact to upload.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task UploadArtifactAsync(ReleaseArtifact? artifact)
    {
        if (artifact == null)
        {
            return;
        }

        var publishShare = GetPublishShareOrWarn();
        if (publishShare == null)
        {
            return;
        }

        await publishShare.UploadArtifactFromLibraryAsync(artifact);
    }

    /// <summary>
    /// Navigates to the Hosting &amp; Cloud Storage tab.
    /// </summary>
    [RelayCommand]
    private void GoToHosting()
    {
        if (parentViewModel != null)
        {
            parentViewModel.SelectedTabIndex = PublisherStudioViewModel.TabHostingStorage;
        }
    }

    /// <summary>
    /// Uploads the active catalog to the connected hosting provider.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task UploadActiveCatalogAsync()
    {
        var publishShare = GetPublishShareOrWarn();
        if (publishShare == null)
        {
            return;
        }

        if (publishShare.IsUploading)
        {
            notificationService?.ShowWarning(
                GetLocalizedString("Tools.PublisherStudio.Publish.PublishCatalog", "Publish Catalog"),
                GetLocalizedString("Tools.PublisherStudio.Publish.UploadAlreadyInProgress", "An upload is already in progress."));
            return;
        }

        await publishShare.PublishCatalogByIdCommand.ExecuteAsync(activeCatalog.Id);
    }

    private PublishShareViewModel? GetPublishShareOrWarn()
    {
        var publishShare = parentViewModel?.PublishShareViewModel;
        if (publishShare != null)
        {
            return publishShare;
        }

        var title = GetLocalizedString("Tools.PublisherStudio.Publish.ProviderNotConnected", "Provider Not Connected");
        var message = GetLocalizedString("Tools.PublisherStudio.Hosting.ConnectBeforeUpload", "Connect to your hosting provider before uploading files.");
        notificationService?.ShowWarning(title, message);
        return null;
    }

    /// <summary>
    /// Renames the active catalog.
    /// </summary>
    [RelayCommand]
    private async Task RenameCatalogAsync()
    {
        if (parentViewModel != null)
        {
            await parentViewModel.RenameCatalogCommand.ExecuteAsync(activeCatalog);
            OnPropertyChanged(nameof(ActiveCatalogName));
            OnPropertyChanged(nameof(SelectedCatalog));
        }
    }

    /// <summary>
    /// Adds a new content item to the active catalog.
    /// </summary>
    [RelayCommand]
    private async Task AddContentAsync()
    {
        await AddContentWithPathAsync(null);
    }

    /// <summary>
    /// Opens a multi-file picker to batch import multiple archives or directories as separate content items (1 release each).
    /// </summary>
    [RelayCommand]
    private async Task BatchAddContentAsync()
    {
        var files = await dialogService.ShowFilesPickerAsync(
            GetLocalizedString("Tools.PublisherStudio.Library.BatchImportPickerTitle", "Select Files or Archives to Batch Import"));
        if (files is { Count: > 0 })
        {
            // Batch import is independent of the upstream preview lifecycle, so it explicitly
            // opts out of cancellation instead of reusing the preview token.
            await BatchImportContentItemsAsync(files, CancellationToken.None);
        }
    }

    /// <summary>
    /// Edits a content item using a pre-populated dialog.
    /// </summary>
    /// <param name="item">The item to edit, or null to edit the selected item.</param>
    [RelayCommand]
    private async Task EditContentAsync(CatalogContentItem? item)
    {
        var target = item ?? SelectedContent;
        if (target == null) return;

        var edited = await dialogService.ShowEditContentDialogAsync(
            target,
            activeCatalog.Catalog,
            onDelete: async itemToDelete =>
            {
                await DeleteContentInternalAsync(itemToDelete);
            });

        if (edited != null)
        {
            var previousUpstreamSync = target.UpstreamSync;
            var upstreamChanged = !AreUpstreamSyncsEqual(previousUpstreamSync, edited.UpstreamSync);

            // Update the existing item's properties
            target.Name = edited.Name;
            target.Description = edited.Description;
            target.ContentType = edited.ContentType;
            target.TargetGame = edited.TargetGame;
            target.Tags = edited.Tags;
            target.ExtendsContentId = edited.ExtendsContentId;
            target.Metadata = edited.Metadata;
            target.IsFeatured = edited.IsFeatured;
            target.FeaturedBadge = edited.FeaturedBadge;
            target.UpstreamSync = edited.UpstreamSync;
            target.PublisherType = edited.PublisherType;
            target.BundledItems = edited.BundledItems;
            target.Releases = edited.Releases;
            target.Addons = edited.Addons;
            target.AddonReleases = edited.AddonReleases;
            var (catalogIcon, publisherAvatar) = ResolveCatalogPresentationUrls(activeCatalog, parentViewModel);
            target.CatalogIconUrl = catalogIcon;
            target.PublisherAvatarUrl = publisherAvatar;
            target.NotifyPresentationChanged();

            // Trigger UI update
            RefreshSelectedContent();

            if (upstreamChanged && ReferenceEquals(target, SelectedContent))
            {
                BeginUpstreamPreviewLoad(target);
            }

            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Updated content item: {ContentId}", target.Id);
        }
    }

    /// <summary>
    /// Deletes a content item from the active catalog.
    /// </summary>
    /// <param name="item">The item to delete, or null to delete the selected item.</param>
    [RelayCommand]
    private async Task DeleteContentAsync(CatalogContentItem? item)
    {
        var target = item ?? SelectedContent;
        if (target == null)
        {
            return;
        }

        var title = GetLocalizedString("Tools.PublisherStudio.Content.DeleteContentTitle", "Delete Content Item");
        var message = string.Format(
            GetLocalizedString(
                "Tools.PublisherStudio.Content.DeleteContentMessageFormat",
                "Are you sure you want to delete '{0}' ({1})? This will also remove all its releases and artifacts."),
            target.Name,
            target.Id);

        var confirmed = await dialogService.ShowConfirmationAsync(title, message);

        if (!confirmed)
        {
            return;
        }

        await DeleteContentInternalAsync(target);
    }

    private async Task DeleteContentInternalAsync(CatalogContentItem target)
    {
        var contentId = target.Id;
        activeCatalog.Catalog.Content.Remove(target);
        ContentItems.Remove(target);
        if (SelectedContent == target)
        {
            SelectedContent = null;
        }

        OnPropertyChanged(nameof(FilteredContent));
        OnPropertyChanged(nameof(CatalogSummaryText));

        MarkProjectAndCatalogDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectAsync();
        }

        logger.LogInformation("Deleted content item: {ContentId} from catalog: {CatalogId}", contentId, activeCatalog.Id);
    }

    /// <summary>
    /// Adds a release to the selected content item.
    /// </summary>
    [RelayCommand]
    private async Task AddReleaseAsync()
    {
        if (SelectedContent == null || !CanAddManualRelease)
        {
            return;
        }

        var newRelease = await dialogService.ShowAddReleaseDialogAsync(SelectedContent, activeCatalog.Catalog);
        if (newRelease != null)
        {
            // If marked as latest, unmark existing
            if (newRelease.IsLatest)
            {
                foreach (var rel in SelectedContent.Releases)
                {
                    rel.IsLatest = false;
                }
            }

            SelectedContent.Releases.Add(newRelease);
            RefreshSelectedContent();
            SelectedDetailTabIndex = DetailTabReleases;

            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Added new release to content: {ContentId} in catalog: {CatalogId} (v{Version})", SelectedContent.Id, activeCatalog.Id, newRelease.Version);
        }
    }

    /// <summary>
    /// Adds a bundled item (dependency) to the selected content item.
    /// </summary>
    [RelayCommand]
    private async Task AddBundledItemAsync()
    {
        if (SelectedContent == null) return;

        var dependency = await dialogService.ShowAddDependencyDialogAsync(activeCatalog.Catalog, SelectedContent);
        if (dependency != null)
        {
            SelectedContent.BundledItems.Add(dependency);
            RefreshSelectedContent();

            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Added bundled item to {ContentId} in catalog: {CatalogId}: {DependencyId}", SelectedContent.Id, activeCatalog.Id, dependency.ContentId);
        }
    }

    /// <summary>
    /// Removes a bundled item (dependency) from the selected content item.
    /// </summary>
    [RelayCommand]
    private async Task RemoveBundledItemAsync(CatalogDependency? dependency)
    {
        if (SelectedContent == null || dependency == null) return;

        SelectedContent.BundledItems.Remove(dependency);
        RefreshSelectedContent();

        MarkProjectAndCatalogDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectAsync();
        }

        logger.LogInformation("Removed bundled item {DependencyId} from {ContentId}", dependency.ContentId, SelectedContent.Id);
    }

    /// <summary>
    /// Removes a screenshot or video URL from the selected content item's metadata.
    /// </summary>
    [RelayCommand]
    private async Task RemoveMediaFromSelectedContentAsync(string? url)
    {
        if (SelectedContent?.Metadata == null || string.IsNullOrWhiteSpace(url)) return;

        var removedShots = SelectedContent.Metadata.ScreenshotUrls.RemoveAll(u => u.Equals(url, StringComparison.OrdinalIgnoreCase));
        var removedVideos = SelectedContent.Metadata.VideoUrls.RemoveAll(u => u.Equals(url, StringComparison.OrdinalIgnoreCase));
        var removedLegacy = 0;
        if (string.Equals(SelectedContent.Metadata.VideoUrl, url, StringComparison.OrdinalIgnoreCase))
        {
            SelectedContent.Metadata.VideoUrl = null;
            removedLegacy = 1;
        }

        if (removedShots + removedVideos + removedLegacy == 0) return;

        var contentId = SelectedContent.Id;
        RefreshSelectedContent();
        MarkProjectAndCatalogDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectAsync();
        }

        logger.LogInformation("Removed media {Url} from {ContentId}", url, contentId);
    }

    /// <summary>
    /// Opens a screenshot or video URL in the default browser or viewer.
    /// </summary>
    [RelayCommand]
    private void OpenMediaUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        var target = url.Trim();
        try
        {
            if (File.Exists(target) && (MediaFileHelper.IsImageFile(target) || MediaFileHelper.IsVideoFile(target)))
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return;
            }

            if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
            {
                if (uri.IsFile)
                {
                    var local = uri.LocalPath;
                    if (File.Exists(local) && (MediaFileHelper.IsImageFile(local) || MediaFileHelper.IsVideoFile(local)))
                    {
                        Process.Start(new ProcessStartInfo(local) { UseShellExecute = true });
                        return;
                    }
                }
                else if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    return;
                }
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            logger.LogWarning(ex, "Failed to open media URL: {Url}", target);
        }

        var title = GetLocalizedString("Tools.PublisherStudio.Library.MediaOpenFailedTitle", "Cannot Open Media");
        var message = GetLocalizedString("Tools.PublisherStudio.Library.MediaOpenFailedMessage", "This media entry is neither an existing file nor a valid web URL.");
        notificationService?.ShowWarning(title, message);
    }

    /// <summary>
    /// Adds an addon dependency to the selected content item.
    /// </summary>
    /// <summary>
    /// Adds an addon release to the selected content item.
    /// </summary>
    [RelayCommand]
    private async Task AddAddonAsync()
    {
        if (SelectedContent == null) return;

        var newAddon = await dialogService.ShowAddAddonDialogAsync(SelectedContent, activeCatalog.Catalog);
        if (newAddon != null)
        {
            SelectedContent.AddonReleases.Add(newAddon);
            RefreshSelectedContent();
            SelectedDetailTabIndex = DetailTabAddons;

            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Added addon {AddonTitle} (v{Version}) to {ContentId} in catalog: {CatalogId}", newAddon.Title, newAddon.Version, SelectedContent.Id, activeCatalog.Id);
        }
    }

    /// <summary>
    /// Edits an addon release on the selected content item.
    /// </summary>
    [RelayCommand]
    private async Task EditAddonAsync(ContentRelease? addon)
    {
        if (SelectedContent == null || addon == null) return;

        var edited = await dialogService.ShowEditAddonDialogAsync(
            addon,
            SelectedContent,
            activeCatalog.Catalog,
            async add =>
            {
                SelectedContent.AddonReleases.Remove(add);
                parentViewModel?.PublishShareViewModel?.RefreshUploadHierarchy();
                parentViewModel?.PublishShareViewModel?.RefreshArtifactStatuses();
                RefreshSelectedContent();
                MarkProjectAndCatalogDirty();
                if (parentViewModel != null)
                {
                    await parentViewModel.SaveProjectSilentAsync();
                }

                var deletedTitle = GetLocalizedString("Tools.PublisherStudio.Library.AddonDeletedTitle", "Addon Deleted");
                var deletedMessage = string.Format(
                    GetLocalizedString(
                        "Tools.PublisherStudio.Library.AddonDeletedMessageFormat",
                        "Addon '{0}' was deleted."),
                    add.Title ?? add.Version);
                notificationService?.ShowSuccess(deletedTitle, deletedMessage);
                logger.LogInformation("Deleted addon {AddonTitle} from content: {ContentId}", add.Title, SelectedContent.Id);
            });
        if (edited != null)
        {
            var index = SelectedContent.AddonReleases.IndexOf(addon);
            if (index >= 0)
            {
                SelectedContent.AddonReleases[index] = edited;
            }

            RefreshSelectedContent();
            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Updated addon {AddonTitle} (v{Version}) on {ContentId}", edited.Title, edited.Version, SelectedContent.Id);
        }
    }

    /// <summary>
    /// Deletes an addon release from the selected content item.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAddonAsync(ContentRelease? addon)
    {
        if (SelectedContent == null || addon == null) return;

        var title = GetLocalizedString("Tools.PublisherStudio.Addon.DeleteTitle", "Delete Addon");
        var message = string.Format(
            GetLocalizedString(
                "Tools.PublisherStudio.Addon.DeleteMessageFormat",
                "Are you sure you want to delete addon '{0}'? This will also remove its artifacts and cannot be undone."),
            addon.Title ?? addon.Version);

        var confirmed = await dialogService.ShowConfirmationAsync(title, message);
        if (!confirmed) return;

        if (!SelectedContent.AddonReleases.Remove(addon)) return;

        RefreshSelectedContent();
        MarkProjectAndCatalogDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectSilentAsync();
        }

        logger.LogInformation("Deleted addon {AddonTitle} from {ContentId}", addon.Title, SelectedContent.Id);
    }

    /// <summary>
    /// Removes a legacy addon dependency from the selected content item.
    /// </summary>
    [RelayCommand]
    private async Task RemoveAddonAsync(CatalogDependency? dependency)
    {
        if (SelectedContent == null || dependency == null) return;

        SelectedContent.Addons.Remove(dependency);
        RefreshSelectedContent();

        MarkProjectAndCatalogDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectAsync();
        }

        logger.LogInformation("Removed addon dependency {DependencyId} from {ContentId}", dependency.ContentId, SelectedContent.Id);
    }

    /// <summary>
    /// Deletes a release from the selected content item after user confirmation.
    /// </summary>
    [RelayCommand]
    private async Task DeleteReleaseAsync(ContentRelease? release)
    {
        if (SelectedContent == null || release == null)
        {
            return;
        }

        var title = GetLocalizedString("Tools.PublisherStudio.Library.DeleteReleaseTitle", "Delete Release");
        var message = string.Format(
            GetLocalizedString(
                "Tools.PublisherStudio.Library.DeleteReleaseMessageFormat",
                "Are you sure you want to delete release v{0}? This will also remove its artifacts and cannot be undone."),
            release.Version);

        var confirmed = await dialogService.ShowConfirmationAsync(title, message);
        if (!confirmed)
        {
            return;
        }

        var contentId = SelectedContent.Id;
        var version = release.Version;
        if (!SelectedContent.Releases.Remove(release))
        {
            return;
        }

        RefreshSelectedContent();

        MarkProjectAndCatalogDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectSilentAsync();
        }

        var deletedTitle = GetLocalizedString("Tools.PublisherStudio.Library.ReleaseDeletedTitle", "Release Deleted");
        var deletedMessage = string.Format(
            GetLocalizedString(
                "Tools.PublisherStudio.Library.ReleaseDeletedMessageFormat",
                "Release v{0} was deleted."),
            version);
        notificationService?.ShowSuccess(deletedTitle, deletedMessage);

        logger.LogInformation("Removed release v{Version} from content: {ContentId}", version, contentId);
    }

    /// <summary>
    /// Edits an existing release using a pre-populated dialog.
    /// </summary>
    [RelayCommand]
    private async Task EditReleaseAsync(ContentRelease? release)
    {
        if (SelectedContent == null || release == null) return;

        var edited = await dialogService.ShowEditReleaseDialogAsync(
            release,
            SelectedContent,
            activeCatalog.Catalog,
            async rel =>
            {
                SelectedContent.Releases.Remove(rel);
                parentViewModel?.PublishShareViewModel?.RefreshUploadHierarchy();
                parentViewModel?.PublishShareViewModel?.RefreshArtifactStatuses();
                RefreshSelectedContent();
                MarkProjectAndCatalogDirty();
                if (parentViewModel != null)
                {
                    await parentViewModel.SaveProjectSilentAsync();
                }

                var deletedTitle = GetLocalizedString("Tools.PublisherStudio.Library.ReleaseDeletedTitle", "Release Deleted");
                var deletedMessage = string.Format(
                    GetLocalizedString(
                        "Tools.PublisherStudio.Library.ReleaseDeletedMessageFormat",
                        "Release v{0} was deleted."),
                    rel.Version);
                notificationService?.ShowSuccess(deletedTitle, deletedMessage);
                logger.LogInformation("Deleted release v{Version} from content: {ContentId}", rel.Version, SelectedContent.Id);
            });
        if (edited != null)
        {
            // If marked as latest, unmark other releases
            if (edited.IsLatest)
            {
                foreach (var r in SelectedContent.Releases.Where(r => r != release))
                {
                    r.IsLatest = false;
                }
            }

            // Update release properties in place
            release.Version = edited.Version;
            release.ReleaseDate = edited.ReleaseDate;
            release.IsLatest = edited.IsLatest;
            release.IsPrerelease = edited.IsPrerelease;
            release.IsFeatured = edited.IsFeatured;
            release.Changelog = edited.Changelog;
            release.Artifacts = edited.Artifacts;
            release.Dependencies = edited.Dependencies;

            // Artifact instances were replaced, so refresh the Publish tab statuses built from the old objects
            parentViewModel?.PublishShareViewModel?.RefreshUploadHierarchy();
            parentViewModel?.PublishShareViewModel?.RefreshArtifactStatuses();

            RefreshSelectedContent();

            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Updated release v{Version} on content: {ContentId}", release.Version, SelectedContent.Id);
        }
    }

    /// <summary>
    /// Adds an artifact to an existing release.
    /// </summary>
    [RelayCommand]
    private async Task AddArtifactToReleaseAsync(ContentRelease? release)
    {
        if (release == null || IsSelectedContentBundle) return;

        var artifact = await dialogService.ShowAddArtifactDialogAsync(!release.BundleArtifacts);
        if (artifact != null)
        {
            if (artifact.IsPrimary)
            {
                foreach (var a in release.Artifacts)
                {
                    a.IsPrimary = false;
                }
            }

            release.Artifacts.Add(artifact);
            RefreshSelectedContent();

            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Added artifact {Filename} to release v{Version}", artifact.Filename, release.Version);
        }
    }

    /// <summary>
    /// Removes an artifact from a release.
    /// </summary>
    [RelayCommand]
    private async Task RemoveArtifactFromReleaseAsync(ReleaseArtifact? artifact)
    {
        if (SelectedContent == null || artifact == null) return;

        var release = SelectedContent.Releases.FirstOrDefault(r => r.Artifacts.Contains(artifact));
        if (release != null)
        {
            release.Artifacts.Remove(artifact);
            RefreshSelectedContent();

            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Removed artifact {Filename} from release v{Version}", artifact.Filename, release.Version);
        }
    }

    /// <summary>
    /// Adds a dependency to an existing release.
    /// </summary>
    [RelayCommand]
    private async Task AddDependencyToReleaseAsync(ContentRelease? release)
    {
        if (SelectedContent == null || release == null) return;

        var dependency = await dialogService.ShowAddDependencyDialogAsync(activeCatalog.Catalog, SelectedContent);
        if (dependency != null)
        {
            release.Dependencies.Add(dependency);
            RefreshSelectedContent();

            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Added dependency {DependencyId} to release v{Version}", dependency.ContentId, release.Version);
        }
    }

    /// <summary>
    /// Removes a dependency from a release.
    /// </summary>
    [RelayCommand]
    private async Task RemoveDependencyFromReleaseAsync(CatalogDependency? dependency)
    {
        if (SelectedContent == null || dependency == null) return;

        var release = SelectedContent.Releases.FirstOrDefault(r => r.Dependencies.Contains(dependency));
        if (release != null)
        {
            release.Dependencies.Remove(dependency);
            RefreshSelectedContent();

            MarkProjectAndCatalogDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Removed dependency {DependencyId} from release v{Version}", dependency.ContentId, release.Version);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredContent));
    }

    partial void OnSelectedContentChanged(CatalogContentItem? value)
    {
        RefreshHostingHint();
        OnPropertyChanged(nameof(SelectedContentTracksUpstream));
        OnPropertyChanged(nameof(ShowFallbackReleasesNote));
        OnPropertyChanged(nameof(ShowManualFallbackReleases));
        OnPropertyChanged(nameof(ShowStaticReleasesList));
        OnPropertyChanged(nameof(CanAddManualRelease));
        OnPropertyChanged(nameof(ShowReleasesDropZone));
        OnPropertyChanged(nameof(ShowUpstreamSyncFailedWarning));
        OnPropertyChanged(nameof(IsSelectedContentBundle));
        OnPropertyChanged(nameof(ShowReleaseArtifacts));
        OnPropertyChanged(nameof(EffectiveSelectedContentReleasesCount));
        if (_suppressUpstreamPreviewReload)
        {
            return;
        }

        BeginUpstreamPreviewLoad(value);
    }

    private bool IsSelectedContentUpstream() =>
        SelectedContent != null && CatalogConstants.UpstreamProviders.IsConfiguredUpstreamSource(SelectedContent);

    /// <summary>
    /// Starts loading a read-only upstream release preview for the selected content item.
    /// The preview is ingested from a clone so the catalog itself is never modified.
    /// </summary>
    private void BeginUpstreamPreviewLoad(CatalogContentItem? value)
    {
        _upstreamPreviewCts?.Cancel();
        _upstreamPreviewCts?.Dispose();
        _upstreamPreviewCts = null;
        UpstreamPreviewReleases.Clear();
        UpstreamPreviewVersions = [];
        IsUpstreamPreviewLoading = false;
        UpstreamPreviewFailed = false;
        OnPropertyChanged(nameof(HasUpstreamPreview));
        OnPropertyChanged(nameof(UpstreamPreviewVersions));
        OnPropertyChanged(nameof(ShowFallbackReleasesNote));
        OnPropertyChanged(nameof(ShowManualFallbackReleases));
        OnPropertyChanged(nameof(ShowStaticReleasesList));
        OnPropertyChanged(nameof(EffectiveSelectedContentReleasesCount));

        if (value == null || upstreamIngestionService == null || !IsSelectedContentUpstream())
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _upstreamPreviewCts = cts;
        IsUpstreamPreviewLoading = true;
        _ = LoadUpstreamPreviewAsync(value, cts);
    }

    private async Task LoadUpstreamPreviewAsync(CatalogContentItem item, CancellationTokenSource cts)
    {
        try
        {
            var clone = CloneForPreview(item);
            if (clone == null || upstreamIngestionService == null)
            {
                return;
            }

            // Ingestion replaces releases on success and leaves them untouched on a miss,
            // so reference comparison tells live data apart from static leftovers.
            var staticRefs = new HashSet<ContentRelease>(clone.Releases);
            var preview = new PublisherCatalog { Content = [clone] };
            await upstreamIngestionService.IngestCatalogAsync(preview, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            var releases = clone.Releases.Where(r => !staticRefs.Contains(r)).ToList();
            var producedUpstream = releases.Count > 0;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_upstreamPreviewCts != cts)
                {
                    return;
                }

                UpstreamPreviewReleases.Clear();
                foreach (var release in releases)
                {
                    UpstreamPreviewReleases.Add(release);
                }

                UpstreamPreviewFailed = !producedUpstream;
                UpstreamPreviewVersions = releases
                    .Where(r => !string.IsNullOrWhiteSpace(r.Version))
                    .Select(r => r.Version)
                    .ToList();
                OnPropertyChanged(nameof(HasUpstreamPreview));
                OnPropertyChanged(nameof(UpstreamPreviewVersions));
                OnPropertyChanged(nameof(ShowFallbackReleasesNote));
                OnPropertyChanged(nameof(ShowManualFallbackReleases));
                OnPropertyChanged(nameof(ShowStaticReleasesList));
                OnPropertyChanged(nameof(EffectiveSelectedContentReleasesCount));
            });
        }
        catch (OperationCanceledException ex)
        {
            logger.LogDebug(ex, "Upstream release preview for '{ContentId}' was canceled", item.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load upstream release preview for '{ContentId}'", item.Id);
        }
        finally
        {
            try
            {
                if (_upstreamPreviewCts == cts)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_upstreamPreviewCts == cts)
                        {
                            IsUpstreamPreviewLoading = false;
                        }
                    });

                    if (_upstreamPreviewCts == cts)
                    {
                        _upstreamPreviewCts = null;
                    }
                }
            }
            finally
            {
                cts.Dispose();
            }
        }
    }

    /// <summary>
    /// Forces the content detail panel to rebuild.
    /// Selected content instances are mutable models, so mutating their releases, artifacts,
    /// or dependencies in place does not raise change notifications on its own.
    /// </summary>
    private void RefreshSelectedContent()
    {
        var selected = SelectedContent;
        if (selected == null)
        {
            return;
        }

        // The selection round-trip only forces the detail panel to rebuild for the same
        // item, so keep the upstream preview instead of cancelling and re-fetching it.
        _suppressUpstreamPreviewReload = true;
        try
        {
            SelectedContent = null;
            SelectedContent = selected;
        }
        finally
        {
            _suppressUpstreamPreviewReload = false;
        }

        OnPropertyChanged(nameof(FilteredContent));
        RefreshHostingHint();
    }

    [ObservableProperty]
    private string _newVideoUrl = string.Empty;

    [RelayCommand]
    private async Task AddScreenshotAsync()
    {
        if (SelectedContent == null)
        {
            return;
        }

        var files = await dialogService.ShowImageFilesPickerAsync(
            GetLocalizedString("Tools.PublisherStudio.Library.PickScreenshotTitle", "Select Screenshot Images"));

        if (files is { Count: > 0 })
        {
            await AddMediaToSelectedContentAsync(files);
        }
    }

    [RelayCommand]
    private async Task PasteScreenshotAsync()
    {
        await PasteClipboardMediaAsync(
            "Tools.PublisherStudio.Library.NoClipboardImageTitle",
            "No Image Found",
            "Tools.PublisherStudio.Library.NoClipboardImageMessage",
            "No image, file, or image URL was found on the clipboard.");
    }

    [RelayCommand]
    private async Task AddVideoAsync()
    {
        if (SelectedContent == null)
        {
            return;
        }

        var files = await dialogService.ShowVideoFilesPickerAsync(
            GetLocalizedString("Tools.PublisherStudio.Library.PickVideoTitle", "Select Video Files"));

        if (files is { Count: > 0 })
        {
            await AddMediaToSelectedContentAsync(files);
        }
    }

    [RelayCommand]
    private async Task PasteVideoAsync()
    {
        await PasteClipboardMediaAsync(
            "Tools.PublisherStudio.Library.NoClipboardVideoTitle",
            "No Video Found",
            "Tools.PublisherStudio.Library.NoClipboardVideoMessage",
            "No video file or video URL was found on the clipboard.");
    }

    private async Task PasteClipboardMediaAsync(
        string noMediaTitleKey,
        string noMediaTitleDefault,
        string noMediaMessageKey,
        string noMediaMessageDefault)
    {
        if (SelectedContent == null)
        {
            return;
        }

        var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var clipboard = desktop?.MainWindow?.Clipboard;
        if (clipboard == null)
        {
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "GenHub", "Clipboard");
        var paths = await ClipboardInputHelper.ExtractClipboardPathsAsync(clipboard, tempDir);
        if (paths.Count > 0)
        {
            await AddMediaToSelectedContentAsync(paths);
        }
        else
        {
            var title = GetLocalizedString(noMediaTitleKey, noMediaTitleDefault);
            var message = GetLocalizedString(noMediaMessageKey, noMediaMessageDefault);
            notificationService?.ShowWarning(title, message);
        }
    }

    [RelayCommand]
    private async Task AddVideoUrlAsync()
    {
        if (SelectedContent == null || string.IsNullOrWhiteSpace(NewVideoUrl))
        {
            return;
        }

        var url = NewVideoUrl.Trim();
        SelectedContent.Metadata ??= new();

        if (string.IsNullOrWhiteSpace(SelectedContent.Metadata.VideoUrl))
        {
            SelectedContent.Metadata.VideoUrl = url;
        }
        else if (!SelectedContent.Metadata.VideoUrls.Contains(url, StringComparer.OrdinalIgnoreCase))
        {
            SelectedContent.Metadata.VideoUrls.Add(url);
        }

        NewVideoUrl = string.Empty;
        RefreshSelectedContent();
        SelectedDetailTabIndex = DetailTabMedia;
        MarkProjectAndCatalogDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectAsync();
        }
    }

    /// <summary>
    /// Loads content items from the active catalog.
    /// </summary>
    private void LoadContent()
    {
        ContentItems.Clear();
        var catalog = activeCatalog;
        var content = catalog?.Catalog?.Content;
        if (catalog != null && content != null)
        {
            var (catalogIcon, publisherAvatar) = ResolveCatalogPresentationUrls(catalog, parentViewModel);

            foreach (var item in content)
            {
                item.CatalogIconUrl = catalogIcon;
                item.PublisherAvatarUrl = publisherAvatar;
                ContentItems.Add(item);
            }
        }

        OnPropertyChanged(nameof(FilteredContent));
        OnPropertyChanged(nameof(CatalogSummaryText));
    }

    partial void OnContentItemsChanged(ObservableCollection<CatalogContentItem> value)
    {
        OnPropertyChanged(nameof(CatalogSummaryText));
    }

    private string GetLocalizedString(string key, string fallback)
    {
        return localizationService?.GetString(key) ?? fallback;
    }

    private void ShowInvalidPathWarning()
    {
        var title = GetLocalizedString("Tools.PublisherStudio.Content.InvalidPathTitle", "Invalid Path");
        var message = GetLocalizedString("Tools.PublisherStudio.Content.InvalidPathMessage", "The specified file or folder does not exist.");
        notificationService?.ShowWarning(title, message);
    }

    private async Task<CatalogContentItem?> CreateBatchContentItemAsync(string path, CancellationToken cancellationToken)
    {
        var rawName = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return null;
        }

        var displayName = NormalizeDisplayName(rawName);
        var contentId = EnsureUniqueContentId(Slugify(displayName));
        var contentType = ClassifyBatchContentType(rawName, path);
        var isMap = contentType is ContentType.Map or ContentType.MapPack;
        var fileName = Path.GetFileName(path);
        var fileInfo = new FileInfo(path);
        var sha256Hash = await ComputeFileSha256Async(path, cancellationToken);

        var artifact = new ReleaseArtifact
        {
            Filename = fileName,
            LocalFilePath = path,
            Size = fileInfo.Exists ? fileInfo.Length : 0,
            Sha256 = sha256Hash,
            ContentType = MimeTypeHelper.FromFileName(fileName),
            IsPrimary = true,
        };

        var release = new ContentRelease
        {
            Version = "1.0.0",
            ReleaseDate = DateTime.UtcNow,
            IsLatest = true,
            Artifacts = [artifact],
            Dependencies =
            [
                new CatalogDependency
                {
                    PublisherId = CatalogConstants.EaPublisherId,
                    ContentId = CatalogConstants.ZeroHourContentId,
                    VersionConstraint = "1.04",
                    ContentType = nameof(ContentType.GameInstallation),
                },
            ],
        };

        return new CatalogContentItem
        {
            Id = contentId,
            Name = displayName,
            Description = $"{displayName} release",
            ContentType = contentType,
            TargetGame = GameType.ZeroHour,
            PublisherType = activeCatalog.Catalog.Publisher?.Id ?? CatalogConstants.GenericPublisherType,
            Tags = BuildBatchContentTags(isMap),
            Releases = [release],
        };
    }

    private string EnsureUniqueContentId(string baseSlug)
    {
        var contentId = baseSlug;
        var counter = 2;
        while (activeCatalog.Catalog.Content.Any(c => string.Equals(c.Id, contentId, StringComparison.OrdinalIgnoreCase)))
        {
            contentId = $"{baseSlug}-{counter++}";
        }

        return contentId;
    }

    private async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hashBytes = await sha.ComputeHashAsync(stream, cancellationToken);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to compute SHA256 for batch imported file {Path}", path);
            return string.Empty;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied while computing SHA256 for batch imported file {Path}", path);
            return string.Empty;
        }
    }

    private async Task FinalizeBatchImportAsync(int importedCount, CatalogContentItem? lastCreated)
    {
        OnPropertyChanged(nameof(FilteredContent));
        OnPropertyChanged(nameof(CatalogSummaryText));
        if (lastCreated != null)
        {
            SelectedContent = lastCreated;
        }

        MarkProjectAndCatalogDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectAsync();
        }

        var successTitle = GetLocalizedString("Tools.PublisherStudio.Library.BatchImportSuccessTitle", "Batch Import Complete");
        var successMessage = string.Format(
            GetLocalizedString(
                "Tools.PublisherStudio.Library.BatchImportSuccessMessage",
                "Successfully imported {0} content items (1 release each)."),
            importedCount);
        notificationService?.ShowSuccess(successTitle, successMessage);
    }

    private string NormalizeDisplayName(string rawName)
    {
        var stripped = ContentFormatPolicy.StripArchiveExtensions(rawName);
        var cleaned = UnderscoreDashRegex().Replace(stripped, " ");
        cleaned = WhitespaceRegex().Replace(cleaned, " ").Trim();
        if (string.IsNullOrEmpty(cleaned))
        {
            return "Content Item";
        }

        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
            {
                words[i] = char.ToUpperInvariant(words[i][0]) + (words[i].Length > 1 ? words[i][1..] : string.Empty);
            }
        }

        return string.Join(" ", words);
    }

    private string Slugify(string text)
    {
        var slug = NonSlugCharRegex().Replace(text.ToLowerInvariant(), "-");
        slug = DashRunRegex().Replace(slug, "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "content-item" : slug;
    }
}
