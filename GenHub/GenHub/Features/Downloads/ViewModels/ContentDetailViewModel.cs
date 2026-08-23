using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Parsers;
using GenHub.Core.Messages;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.ModDB;
using GenHub.Core.Models.Parsers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Features.Downloads.Views;
using GenHub.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Initializes a new instance of the <see cref="ContentDetailViewModel"/> class.
/// </summary>
/// <param name="searchResult">The content search result to display.</param>
/// <param name="parsers">The available web page parsers.</param>
/// <param name="profileContentService">The profile content service.</param>
/// <param name="profileManager">The game profile manager.</param>
/// <param name="notificationService">The notification service.</param>
/// <param name="tabProviderRegistry">The tab provider registry.</param>
/// <param name="contentStateService">The content state service.</param>
/// <param name="downloadCoordinator">The download coordinator.</param>
/// <param name="manifestPool">The content manifest pool.</param>
/// <param name="loggerFactory">The logger factory.</param>
/// <param name="logger">The logger.</param>
/// <param name="closeAction">Optional action to invoke when the view should close.</param>
/// <param name="variantSearchResults">Optional map of sibling variant search results.</param>
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "ContentDetailViewModel coordinates rich media, downloads, profile binding, and custom tabs.")]
[SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Properties and methods access CommunityToolkit MVVM generated instance properties.")]
[SuppressMessage("Critical Code Smell", "S3776:Cognitive Complexity of methods should not be too high", Justification = "Content detail ViewModel coordinates complex UI state, downloads, and multiple catalog sources.")]
public partial class ContentDetailViewModel(
    ContentSearchResult searchResult,
    IReadOnlyList<IWebPageParser> parsers,
    IProfileContentService profileContentService,
    IGameProfileManager profileManager,
    INotificationService notificationService,
    ITabProviderRegistry tabProviderRegistry,
    IContentStateService contentStateService,
    IContentDownloadCoordinator downloadCoordinator,
    IContentManifestPool manifestPool,
    ILoggerFactory loggerFactory,
    ILogger<ContentDetailViewModel> logger,
    Action? closeAction = null,
    IReadOnlyDictionary<string, ContentSearchResult>? variantSearchResults = null) : ObservableObject, IDisposable
{
    private const string UnknownValue = "Unknown";
    private const string ContentNotDownloadedTitle = "Content Not Downloaded";

    [ObservableProperty]
    private ObservableCollection<InstallableVariant> _variants = [];

    [ObservableProperty]
    private InstallableVariant? _selectedVariant;

    /// <summary>
    /// Gets a value indicating whether this content has variants to choose from.
    /// </summary>
    public bool HasVariants => Variants.Count > 0;

    /// <summary>
    /// Gets variant options grouped by axis for multi-ComboBox UI.
    /// </summary>
    public ObservableCollection<VariantAxisGroup> VariantAxes { get; } = [];

    /// <summary>
    /// Gets a value indicating whether more than one variant axis is present.
    /// </summary>
    public bool HasMultipleVariantAxes => VariantAxes.Count > 1;

    /// <summary>
    /// Gets downloadable members of a ContentBundle.
    /// </summary>
    public ObservableCollection<BundleComponentViewModel> BundleComponents { get; } = [];

    /// <summary>
    /// Gets a value indicating whether this detail page is a multi-content bundle.
    /// </summary>
    public bool HasBundleComponents => BundleComponents.Count > 0;

    /// <summary>
    /// Gets a value indicating whether every required selected bundle member is acquired.
    /// </summary>
    public bool AreBundleComponentsReadyForProfile =>
        HasBundleComponents && BundleComponentViewModel.AreRequiredSelectionsDownloaded(BundleComponents);

    private readonly object _basicContentLoadLock = new();
    private readonly object _preloadLock = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;
    private bool _userManuallySelectedDownloadableItem;
    private Action? _unsubscribeAxisHandlers;
    private Task? _preloadTask;

    /// <summary>
    /// When true, content-type changes skip persisting to the manifest pool
    /// (used while syncing the dropdown from an already-stored manifest).
    /// </summary>
    private bool _suppressContentTypePersist;

    /// <summary>
    /// Last post-download content-type persist task (for tests to await).
    /// </summary>
    private Task? _contentTypePersistTask;

    /// <summary>
    /// Gets the content search result this detail view is displaying.
    /// </summary>
    public ContentSearchResult SearchResult => searchResult;

    [ObservableProperty]
    private string _selectedScreenshotUrl = searchResult.ScreenshotUrls.FirstOrDefault() ?? string.Empty;

    [ObservableProperty]
    private int _selectedTabIndex;

    // Lazy loading flags to track which sections have been loaded
    private bool _imagesLoaded;
    private bool _videosLoaded;
    private bool _releasesLoaded;
    private bool _addonsLoaded;
    private bool _basicContentLoaded;
    private Task? _basicContentLoadTask;

    /// <summary>
    /// Gets the collection of screenshot URLs.
    /// </summary>
    public ObservableCollection<string> Screenshots { get; } = new(searchResult.ScreenshotUrls);

    /// <summary>
    /// Gets the collection of tags associated with the content.
    /// </summary>
    public ObservableCollection<string> Tags { get; } = new(searchResult.Tags);

    /// <summary>
    /// Gets a value indicating whether there are multiple screenshots to display.
    /// </summary>
    public bool HasMultipleScreenshots => Screenshots.Count > 1;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _iconBitmap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownloadButton))]
    [NotifyPropertyChangedFor(nameof(ShowAddToProfileButton))]
    private bool _isDownloading;

    partial void OnIsDownloadingChanged(bool value)
    {
        foreach (var release in Releases)
        {
            (release.SelectCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        }

        foreach (var addon in Addons)
        {
            (addon.SelectCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        }
    }

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
    [NotifyPropertyChangedFor(nameof(HasRequiredDependencies))]
    [NotifyPropertyChangedFor(nameof(IncludesSummary))]
    [NotifyPropertyChangedFor(nameof(HasIncludesSummary))]
    [NotifyPropertyChangedFor(nameof(IncludesSectionTitle))]
    private string? _requiredDependenciesSummary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentType))]
    private ContentType _selectedContentType = searchResult.ContentType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDownloadableItem))]
    [NotifyPropertyChangedFor(nameof(ShowSelectedTargetBanner))]
    [NotifyPropertyChangedFor(nameof(SelectedTargetTitle))]
    [NotifyPropertyChangedFor(nameof(SelectedTargetCategory))]
    [NotifyPropertyChangedFor(nameof(DownloadSize))]
    [NotifyPropertyChangedFor(nameof(HasDownloadSize))]
    [NotifyPropertyChangedFor(nameof(LastUpdatedDisplay))]
    [NotifyPropertyChangedFor(nameof(HasLastUpdated))]
    [NotifyPropertyChangedFor(nameof(Version))]
    [NotifyPropertyChangedFor(nameof(HasVersion))]
    [NotifyPropertyChangedFor(nameof(ShowDownloadButton))]
    [NotifyPropertyChangedFor(nameof(ShowAddToProfileButton))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateButton))]
    [NotifyPropertyChangedFor(nameof(HasSelectedDownloadableItem))]
    [NotifyPropertyChangedFor(nameof(ShowSelectedTargetBanner))]
    private DownloadableItemViewModel? _selectedDownloadableItem;

    /// <summary>
    /// Gets a value indicating whether a specific release or addon row is selected.
    /// </summary>
    public bool HasSelectedDownloadableItem => SelectedDownloadableItem != null;

    /// <summary>
    /// Gets a value indicating whether the selected target banner should be displayed in the sidebar.
    /// Only shown when a specific target is selected and multiple choices exist (e.g. multiple releases, addons, or variants).
    /// </summary>
    public bool ShowSelectedTargetBanner =>
        SelectedDownloadableItem != null && (Releases.Count > 1 || Addons.Count > 0 || Variants.Count > 1);

    /// <summary>
    /// Gets the display title of the active download target (selected row or main content).
    /// </summary>
    public string SelectedTargetTitle => SelectedDownloadableItem?.Name ?? Name;

    /// <summary>
    /// Gets the category or type description of the active download target.
    /// </summary>
    public string SelectedTargetCategory
    {
        get
        {
            if (SelectedDownloadableItem == null)
            {
                return ContentType.GetDisplayName();
            }

            if (!string.IsNullOrWhiteSpace(SelectedDownloadableItem.Category))
            {
                return SelectedDownloadableItem.Category;
            }

            return SelectedDownloadableItem is ReleaseItemViewModel ? "Release" : "Addon";
        }
    }

    [ObservableProperty]
    private string? _fullScreenMediaUrl;

    [ObservableProperty]
    private string? _fullScreenMediaTitle;

    [ObservableProperty]
    private bool _isFullScreenMediaOpen;

    /// <summary>
    /// Gets the content classifications the user can apply before or after download.
    /// After acquisition, changing the type updates the stored manifest (e.g. Addon → Executable).
    /// </summary>
    public IReadOnlyList<ContentType> ContentTypeOptions { get; } = Enum.GetValues<ContentType>();

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
    /// Disposes resources used by the view model.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Performs asynchronous initialization of the view model content.
    /// </summary>
    public void Initialize()
    {
        // Load rich content from parsed page if already available
        LoadRichContent();

        // Subscribe to content state changes
        contentStateService.ContentStateChanged -= OnContentStateChanged;
        contentStateService.ContentStateChanged += OnContentStateChanged;
        WeakReferenceMessenger.Default.Register<ContentLibraryClearedMessage>(
            this,
            static (recipient, _) => ((ContentDetailViewModel)recipient).ResetDownloadState());

        // Hydrate bundle members before reading install state so an empty ContentBundle
        // recipe is never treated as "already downloaded".
        if (BundleComponents.Count == 0)
        {
            AttachBundleComponents(BundleComponentViewModel.CreateFromSearchResult(searchResult));
        }

        // Determine the initial downloaded/update state so a previously downloaded item
        // opens with "Add to Profile" instead of "Download Now".
        _ = LoadInitialStateAsync();

        // Load icon and parsed data asynchronously
        // Note: Full details are loaded eagerly for ModDB and similar content
        // that requires page parsing to show releases, addons, etc.
        _ = LoadIconAsync();
        _ = LoadBasicParsedDataAsync();
        _ = LoadCustomTabsAsync();
        _ = InitializeVariantsAsync();
    }

    /// <summary>
    /// Attaches shared bundle-component view-models (typically from the grid card) so selection
    /// and download state stay in sync.
    /// </summary>
    /// <param name="components">Bundle members to display.</param>
    public void AttachBundleComponents(IEnumerable<BundleComponentViewModel> components)
    {
        foreach (var existing in BundleComponents)
        {
            existing.PropertyChanged -= OnBundleComponentPropertyChanged;
        }

        BundleComponents.Clear();
        foreach (var component in components)
        {
            component.PropertyChanged += OnBundleComponentPropertyChanged;
            BundleComponents.Add(component);
        }

        OnPropertyChanged(nameof(HasBundleComponents));
        OnPropertyChanged(nameof(AreBundleComponentsReadyForProfile));
        OnPropertyChanged(nameof(ShowDownloadButton));
        OnPropertyChanged(nameof(ShowAddToProfileButton));
        OnPropertyChanged(nameof(HasIncludesSummary));
        if (HasBundleComponents)
        {
            foreach (var rel in Releases)
            {
                rel.IsDownloaded = AreBundleComponentsReadyForProfile;
            }
        }

        _ = RefreshBundleComponentStatesAsync();
    }

    /// <summary>
    /// Rebuilds <see cref="VariantAxes"/> from the flat <see cref="Variants"/> list.
    /// </summary>
    public void RebuildVariantAxes()
    {
        _unsubscribeAxisHandlers = VariantAxisGrouping.Rebuild(
            Variants,
            VariantAxes,
            SelectedVariant,
            OnAxisSelectionCommitted,
            _unsubscribeAxisHandlers);
        OnPropertyChanged(nameof(HasMultipleVariantAxes));
        OnPropertyChanged(nameof(HasVariants));
    }

    /// <summary>
    /// Selects a variant matching the specified manifest ID.
    /// </summary>
    /// <param name="manifestId">The manifest ID of the variant to select.</param>
    public void SelectVariantByManifestId(string manifestId)
    {
        if (string.IsNullOrWhiteSpace(manifestId) || Variants == null)
        {
            return;
        }

        var match = Variants.FirstOrDefault(v => string.Equals(v.ManifestId, manifestId, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            SelectedVariant = match;
        }
    }

    /// <summary>
    /// Displays the profile selection flow for a manifest. Kept overridable so derived detail
    /// views can provide a host-specific dialog while preserving the manifest chosen by a row.
    /// </summary>
    /// <param name="manifestId">Optional manifest ID for a specific release or addon row.</param>
    /// <param name="contentName">Optional display name for a specific release or addon row.</param>
    /// <param name="targetGame">Optional target game for a specific release or addon row.</param>
    /// <returns>A task representing the profile-selection flow.</returns>
    protected virtual Task ShowProfileSelectionDialogAsync(
        string? manifestId = null,
        string? contentName = null,
        GameType? targetGame = null) =>
        ShowProfileSelectionDialogCoreAsync(manifestId, contentName, targetGame);

    /// <summary>
    /// Awaits any in-flight content-type persist started by the Type dropdown.
    /// </summary>
    /// <returns>A task that completes when persistence finishes.</returns>
    protected Task WaitForContentTypePersistAsync() => _contentTypePersistTask ?? Task.CompletedTask;

    /// <summary>
    /// Disposes unmanaged and managed resources.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _cts.Cancel();
                _cts.Dispose();

                // Unsubscribe from state changes
                contentStateService.ContentStateChanged -= OnContentStateChanged;
                WeakReferenceMessenger.Default.Unregister<ContentLibraryClearedMessage>(this);
                _unsubscribeAxisHandlers?.Invoke();
                _unsubscribeAxisHandlers = null;
                foreach (var component in BundleComponents)
                {
                    component.PropertyChanged -= OnBundleComponentPropertyChanged;
                }

                IconBitmap = null;
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Extracts a filename from a URL, or returns null if not possible.
    /// </summary>
    private static string? GetFileNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var fileName = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(fileName) && fileName.Contains('.'))
            {
                return fileName;
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return null;
    }

    private static string CreateFileContentId(DownloadableFile file) =>
        $"file:{file.DownloadUrl ?? file.Name}";

    private static bool IsModDbContent(ContentSearchResult content) =>
        string.Equals(content.ProviderName, "ModDB", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(content.ProviderName, ModDBConstants.PublisherType, StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrEmpty(content.SourceUrl) &&
         content.SourceUrl.Contains("moddb.com", StringComparison.OrdinalIgnoreCase));

    private static List<Comment> FlattenComments(IEnumerable<Comment> comments)
    {
        var list = new List<Comment>();
        foreach (var c in comments)
        {
            list.Add(c);
            if (c.Replies is { Count: > 0 })
            {
                list.AddRange(FlattenComments(c.Replies));
            }
        }

        return list;
    }

    /// <summary>
    /// Runs an action on the Avalonia UI thread when an application is running; otherwise
    /// executes inline so unit tests without a dispatcher do not hang.
    /// </summary>
    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess() || Avalonia.Application.Current == null)
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    /// <summary>
    /// Executes the specified action on the UI thread asynchronously, or immediately if already on the UI thread / in test runner.
    /// </summary>
    private static async Task RunOnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess() || Avalonia.Application.Current == null)
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }

    /// <summary>
    /// Re-adds a RequireExisting game-installation dependency when correcting a tool back to
    /// game-bound content (mirrors ModDBManifestFactory.AddGameDependencies).
    /// </summary>
    private static void EnsureGameInstallationDependency(ContentManifest manifest)
    {
        manifest.Dependencies ??= [];
        if (manifest.Dependencies.Any(dependency => dependency.DependencyType == ContentType.GameInstallation))
        {
            return;
        }

        if (manifest.TargetGame == GameType.ZeroHour)
        {
            manifest.Dependencies.Add(new ContentDependency
            {
                Id = ManifestId.Create("1.104.any.gameinstallation.zerohour"),
                Name = "Zero Hour Installation",
                DependencyType = ContentType.GameInstallation,
                InstallBehavior = DependencyInstallBehavior.RequireExisting,
                MinVersion = ManifestConstants.ZeroHourManifestVersion,
            });
        }
        else if (manifest.TargetGame == GameType.Generals)
        {
            manifest.Dependencies.Add(new ContentDependency
            {
                Id = ManifestId.Create("1.108.any.gameinstallation.generals"),
                Name = "Generals Installation",
                DependencyType = ContentType.GameInstallation,
                InstallBehavior = DependencyInstallBehavior.RequireExisting,
                MinVersion = ManifestConstants.GeneralsManifestVersion,
            });
        }
    }

    private static (string Name, string Website, string SupportUrl) GetBuiltInPublisherInfo(string providerName)
    {
        if (providerName.Equals(PublisherInfoConstants.TheSuperHackers.Name, StringComparison.OrdinalIgnoreCase) ||
            providerName.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase))
        {
            return (PublisherInfoConstants.TheSuperHackers.Name, "https://github.com/thesuperhackers", "https://github.com/thesuperhackers/GeneralsGameCode/issues");
        }

        if (providerName.Equals(PublisherInfoConstants.GeneralsOnline.Name, StringComparison.OrdinalIgnoreCase) ||
            providerName.Equals(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase))
        {
            return (PublisherInfoConstants.GeneralsOnline.Name, PublisherInfoConstants.GeneralsOnline.Website, PublisherInfoConstants.GeneralsOnline.SupportUrl);
        }

        if (providerName.Equals(PublisherInfoConstants.CommunityOutpost.Name, StringComparison.OrdinalIgnoreCase) ||
            providerName.Equals("community-outpost", StringComparison.OrdinalIgnoreCase))
        {
            return (PublisherInfoConstants.CommunityOutpost.Name, "https://legi.cc", "https://legi.cc/patch");
        }

        if (providerName.Equals(PublisherInfoConstants.ModDB.Name, StringComparison.OrdinalIgnoreCase))
        {
            return (PublisherInfoConstants.ModDB.Name, PublisherInfoConstants.ModDB.Website, PublisherInfoConstants.ModDB.SupportUrl);
        }

        if (providerName.Equals(PublisherInfoConstants.CNCLabs.Name, StringComparison.OrdinalIgnoreCase))
        {
            return (PublisherInfoConstants.CNCLabs.Name, PublisherInfoConstants.CNCLabs.Website, PublisherInfoConstants.CNCLabs.SupportUrl);
        }

        if (providerName.Equals(PublisherInfoConstants.GitHub.Name, StringComparison.OrdinalIgnoreCase))
        {
            return (PublisherInfoConstants.GitHub.Name, PublisherInfoConstants.GitHub.Website, PublisherInfoConstants.GitHub.SupportUrl);
        }

        if (providerName.Equals(PublisherInfoConstants.AODMaps.Name, StringComparison.OrdinalIgnoreCase))
        {
            return (PublisherInfoConstants.AODMaps.Name, PublisherInfoConstants.AODMaps.Website, PublisherInfoConstants.AODMaps.SupportUrl);
        }

        return (providerName, string.Empty, string.Empty);
    }

    private static string? GetDeduplicationKey(string? url, string? name, string? filename = null)
    {
        if (!string.IsNullOrWhiteSpace(filename))
        {
            return filename.Trim().ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            var trimmed = url.Trim().TrimEnd('/');
            var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
            {
                var last = segments[^1].ToLowerInvariant();
                if (!last.Equals("downloads", StringComparison.OrdinalIgnoreCase) &&
                    !last.Equals("addons", StringComparison.OrdinalIgnoreCase) &&
                    !last.Equals("files", StringComparison.OrdinalIgnoreCase))
                {
                    return last;
                }
            }

            return trimmed.ToLowerInvariant();
        }

        return !string.IsNullOrWhiteSpace(name) ? name.Trim().ToLowerInvariant() : null;
    }

    /// <summary>
    /// Computes selection priority for a release, prioritizing full mod releases over patches.
    /// </summary>
    private static int GetReleasePriority(ReleaseItemViewModel release)
    {
        var category = release.Category?.Trim() ?? string.Empty;
        var name = release.Name?.Trim() ?? string.Empty;

        var isExplicitPatch = category.Contains("patch", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("patch", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("hotfix", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("update", StringComparison.OrdinalIgnoreCase);

        var isFullVersion = category.Contains("full version", StringComparison.OrdinalIgnoreCase) ||
                            category.Contains("full", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("full version", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("standalone", StringComparison.OrdinalIgnoreCase);

        if (isFullVersion && !isExplicitPatch)
        {
            return 3;
        }

        if (!isExplicitPatch)
        {
            return 2;
        }

        return 1;
    }

    /// <summary>
    /// Finds the preferred initial release from a list of releases, prioritizing full mod releases over patches.
    /// </summary>
    private static ReleaseItemViewModel? FindPreferredRelease(IReadOnlyList<ReleaseItemViewModel> releases)
    {
        if (releases.Count == 0)
        {
            return null;
        }

        return releases.OrderByDescending(GetReleasePriority).First();
    }

    private static bool IsFileDetailsAlreadyLoaded(DownloadableFile file) =>
        !string.IsNullOrEmpty(file.Filename) ||
        !string.IsNullOrEmpty(file.Md5Hash) ||
        file.DownloadCount.HasValue ||
        (file.PreviewImages is { Count: > 0 }) ||
        !string.IsNullOrEmpty(file.Description);

    private static ContentVariantInfo MatchVariantInfo(ContentSearchResult sibling, string key)
    {
        var variantInfo = sibling.Variants?.FirstOrDefault(v =>
            string.Equals(v.Id, sibling.Id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v.Id, key, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(v.Id) && sibling.Id?.EndsWith($".{v.Id}", StringComparison.OrdinalIgnoreCase) == true) ||
            (!string.IsNullOrEmpty(v.Id) && key.EndsWith($".{v.Id}", StringComparison.OrdinalIgnoreCase)));

        if (variantInfo == null && sibling.Variants != null)
        {
            var gameSuffix = sibling.TargetGame switch
            {
                GameType.Generals => "generals",
                GameType.ZeroHour => "zerohour",
                _ => null,
            };
            if (gameSuffix != null)
            {
                variantInfo = sibling.Variants.FirstOrDefault(v =>
                    v.Id.EndsWith($".{gameSuffix}", StringComparison.OrdinalIgnoreCase) ||
                    v.Name.Contains(gameSuffix, StringComparison.OrdinalIgnoreCase) ||
                    (sibling.TargetGame == GameType.ZeroHour && v.Name.Contains("Zero Hour", StringComparison.OrdinalIgnoreCase)) ||
                    (sibling.TargetGame == GameType.Generals && v.Name.Contains("Generals", StringComparison.OrdinalIgnoreCase) && !v.Name.Contains("Zero Hour", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return variantInfo ?? new ContentVariantInfo
        {
            Id = !string.IsNullOrEmpty(key) ? key : (sibling.Id ?? string.Empty),
            Name = sibling.Name ?? sibling.Id ?? UnknownValue,
            ManifestId = !string.IsNullOrEmpty(key) ? key : (sibling.Id ?? string.Empty),
        };
    }

    private static ContentSearchResult PrepareVariantSnapshot(ContentSearchResult sibling, ContentVariantInfo info, string catalogKey)
    {
        var snapshot = VariantSwap.Clone(sibling);

        if (!string.IsNullOrEmpty(catalogKey) &&
            (!ManifestIdValidator.IsValid(snapshot.Id ?? string.Empty, out _) ||
             string.Equals(snapshot.Id, catalogKey, StringComparison.OrdinalIgnoreCase)))
        {
            snapshot.Id = catalogKey;
        }

        var displayName = VariantSwap.ResolveDisplayName(sibling, info);
        if (string.Equals(snapshot.Name, snapshot.VariantFamilyName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(snapshot.Name))
        {
            snapshot.Name = displayName;
        }

        return snapshot;
    }

    private static void UpdateItemFileProperties(DownloadableItemViewModel item, DownloadableFile detailedFile)
    {
        if (!string.IsNullOrEmpty(detailedFile.Filename))
        {
            item.Filename = detailedFile.Filename;
        }

        if (!string.IsNullOrEmpty(detailedFile.Category))
        {
            item.Category = detailedFile.Category;
            item.ContentType = ModDBCategoryMapper.MapCategoryByName(detailedFile.Category);
        }

        if (!string.IsNullOrEmpty(detailedFile.Uploader))
        {
            item.Uploader = detailedFile.Uploader;
        }

        if (!string.IsNullOrEmpty(detailedFile.Md5Hash))
        {
            item.Md5Hash = detailedFile.Md5Hash;
        }

        if (detailedFile.DownloadCount.HasValue)
        {
            item.DownloadCount = detailedFile.DownloadCount;
        }

        if (!string.IsNullOrEmpty(detailedFile.Description))
        {
            item.FullDescription = detailedFile.Description;
        }

        if (!string.IsNullOrEmpty(detailedFile.SizeDisplay))
        {
            item.SizeDisplay = detailedFile.SizeDisplay;
        }

        if (detailedFile.SizeBytes.HasValue && detailedFile.SizeBytes.Value > 0)
        {
            item.FileSize = detailedFile.SizeBytes.Value;
        }

        if (!string.IsNullOrEmpty(detailedFile.DownloadUrl))
        {
            item.DownloadUrl = detailedFile.DownloadUrl;
        }

        if (!string.IsNullOrEmpty(detailedFile.ThumbnailUrl))
        {
            item.ThumbnailUrl = detailedFile.ThumbnailUrl;
        }
    }

    private static void UpdateItemPreviewImages(DownloadableItemViewModel item, DownloadableFile detailedFile)
    {
        if (detailedFile.PreviewImages == null)
        {
            return;
        }

        item.PreviewImages.Clear();
        foreach (var img in detailedFile.PreviewImages)
        {
            item.PreviewImages.Add(img);
        }
    }

    private static string? ResolveTargetGameString(ContentSearchResult? sibling, ContentSearchResult fallbackResult)
    {
        if (sibling?.TargetGame is not null and not GameType.Unknown)
        {
            return sibling.TargetGame.ToString();
        }

        return fallbackResult.TargetGame != GameType.Unknown ? fallbackResult.TargetGame.ToString() : null;
    }

    private async Task InitializeVariantsAsync()
    {
        EnsureSynthesizedVariantSearchResults();

        if (variantSearchResults == null || variantSearchResults.Count == 0)
        {
            return;
        }

        var normalized = new Dictionary<string, ContentSearchResult>(StringComparer.OrdinalIgnoreCase);
        var variantsList = new List<InstallableVariant>();
        InstallableVariant? defaultSelection = null;

        foreach (var kvp in variantSearchResults)
        {
            var sibling = kvp.Value;
            var info = MatchVariantInfo(sibling, kvp.Key);
            var catalogKey = VariantSwap.ResolveCatalogKey(sibling, info);
            if (string.IsNullOrEmpty(catalogKey))
            {
                catalogKey = kvp.Key;
            }

            var snapshot = PrepareVariantSnapshot(sibling, info, catalogKey);
            normalized[catalogKey] = snapshot;

            var installable = new InstallableVariant
            {
                Name = snapshot.Name,
                ManifestId = catalogKey,
                IconUrl = sibling.IconUrl ?? string.Empty,
                VariantType = info.VariantType ?? string.Empty,
            };

            try
            {
                installable.CurrentState = await contentStateService.GetStateAsync(snapshot, _cts.Token);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to resolve state for detail variant {ManifestId}", installable.ManifestId);
            }

            variantsList.Add(installable);

            if (string.Equals(catalogKey, searchResult.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sibling.Id, searchResult.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kvp.Key, searchResult.Id, StringComparison.OrdinalIgnoreCase) ||
                (searchResult.TargetGame != GameType.Unknown && sibling.TargetGame == searchResult.TargetGame))
            {
                defaultSelection ??= installable;
            }
        }

        variantSearchResults = normalized;

        await RunOnUiThreadAsync(() =>
        {
            Variants = new ObservableCollection<InstallableVariant>(variantsList);
            OnPropertyChanged(nameof(HasVariants));
            RebuildVariantAxes();
            SelectedVariant = defaultSelection ?? Variants.FirstOrDefault();

            if (Releases.Count == 0)
            {
                PopulateReleasesFromVariants();
            }
        });
    }

    private void EnsureSynthesizedVariantSearchResults()
    {
        if ((variantSearchResults == null || variantSearchResults.Count == 0) && searchResult.Variants is { Count: > 0 } searchVariants)
        {
            var dict = new Dictionary<string, ContentSearchResult>(StringComparer.OrdinalIgnoreCase);
            var lastSegment = searchResult.Id?.Split('.').LastOrDefault() ?? "content";
            foreach (var v in searchVariants)
            {
                var manifestId = !string.IsNullOrEmpty(v.ManifestId)
                    ? v.ManifestId
                    : $"1.0.{searchResult.ProviderName.ToLowerInvariant()}.{searchResult.ContentType.ToString().ToLowerInvariant()}.{lastSegment}-{v.Id}";

                var variantSr = new ContentSearchResult
                {
                    Id = manifestId,
                    Name = string.IsNullOrEmpty(searchResult.VariantFamilyName) ? $"{searchResult.Name} - {v.Name}" : $"{searchResult.VariantFamilyName} - {v.Name}",
                    Description = searchResult.Description,
                    Version = searchResult.Version,
                    ContentType = searchResult.ContentType,
                    TargetGame = searchResult.TargetGame,
                    ProviderName = searchResult.ProviderName,
                    AuthorName = searchResult.AuthorName,
                    IconUrl = searchResult.IconUrl,
                    SourceUrl = searchResult.SourceUrl,
                    DownloadSize = searchResult.DownloadSize,
                    RequiresResolution = searchResult.RequiresResolution,
                    ResolverId = searchResult.ResolverId,
                    VariantGroupId = searchResult.VariantGroupId,
                    VariantFamilyName = searchResult.VariantFamilyName,
                    Variants = searchResult.Variants,
                };

                foreach (var kvp in searchResult.ResolverMetadata)
                {
                    variantSr.ResolverMetadata[kvp.Key] = kvp.Value;
                }

                dict[manifestId] = variantSr;
            }

            variantSearchResults = dict;
        }
    }

    private void OnAxisSelectionCommitted(InstallableVariant? value)
    {
        if (value == null || ReferenceEquals(SelectedVariant, value))
        {
            return;
        }

        SelectedVariant = value;
    }

    partial void OnSelectedVariantChanged(InstallableVariant? value)
    {
        VariantAxisGrouping.SyncSelections(VariantAxes, value);

        if (value != null)
        {
            IsDownloaded = value.CurrentState is ContentState.Downloaded or ContentState.UpdateAvailable;
            IsUpdateAvailable = value.CurrentState is ContentState.UpdateAvailable;
        }

        if (value != null &&
            !string.IsNullOrEmpty(value.ManifestId) &&
            variantSearchResults != null &&
            variantSearchResults.TryGetValue(value.ManifestId, out var sr))
        {
            VariantSwap.Apply(searchResult, sr);

            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(DownloadSize));
            OnPropertyChanged(nameof(LastUpdatedDisplay));
            OnPropertyChanged(nameof(Version));
            OnPropertyChanged(nameof(HasDownloadSize));
            OnPropertyChanged(nameof(ShowDownloadButton));
            OnPropertyChanged(nameof(ShowAddToProfileButton));
            OnPropertyChanged(nameof(ShowUpdateButton));
            OnPropertyChanged(nameof(IconUrl));
            OnPropertyChanged(nameof(ThumbnailUrl));
            OnPropertyChanged(nameof(IncludesSummary));
            OnPropertyChanged(nameof(HasIncludesSummary));
            OnPropertyChanged(nameof(IncludesSectionTitle));

            _ = LoadIconAsync();
            _ = LoadInitialStateAsync();
        }
        else
        {
            OnPropertyChanged(nameof(Name));
        }

        if (value != null && Releases.Count > 0)
        {
            var match = Releases.FirstOrDefault(r =>
                string.Equals(r.DownloadedManifestId, value.ManifestId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.Name, value.Name, StringComparison.OrdinalIgnoreCase));
            if (match != null && !ReferenceEquals(SelectedDownloadableItem, match))
            {
                SelectDownloadableItem(match);
            }
        }
    }

    /// <summary>
    /// Handles content state changes from the ContentStateService.
    /// </summary>
    private void OnContentStateChanged(object? sender, ContentStateChangedEventArgs e)
    {
        // A release/addon row publishes state under its synthesized "file:..." content ID. Flip
        // the matching row so a download completed anywhere (this tab, the card, or a wizard)
        // surfaces as "Add to Profile" on the correct row without reopening the detail view.
        if (e.NewState == ContentState.Downloaded)
        {
            UpdateRowStateForContentId(e.ContentId, e.ManifestId, downloaded: true);
        }
        else if (e.NewState == ContentState.NotDownloaded)
        {
            UpdateRowStateForContentId(e.ContentId, e.ManifestId, downloaded: false);
        }

        if (Variants.Count > 0)
        {
            SyncVariantsOnContentStateChanged(e);
        }

        UpdateMainContentState(e);
    }

    private void SyncVariantsOnContentStateChanged(ContentStateChangedEventArgs e)
    {
        var variantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { e.ContentId };
        if (!string.IsNullOrEmpty(e.ManifestId))
        {
            variantIds.Add(e.ManifestId);
        }

        Dispatcher.UIThread.Post(() =>
        {
            var selectedMatched = false;
            foreach (var variant in Variants)
            {
                if (UpdateSingleVariantState(variant, variantIds, e))
                {
                    selectedMatched = true;
                }
            }

            if (selectedMatched)
            {
                IsDownloaded = e.NewState is ContentState.Downloaded;
                IsUpdateAvailable = e.NewState == ContentState.UpdateAvailable;
                OnPropertyChanged(nameof(ShowDownloadButton));
                OnPropertyChanged(nameof(ShowAddToProfileButton));
                OnPropertyChanged(nameof(ShowUpdateButton));
            }

            SyncReleasesOnVariantStateChanged(variantIds, e);
        });
    }

    private bool UpdateSingleVariantState(InstallableVariant variant, HashSet<string> variantIds, ContentStateChangedEventArgs e)
    {
        var matched = !string.IsNullOrEmpty(variant.ManifestId) && variantIds.Contains(variant.ManifestId);
        if (!matched &&
            variantSearchResults != null &&
            !string.IsNullOrEmpty(variant.ManifestId) &&
            variantSearchResults.TryGetValue(variant.ManifestId, out var sibling) &&
            !string.IsNullOrEmpty(sibling.Id) &&
            variantIds.Contains(sibling.Id))
        {
            matched = true;
        }

        if (!matched)
        {
            return false;
        }

        variant.CurrentState = e.NewState;

        if (e.NewState == ContentState.Downloaded &&
            !string.IsNullOrEmpty(e.ManifestId) &&
            variantSearchResults != null &&
            !string.IsNullOrEmpty(variant.ManifestId) &&
            variantSearchResults.TryGetValue(variant.ManifestId, out var stored))
        {
            stored.UpdateId(e.ManifestId);
        }

        return ReferenceEquals(variant, SelectedVariant);
    }

    private void SyncReleasesOnVariantStateChanged(HashSet<string> variantIds, ContentStateChangedEventArgs e)
    {
        foreach (var release in Releases)
        {
            if (!string.IsNullOrEmpty(release.DownloadedManifestId) &&
                variantIds.Contains(release.DownloadedManifestId))
            {
                release.IsDownloaded = e.NewState == ContentState.Downloaded;
                if (!string.IsNullOrEmpty(e.ManifestId))
                {
                    release.DownloadedManifestId = e.ManifestId;
                }
            }
        }
    }

    private void UpdateMainContentState(ContentStateChangedEventArgs e)
    {
        var isForThisContent = e.ContentId == searchResult.Id ||
                               (!string.IsNullOrEmpty(e.ManifestId) && e.ManifestId == searchResult.Id);
        if (!isForThisContent)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            switch (e.NewState)
            {
                case ContentState.Downloaded:
                    IsDownloaded = true;
                    IsUpdateAvailable = false;
                    break;
                case ContentState.UpdateAvailable:
                    IsUpdateAvailable = true;
                    IsDownloaded = false;
                    break;
                case ContentState.NotDownloaded:
                    IsDownloaded = false;
                    IsUpdateAvailable = false;
                    break;
                default:
                    // State unchanged
                    break;
            }

            logger.LogDebug("Content state updated for {ContentId}: {State}", e.ContentId, e.NewState);
        });
    }

    /// <summary>
    /// Updates any release/addon row whose content ID (or resolved manifest ID) matches the
    /// changed content. Rows are keyed by their synthesized <c>file:</c> ID, so a download
    /// publishes the matching event; a manifest ID is also accepted so a row whose
    /// <see cref="IDownloadableRowViewModel.DownloadedManifestId"/> was resolved on populate
    /// still receives updates keyed by the manifest.
    /// </summary>
    private void UpdateRowStateForContentId(string contentId, string? manifestId, bool downloaded)
    {
        if (string.IsNullOrEmpty(contentId) && string.IsNullOrEmpty(manifestId))
        {
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (var row in EnumerateRows())
            {
                // Mirror CreateFileContentId: "file:<downloadUrl ?? name>".
                var rowKey = !string.IsNullOrEmpty(row.DownloadUrl) ? row.DownloadUrl : row.Name;
                var rowContentId = $"file:{rowKey}";
                var matches = rowContentId == contentId
                              || (!string.IsNullOrEmpty(manifestId) && row.DownloadedManifestId == manifestId);
                if (!matches)
                {
                    continue;
                }

                if (downloaded)
                {
                    if (!string.IsNullOrEmpty(manifestId))
                    {
                        row.DownloadedManifestId = manifestId;
                    }

                    row.IsDownloaded = true;
                    row.IsUpdateAvailable = false;
                }
                else
                {
                    row.IsDownloaded = false;
                    row.IsUpdateAvailable = false;
                }
            }
        });
    }

    private IEnumerable<IDownloadableRowViewModel> EnumerateRows()
    {
        foreach (var release in Releases)
        {
            yield return release;
        }

        foreach (var addon in Addons)
        {
            yield return addon;
        }
    }

    /// <summary>
    /// Queries the content state service for the initial state of this content and, when the
    /// content is already downloaded, rewrites the search-result ID to the stored manifest ID
    /// so that Add to Profile works without a re-download.
    /// </summary>
    private async Task LoadInitialStateAsync()
    {
        try
        {
            if (HasBundleComponents)
            {
                await RefreshBundleComponentStatesAsync();
                return;
            }

            var state = await contentStateService.GetStateAsync(searchResult, _cts.Token);

            if (state == ContentState.Downloaded &&
                (string.IsNullOrEmpty(searchResult.Id) || !ManifestIdValidator.IsValid(searchResult.Id, out _)))
            {
                var manifestId = await contentStateService.GetLocalManifestIdAsync(searchResult, _cts.Token);
                if (!string.IsNullOrEmpty(manifestId))
                {
                    searchResult.UpdateId(manifestId);
                }
            }

            await RunOnUiThreadAsync(() =>
            {
                IsDownloaded = state == ContentState.Downloaded;
                IsUpdateAvailable = state == ContentState.UpdateAvailable;
            });

            if (state == ContentState.Downloaded && !string.IsNullOrEmpty(searchResult.Id))
            {
                await LoadDependencySummaryAsync(searchResult.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to determine initial content state for {Name}", Name);
        }
    }

    /// <summary>
    /// Command to close the detail view (navigate back).
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        closeAction?.Invoke();
    }

    /// <summary>
    /// Opens the content's source page in the system browser.
    /// </summary>
    [RelayCommand]
    private void OpenInBrowser()
    {
        var url = searchResult.SourceUrl;
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            logger.LogWarning("Refusing to open non-http/https URL in browser: {Url}", url);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to open browser for {Url}", url);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the content has a source page to open.
    /// </summary>
    public bool HasSourceUrl => !string.IsNullOrEmpty(searchResult.SourceUrl);

    private int _iconLoadVersion;

    private async Task LoadIconAsync()
    {
        var thumbnailUrl = ThumbnailUrl;
        if (string.IsNullOrEmpty(thumbnailUrl))
        {
            logger.LogDebug("No thumbnail URL available for content: {Name}", Name);
            return;
        }

        var currentVersion = ++_iconLoadVersion;

        try
        {
            logger.LogDebug("Loading thumbnail from URL: {ThumbnailUrl}", thumbnailUrl);
            var loadedBitmap = await ImageCacheService.Instance.GetBitmapAsync(thumbnailUrl, _cts.Token);

            if (currentVersion == _iconLoadVersion && loadedBitmap != null)
            {
                IconBitmap = loadedBitmap;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load thumbnail from {ThumbnailUrl} for content: {Name}", thumbnailUrl, Name);
        }
    }

    /// <summary>
    /// Loads the basic parsed page data (context and overview info) without loading all tab content.
    /// </summary>
    private async Task LoadBasicParsedDataAsync()
    {
        lock (_basicContentLoadLock)
        {
            if (_basicContentLoaded || ParsedPage != null)
            {
                return;
            }

            _basicContentLoadTask ??= LoadBasicParsedDataCoreAsync();
        }

        await _basicContentLoadTask;
    }

    private void ResetDownloadState()
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsDownloaded = false;
            IsUpdateAvailable = false;
            DownloadProgress = 0;
            DownloadStatusMessage = string.Empty;
        });
    }

    /// <summary>
    /// Loads parsed details once for the lifetime of the detail view. ModDB owns its browser
    /// readiness check, so it must not be cancelled by the old generic fifteen-second timeout
    /// while the user completes a real Cloudflare verification in Chromium.
    /// </summary>
    private async Task LoadBasicParsedDataCoreAsync()
    {
        var loaded = false;

        try
        {
            IsLoadingDetails = true;
            if (string.IsNullOrEmpty(searchResult.SourceUrl))
            {
                return;
            }

            var parser = parsers.FirstOrDefault(p => p.CanParse(searchResult.SourceUrl));
            if (parser == null)
            {
                // No parser found for this URL
                logger.LogDebug("No parser found for URL: {Url}", searchResult.SourceUrl);
                return;
            }

            logger.LogInformation("Parsing web page: {Url}", searchResult.SourceUrl);

            var parsedPage = await FetchParsedPageAsync(parser);
            if (parsedPage == null)
            {
                logger.LogWarning("No parsed page data returned for {Url}; showing catalog data only", searchResult.SourceUrl);
                return;
            }

            if (IsBotProtectionPage(parsedPage))
            {
                return;
            }

            // Update on UI thread
            await RunOnUiThreadAsync(() =>
            {
                searchResult.ParsedPageData = parsedPage;
                _basicContentLoaded = true;

                // Load basic overview data
                LoadRichContent();
                loaded = true;
            });
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "Timed out waiting for verified ModDB details from {Url}", searchResult.SourceUrl);
            notificationService.ShowWarning("ModDB verification required", ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException && (ex.Message.Contains("Chromium", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("Playwright", StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogError(ex, "Managed Chromium setup failed while parsing ModDB details from {Url}", searchResult.SourceUrl);
            notificationService.ShowError("Chromium setup failed", ex.Message);
        }
        catch (OperationCanceledException ex) when (_cts.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Parsing web page data cancelled for {Url}", searchResult.SourceUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing web page data for {Url}", searchResult.SourceUrl);
        }
        finally
        {
            IsLoadingDetails = false;
            lock (_basicContentLoadLock)
            {
                // A failed parse remains retryable (for example, after the user completes a
                // Cloudflare challenge). A successful parse is protected by _basicContentLoaded.
                if (!loaded)
                {
                    _basicContentLoadTask = null;
                }
            }
        }
    }

    private async Task<ParsedWebPage?> FetchParsedPageAsync(IWebPageParser parser)
    {
        var isModDb = string.Equals(parser.ParserId, ModDBConstants.ResolverId, StringComparison.OrdinalIgnoreCase);
        if (isModDb)
        {
            notificationService.ShowInfo(
                "Loading ModDB details",
                "A browser window is opening to read this ModDB page. If it asks for verification, complete it there. Otherwise wait and do not click anything in that window — details will fill in automatically.",
                autoDismissMs: NotificationDurations.VeryLong);

            // PlaywrightService actively waits for a real ModDB document (or reports an
            // actionable verification timeout). Use cancellable view token.
            return await parser.ParseAsync(searchResult.SourceUrl ?? string.Empty, _cts.Token);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            return await parser.ParseAsync(searchResult.SourceUrl ?? string.Empty, timeoutCts.Token)
                .WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException ex)
        {
            if (_cts.IsCancellationRequested)
            {
                throw;
            }

            logger.LogWarning(ex, "Timed out parsing {Url}; showing catalog data only", searchResult.SourceUrl);
            return null;
        }
    }

    private bool IsBotProtectionPage(ParsedWebPage parsedPage)
    {
        // A bot-protection interstitial parses "successfully" but carries no real content.
        // Discard it so it cannot overwrite the name/description from the catalog.
        var parsedTitle = parsedPage.Context?.Title ?? string.Empty;
        if (parsedPage.Sections.Count == 0 &&
            (string.IsNullOrWhiteSpace(parsedTitle) ||
             parsedTitle.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
             parsedTitle.Contains("Attention Required", StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogWarning(
                "Parsed page for {Url} looks like a bot-protection challenge (title: '{Title}'); ignoring it",
                searchResult.SourceUrl,
                parsedTitle);
            return true;
        }

        return false;
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
        LoadPublisherMetadata();

        // Check both the new ParsedPageData property and the legacy Data property
        var parsedPage = searchResult.ParsedPageData ?? searchResult.GetData<ParsedWebPage>();
        if (parsedPage == null)
        {
            if (searchResult.ResolverMetadata.TryGetValue(CatalogConstants.CatalogItemJsonMetadataKey, out var catalogItemJson) &&
                !string.IsNullOrWhiteSpace(catalogItemJson))
            {
                PopulateFromCatalogMetadata(catalogItemJson);
            }

            return;
        }

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

        // Reload icon if the URL changed from parsed context or if previous load failed
        if (!string.IsNullOrEmpty(parsedPage.Context.IconUrl))
        {
            var iconUrlChanged = IconUrl != parsedPage.Context.IconUrl;
            if (iconUrlChanged || IconBitmap == null)
            {
                logger.LogDebug(
                    "Retrying icon load after ParsedPage loaded (URL changed: {Changed}, Previous load failed: {Failed})",
                    iconUrlChanged,
                    IconBitmap == null);
                _ = LoadIconAsync();
            }
        }

        UpdateContentTypeFromParsedPage(parsedPage);

        // Update parsed content collections
        Articles = parsedPage.Sections.OfType<Article>().ToObservableCollection() ?? [];
        Videos = parsedPage.Sections.OfType<Video>().ToObservableCollection() ?? [];
        Images = parsedPage.Sections.OfType<Image>().ToObservableCollection() ?? [];
        Reviews = parsedPage.Sections.OfType<Review>().ToObservableCollection() ?? [];
        Comments = FlattenComments(parsedPage.Sections.OfType<Comment>()).ToObservableCollection();

        PopulateFilesFromParsedPage(parsedPage);

        // Notify visibility properties
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(ShowFilesTab));
        OnPropertyChanged(nameof(HasImages));
        OnPropertyChanged(nameof(HasVideos));
        OnPropertyChanged(nameof(HasComments));
        OnPropertyChanged(nameof(HasReviews));
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(HasCommunity));
        OnPropertyChanged(nameof(HasReleases));
        OnPropertyChanged(nameof(HasAddons));
        OnPropertyChanged(nameof(AddonsCount));
    }

    private void UpdateContentTypeFromParsedPage(ParsedWebPage parsedPage)
    {
        var detailedPrimaryFile = parsedPage.Sections.OfType<DownloadableFile>().FirstOrDefault();
        if (detailedPrimaryFile != null && !string.IsNullOrWhiteSpace(detailedPrimaryFile.Category))
        {
            var detectedType = ModDBCategoryMapper.MapCategoryByName(detailedPrimaryFile.Category);
            if (detectedType != ContentType.Addon || detailedPrimaryFile.FileSectionType == FileSectionType.Addons)
            {
                SelectedContentType = detectedType;
            }
        }
        else if (searchResult.SourceUrl?.Contains("/mods/", StringComparison.OrdinalIgnoreCase) == true &&
                 !searchResult.SourceUrl.Contains("/addons/", StringComparison.OrdinalIgnoreCase) &&
                 (SelectedContentType == ContentType.Addon || SelectedContentType == ContentType.UnknownContentType))
        {
            SelectedContentType = ContentType.Mod;
        }
    }

    private void PopulateFilesFromParsedPage(ParsedWebPage parsedPage)
    {
        if (parsedPage.Sections.Any(s => s is DownloadableFile))
        {
            Files = parsedPage.Sections.OfType<DownloadableFile>()
                .OrderByDescending(f => f.ReleaseDate ?? f.UploadDate ?? DateTime.MinValue)
                .ThenByDescending(f => f.Version, StringComparer.OrdinalIgnoreCase)
                .ToObservableCollection();
            PopulateReleases(Files);
            PopulateAddons(Files);
            _ = TriggerPreloadRecentItemDetailsAsync(_cts.Token);
        }
        else if (!string.IsNullOrEmpty(searchResult.SourceUrl) && searchResult.RequiresResolution)
        {
            var fileName = GetFileNameFromUrl(searchResult.SourceUrl) ?? $"{searchResult.Name}.zip";
            var file = new DownloadableFile(
                Name: fileName,
                DownloadUrl: searchResult.SourceUrl,
                SizeBytes: searchResult.DownloadSize > 0 ? searchResult.DownloadSize : null);
            Files = [file];
            PopulateReleases(Files);
        }
        else
        {
            Files = [];
        }
    }

    /// <summary>
    /// Loads publisher profile and referrals metadata from catalog or built-in constants.
    /// </summary>
    private void LoadPublisherMetadata()
    {
        if (searchResult.ResolverMetadata.TryGetValue(CatalogConstants.PublisherProfileJsonMetadataKey, out var pubJson) &&
            !string.IsNullOrWhiteSpace(pubJson))
        {
            try
            {
                PublisherProfile = JsonSerializer.Deserialize<PublisherProfile>(pubJson);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deserialize publisher profile");
            }
        }
        else if (!string.IsNullOrWhiteSpace(searchResult.ProviderName))
        {
            var (builtInName, builtInWeb, builtInSupport) = GetBuiltInPublisherInfo(searchResult.ProviderName);
            PublisherProfile = new PublisherProfile
            {
                Name = builtInName,
                Website = builtInWeb,
                SupportUrl = builtInSupport,
                AvatarUrl = PublisherInfoConstants.GetPublisherLogo(searchResult.ProviderName, searchResult.Id),
            };
        }

        if (searchResult.ResolverMetadata.TryGetValue(CatalogConstants.CatalogReferralsJsonMetadataKey, out var refJson) &&
            !string.IsNullOrWhiteSpace(refJson))
        {
            try
            {
                var refs = JsonSerializer.Deserialize<List<PublisherReferral>>(refJson);
                if (refs != null)
                {
                    PublisherReferrals.Clear();
                    foreach (var r in refs)
                    {
                        PublisherReferrals.Add(r);
                    }

                    OnPropertyChanged(nameof(HasPublisherReferrals));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deserialize publisher referrals");
            }
        }

        OnPropertyChanged(nameof(PublisherDisplayName));
        OnPropertyChanged(nameof(PublisherAvatarUrl));
        OnPropertyChanged(nameof(PublisherWebsite));
        OnPropertyChanged(nameof(PublisherSupportUrl));
        OnPropertyChanged(nameof(PublisherContactEmail));
        OnPropertyChanged(nameof(HasPublisherProfile));
        OnPropertyChanged(nameof(HasPublisherInfo));
    }

    /// <summary>
    /// Populates view model content and tab collections from serialized catalog item metadata.
    /// </summary>
    /// <param name="catalogItemJson">The JSON string representing the catalog content item.</param>
    private void PopulateFromCatalogMetadata(string catalogItemJson)
    {
        try
        {
            var catalogItem = JsonSerializer.Deserialize<CatalogContentItem>(catalogItemJson);
            if (catalogItem == null)
            {
                return;
            }

            PopulateCatalogScreenshots(catalogItem);
            PopulateCatalogReleases(catalogItem);
            PopulateCatalogMedia(catalogItem);

            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(HasReleases));
            OnPropertyChanged(nameof(HasVideos));
            OnPropertyChanged(nameof(HasImages));
            OnPropertyChanged(nameof(HasMedia));
            OnPropertyChanged(nameof(HasMultipleScreenshots));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "error populating content details from catalog metadata json");
        }
    }

    private void PopulateCatalogScreenshots(CatalogContentItem catalogItem)
    {
        if (catalogItem.Metadata?.ScreenshotUrls == null || catalogItem.Metadata.ScreenshotUrls.Count == 0)
        {
            return;
        }

        foreach (var screenshot in catalogItem.Metadata.ScreenshotUrls.Where(screenshot => !Screenshots.Contains(screenshot)))
        {
            Screenshots.Add(screenshot);
        }

        if (string.IsNullOrEmpty(SelectedScreenshotUrl))
        {
            SelectedScreenshotUrl = Screenshots.FirstOrDefault() ?? string.Empty;
        }
    }

    private void PopulateCatalogReleases(CatalogContentItem catalogItem)
    {
        if (catalogItem.Releases is not { Count: > 0 })
        {
            return;
        }

        Releases.Clear();
        var sortedCatalogReleases = catalogItem.Releases
            .OrderByDescending(r => r.ReleaseDate)
            .ThenByDescending(r => r.Version, StringComparer.OrdinalIgnoreCase);

        foreach (var rel in sortedCatalogReleases)
        {
            var releaseItem = CreateCatalogReleaseItem(rel, catalogItem);
            Releases.Add(releaseItem);
            if (!HasBundleComponents && searchResult.ContentType != ContentType.ContentBundle && releaseItem.File != null)
            {
                _ = ResolveRowStateAsync(releaseItem, releaseItem.File);
            }
        }

        if (!_userManuallySelectedDownloadableItem || SelectedDownloadableItem == null || !Releases.Contains(SelectedDownloadableItem))
        {
            var preferredRelease = FindPreferredRelease(Releases);
            if (preferredRelease != null)
            {
                SelectDownloadableItem(preferredRelease, isUserInitiated: false);
            }
        }

        OnPropertyChanged(nameof(HasReleases));
        OnPropertyChanged(nameof(ReleasesCount));
        OnPropertyChanged(nameof(ShowSelectedTargetBanner));
    }

    private void PopulateCatalogMedia(CatalogContentItem catalogItem)
    {
        var videoList = new List<Video>();
        if (!string.IsNullOrWhiteSpace(catalogItem.Metadata?.VideoUrl))
        {
            videoList.Add(new Video(
                Title: $"{catalogItem.Name} Preview",
                ThumbnailUrl: catalogItem.Metadata.BannerUrl ?? searchResult.IconUrl ?? string.Empty,
                EmbedUrl: catalogItem.Metadata.VideoUrl,
                Platform: "Web"));
        }

        Videos = videoList.ToObservableCollection();

        var imageList = new List<Image>();
        if (catalogItem.Metadata?.ScreenshotUrls != null)
        {
            var shotIndex = 1;
            foreach (var shot in catalogItem.Metadata.ScreenshotUrls)
            {
                imageList.Add(new Image(
                    Title: $"Screenshot {shotIndex++}",
                    ThumbnailUrl: shot,
                    FullSizeUrl: shot));
            }
        }

        Images = imageList.ToObservableCollection();
    }

    private ReleaseItemViewModel CreateCatalogReleaseItem(ContentRelease rel, CatalogContentItem catalogItem)
    {
        var primaryArtifact = rel.Artifacts.FirstOrDefault(a => a.IsPrimary) ?? rel.Artifacts.FirstOrDefault();
        var downloadUrl = primaryArtifact?.DownloadUrl ?? string.Empty;
        var fileSize = primaryArtifact?.Size ?? 0;
        var filename = primaryArtifact?.Filename ?? GetFileNameFromUrl(downloadUrl) ?? $"{catalogItem.Name} v{rel.Version}";
        var description = !string.IsNullOrWhiteSpace(rel.Changelog) ? rel.Changelog : catalogItem.Description;
        var category = searchResult.ContentType.GetDisplayName();
        var uploader = searchResult.AuthorName;

        var file = new DownloadableFile(
            Name: filename,
            DownloadUrl: downloadUrl,
            SizeBytes: fileSize > 0 ? fileSize : null,
            UploadDate: rel.ReleaseDate,
            Version: rel.Version,
            Category: category,
            Uploader: uploader,
            Filename: filename,
            Description: description,
            FileSectionType: FileSectionType.Downloads);

        var releaseName = $"Version {rel.Version}";

        var releaseItem = new ReleaseItemViewModel
        {
            Id = Guid.NewGuid().ToString(),
            Name = releaseName,
            Version = rel.Version,
            ReleaseDate = rel.ReleaseDate,
            FileSize = fileSize,
            DownloadUrl = downloadUrl,
            DetailsUrl = downloadUrl,
            File = file,
            ContentType = searchResult.ContentType,
            Category = category,
            Uploader = uploader,
            Filename = filename,
            FullDescription = description,
            Md5Hash = primaryArtifact?.Sha256,
            IsDetailsLoaded = true,
        };

        releaseItem.SelectCommand = new RelayCommand(
            () => SelectDownloadableItem(releaseItem, isUserInitiated: true),
            () => !IsDownloading);

        if (HasBundleComponents || searchResult.ContentType == ContentType.ContentBundle)
        {
            releaseItem.DownloadCommand = new AsyncRelayCommand(() => DownloadBundleComponentsAsync(CancellationToken.None));
            releaseItem.AddToProfileCommand = new AsyncRelayCommand(() => AddToProfileAsync());
            releaseItem.IsDownloaded = AreBundleComponentsReadyForProfile;
        }
        else
        {
            releaseItem.DownloadCommand = new AsyncRelayCommand(() => DownloadReleaseAsync(releaseItem, releaseItem.File ?? file));
            releaseItem.AddToProfileCommand = new AsyncRelayCommand(
                () => AddFileToProfileAsync(releaseItem.File ?? file, releaseItem.DownloadedManifestId));
        }

        return releaseItem;
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
            await RunOnUiThreadAsync(() =>
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
            await RunOnUiThreadAsync(() =>
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

            await RunOnUiThreadAsync(() =>
            {
                // Populate releases from the Files collection
                if (ParsedPage != null)
                {
                    var files = ParsedPage.Sections.OfType<DownloadableFile>().ToList();
                    PopulateReleases(files);
                    _ = TriggerPreloadRecentItemDetailsAsync(_cts.Token);
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

            await RunOnUiThreadAsync(() =>
            {
                // Populate addons from the Files collection
                // Addons are typically marked differently in the parsed page
                // For now, we'll use all files that aren't main downloads
                if (ParsedPage != null)
                {
                    var files = ParsedPage.Sections.OfType<DownloadableFile>().ToList();
                    PopulateAddons(files);
                    _ = TriggerPreloadRecentItemDetailsAsync(_cts.Token);
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
    [ObservableProperty]
    private ObservableCollection<Article> _articles = [];

    /// <summary>
    /// Gets the videos from the parsed page.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<Video> _videos = [];

    /// <summary>
    /// Gets the images from the parsed page (excluding screenshots).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<Image> _images = [];

    /// <summary>
    /// Gets the files from the parsed page, or creates a fallback file entry for catalog-based content.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DownloadableFile> _files = [];

    /// <summary>
    /// Gets the reviews from the parsed page.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<Review> _reviews = [];

    /// <summary>
    /// Gets the comments from the parsed page.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<Comment> _comments = [];

    /// <summary>
    /// Gets a value indicating whether files are available.
    /// </summary>
    public bool HasFiles => Files.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the Files tab should be shown.
    /// Structured releases and addons own their respective lists; the raw Files tab is reserved
    /// for content that has no structured release or addon grouping.
    /// </summary>
    public bool ShowFilesTab => Files.Count > 0 && !HasReleases && !HasAddons;

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
    /// Gets the content name. Prefer the selected variant label or user-selected downloadable item
    /// so the specific mod/patch/addon title stays visible; fall back to search result, then parsed page title.
    /// </summary>
    public string Name => SelectedVariant?.Name
        ?? (SelectedDownloadableItem != null &&
            !string.IsNullOrWhiteSpace(SelectedDownloadableItem.Name) &&
            !SelectedDownloadableItem.Name.StartsWith(UnknownValue, StringComparison.OrdinalIgnoreCase)
                ? SelectedDownloadableItem.Name
                : null)
        ?? (!string.IsNullOrWhiteSpace(searchResult.Name) ? searchResult.Name : null)
        ?? ParsedPage?.Context.Title
        ?? UnknownValue;

    /// <summary>
    /// Gets the content description (full) - prefers parsed page context description.
    /// </summary>
    public string Description =>
        HtmlTextHelper.NormalizeHtml(ParsedPage?.Context.Description ?? searchResult.Description);

    /// <summary>
    /// Gets the author name - prefers parsed page context developer.
    /// </summary>
    public string AuthorName =>
        ParsedPage?.Context.Developer ?? searchResult.AuthorName ?? UnknownValue;

    /// <summary>
    /// Gets the version.
    /// </summary>
    public string Version => SelectedDownloadableItem?.Version ?? searchResult.Version ?? string.Empty;

    /// <summary>
    /// Gets the last updated date (optional) - prefers parsed page context release date.
    /// </summary>
    public DateTime? LastUpdated =>
        SelectedDownloadableItem?.ReleaseDate ?? ParsedPage?.Context.ReleaseDate ?? searchResult.LastUpdated;

    /// <summary>
    /// Gets the formatted last updated string.
    /// </summary>
    public string LastUpdatedDisplay => LastUpdated?.ToString("MMM dd, yyyy") ?? string.Empty;

    /// <summary>
    /// Gets the download size - prefers size from parsed files or selected item.
    /// </summary>
    public long DownloadSize
    {
        get
        {
            if (SelectedDownloadableItem is { FileSize: > 0 })
            {
                return SelectedDownloadableItem.FileSize;
            }

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
                             !string.Equals(AuthorName, UnknownValue, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the content type.
    /// </summary>
    public ContentType ContentType => SelectedContentType;

    partial void OnSelectedContentTypeChanged(ContentType value)
    {

        if (SelectedDownloadableItem != null)
        {
            SelectedDownloadableItem.ContentType = value;
            if (!_suppressContentTypePersist && SelectedDownloadableItem.IsDownloaded && !string.IsNullOrEmpty(SelectedDownloadableItem.DownloadedManifestId))
            {
                _contentTypePersistTask = PersistContentTypeChangeAsync(value, SelectedDownloadableItem.DownloadedManifestId);
            }

            return;
        }

        searchResult.ContentType = value;

        // Pre-download: the coordinator reads searchResult.ContentType when building the manifest.
        // Post-download: persist so Add to Profile / launch use the corrected classification.
        if (!_suppressContentTypePersist && IsDownloaded)
        {
            _contentTypePersistTask = PersistContentTypeChangeAsync(value);
        }
    }

    /// <summary>
    /// Selects a release or addon row as the primary action target.
    /// </summary>
    /// <param name="item">The row item to select.</param>
    [RelayCommand]
    private void SelectDownloadableItem(DownloadableItemViewModel item)
    {
        SelectDownloadableItem(item, isUserInitiated: true);
    }

    private void SelectDownloadableItem(DownloadableItemViewModel item, bool isUserInitiated)
    {
        if (item == null || IsDownloading)
        {
            return;
        }

        if (isUserInitiated)
        {
            _userManuallySelectedDownloadableItem = true;
        }

        SelectedDownloadableItem = item;

        foreach (var release in Releases)
        {
            release.IsSelected = ReferenceEquals(release, item);
        }

        foreach (var addon in Addons)
        {
            addon.IsSelected = ReferenceEquals(addon, item);
        }

        if (Variants.Count > 0)
        {
            var matchingVariant = Variants.FirstOrDefault(v =>
                string.Equals(v.ManifestId, item.DownloadedManifestId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v.Name, item.Name, StringComparison.OrdinalIgnoreCase));
            if (matchingVariant != null && !ReferenceEquals(SelectedVariant, matchingVariant))
            {
                SelectedVariant = matchingVariant;
            }
        }

        _suppressContentTypePersist = true;
        try
        {
            SelectedContentType = item.ContentType;
        }
        finally
        {
            _suppressContentTypePersist = false;
        }

        RefreshSelectedTargetProperties();
    }

    /// <summary>
    /// Clears the selected release/addon row, returning the action panel to the primary content.
    /// </summary>
    [RelayCommand]
    private void ClearSelectedDownloadableItem()
    {
        _userManuallySelectedDownloadableItem = false;
        SelectedDownloadableItem = null;

        foreach (var release in Releases)
        {
            release.IsSelected = false;
        }

        foreach (var addon in Addons)
        {
            addon.IsSelected = false;
        }

        _suppressContentTypePersist = true;
        try
        {
            SelectedContentType = searchResult.ContentType;
        }
        finally
        {
            _suppressContentTypePersist = false;
        }

        RefreshSelectedTargetProperties();
    }

    private void RefreshSelectedTargetProperties()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(HasSelectedDownloadableItem));
        OnPropertyChanged(nameof(ShowSelectedTargetBanner));
        OnPropertyChanged(nameof(SelectedTargetTitle));
        OnPropertyChanged(nameof(SelectedTargetCategory));
        OnPropertyChanged(nameof(DownloadSize));
        OnPropertyChanged(nameof(HasDownloadSize));
        OnPropertyChanged(nameof(LastUpdatedDisplay));
        OnPropertyChanged(nameof(HasLastUpdated));
        OnPropertyChanged(nameof(Version));
        OnPropertyChanged(nameof(HasVersion));
        OnPropertyChanged(nameof(ShowDownloadButton));
        OnPropertyChanged(nameof(ShowAddToProfileButton));
        OnPropertyChanged(nameof(ShowUpdateButton));
    }

    /// <summary>
    /// Gets the provider name.
    /// </summary>
    public string ProviderName => searchResult.ProviderName ?? string.Empty;

    /// <summary>
    /// Gets the icon URL - prefers parsed page context icon.
    /// </summary>
    public string? IconUrl => ParsedPage?.Context.IconUrl ?? searchResult.IconUrl;

    /// <summary>
    /// Gets the preferred header thumbnail URL (banner / screenshot / icon).
    /// </summary>
    public string? ThumbnailUrl =>
        !string.IsNullOrWhiteSpace(searchResult.BannerUrl)
            ? searchResult.BannerUrl
            : ContentCardBadgeHelper.GetThumbnailUrl(searchResult) ?? IconUrl;

    /// <summary>
    /// Gets a comma-separated includes summary for bundles / multi-content packages.
    /// Prefers the post-download required-dependency list when available.
    /// </summary>
    public string IncludesSummary =>
        !string.IsNullOrWhiteSpace(RequiredDependenciesSummary)
            ? RequiredDependenciesSummary
            : ContentCardBadgeHelper.GetIncludesSummary(searchResult);

    /// <summary>
    /// Gets a value indicating whether an includes / requires summary is available.
    /// </summary>
    public bool HasIncludesSummary => !HasBundleComponents && !string.IsNullOrWhiteSpace(IncludesSummary);

    /// <summary>
    /// Gets the sidebar section title for included or required content.
    /// </summary>
    public string IncludesSectionTitle =>
        !string.IsNullOrWhiteSpace(RequiredDependenciesSummary) ? "Requires" : "Includes";

    /// <summary>
    /// Gets a value indicating whether the Download button should be shown.
    /// </summary>
    public bool ShowDownloadButton
    {
        get
        {
            if (HasBundleComponents)
            {
                return !AreBundleComponentsReadyForProfile && !IsDownloading;
            }

            if (SelectedDownloadableItem != null)
            {
                return !SelectedDownloadableItem.IsDownloaded && !SelectedDownloadableItem.IsDownloading && !SelectedDownloadableItem.IsUpdateAvailable;
            }

            return !IsDownloaded && !IsUpdateAvailable;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the Update button should be shown.
    /// </summary>
    public bool ShowUpdateButton
    {
        get
        {
            if (HasBundleComponents)
            {
                return false;
            }

            if (SelectedDownloadableItem != null)
            {
                return SelectedDownloadableItem.IsUpdateAvailable && !SelectedDownloadableItem.IsDownloading;
            }

            return IsUpdateAvailable && !IsDownloading;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the Add to Profile button should be shown.
    /// </summary>
    public bool ShowAddToProfileButton
    {
        get
        {
            if (HasBundleComponents)
            {
                return AreBundleComponentsReadyForProfile;
            }

            return SelectedDownloadableItem != null
                ? SelectedDownloadableItem.IsDownloaded
                : IsDownloaded;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the content type can be manually changed.
    /// </summary>
    public bool IsContentTypeEditable => !HasBundleComponents && searchResult.ResolverId != CatalogConstants.GenericCatalogResolverId;

    /// <summary>
    /// Command to download the main content or selected row target.
    /// </summary>
    [RelayCommand]
    private async Task DownloadAsync(CancellationToken cancellationToken = default)
    {
        if (HasBundleComponents)
        {
            await DownloadBundleComponentsAsync(cancellationToken);
            return;
        }

        if (SelectedDownloadableItem != null)
        {
            var file = SelectedDownloadableItem.File;
            if (file != null)
            {
                if (SelectedDownloadableItem is ReleaseItemViewModel rel)
                {
                    await DownloadReleaseAsync(rel, file);
                }
                else if (SelectedDownloadableItem is AddonItemViewModel addon)
                {
                    await DownloadAddonAsync(addon, file);
                }
                else
                {
                    await DownloadFileCoreAsync(
                        file,
                        manifest =>
                        {
                            SelectedDownloadableItem.DownloadedManifestId = manifest.Id.Value;
                            SelectedDownloadableItem.IsDownloaded = true;
                            RefreshSelectedTargetProperties();
                        },
                        cancellationToken);
                }
            }

            return;
        }

        await ExecuteDownloadFlowAsync(searchResult, cancellationToken);
    }

    private async Task DownloadBundleComponentsAsync(CancellationToken cancellationToken)
    {
        var targets = BundleComponentViewModel.GetRequiredDownloadTargets(BundleComponents);
        if (targets.Count == 0)
        {
            DownloadStatusMessage = "All selected content is already downloaded";
            await RefreshBundleComponentStatesAsync();
            return;
        }

        IsDownloading = true;
        DownloadProgress = 0;
        var completed = 0;
        try
        {
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DownloadStatusMessage = $"Downloading {target.Name} ({completed + 1}/{targets.Count})...";

                var progress = new Progress<ContentAcquisitionProgress>(p =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!IsDownloading)
                        {
                            return;
                        }

                        var slice = 100.0 / targets.Count;
                        DownloadProgress = (int)((completed * slice) + (p.ProgressPercentage * slice / 100.0));
                        DownloadStatusMessage = $"{target.Name}: {p.FormatProgressStatus()}";
                    });
                });

                var result = await downloadCoordinator.DownloadContentAsync(target, progress, cancellationToken);
                if (!result.Success || result.Data == null)
                {
                    DownloadStatusMessage = result.FirstError ?? "Download failed";
                    return;
                }

                var originalContentId = target.Id ?? string.Empty;
                foreach (var component in BundleComponents)
                {
                    component.MarkDownloaded(originalContentId, result.Data.Id.Value);
                }

                completed++;
            }

            await RefreshBundleComponentStatesAsync();
            DownloadProgress = 100;
            DownloadStatusMessage = "Download complete!";
            IsDownloaded = AreBundleComponentsReadyForProfile;
            if (HasBundleComponents)
            {
                foreach (var rel in Releases)
                {
                    rel.IsDownloaded = AreBundleComponentsReadyForProfile;
                }
            }

            OnPropertyChanged(nameof(ShowDownloadButton));
            OnPropertyChanged(nameof(ShowAddToProfileButton));
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private async Task RefreshBundleComponentStatesAsync()
    {
        foreach (var component in BundleComponents)
        {
            await component.RefreshStateAsync(contentStateService, _cts.Token);
        }

        await RunOnUiThreadAsync(() =>
        {
            IsDownloaded = AreBundleComponentsReadyForProfile;
            if (HasBundleComponents)
            {
                foreach (var rel in Releases)
                {
                    rel.IsDownloaded = AreBundleComponentsReadyForProfile;
                }
            }

            OnPropertyChanged(nameof(AreBundleComponentsReadyForProfile));
            OnPropertyChanged(nameof(ShowDownloadButton));
            OnPropertyChanged(nameof(ShowAddToProfileButton));
        });
    }

    private void OnBundleComponentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BundleComponentViewModel.SelectedVariant)
            or nameof(BundleComponentViewModel.IsSelectedDownloaded)
            or nameof(BundleComponentViewModel.CurrentState)
            or nameof(BundleComponentViewModel.RequiresDownload))
        {
            if (HasBundleComponents)
            {
                foreach (var rel in Releases)
                {
                    rel.IsDownloaded = AreBundleComponentsReadyForProfile;
                }
            }

            OnPropertyChanged(nameof(AreBundleComponentsReadyForProfile));
            OnPropertyChanged(nameof(ShowDownloadButton));
            OnPropertyChanged(nameof(ShowAddToProfileButton));
        }
    }

    /// <summary>
    /// Executes the download flow for a specific content search result.
    /// </summary>
    private async Task ExecuteDownloadFlowAsync(
        ContentSearchResult targetContent,
        CancellationToken cancellationToken,
        Action<ContentManifest>? onDownloadCompleted = null)
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

            if (IsModDbContent(targetContent))
            {
                notificationService.ShowInfo(
                    "ModDB download starting",
                    "A browser window will open to fetch this file. Wait for the download to finish and do not click anything in that window.",
                    autoDismissMs: NotificationDurations.VeryLong);
            }

            // Use the ContentDownloadCoordinator to properly acquire content. Coalesce progress
            // updates so the bar never moves backward and the status text does not churn on every
            // sub-step (the orchestrator reports several sub-steps per stage, which otherwise makes
            // the bar flicker near completion).
            var progress = new Progress<ContentAcquisitionProgress>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!IsDownloading)
                    {
                        return;
                    }

                    var intPercent = (int)Math.Round(p.ProgressPercentage);
                    if (intPercent >= DownloadProgress)
                    {
                        DownloadProgress = intPercent;
                    }

                    var status = p.FormatProgressStatus();
                    if (!string.Equals(status, DownloadStatusMessage, StringComparison.Ordinal))
                    {
                        DownloadStatusMessage = status;
                    }
                });
            });

            var result = await downloadCoordinator.DownloadContentAsync(targetContent, progress, cancellationToken);

            if (result.Success && result.Data != null)
            {
                var manifest = result.Data;
                DownloadProgress = 100;
                DownloadStatusMessage = "Download complete!";
                UpdateDependencySummary(manifest);

                // Only update the main search result and downloaded state if we were downloading the main content
                // and it was not previously downloaded
                if (targetContent == searchResult)
                {
                    IsDownloaded = true;

                    if (SelectedVariant != null)
                    {
                        SelectedVariant.CurrentState = ContentState.Downloaded;
                        if (variantSearchResults != null &&
                            !string.IsNullOrEmpty(SelectedVariant.ManifestId) &&
                            variantSearchResults.TryGetValue(SelectedVariant.ManifestId, out var stored))
                        {
                            stored.UpdateId(manifest.Id.Value);
                        }
                    }

                    foreach (var release in Releases)
                    {
                        if (SelectedVariant != null &&
                            string.Equals(release.DownloadedManifestId, SelectedVariant.ManifestId, StringComparison.OrdinalIgnoreCase))
                        {
                            release.IsDownloaded = true;
                            release.DownloadedManifestId = manifest.Id.Value;
                        }
                    }

                    // Note: searchResult ID update and state change notification are handled by the coordinator
                }

                onDownloadCompleted?.Invoke(manifest);
            }
            else
            {
                var errorMsg = result.FirstError ?? "Unknown error";
                DownloadStatusMessage = $"Error: {errorMsg}";

                // Surface the failure as a toast so the user sees actionable text (e.g. the ModDB
                // WAF block message) instead of only the inline status label.
                notificationService.ShowError("Download failed", errorMsg);
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Download cancelled for: {Name}", targetContent.Name);
            DownloadStatusMessage = "Download cancelled";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error downloading content: {Name}", targetContent.Name);
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
    private Task DownloadFileAsync(
        DownloadableFile file,
        CancellationToken cancellationToken = default) =>
        DownloadFileCoreAsync(file, null, cancellationToken);

    private async Task DownloadFileCoreAsync(
        DownloadableFile file,
        Action<ContentManifest>? onDownloadCompleted = null,
        CancellationToken cancellationToken = default)
    {
        if (file == null || string.IsNullOrEmpty(file.DownloadUrl))
        {
            logger.LogWarning("Cannot download file: invalid file or missing download URL");
            return;
        }

        try
        {
            logger.LogInformation("Downloading individual file: {FileName} from {Url}", file.Name, file.DownloadUrl);
            await ExecuteDownloadFlowAsync(CreateFileSearchResult(file), cancellationToken, onDownloadCompleted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error downloading file: {FileName}", file.Name);
        }
    }

    /// <summary>
    /// Builds the per-row <see cref="ContentSearchResult"/> for an individual release/addon file.
    /// Both the download path and the install-state detection path MUST use this same identity so
    /// that a row detected as already-installed resolves to the exact manifest that a download
    /// would have produced.
    /// </summary>
    private ContentSearchResult CreateFileSearchResult(DownloadableFile file, ContentType? overrideContentType = null)
    {
        ArgumentNullException.ThrowIfNull(file);

        ContentType fileContentType;
        if (overrideContentType.HasValue)
        {
            fileContentType = overrideContentType.Value;
        }
        else if (SelectedDownloadableItem?.File == file)
        {
            fileContentType = SelectedDownloadableItem.ContentType;
        }
        else if (!string.IsNullOrWhiteSpace(file.Category))
        {
            fileContentType = ModDBCategoryMapper.MapCategoryByName(file.Category);
        }
        else
        {
            fileContentType = file.FileSectionType == FileSectionType.Downloads ? ContentType.Mod : searchResult.ContentType;
        }

        var rowVersion = CommunityOutpostCatalogConstants.DefaultMetadataVersion;
        if (!string.IsNullOrWhiteSpace(file.Version))
        {
            rowVersion = file.Version;
        }
        else if (!string.IsNullOrWhiteSpace(searchResult.Version))
        {
            rowVersion = searchResult.Version;
        }

        var rowSearchResult = new ContentSearchResult
        {
            // A row download must not reuse the parent catalog ID. The coordinator publishes
            // state for the supplied ID, so sharing it would incorrectly mark the parent as
            // downloaded and make its Add to Profile action target whichever row finished last.
            Id = CreateFileContentId(file),
            Name = file.Name ?? file.DownloadUrl ?? UnknownValue,
            Version = rowVersion,
            ProviderName = searchResult.ProviderName,
            ContentType = fileContentType,
            TargetGame = searchResult.TargetGame,
            LastUpdated = file.ReleaseDate ?? file.UploadDate ?? searchResult.LastUpdated,

            // Preserve the page URL for metadata and browser Referer handling. The selected
            // direct URL tells the resolver which already-discovered release to acquire.
            SourceUrl = file.DetailsUrl ?? searchResult.SourceUrl,
            SelectedDownloadUrl = file.DownloadUrl,
            ParsedPageData = ParsedPage ?? searchResult.ParsedPageData,
            ResolverId = searchResult.ResolverId,
            RequiresResolution = true,
        };

        // Copy resolver metadata (e.g. GitHub owner/tag, CommunityOutpost content code) so the
        // provenance-aware state matcher and SuperHackers variant detection treat the row like
        // the parent card.
        foreach (var pair in searchResult.ResolverMetadata)
        {
            rowSearchResult.ResolverMetadata[pair.Key] = pair.Value;
        }

        if (IsModDbContent(searchResult))
        {
            var detailUrl = file.DetailsUrl ?? file.DownloadUrl;
            if (!string.IsNullOrWhiteSpace(detailUrl))
            {
                rowSearchResult.ResolverMetadata[ModDBConstants.ContentIdMetadataKey] = ModDBDiscoverer.ExtractModDBIdFromUrl(detailUrl);
            }
            else
            {
                rowSearchResult.ResolverMetadata.Remove(ModDBConstants.ContentIdMetadataKey);
            }
        }

        return rowSearchResult;
    }

    private async Task DownloadReleaseAsync(ReleaseItemViewModel releaseItem, DownloadableFile file)
    {
        releaseItem.IsDownloading = true;
        if (ReferenceEquals(SelectedDownloadableItem, releaseItem))
        {
            RefreshSelectedTargetProperties();
        }

        try
        {
            await DownloadFileCoreAsync(file, manifest =>
            {
                releaseItem.DownloadedManifestId = manifest.Id.Value;
                releaseItem.IsDownloaded = true;
                if (ReferenceEquals(SelectedDownloadableItem, releaseItem))
                {
                    RefreshSelectedTargetProperties();
                }
            });
        }
        finally
        {
            releaseItem.IsDownloading = false;
            if (ReferenceEquals(SelectedDownloadableItem, releaseItem))
            {
                RefreshSelectedTargetProperties();
            }
        }
    }

    private async Task DownloadAddonAsync(AddonItemViewModel addonItem, DownloadableFile file)
    {
        addonItem.IsDownloading = true;
        if (ReferenceEquals(SelectedDownloadableItem, addonItem))
        {
            RefreshSelectedTargetProperties();
        }

        try
        {
            await DownloadFileCoreAsync(file, manifest =>
            {
                addonItem.DownloadedManifestId = manifest.Id.Value;
                addonItem.IsDownloaded = true;
                if (ReferenceEquals(SelectedDownloadableItem, addonItem))
                {
                    RefreshSelectedTargetProperties();
                }
            });
        }
        finally
        {
            addonItem.IsDownloading = false;
            if (ReferenceEquals(SelectedDownloadableItem, addonItem))
            {
                RefreshSelectedTargetProperties();
            }
        }
    }

    /// <summary>
    /// Resolves the install state for a release/addon row using the same provenance-aware
    /// detection path as the parent content card. A row that is already on disk (manifest in the
    /// pool, content in CAS) is marked downloaded and bound to its on-disk manifest ID so the row
    /// shows "Add to Profile" instead of "Download" and Add to Profile works without re-acquiring.
    /// </summary>
    private async Task ResolveRowStateAsync(IDownloadableRowViewModel row, DownloadableFile file)
    {
        if (string.IsNullOrEmpty(file.DownloadUrl))
        {
            return;
        }

        try
        {
            var rowSearchResult = CreateFileSearchResult(file, row.ContentType);
            var state = await contentStateService.GetStateAsync(rowSearchResult, _cts.Token);
            if (state == ContentState.NotDownloaded)
            {
                return;
            }

            // Prefer the on-disk manifest ID from the pool over the synthesized row ID, which is a
            // "file:..." placeholder and not a real manifest. GetLocalManifestIdAsync walks the
            // provenance (OriginalProviderName/OriginalContentId) and publisher+type+game fallbacks.
            var manifestId = await contentStateService.GetLocalManifestIdAsync(rowSearchResult, _cts.Token);
            if (string.IsNullOrEmpty(manifestId))
            {
                logger.LogWarning(
                    "Row '{RowName}' detected as downloaded/update but no on-disk manifest ID resolved",
                    row.Name);
                return;
            }

            await RunOnUiThreadAsync(() =>
            {
                row.DownloadedManifestId = manifestId;
                row.IsDownloaded = state == ContentState.Downloaded;
                row.IsUpdateAvailable = state == ContentState.UpdateAvailable;
                if (ReferenceEquals(SelectedDownloadableItem, row))
                {
                    RefreshSelectedTargetProperties();
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve install state for row '{RowName}'", row.Name);
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
    /// Opens full-screen view for the specified media item or URL.
    /// </summary>
    [RelayCommand]
    private void OpenFullScreenMedia(object? item)
    {
        if (item is Image img)
        {
            FullScreenMediaUrl = img.FullSizeUrl ?? img.ThumbnailUrl;
            FullScreenMediaTitle = img.Title;
            IsFullScreenMediaOpen = true;
        }
        else if (item is Video vid)
        {
            if (!string.IsNullOrWhiteSpace(vid.EmbedUrl))
            {
                var targetUrl = vid.EmbedUrl;
                if (targetUrl.Contains("/embed/", StringComparison.OrdinalIgnoreCase) &&
                    targetUrl.Contains("youtube", StringComparison.OrdinalIgnoreCase))
                {
                    var embedParts = targetUrl.Split("/embed/", StringSplitOptions.RemoveEmptyEntries);
                    if (embedParts.Length > 1)
                    {
                        var id = embedParts[1].Split('?')[0];
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            targetUrl = $"https://www.youtube.com/watch?v={id}";
                        }
                    }
                }

                if (Uri.TryCreate(targetUrl, UriKind.Absolute, out var videoUri) &&
                    (videoUri.Scheme == Uri.UriSchemeHttp || videoUri.Scheme == Uri.UriSchemeHttps))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = videoUri.AbsoluteUri,
                            UseShellExecute = true,
                        });
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to open video in browser: {Url}", targetUrl);
                    }
                }
                else
                {
                    logger.LogWarning("Refusing to open non-http/https video URL in browser: {Url}", targetUrl);
                }
            }

            if (!string.IsNullOrWhiteSpace(vid.ThumbnailUrl))
            {
                FullScreenMediaUrl = vid.ThumbnailUrl;
                FullScreenMediaTitle = vid.Title;
                IsFullScreenMediaOpen = true;
            }
        }
        else if (item is string url && !string.IsNullOrWhiteSpace(url))
        {
            FullScreenMediaUrl = url;
            FullScreenMediaTitle = "Image Preview";
            IsFullScreenMediaOpen = true;
        }
    }

    /// <summary>
    /// Closes full-screen media view.
    /// </summary>
    [RelayCommand]
    private void CloseFullScreenMedia()
    {
        IsFullScreenMediaOpen = false;
        FullScreenMediaUrl = null;
        FullScreenMediaTitle = null;
    }

    /// <summary>
    /// Gets a value indicating whether the downloaded content has mandatory dependencies.
    /// </summary>
    public bool HasRequiredDependencies => !string.IsNullOrWhiteSpace(RequiredDependenciesSummary);

    private async Task LoadDependencySummaryAsync(string manifestId)
    {
        var manifestResult = await manifestPool.GetManifestAsync(ManifestId.Create(manifestId), _cts.Token);
        if (manifestResult.Success && manifestResult.Data != null)
        {
            var manifest = manifestResult.Data;
            await RunOnUiThreadAsync(() => ApplyStoredManifestMetadata(manifest));
        }
    }

    /// <summary>
    /// Syncs the Type dropdown and required-dependency banner from a stored manifest.
    /// </summary>
    private void ApplyStoredManifestMetadata(ContentManifest manifest)
    {
        if (SelectedContentType != manifest.ContentType)
        {
            _suppressContentTypePersist = true;
            try
            {
                SelectedContentType = manifest.ContentType;
            }
            finally
            {
                _suppressContentTypePersist = false;
            }
        }

        UpdateDependencySummary(manifest);
    }

    private void UpdateDependencySummary(ContentManifest manifest)
    {
        var requirements = (manifest.Dependencies ?? [])
            .Where(dependency =>
                !dependency.IsOptional &&
                dependency.InstallBehavior is DependencyInstallBehavior.AutoInstall or DependencyInstallBehavior.RequireExisting)
            .Select(dependency => dependency.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        RequiredDependenciesSummary = requirements.Count == 0
            ? null
            : string.Join(", ", requirements);
    }

    /// <summary>
    /// Persists a post-download content-type correction to the stored manifest.
    /// Standalone types (Executable / ModdingTool) drop required game-installation dependencies
    /// so Create Profile builds a tool profile that GenHub can launch directly.
    /// </summary>
    private async Task PersistContentTypeChangeAsync(ContentType newType, string? explicitManifestId = null)
    {
        try
        {
            var manifestId = !string.IsNullOrWhiteSpace(explicitManifestId)
                ? explicitManifestId
                : await ResolveDownloadedManifestIdAsync();

            if (string.IsNullOrWhiteSpace(manifestId) || !ManifestIdValidator.IsValid(manifestId, out _))
            {
                logger.LogWarning(
                    "Cannot persist content type change for {Name}: no downloaded manifest ID",
                    Name);
                return;
            }

            var manifestResult = await manifestPool.GetManifestAsync(ManifestId.Create(manifestId), _cts.Token);
            if (!manifestResult.Success || manifestResult.Data == null)
            {
                logger.LogWarning(
                    "Cannot persist content type change for {ManifestId}: {Error}",
                    manifestId,
                    manifestResult.FirstError ?? "manifest not found");
                return;
            }

            var manifest = manifestResult.Data;
            if (manifest.ContentType == newType)
            {
                await RunOnUiThreadAsync(() => UpdateDependencySummary(manifest));
                return;
            }

            var wasStandalone = manifest.ContentType.IsStandalone();
            var isStandalone = newType.IsStandalone();
            manifest.ContentType = newType;

            if (isStandalone)
            {
                // Tools/executables run via the tool-profile path and must not require a game install.
                manifest.Dependencies = [.. (manifest.Dependencies ?? [])
                    .Where(dependency => dependency.DependencyType != ContentType.GameInstallation)];
            }
            else if (wasStandalone)
            {
                EnsureGameInstallationDependency(manifest);
            }

            var saveResult = await manifestPool.AddManifestAsync(manifest, _cts.Token);
            if (!saveResult.Success)
            {
                logger.LogError(
                    "Failed to persist content type {ContentType} for {ManifestId}: {Error}",
                    newType,
                    manifestId,
                    saveResult.FirstError);
                notificationService.ShowError(
                    "Content Type Not Saved",
                    saveResult.FirstError ?? "Could not update the stored manifest type.");
                return;
            }

            await RunOnUiThreadAsync(() => UpdateDependencySummary(manifest));
            logger.LogInformation(
                "Updated stored content type for {ManifestId} to {ContentType}",
                manifestId,
                newType);
            notificationService.ShowSuccess(
                "Content Type Updated",
                $"'{manifest.Name}' is now classified as {newType.GetDisplayName()}.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist content type change for {Name}", Name);
            notificationService.ShowError(
                "Content Type Not Saved",
                "Could not update the stored manifest type. Please try again.");
        }
    }

    private async Task<string?> ResolveDownloadedManifestIdAsync()
    {
        if (!string.IsNullOrEmpty(searchResult.Id) && ManifestIdValidator.IsValid(searchResult.Id, out _))
        {
            return searchResult.Id;
        }

        return await contentStateService.GetLocalManifestIdAsync(searchResult, _cts.Token);
    }

    /// <summary>
    /// Command to add the downloaded content or selected target row to a game profile.
    /// </summary>
    [RelayCommand]
    private async Task AddToProfileAsync()
    {
        if (HasBundleComponents)
        {
            if (!AreBundleComponentsReadyForProfile)
            {
                logger.LogWarning("Cannot add to profile: bundle members are not all downloaded");
                notificationService.ShowWarning(
                    ContentNotDownloadedTitle,
                    "Download every selected bundle item (including the chosen variants) before adding them to a profile.");
                return;
            }

            logger.LogInformation("Add to Profile clicked for bundle: {Name}", Name);
            await ShowProfileSelectionDialogAsync();
            return;
        }

        if (SelectedDownloadableItem != null)
        {
            if (!SelectedDownloadableItem.IsDownloaded || string.IsNullOrWhiteSpace(SelectedDownloadableItem.DownloadedManifestId))
            {
                logger.LogWarning("Cannot add to profile: selected downloadable item not downloaded yet");
                notificationService.ShowWarning(ContentNotDownloadedTitle, "Please download this item before adding it to a profile.");
                return;
            }

            if (SelectedDownloadableItem.File != null)
            {
                await AddFileToProfileAsync(SelectedDownloadableItem.File, SelectedDownloadableItem.DownloadedManifestId);
            }
            else
            {
                await ShowProfileSelectionDialogAsync(
                    SelectedDownloadableItem.DownloadedManifestId,
                    SelectedDownloadableItem.Name,
                    searchResult.TargetGame);
            }

            return;
        }

        if (!IsDownloaded)
        {
            logger.LogWarning("Cannot add to profile: content not downloaded yet");
            notificationService.ShowWarning(ContentNotDownloadedTitle, "Please download the content before adding it to a profile.");
            return;
        }

        logger.LogInformation("Add to Profile clicked for content: {Name}", Name);

        // Show profile selection dialog
        await ShowProfileSelectionDialogAsync();
    }

    /// <summary>
    /// Adds a specific file's manifest to a profile.
    /// </summary>
    /// <param name="file">The file whose manifest should be added to a profile.</param>
    /// <param name="manifestId">The manifest ID created for this exact file row.</param>
    private async Task AddFileToProfileAsync(DownloadableFile file, string? manifestId)
    {
        if (file == null)
        {
            logger.LogWarning("Cannot add file to profile: file is null");
            return;
        }

        logger.LogInformation("Add to Profile clicked for file: {FileName}", file.Name);

        if (string.IsNullOrWhiteSpace(manifestId) || !ManifestIdValidator.IsValid(manifestId, out _))
        {
            notificationService.ShowWarning(ContentNotDownloadedTitle, "Please download this file before adding it to a profile.");
            return;
        }

        // A detail page can contain multiple releases/addons. Always send the manifest created
        // for this exact row to the profile dialog instead of resolving the parent content again.
        await ShowProfileSelectionDialogAsync(manifestId, file.Name, searchResult.TargetGame);
    }

    /// <summary>
    /// Shows the profile selection dialog for adding content to a profile.
    /// </summary>
    /// <param name="manifestId">Optional manifest ID for a specific release or addon row.</param>
    /// <param name="contentName">Optional display name for a specific release or addon row.</param>
    /// <param name="targetGame">Optional target game for a specific release or addon row.</param>
    private async Task ShowProfileSelectionDialogCoreAsync(
        string? manifestId = null,
        string? contentName = null,
        GameType? targetGame = null)
    {
        try
        {
            // Determine the content manifest ID to add
            string? contentManifestId = null;
            var selectedContentName = contentName;
            var selectedTargetGame = targetGame ?? searchResult.TargetGame;
            IReadOnlyList<string> additionalManifestIds = [];

            if (HasBundleComponents && string.IsNullOrWhiteSpace(manifestId))
            {
                var bundleIds = await BundleComponentViewModel.GetRequiredProfileManifestIdsAsync(
                    BundleComponents,
                    contentStateService,
                    CancellationToken.None);
                if (bundleIds.Count == 0)
                {
                    notificationService.ShowWarning(
                        ContentNotDownloadedTitle,
                        "Please download the content before adding it to a profile.");
                    return;
                }

                contentManifestId = bundleIds[0];
                additionalManifestIds = [.. bundleIds.Skip(1)];
                selectedContentName ??= searchResult.Name;
            }
            else if (!string.IsNullOrWhiteSpace(manifestId))
            {
                contentManifestId = manifestId;
                selectedContentName ??= searchResult.Name;
            }

            // First, check if the SearchResult has a valid manifest ID (set during download).
            else if (!string.IsNullOrEmpty(searchResult.Id) && ManifestIdValidator.IsValid(searchResult.Id, out _))
            {
                contentManifestId = searchResult.Id;
                selectedContentName = searchResult.Name;
            }
            else
            {
                // The search result still carries the catalog ID — look the manifest up in the
                // pool before concluding the content is not downloaded (same fallback as the
                // grid path in DownloadsBrowserViewModel.AddContentToProfileAsync).
                contentManifestId = await contentStateService.GetLocalManifestIdAsync(searchResult, _cts.Token);
                selectedContentName = searchResult.Name;

                if (string.IsNullOrEmpty(contentManifestId))
                {
                    notificationService.ShowWarning(
                        ContentNotDownloadedTitle,
                        "Please download the content before adding it to a profile.");
                    return;
                }
            }

            // Create the profile selection view model
            var profileSelectionViewModel = new ProfileSelectionViewModel(
                loggerFactory.CreateLogger<ProfileSelectionViewModel>(),
                profileManager,
                profileContentService,
                manifestPool,
                notificationService);

            // Load profiles into the view model
            await profileSelectionViewModel.LoadProfilesAsync(
                selectedTargetGame,
                contentManifestId,
                selectedContentName,
                additionalManifestIds,
                CancellationToken.None);

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
    [NotifyPropertyChangedFor(nameof(ReleasesCount))]
    [NotifyPropertyChangedFor(nameof(ShowFilesTab))]
    private ObservableCollection<ReleaseItemViewModel> _releases = [];

    /// <summary>
    /// Gets a value indicating whether there are releases to display.
    /// </summary>
    public bool HasReleases => (Releases?.Count > 0) || (Variants?.Count > 0);

    /// <summary>
    /// Gets the count of releases for display in the tab badge.
    /// </summary>
    public int ReleasesCount => Releases?.Count ?? 0;

    /// <summary>
    /// Populates the Releases collection from available variants when no web releases exist.
    /// </summary>
    public void PopulateReleasesFromVariants()
    {
        if (Variants.Count == 0)
        {
            return;
        }

        Releases.Clear();
        var sortedVariants = Variants
            .OrderByDescending(v =>
            {
                if (variantSearchResults != null &&
                    !string.IsNullOrEmpty(v.ManifestId) &&
                    variantSearchResults.TryGetValue(v.ManifestId, out var sr))
                {
                    return sr.LastUpdated ?? DateTime.MinValue;
                }

                return searchResult.LastUpdated ?? DateTime.MinValue;
            })
            .ThenByDescending(v => v.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var variant in sortedVariants)
        {
            var manifestId = variant.ManifestId;
            ContentSearchResult? sibling = null;
            if (!string.IsNullOrEmpty(manifestId) &&
                variantSearchResults != null &&
                variantSearchResults.TryGetValue(manifestId, out var sr))
            {
                sibling = sr;
            }

            var url = sibling?.SourceUrl ?? searchResult.SourceUrl ?? string.Empty;
            var size = sibling?.DownloadSize ?? searchResult.DownloadSize;
            var displayName = variant.Name;
            var itemVersion = sibling?.Version ?? Version;
            var itemAuthor = sibling?.AuthorName ?? searchResult.AuthorName;
            var itemDescription = sibling?.Description ?? searchResult.Description;
            var itemContentType = sibling?.ContentType ?? searchResult.ContentType;
            var itemCategory = itemContentType.GetDisplayName();
            var itemFilename = GetFileNameFromUrl(url) ?? displayName;

            var file = new DownloadableFile(
                Name: displayName,
                DownloadUrl: url,
                SizeBytes: size > 0 ? size : null,
                UploadDate: sibling?.LastUpdated ?? searchResult.LastUpdated,
                Version: itemVersion,
                Category: itemCategory,
                Uploader: itemAuthor,
                Filename: itemFilename,
                Description: itemDescription,
                FileSectionType: FileSectionType.Downloads);

            ReleaseItemViewModel releaseItem = new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = displayName,
                Version = itemVersion,
                ReleaseDate = sibling?.LastUpdated ?? searchResult.LastUpdated,
                FileSize = size,
                DownloadUrl = url,
                DetailsUrl = url,
                DownloadedManifestId = manifestId,
                ContentType = itemContentType,
                Category = itemCategory,
                Uploader = itemAuthor,
                Filename = itemFilename,
                FullDescription = itemDescription,
                TargetGame = ResolveTargetGameString(sibling, searchResult),
                IsDetailsLoaded = true,
                File = file,
                IsDownloaded = variant.CurrentState is ContentState.Downloaded or ContentState.UpdateAvailable,
                IsUpdateAvailable = variant.CurrentState is ContentState.UpdateAvailable,
                FetchDetailsAsync = LoadItemDetailsAsync,
            };

            var screenshots = sibling?.ScreenshotUrls ?? searchResult.ScreenshotUrls;
            if (screenshots != null)
            {
                foreach (var shot in screenshots)
                {
                    releaseItem.PreviewImages.Add(shot);
                }
            }

            releaseItem.SelectCommand = new RelayCommand(
                () =>
                {
                    if (variantSearchResults is not null && variantSearchResults.TryGetValue(manifestId, out var swapSr))
                    {
                        VariantSwap.Apply(searchResult, swapSr);
                        SelectedVariant = variant;
                    }

                    SelectDownloadableItem(releaseItem, isUserInitiated: true);
                },
                () => !IsDownloading);

            releaseItem.DownloadCommand = new AsyncRelayCommand(async () =>
            {
                if (variantSearchResults is not null && variantSearchResults.TryGetValue(manifestId, out var swapSr))
                {
                    VariantSwap.Apply(searchResult, swapSr);
                    SelectedVariant = variant;
                }

                await DownloadReleaseAsync(releaseItem, releaseItem.File ?? file);
            });

            releaseItem.AddToProfileCommand = new AsyncRelayCommand(async () =>
            {
                if (variantSearchResults is not null && variantSearchResults.TryGetValue(manifestId, out var swapSr))
                {
                    VariantSwap.Apply(searchResult, swapSr);
                    SelectedVariant = variant;
                }

                var targetManifestId = releaseItem.DownloadedManifestId ?? manifestId;
                await AddFileToProfileAsync(releaseItem.File ?? file, targetManifestId);
            });

            Releases.Add(releaseItem);
            _ = ResolveRowStateAsync(releaseItem, file);
        }

        var initialRelease = (SelectedVariant != null
            ? Releases.FirstOrDefault(r =>
                string.Equals(r.DownloadedManifestId, SelectedVariant.ManifestId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.Name, SelectedVariant.Name, StringComparison.OrdinalIgnoreCase))
            : null) ?? FindPreferredRelease(Releases);

        if (initialRelease != null)
        {
            SelectDownloadableItem(initialRelease, isUserInitiated: false);
        }

        OnPropertyChanged(nameof(HasReleases));
        OnPropertyChanged(nameof(ReleasesCount));
        OnPropertyChanged(nameof(ShowSelectedTargetBanner));
    }

    /// <summary>
    /// Gets the collection of addons (from /addons section for mods).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddons))]
    [NotifyPropertyChangedFor(nameof(AddonsCount))]
    [NotifyPropertyChangedFor(nameof(ShowFilesTab))]
    private ObservableCollection<AddonItemViewModel> _addons = [];

    /// <summary>
    /// Gets a value indicating whether there are addons to display.
    /// </summary>
    public bool HasAddons => Addons.Count > 0;

    /// <summary>
    /// Gets the count of addons for display.
    /// </summary>
    public int AddonsCount => Addons.Count;

    /// <summary>
    /// Populates the Releases collection from parsed page data.
    /// </summary>
    /// <param name="files">The files to populate releases from.</param>
    public void PopulateReleases(IEnumerable<DownloadableFile> files)
    {
        Releases.Clear();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sortedFiles = files
            .Where(f => f.FileSectionType == FileSectionType.Downloads)
            .OrderByDescending(f => f.ReleaseDate ?? f.UploadDate ?? DateTime.MinValue)
            .ThenByDescending(f => f.Version, StringComparer.OrdinalIgnoreCase);

        foreach (var file in sortedFiles)
        {
            var dedupeKey = GetDeduplicationKey(file.DetailsUrl ?? file.DownloadUrl, file.Name, file.Filename);
            if (!string.IsNullOrEmpty(dedupeKey) && !seenKeys.Add(dedupeKey))
            {
                continue;
            }

            var releaseItem = CreateReleaseItemViewModel(file);
            Releases.Add(releaseItem);
            _ = ResolveRowStateAsync(releaseItem, file);
        }

        SelectInitialPreferredRelease();

        OnPropertyChanged(nameof(HasReleases));
        OnPropertyChanged(nameof(ReleasesCount));
        OnPropertyChanged(nameof(ShowSelectedTargetBanner));
    }

    /// <summary>
    /// Populates the Addons collection from parsed page data.
    /// </summary>
    /// <param name="files">The files to populate addons from.</param>
    public void PopulateAddons(IEnumerable<DownloadableFile> files)
    {
        Addons.Clear();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sortedFiles = files
            .Where(f => f.FileSectionType == FileSectionType.Addons)
            .OrderByDescending(f => f.ReleaseDate ?? f.UploadDate ?? DateTime.MinValue)
            .ThenByDescending(f => f.Version, StringComparer.OrdinalIgnoreCase);

        foreach (var file in sortedFiles)
        {
            var dedupeKey = GetDeduplicationKey(file.DetailsUrl ?? file.DownloadUrl, file.Name, file.Filename);
            if (!string.IsNullOrEmpty(dedupeKey) && !seenKeys.Add(dedupeKey))
            {
                continue;
            }

            var addonItem = CreateAddonItemViewModel(file);
            Addons.Add(addonItem);
            _ = ResolveRowStateAsync(addonItem, file);
        }

        SelectInitialPreferredAddon();
    }

    /// <summary>
    /// Triggers asynchronous background preloading for the most recent releases and addons.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the preload operation.</returns>
    public Task TriggerPreloadRecentItemDetailsAsync(CancellationToken cancellationToken = default)
    {
        lock (_preloadLock)
        {
            if (_preloadTask != null && !_preloadTask.IsCompleted)
            {
                return _preloadTask;
            }

            _preloadTask = PreloadRecentItemDetailsCoreAsync(cancellationToken);
            return _preloadTask;
        }
    }

    private ReleaseItemViewModel CreateReleaseItemViewModel(DownloadableFile file)
    {
        var isDetailsAlreadyLoaded = IsFileDetailsAlreadyLoaded(file);
        var mappedType = !string.IsNullOrWhiteSpace(file.Category)
            ? ModDBCategoryMapper.MapCategoryByName(file.Category)
            : ContentType.Mod;

        ReleaseItemViewModel releaseItem = new()
        {
            Id = Guid.NewGuid().ToString(),
            Name = file.Name ?? "Unknown Release",
            Version = file.Version,
            ReleaseDate = file.ReleaseDate ?? file.UploadDate,
            FileSize = file.SizeBytes ?? 0,
            SizeDisplay = file.SizeDisplay,
            DownloadUrl = file.DownloadUrl,
            DetailsUrl = file.DetailsUrl ?? file.DownloadUrl,
            ThumbnailUrl = file.ThumbnailUrl,
            Category = file.Category,
            ContentType = mappedType,
            File = file,
            Uploader = file.Uploader,
            Filename = file.Filename,
            Md5Hash = file.Md5Hash,
            CommentCount = file.CommentCount,
            DownloadCount = file.DownloadCount,
            FullDescription = file.Description,
            TargetGame = searchResult.TargetGame != GameType.Unknown ? searchResult.TargetGame.ToString() : null,
            IsDetailsLoaded = isDetailsAlreadyLoaded,
            FetchDetailsAsync = LoadItemDetailsAsync,
        };

        if (file.PreviewImages != null)
        {
            foreach (var img in file.PreviewImages)
            {
                releaseItem.PreviewImages.Add(img);
            }
        }

        // Keep this row's state and manifest ID independent of the parent content card.
        releaseItem.SelectCommand = new RelayCommand(
            () => SelectDownloadableItem(releaseItem, isUserInitiated: true),
            () => !IsDownloading);
        releaseItem.DownloadCommand = new AsyncRelayCommand(() => DownloadReleaseAsync(releaseItem, releaseItem.File ?? file));
        releaseItem.AddToProfileCommand = new AsyncRelayCommand(
            () => AddFileToProfileAsync(releaseItem.File ?? file, releaseItem.DownloadedManifestId));

        return releaseItem;
    }

    private void SelectInitialPreferredRelease()
    {
        if (_userManuallySelectedDownloadableItem && SelectedDownloadableItem != null && Releases.Contains(SelectedDownloadableItem))
        {
            return;
        }

        var preferredRelease = (SelectedVariant != null
            ? Releases.FirstOrDefault(r =>
                string.Equals(r.DownloadedManifestId, SelectedVariant.ManifestId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.Name, SelectedVariant.Name, StringComparison.OrdinalIgnoreCase))
            : null)
            ?? FindMatchingReleaseForSearchResult(Releases)
            ?? FindPreferredRelease(Releases);

        if (preferredRelease != null)
        {
            SelectDownloadableItem(preferredRelease, isUserInitiated: false);
        }
    }

    private AddonItemViewModel CreateAddonItemViewModel(DownloadableFile file)
    {
        var isDetailsAlreadyLoaded = IsFileDetailsAlreadyLoaded(file);
        var mappedType = !string.IsNullOrWhiteSpace(file.Category)
            ? ModDBCategoryMapper.MapCategoryByName(file.Category)
            : ContentType.Addon;

        AddonItemViewModel addonItem = new()
        {
            Id = Guid.NewGuid().ToString(),
            Name = file.Name ?? "Unknown Addon",
            ReleaseDate = file.ReleaseDate ?? file.UploadDate,
            FileSize = file.SizeBytes ?? 0,
            SizeDisplay = file.SizeDisplay,
            DownloadUrl = file.DownloadUrl,
            DetailsUrl = file.DetailsUrl ?? file.DownloadUrl,
            ThumbnailUrl = file.ThumbnailUrl,
            Category = file.Category,
            ContentType = mappedType,
            File = file,
            Uploader = file.Uploader,
            Filename = file.Filename,
            Md5Hash = file.Md5Hash,
            CommentCount = file.CommentCount,
            DownloadCount = file.DownloadCount,
            FullDescription = file.Description,
            TargetGame = searchResult.TargetGame != GameType.Unknown ? searchResult.TargetGame.ToString() : null,
            IsDetailsLoaded = isDetailsAlreadyLoaded,
            FetchDetailsAsync = LoadItemDetailsAsync,
        };

        if (file.PreviewImages != null)
        {
            foreach (var img in file.PreviewImages)
            {
                addonItem.PreviewImages.Add(img);
            }
        }

        // Keep this row's state and manifest ID independent of the parent content card.
        addonItem.SelectCommand = new RelayCommand(
            () => SelectDownloadableItem(addonItem, isUserInitiated: true),
            () => !IsDownloading);
        addonItem.DownloadCommand = new AsyncRelayCommand(() => DownloadAddonAsync(addonItem, addonItem.File ?? file));
        addonItem.AddToProfileCommand = new AsyncRelayCommand(
            () => AddFileToProfileAsync(addonItem.File ?? file, addonItem.DownloadedManifestId));

        return addonItem;
    }

    private void SelectInitialPreferredAddon()
    {
        if (_userManuallySelectedDownloadableItem || (SelectedDownloadableItem != null && Releases.Contains(SelectedDownloadableItem)))
        {
            return;
        }

        var matchingAddon = FindMatchingAddonForSearchResult(Addons);
        if (matchingAddon != null)
        {
            SelectDownloadableItem(matchingAddon, isUserInitiated: false);
        }
        else if (SelectedDownloadableItem == null && Releases.Count == 0 && Addons.Count > 0)
        {
            SelectDownloadableItem(Addons[0], isUserInitiated: false);
        }

        OnPropertyChanged(nameof(HasAddons));
        OnPropertyChanged(nameof(AddonsCount));
        OnPropertyChanged(nameof(ShowSelectedTargetBanner));
    }

    private async Task PreloadRecentItemDetailsCoreAsync(CancellationToken cancellationToken = default)
    {
        if (parsers == null || parsers.Count == 0)
        {
            return;
        }

        var itemsToLoad = Releases.Take(ContentConstants.PreloadRecentItemsLimit)
            .Concat<DownloadableItemViewModel>(Addons.Take(ContentConstants.PreloadRecentItemsLimit))
            .Where(item => !item.IsDetailsLoaded &&
                           (!string.IsNullOrEmpty(item.DetailsUrl) || !string.IsNullOrEmpty(item.DownloadUrl)))
            .ToList();

        if (itemsToLoad.Count == 0)
        {
            return;
        }

        logger.LogInformation("Preloading extended details for {Count} recent releases/addons in parallel", itemsToLoad.Count);

        var itemsGroupedByParser = itemsToLoad
            .Select(item =>
            {
                var targetUrl = item.DetailsUrl ?? item.DownloadUrl;
                var parser = string.IsNullOrEmpty(targetUrl) ? null : parsers.FirstOrDefault(p => p.CanParse(targetUrl));
                return (Item: item, Url: targetUrl, Parser: parser);
            })
            .Where(x => x.Parser != null && !string.IsNullOrEmpty(x.Url))
            .GroupBy(x => x.Parser);

        foreach (var group in itemsGroupedByParser)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var parser = group.Key;
            if (parser == null)
            {
                continue;
            }

            var groupItems = group.ToList();
            var urls = groupItems
                .Select(x => x.Url)
                .OfType<string>()
                .Where(u => !string.IsNullOrEmpty(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            try
            {
                var parsedPages = await parser.ParseFileDetailsManyAsync(urls, cancellationToken);
                foreach (var (item, url, _) in groupItems)
                {
                    if (!string.IsNullOrEmpty(url) && parsedPages.TryGetValue(url, out var parsedPage))
                    {
                        var detailedFile = parsedPage.Sections.OfType<DownloadableFile>().FirstOrDefault();
                        if (detailedFile != null)
                        {
                            ApplyDetailedFileToItem(item, detailedFile);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // ignore cancellation
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to batch fetch file details for parser {Parser}", parser.GetType().Name);
            }
        }

        if (!_userManuallySelectedDownloadableItem && Releases.Count > 0)
        {
            var preferred = FindPreferredRelease(Releases);
            if (preferred != null && !ReferenceEquals(SelectedDownloadableItem, preferred))
            {
                await RunOnUiThreadAsync(() => SelectDownloadableItem(preferred, isUserInitiated: false));
            }
        }
    }

    /// <summary>
    /// Fetches extended item details on demand for an expanded downloadable row.
    /// </summary>
    /// <param name="item">The row item to load extended details for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task LoadItemDetailsAsync(DownloadableItemViewModel item, CancellationToken cancellationToken)
    {
        var targetUrl = item.DetailsUrl ?? item.DownloadUrl;
        if (string.IsNullOrEmpty(targetUrl))
        {
            return;
        }

        var parser = parsers.FirstOrDefault(p => p.CanParse(targetUrl));
        if (parser == null)
        {
            logger.LogWarning("No web page parser found for detail URL: {Url}", targetUrl);
            return;
        }

        logger.LogInformation("Fetching extended details for item '{Name}' from URL: {Url}", item.Name, targetUrl);

        var parsedPage = await parser.ParseFileDetailAsync(targetUrl, cancellationToken);
        var detailedFile = parsedPage.Sections.OfType<DownloadableFile>().FirstOrDefault();

        if (detailedFile != null)
        {
            ApplyDetailedFileToItem(item, detailedFile);
        }
    }

    /// <summary>
    /// Applies detailed file metadata to a downloadable item view model.
    /// </summary>
    /// <param name="item">The item to update.</param>
    /// <param name="detailedFile">The detailed file extracted from parsing.</param>
    private void ApplyDetailedFileToItem(DownloadableItemViewModel item, DownloadableFile detailedFile)
    {
        RunOnUiThread(() =>
        {
            item.File = detailedFile;
            UpdateItemFileProperties(item, detailedFile);
            UpdateItemPreviewImages(item, detailedFile);

            item.IsDetailsLoaded = true;

            if (ReferenceEquals(SelectedDownloadableItem, item))
            {
                RefreshSelectedTargetProperties();
            }

            _ = ResolveRowStateAsync(item, detailedFile);
        });
    }

    /// <summary>
    /// Gets or sets the publisher profile metadata.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PublisherDisplayName))]
    [NotifyPropertyChangedFor(nameof(PublisherAvatarUrl))]
    [NotifyPropertyChangedFor(nameof(PublisherWebsite))]
    [NotifyPropertyChangedFor(nameof(PublisherSupportUrl))]
    [NotifyPropertyChangedFor(nameof(PublisherContactEmail))]
    [NotifyPropertyChangedFor(nameof(HasPublisherProfile))]
    [NotifyPropertyChangedFor(nameof(HasPublisherInfo))]
    private PublisherProfile? _publisherProfile;

    /// <summary>
    /// Gets the collection of publisher referrals to other catalogs.
    /// </summary>
    public ObservableCollection<PublisherReferral> PublisherReferrals { get; } = [];

    /// <summary>
    /// Gets a value indicating whether publisher referrals are available.
    /// </summary>
    public bool HasPublisherReferrals => PublisherReferrals.Count > 0;

    /// <summary>
    /// Gets the publisher display name.
    /// </summary>
    public string PublisherDisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(PublisherProfile?.Name))
            {
                return PublisherProfile.Name;
            }

            if (!string.IsNullOrWhiteSpace(searchResult.ProviderName))
            {
                return searchResult.ProviderName;
            }

            return searchResult.AuthorName ?? "Publisher";
        }
    }

    /// <summary>
    /// Gets the publisher avatar or logo URL.
    /// </summary>
    public string? PublisherAvatarUrl => !string.IsNullOrWhiteSpace(PublisherProfile?.AvatarUrl)
        ? PublisherProfile.AvatarUrl
        : (PublisherInfoConstants.GetPublisherLogo(searchResult.ProviderName, searchResult.Id) ?? searchResult.IconUrl);

    /// <summary>
    /// Gets the publisher website URL.
    /// </summary>
    public string? PublisherWebsite => PublisherProfile?.Website;

    /// <summary>
    /// Gets the publisher support URL.
    /// </summary>
    public string? PublisherSupportUrl => PublisherProfile?.SupportUrl;

    /// <summary>
    /// Gets the publisher contact email.
    /// </summary>
    public string? PublisherContactEmail => PublisherProfile?.ContactEmail;

    /// <summary>
    /// Gets a value indicating whether publisher profile metadata is present.
    /// </summary>
    public bool HasPublisherProfile => !string.IsNullOrWhiteSpace(PublisherDisplayName) &&
        (!string.IsNullOrWhiteSpace(PublisherWebsite) || !string.IsNullOrWhiteSpace(PublisherSupportUrl) || !string.IsNullOrWhiteSpace(PublisherContactEmail) || HasPublisherReferrals);

    /// <summary>
    /// Gets a value indicating whether the Publisher tab should be visible.
    /// </summary>
    public bool HasPublisherInfo => HasCustomTabs || HasPublisherProfile;

    /// <summary>
    /// Gets the publisher category or role badge text.
    /// </summary>
    public string PublisherTypeBadge => searchResult.ResolverId == CatalogConstants.GenericCatalogResolverId
        ? "Subscribed Catalog Publisher"
        : "Official Provider";

    [ObservableProperty]
    private CustomTabDefinition? _selectedCustomTab;

    /// <summary>
    /// Command to open an arbitrary URL in the system default browser.
    /// </summary>
    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to open URL in browser: {Url}", url);
        }
    }

    /// <summary>
    /// Gets the collection of custom tabs from publishers.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomTabs))]
    [NotifyPropertyChangedFor(nameof(HasPublisherInfo))]
    private ObservableCollection<CustomTabDefinition> _customTabs = [];

    /// <summary>
    /// Gets a value indicating whether there are custom tabs to display.
    /// </summary>
    public bool HasCustomTabs => CustomTabs?.Count > 0;

    /// <summary>
    /// Loads custom tabs from registered tab providers.
    /// </summary>
    private async Task LoadCustomTabsAsync()
    {
        try
        {
            var tabs = await tabProviderRegistry.GetTabsForContentAsync(searchResult, _cts.Token);

            await RunOnUiThreadAsync(() =>
            {
                CustomTabs.Clear();
                foreach (var tab in tabs)
                {
                    CustomTabs.Add(tab);
                }

                if (CustomTabs.Count > 0 && (SelectedCustomTab == null || !CustomTabs.Contains(SelectedCustomTab)))
                {
                    SelectedCustomTab = CustomTabs[0];
                }

                OnPropertyChanged(nameof(HasCustomTabs));
                OnPropertyChanged(nameof(HasPublisherInfo));
            });

            logger.LogDebug("Loaded {Count} custom tabs for content: {Name}", CustomTabs.Count, Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load custom tabs for content: {Name}", Name);
        }
    }

    private ReleaseItemViewModel? FindMatchingReleaseForSearchResult(IReadOnlyList<ReleaseItemViewModel> releases)
    {
        if (releases.Count == 0)
        {
            return null;
        }

        var searchSourceUrl = searchResult.SourceUrl?.TrimEnd('/');
        var searchDownloadUrl = searchResult.SelectedDownloadUrl?.TrimEnd('/');

        if (!string.IsNullOrEmpty(searchSourceUrl) || !string.IsNullOrEmpty(searchDownloadUrl))
        {
            var matchByUrl = releases.FirstOrDefault(r => MatchesReleaseUrl(r, searchSourceUrl, searchDownloadUrl));
            if (matchByUrl != null)
            {
                return matchByUrl;
            }
        }

        return FindMatchingReleaseByName(releases);
    }

    private bool MatchesReleaseUrl(ReleaseItemViewModel r, string? searchSourceUrl, string? searchDownloadUrl)
    {
        var urls = new[] { r.DetailsUrl?.TrimEnd('/'), r.DownloadUrl?.TrimEnd('/'), r.File?.DetailsUrl?.TrimEnd('/'), r.File?.DownloadUrl?.TrimEnd('/') };
        return (!string.IsNullOrEmpty(searchSourceUrl) && urls.Any(u => string.Equals(u, searchSourceUrl, StringComparison.OrdinalIgnoreCase))) ||
               (!string.IsNullOrEmpty(searchDownloadUrl) && urls.Any(u => string.Equals(u, searchDownloadUrl, StringComparison.OrdinalIgnoreCase)));
    }

    private ReleaseItemViewModel? FindMatchingReleaseByName(IReadOnlyList<ReleaseItemViewModel> releases)
    {
        if (string.IsNullOrWhiteSpace(searchResult.Name))
        {
            return null;
        }

        var trimmedSearchName = searchResult.Name.Trim();
        return releases.FirstOrDefault(r => string.Equals(r.Name?.Trim(), trimmedSearchName, StringComparison.OrdinalIgnoreCase))
            ?? releases.FirstOrDefault(r =>
                !string.IsNullOrWhiteSpace(r.Name) &&
                (trimmedSearchName.Contains(r.Name.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 r.Name.Trim().Contains(trimmedSearchName, StringComparison.OrdinalIgnoreCase)));
    }

    private AddonItemViewModel? FindMatchingAddonForSearchResult(IReadOnlyList<AddonItemViewModel> addons)
    {
        if (addons.Count == 0)
        {
            return null;
        }

        var searchSourceUrl = searchResult.SourceUrl?.TrimEnd('/');
        var searchDownloadUrl = searchResult.SelectedDownloadUrl?.TrimEnd('/');

        if (!string.IsNullOrEmpty(searchSourceUrl) || !string.IsNullOrEmpty(searchDownloadUrl))
        {
            var matchByUrl = addons.FirstOrDefault(a => MatchesAddonUrl(a, searchSourceUrl, searchDownloadUrl));
            if (matchByUrl != null)
            {
                return matchByUrl;
            }
        }

        return FindMatchingAddonByName(addons);
    }

    private bool MatchesAddonUrl(AddonItemViewModel a, string? searchSourceUrl, string? searchDownloadUrl)
    {
        var urls = new[] { a.DetailsUrl?.TrimEnd('/'), a.DownloadUrl?.TrimEnd('/'), a.File?.DetailsUrl?.TrimEnd('/'), a.File?.DownloadUrl?.TrimEnd('/') };
        return (!string.IsNullOrEmpty(searchSourceUrl) && urls.Any(u => string.Equals(u, searchSourceUrl, StringComparison.OrdinalIgnoreCase))) ||
               (!string.IsNullOrEmpty(searchDownloadUrl) && urls.Any(u => string.Equals(u, searchDownloadUrl, StringComparison.OrdinalIgnoreCase)));
    }

    private AddonItemViewModel? FindMatchingAddonByName(IReadOnlyList<AddonItemViewModel> addons)
    {
        if (string.IsNullOrWhiteSpace(searchResult.Name))
        {
            return null;
        }

        var trimmedSearchName = searchResult.Name.Trim();
        return addons.FirstOrDefault(a => string.Equals(a.Name?.Trim(), trimmedSearchName, StringComparison.OrdinalIgnoreCase))
            ?? addons.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(a.Name) &&
                (trimmedSearchName.Contains(a.Name.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 a.Name.Trim().Contains(trimmedSearchName, StringComparison.OrdinalIgnoreCase)));
    }
}
