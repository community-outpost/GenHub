using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Parsers;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Messages;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.Content.Services;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Features.Content.Services.Reconciliation;
using GenHub.Features.Content.Services.SteamWorkshop;
using GenHub.Features.Downloads.Services;
using GenHub.Features.Downloads.ViewModels.Filters;
using GenHub.Features.Downloads.Views;
using GenHub.Features.Info.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Downloads browser: built-in publishers plus user-subscribed GenHub catalogs.
/// </summary>
/// <remarks>
/// Built-in entries (GeneralsOnline, TheSuperHackers, CommunityOutpost, GitHub) use specialized discoverers.
/// Subscribed entries come from <see cref="IPublisherSubscriptionStore"/> and use
/// <see cref="GenericCatalogDiscoverer"/> so any schema-valid <c>catalog.json</c> is browsable
/// without a custom publisher class. After <c>genhub://subscribe</c> confirms,
/// <see cref="InitializeAsync"/> refreshes only the subscribed sidebar rows.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Primary constructor injects required services for browser and discovery orchestrator.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel properties and methods bound to MVVM UI.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S3776:Cognitive Complexity of methods should not be too high", Justification = "Downloads browser orchestrates multi-source discovery, filtering, pagination, and subscription catalog caching.")]
public sealed partial class DownloadsBrowserViewModel(
    IServiceProvider serviceProvider,
    ILogger<DownloadsBrowserViewModel> logger,
    IReadOnlyList<IContentDiscoverer> contentDiscoverers,
    IContentStateService contentStateService,
    IContentOrchestrator contentOrchestrator,
    IProfileContentService profileContentService,
    IGameProfileManager profileManager,
    INotificationService notificationService,
    ILoggerFactory loggerFactory,
    IPublisherSubscriptionStore subscriptionStore,
    IContentDownloadCoordinator? downloadCoordinator = null,
    IPublisherReconcilerRegistry? reconcilerRegistry = null,
    ILocalizationService? localizationService = null) : ObservableObject, IDisposable
{
    private const string DefaultPublisherName = "Content";

    private long _subscriptionRefreshVersion;

    /// <summary>
    /// Tracks an in-flight background default browse operation so switching away
    /// allows the fetch to complete into cache, and switching back can attach to it.
    /// </summary>
    internal sealed class PublisherInFlightOperation(
        string publisherId,
        ContentSearchQuery query,
        CancellationTokenSource cts,
        string? catalogId = null)
    {
        /// <summary>Gets the publisher identifier.</summary>
        public string PublisherId { get; } = publisherId;

        /// <summary>Gets the search query used for this operation.</summary>
        public ContentSearchQuery Query { get; } = query;

        /// <summary>Gets the cancellation token source for this operation.</summary>
        public CancellationTokenSource Cts { get; } = cts;

        /// <summary>Gets or sets the catalog identifier captured for this operation.</summary>
        public string? CatalogId { get; set; } = catalogId;

        /// <summary>Gets the sync root for thread-safe list operations.</summary>
        public object SyncRoot { get; } = new();

        /// <summary>Gets the list of items resolved so far.</summary>
        public List<ContentGridItemViewModel> ResolvedItems { get; } = [];

        /// <summary>Gets or sets a value indicating whether the operation has completed.</summary>
        public bool IsCompleted { get; set; }

        /// <summary>Gets or sets a value indicating whether more items are available from the provider.</summary>
        public bool HasMoreItems { get; set; }

        /// <summary>Gets or sets the current UI request ID attached to this in-flight operation.</summary>
        public int ActiveRequestId { get; set; }
    }

    /// <summary>
    /// Snapshot of a publisher's browse state so switching back does not re-run discovery.
    /// </summary>
    private sealed class PublisherBrowseState
    {
        /// <summary>Gets or sets the grid item view models that were displayed.</summary>
        public List<ContentGridItemViewModel> Items { get; set; } = [];

        /// <summary>Gets or sets the page counter at the time of the snapshot.</summary>
        public int CurrentPage { get; set; } = 1;

        /// <summary>Gets or sets a value indicating whether more items could be loaded.</summary>
        public bool CanLoadMore { get; set; }

        /// <summary>Gets or sets the search term for this publisher.</summary>
        public string SearchTerm { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether a custom search query was active.</summary>
        public bool HasCustomQuery { get; set; }

        /// <summary>Gets or sets the active detail view model for this publisher.</summary>
        public ContentDetailViewModel? ActiveDetailViewModel { get; set; }

        /// <summary>Gets or sets the catalog ID associated with this browse snapshot.</summary>
        public string? CatalogId { get; set; }
    }

    private readonly Dictionary<string, IFilterPanelViewModel> _filterViewModels = [];
    private readonly Dictionary<string, PublisherBrowseState> _browseCache = [];
    private readonly Dictionary<string, PublisherInFlightOperation> _inFlightOperations = [];
    private readonly object _cacheLock = new();
    private readonly IContentDownloadCoordinator? _downloadCoordinator =
        downloadCoordinator ?? serviceProvider.GetService<IContentDownloadCoordinator>();

    private readonly IPublisherReconcilerRegistry? _reconcilerRegistry =
        reconcilerRegistry ?? serviceProvider.GetService<IPublisherReconcilerRegistry>();

    private readonly ITelemetryService? _telemetryService =
        serviceProvider.GetService<ITelemetryService>();

    private readonly ILocalizationService? _localizationService =
        serviceProvider.GetService<ILocalizationService>();

    // GenericCatalogDiscoverer instances mapped by publisher ID for subscriber feeds.
    private readonly Dictionary<string, GenericCatalogDiscoverer> _subscribedDiscoverers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, HashSet<string>> _knownCatalogIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<string>> _knownContentItemIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _vmCts = new();
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _catalogsCts;
    private int _activeRequestId;
    private int _activeCatalogLoadId;
    private string? _lastPopulatedPublisherId;
    private string? _lastCatalogPublisherId;
    private bool _hasCustomQuery;
    private bool _suppressPublisherChangedRefresh;
    private bool _suppressCatalogChanged;
    private bool _disposed;
    private bool _builtInPublishersInitialized;

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private double _openPaneLength = 220.0;

    [ObservableProperty]
    private bool _isFilterPanelVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReloadCatalog))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCatalogCommand))]
    private bool _isLoading;

    private PublisherItemViewModel? _selectedPublisher;

    [ObservableProperty]
    private ObservableCollection<PublisherItemViewModel> _publishers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowFilters))]
    [NotifyPropertyChangedFor(nameof(CanSearchOrFilter))]
    private IFilterPanelViewModel? _currentFilterViewModel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailViewVisible))]
    private ContentDetailViewModel? _selectedContent;

    [ObservableProperty]
    private ObservableCollection<ContentGridItemViewModel> _contentItems = [];

    [ObservableProperty]
    private ObservableCollection<CatalogEntry> _availableCatalogs = [];

    [ObservableProperty]
    private CatalogEntry? _selectedCatalog;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowCatalogSwitcher))]
    [NotifyPropertyChangedFor(nameof(CatalogLoadingStatusText))]
    [NotifyPropertyChangedFor(nameof(CanReloadCatalog))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCatalogCommand))]
    private bool _isLoadingCatalogs;

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private bool _canLoadMore;

    [ObservableProperty]
    private int _pageSize = 24;

    /// <summary>
    /// Gets or sets the currently selected publisher.
    /// </summary>
    public PublisherItemViewModel? SelectedPublisher
    {
        get => _selectedPublisher;
        set
        {
            if (value == null && _selectedPublisher != null && Publishers.Count > 0)
            {
                // Ignore spurious null assignments from UI unbinding or tab detach
                return;
            }

            if (SetProperty(ref _selectedPublisher, value))
            {
                OnPropertyChanged(nameof(CanSearch));
                OnPropertyChanged(nameof(CanSearchOrFilter));
                OnPropertyChanged(nameof(IsSubscribedPublisher));
                OnPropertyChanged(nameof(CanShowCatalogSwitcher));
                OnPropertyChanged(nameof(CanReloadCatalog));
                ReloadCatalogCommand.NotifyCanExecuteChanged();
                HandleSelectedPublisherChanged(value);
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether filters are available for the current publisher.
    /// </summary>
    public bool CanShowFilters => CurrentFilterViewModel != null;

    /// <summary>
    /// Gets a value indicating whether free-text search is meaningful for the selected publisher.
    /// Static curated catalogues deliberately expose only their content cards; a search box there
    /// implies capabilities those providers do not implement.
    /// </summary>
    public bool CanSearch => SelectedPublisher?.PublisherId is not
        PublisherTypeConstants.GeneralsOnline and not
        CommunityOutpostConstants.PublisherType and not
        PublisherTypeConstants.TheSuperHackers and not
        AODMapsConstants.PublisherType and not
        CNCLabsConstants.PublisherType and not
        PublisherTypeConstants.GenLauncher;

    /// <summary>
    /// Gets a value indicating whether search or filter UI controls are available for the current publisher.
    /// </summary>
    public bool CanSearchOrFilter => CanSearch || CanShowFilters;

    /// <summary>
    /// Gets a value indicating whether the selected publisher offers more than one catalog.
    /// Drives the catalog switcher visibility in the toolbar.
    /// </summary>
    public bool HasMultipleCatalogs => AvailableCatalogs.Count > 1;

    /// <summary>
    /// Gets a value indicating whether the selected publisher is a subscribed publisher.
    /// </summary>
    public bool IsSubscribedPublisher => SelectedPublisher != null &&
        SelectedPublisher.PublisherType.Equals(CatalogConstants.SubscribedPublisherCategory, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value indicating whether the catalog switcher and reload bar should be visible in the toolbar.
    /// </summary>
    public bool CanShowCatalogSwitcher => HasMultipleCatalogs || IsSubscribedPublisher;

    /// <summary>
    /// Gets a value indicating whether the catalog can currently be reloaded.
    /// </summary>
    public bool CanReloadCatalog => !IsLoadingCatalogs && !IsLoading && IsSubscribedPublisher;

    /// <summary>
    /// Gets the localized status text displayed while catalogs are loading or updating.
    /// </summary>
    public string CatalogLoadingStatusText => AvailableCatalogs.Count > 0
        ? _localizationService?.GetString("Downloads.Browser.UpdatingCatalog") ?? "Updating catalog..."
        : _localizationService?.GetString("Downloads.Browser.LoadingCatalogs") ?? "Loading catalogs...";

    /// <summary>
    /// Gets a value indicating whether the detail view is currently visible.
    /// </summary>
    public bool IsDetailViewVisible => SelectedContent != null;

    /// <summary>
    /// Gets the current active request ID (for testing).
    /// </summary>
    internal int ActiveRequestId => _activeRequestId;

    /// <summary>
    /// Ensures built-in publishers exist, then reloads subscribed catalogs from disk.
    /// </summary>
    /// <remarks>
    /// Safe to call repeatedly (e.g. after <c>genhub://subscribe</c> confirms) — does not clear
    /// browse cache or selection for built-in publishers; only syncs subscribed sidebar entries.
    /// </remarks>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        if (!_builtInPublishersInitialized)
        {
            if (_localizationService != null)
            {
                _localizationService.PropertyChanged += OnLocalizationChanged;
            }

            Publishers = CreateBuiltInPublishers(ResolveDownloadedContentLabel());
            InitializeFilterViewModels();
            contentStateService.ContentStateChanged += OnContentStateChanged;
            WeakReferenceMessenger.Default.Register<ContentLibraryClearedMessage>(
                this,
                static (recipient, _) => ((DownloadsBrowserViewModel)recipient).OnContentLibraryCleared());
            WeakReferenceMessenger.Default.Register<DownloadsBrowserViewModel, PublisherSubscriptionsChangedMessage>(
                this,
                static (recipient, _) => recipient.OnPublisherSubscriptionsChanged());
            WeakReferenceMessenger.Default.Register<PublisherSubscriptionRemovedMessage>(
                this,
                static (recipient, message) => ((DownloadsBrowserViewModel)recipient).OnPublisherSubscriptionRemoved(message.PublisherId));
            _builtInPublishersInitialized = true;
        }

        await RefreshSubscribedPublishersAsync();
        _ = RefreshDownloadedContentCountAsync();
    }

    /// <summary>
    /// Called when the Downloads tab is activated.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task OnTabActivatedAsync()
    {
        await RefreshSubscribedPublishersAsync();

        if (SelectedPublisher == null && Publishers.Count > 0)
        {
            // First activation: selecting the publisher triggers the initial refresh
            // via SelectedPublisher setter.
            SelectedPublisher = Publishers[0];
            return;
        }

        if (ContentItems.Count == 0 && !IsLoading && SelectedContent == null)
        {
            await RefreshContentAsync();
            return;
        }

        foreach (var item in ContentItems)
        {
            _ = item.EnsureIconsLoadedAsync(_vmCts.Token);
        }
    }

    /// <summary>
    /// Prompts the user to subscribe to a dropped catalog JSON file.
    /// </summary>
    /// <param name="filePath">The file path to the catalog JSON.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task PromptSubscribeToCatalogPathAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            var importVm = new ImportSubscriptionViewModel(serviceProvider);
            await importVm.LaunchConfirmationDialogAsync(filePath);
            if (importVm.LastConfirmResult == true)
            {
                await InitializeAsync();
                notificationService.ShowSuccess(
                    localizationService?.GetString("Downloads.Subscription.SubscribedNotificationTitle") ?? "Subscribed",
                    localizationService?.GetString("Downloads.Subscription.SubscribedNotificationBody") ?? "Catalog subscription added successfully.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to import catalog subscription from dropped file: {FilePath}", filePath);
            notificationService.ShowError(
                localizationService?.GetString("Downloads.ImportSubscription.ImportErrorTitle") ?? "Import Error",
                localizationService?.GetString("Downloads.ImportSubscription.ImportErrorBody", ex.Message) ?? $"Failed to open import dialog: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows an error notification when a dropped catalog file cannot be read.
    /// </summary>
    /// <param name="details">The underlying error details for diagnostics.</param>
    public void NotifyCatalogDropFailed(string details)
    {
        notificationService.ShowError(
            localizationService?.GetString("Downloads.ImportSubscription.ImportErrorTitle") ?? "Import Error",
            localizationService?.GetString("Downloads.ImportSubscription.ImportErrorBody", details) ?? $"Failed to open import dialog: {details}");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            // Unsubscribe from event handlers
            if (_builtInPublishersInitialized)
            {
                WeakReferenceMessenger.Default.Unregister<ContentLibraryClearedMessage>(this);
                WeakReferenceMessenger.Default.Unregister<PublisherSubscriptionsChangedMessage>(this);
                WeakReferenceMessenger.Default.Unregister<PublisherSubscriptionRemovedMessage>(this);
                contentStateService.ContentStateChanged -= OnContentStateChanged;
                if (_localizationService != null)
                {
                    _localizationService.PropertyChanged -= OnLocalizationChanged;
                }

                foreach (var filterViewModel in _filterViewModels.Values.OfType<IDisposable>())
                {
                    filterViewModel.Dispose();
                }
            }

            if (CurrentFilterViewModel != null)
            {
                CurrentFilterViewModel.FiltersApplied -= OnFiltersApplied;
                CurrentFilterViewModel.FiltersCleared -= OnFiltersCleared;
            }

            _vmCts.Cancel();
            _vmCts.Dispose();

            _searchCts?.Cancel();
            _searchCts?.Dispose();

            _catalogsCts?.Cancel();
            _catalogsCts?.Dispose();

            lock (_cacheLock)
            {
                foreach (var op in _inFlightOperations.Values)
                {
                    try
                    {
                        op.Cts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // In-flight operation CTS already cancelled/disposed.
                    }

                    try
                    {
                        op.Cts.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // In-flight operation CTS already disposed.
                    }

                    foreach (var item in op.ResolvedItems)
                    {
                        item.Dispose();
                    }
                }

                _inFlightOperations.Clear();

                foreach (var state in _browseCache.Values)
                {
                    state.ActiveDetailViewModel?.Dispose();
                    state.ActiveDetailViewModel = null;
                    foreach (var item in state.Items)
                    {
                        item.Dispose();
                    }
                }

                _browseCache.Clear();
            }

            SelectedContent?.Dispose();
            SelectedContent = null;

            foreach (var item in ContentItems)
            {
                item.Dispose();
            }

            ContentItems.Clear();

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Reconciles the update and download states across multiple releases belonging to the same content family.
    /// In multi-release feeds, the prospective newest release is marked NotDownloaded (showing only Download),
    /// while older downloaded releases are marked UpdateAvailable targeting the newest release.
    /// </summary>
    /// <param name="items">The collection of content grid items to reconcile.</param>
    internal static void ReconcileReleaseUpdateStates(IReadOnlyCollection<ContentGridItemViewModel> items)
    {
        if (items.Count <= 1)
        {
            return;
        }

        var contentFamilies = items.GroupBy(GetContentFamilyKey, StringComparer.OrdinalIgnoreCase);

        foreach (var family in contentFamilies)
        {
            if (string.IsNullOrEmpty(family.Key))
            {
                continue;
            }

            var isGeneralsOnline = family.Any(f =>
                string.Equals(f.SearchResult.ProviderName, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase));

            var familyItems = family
                .OrderByDescending(it => it.SearchResult.LastUpdated ?? DateTime.MinValue)
                .ThenByDescending(it => it.SearchResult.Version, Comparer<string?>.Create((a, b) => ContentStateService.CompareVersions(a, b, isGeneralsOnline)))
                .ToList();

            // Single-item families need no reconciliation. In particular, sibling
            // variants on one card are never compared against each other: they are
            // parallel install options or unrelated content (such as the releases
            // plus addons GenLauncher groups per mod), and a self-targeted update
            // claim would re-download the installed result. Genuine updates surface
            // through per-variant states and cross-release families instead.
            if (familyItems.Count <= 1)
            {
                continue;
            }

            var installedItems = new HashSet<ContentGridItemViewModel>();
            foreach (var item in familyItems)
            {
                if (item.CurrentState == ContentState.Downloaded ||
                    item.Variants.Any(v => v.CurrentState == ContentState.Downloaded) ||
                    item.UpdateTargetVm != null)
                {
                    installedItems.Add(item);
                }
            }

            ResetUninstalledFamilyItems(familyItems, installedItems);
            ReconcileInstalledFamilyItems(familyItems, installedItems, isGeneralsOnline);
        }
    }

    /// <summary>
    /// Gets the unique family key used to group releases of the same content across versions.
    /// </summary>
    /// <param name="vm">The content grid item view model.</param>
    /// <returns>A string identifying the content family, or null if it cannot be grouped.</returns>
    internal static string? GetContentFamilyKey(ContentGridItemViewModel vm)
    {
        if (vm.SearchResult.ResolverMetadata?.TryGetValue(GitHubConstants.OwnerMetadataKey, out var owner) == true &&
            vm.SearchResult.ResolverMetadata.TryGetValue(GitHubConstants.RepoMetadataKey, out var repo) &&
            !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo))
        {
            return $"{owner}/{repo}/{vm.SearchResult.ContentType}";
        }

        if (!string.IsNullOrWhiteSpace(vm.SearchResult.ProviderName))
        {
            var effectiveName = !string.IsNullOrWhiteSpace(vm.SearchResult.VariantFamilyName)
                ? vm.SearchResult.VariantFamilyName
                : vm.SearchResult.Name;

            if (!string.IsNullOrWhiteSpace(effectiveName))
            {
                var baseName = effectiveName.Split('—')[0].Trim();
                return $"{vm.SearchResult.ProviderName}/{vm.SearchResult.ContentType}/{baseName}";
            }
        }

        return null;
    }

    /// <summary>
    /// Reconciles the update state of a downloaded variant against its non-downloaded siblings.
    /// Only manifest IDs arbitrate newness; variant display names are labels, never versions.
    /// </summary>
    /// <param name="item">The content grid item whose variants to reconcile.</param>
    internal static void ReconcileItemVariants(ContentGridItemViewModel item)
    {
        if (item.Variants.Count <= 1)
        {
            return;
        }

        var downloadedVariants = item.Variants.Where(v => v.CurrentState == ContentState.Downloaded).ToList();
        if (downloadedVariants.Count == 0)
        {
            return;
        }

        var hasNewerVariant = false;
        foreach (var downloaded in downloadedVariants)
        {
            var isAnyNewer = item.Variants.Any(v =>
                v.CurrentState != ContentState.Downloaded &&
                !string.IsNullOrEmpty(v.ManifestId) &&
                !string.IsNullOrEmpty(downloaded.ManifestId) &&
                ContentStateService.IsNewerVersion(v.ManifestId, downloaded.ManifestId));

            if (isAnyNewer)
            {
                downloaded.CurrentState = ContentState.UpdateAvailable;
                hasNewerVariant = true;
            }
        }

        if (hasNewerVariant)
        {
            item.CurrentState = ContentState.UpdateAvailable;
            item.IsDownloaded = true;
            item.UpdateTargetVm = item;
            item.NotifyStateChanged();
        }
    }

    /// <summary>
    /// Determines whether the content search result belongs to the Generals Online provider.
    /// </summary>
    /// <param name="item">The content search result to check.</param>
    /// <returns>True if the item is a Generals Online item; otherwise, false.</returns>
    internal static bool IsGeneralsOnlineResult(ContentSearchResult item)
    {
        return item.ProviderName?.Contains(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) == true ||
               item.ResolverId?.Contains(GeneralsOnlineConstants.ResolverId, StringComparison.OrdinalIgnoreCase) == true ||
               item.Id?.Contains(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Determines whether a content card matches a newly discovered content search result, either by primary ID or by variant manifest ID.
    /// </summary>
    /// <param name="card">The content grid item view model card.</param>
    /// <param name="item">The newly discovered content item.</param>
    /// <returns>True if the card matches the item; otherwise, false.</returns>
    internal static bool MatchesNotificationTarget(ContentGridItemViewModel card, ContentSearchResult item)
    {
        return string.Equals(card.Id, item.Id, StringComparison.OrdinalIgnoreCase) ||
               card.Variants.Any(v => string.Equals(v.ManifestId, item.Id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves the default content search result variant for a group.
    /// </summary>
    /// <param name="groupItems">The collection of content search results in the group.</param>
    /// <param name="primaryItem">The fallback primary search result.</param>
    /// <returns>The resolved default search result.</returns>
    internal static ContentSearchResult ResolveDefaultVariant(IReadOnlyList<ContentSearchResult> groupItems, ContentSearchResult primaryItem)
    {
        return groupItems.FirstOrDefault(i =>
            i.Variants?.Any(v => v.IsDefault && (v.ManifestId == i.Id || i.Id?.EndsWith($".{v.ManifestId}", StringComparison.OrdinalIgnoreCase) == true)) == true)
            ?? groupItems.FirstOrDefault(i =>
                i.ContentType == ContentType.GameClient &&
                IsGeneralsOnlineResult(i) &&
                (i.Id?.Contains(GeneralsOnlineConstants.Variant60HzSuffix, StringComparison.OrdinalIgnoreCase) == true ||
                 i.Name?.Contains(GeneralsOnlineConstants.Variant60HzSuffix, StringComparison.OrdinalIgnoreCase) == true))
            ?? groupItems.FirstOrDefault(i =>
                i.ContentType == ContentType.GameClient &&
                IsGeneralsOnlineResult(i))
            ?? groupItems.FirstOrDefault(i =>
                i.ContentType == ContentType.GameClient &&
                (i.ProviderName?.Contains(SuperHackersConstants.PublisherName, StringComparison.OrdinalIgnoreCase) == true ||
                 i.ResolverId?.Contains(PublisherTypeConstants.GitHub, StringComparison.OrdinalIgnoreCase) == true) &&
                i.TargetGame == GameType.ZeroHour)
            ?? groupItems.FirstOrDefault(i => i.Variants?.Any(v => v.IsDefault) == true)
            ?? groupItems.FirstOrDefault(i => i.ContentType == ContentType.GameClient)
            ?? primaryItem;
    }

    /// <summary>
    /// Selects the default variant on a content card view model based on the group and default variant.
    /// </summary>
    /// <param name="variantVm">The content card view model.</param>
    /// <param name="groupItems">The collection of content search results in the group.</param>
    /// <param name="primaryItem">The primary search result for the card.</param>
    /// <param name="defaultVariant">The default search result variant.</param>
    internal static void SelectDefaultVariant(
        ContentGridItemViewModel variantVm,
        IReadOnlyList<ContentSearchResult> groupItems,
        ContentSearchResult primaryItem,
        ContentSearchResult defaultVariant)
    {
        if (variantVm.Variants.Count == 0)
        {
            return;
        }

        InstallableVariant? defaultSelection = null;
        if (groupItems.Count == 1 && primaryItem.Variants is { Count: > 0 } singleVars)
        {
            var defVarInfo = singleVars.FirstOrDefault(v => v.IsDefault) ?? singleVars[0];
            defaultSelection = variantVm.Variants.FirstOrDefault(v =>
                (!string.IsNullOrEmpty(defVarInfo.ManifestId) &&
                 string.Equals(v.ManifestId, defVarInfo.ManifestId, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(v.Name, defVarInfo.Name, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(defVarInfo.Name) &&
                 v.Name.EndsWith(defVarInfo.Name, StringComparison.OrdinalIgnoreCase)));
        }
        else if (groupItems.Count > 1)
        {
            var defaultSibling = groupItems.FirstOrDefault(sibling =>
                sibling.Variants?.Any(v => v.IsDefault && (
                    string.Equals(v.Id, sibling.Id, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(v.Id) && sibling.Id?.EndsWith($".{v.Id}", StringComparison.OrdinalIgnoreCase) == true))) == true);

            if (defaultSibling != null)
            {
                defaultSelection = variantVm.Variants.FirstOrDefault(v =>
                    string.Equals(v.ManifestId, defaultSibling.Id, StringComparison.OrdinalIgnoreCase));
            }
        }

        var isGeneralsOnline = groupItems.Any(IsGeneralsOnlineResult);

        variantVm.SelectedVariant = defaultSelection
            ?? variantVm.Variants.FirstOrDefault(v => string.Equals(v.ManifestId, defaultVariant.Id, StringComparison.OrdinalIgnoreCase))
            ?? (isGeneralsOnline ? variantVm.Variants.FirstOrDefault(v => v.Name.Contains(GeneralsOnlineConstants.Variant60HzSuffix, StringComparison.OrdinalIgnoreCase)) : null)
            ?? variantVm.Variants.FirstOrDefault(v => v.Name.Contains("Zero Hour", StringComparison.OrdinalIgnoreCase))
            ?? variantVm.Variants[^1];
    }

    /// <summary>
    /// Cleans up in-flight browse operations and disposes un-retained items.
    /// </summary>
    /// <param name="publisherId">The publisher ID whose in-flight operation completed or faulted.</param>
    /// <param name="inFlightOp">The in-flight operation context.</param>
    internal void CleanupInFlight(string publisherId, PublisherInFlightOperation? inFlightOp)
    {
        if (inFlightOp != null)
        {
            lock (_cacheLock)
            {
                if (_inFlightOperations.TryGetValue(publisherId, out var current) && ReferenceEquals(current, inFlightOp))
                {
                    _inFlightOperations.Remove(publisherId);
                }

                var retainedItems = new HashSet<ContentGridItemViewModel>(_browseCache.Values.SelectMany(s => s.Items));
                foreach (var item in ContentItems)
                {
                    retainedItems.Add(item);
                }

                foreach (var item in inFlightOp.ResolvedItems.Where(item => !retainedItems.Contains(item)))
                {
                    item.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Disposes view models that are not retained in active UI collections, cached publisher browse states,
    /// or active in-flight operation resolved lists.
    /// </summary>
    /// <param name="vms">The view models to evaluate for disposal.</param>
    internal void DisposeUnretainedViewModels(IEnumerable<ContentGridItemViewModel> vms)
    {
        lock (_cacheLock)
        {
            var retainedItems = new HashSet<ContentGridItemViewModel>(_browseCache.Values.SelectMany(s => s.Items));
            foreach (var item in ContentItems)
            {
                retainedItems.Add(item);
            }

            foreach (var inFlight in _inFlightOperations.Values)
            {
                lock (inFlight.SyncRoot)
                {
                    foreach (var item in inFlight.ResolvedItems)
                    {
                        retainedItems.Add(item);
                    }
                }
            }

            foreach (var vm in vms.Where(vm => !retainedItems.Contains(vm)))
            {
                vm.Dispose();
            }
        }
    }

    /// <summary>
    /// Injects an in-flight operation for unit testing.
    /// </summary>
    /// <param name="publisherId">The publisher ID to attach.</param>
    /// <param name="inFlightOp">The in-flight operation context.</param>
    internal void SetInFlightOperationForTesting(string publisherId, PublisherInFlightOperation inFlightOp)
    {
        lock (_cacheLock)
        {
            _inFlightOperations[publisherId] = inFlightOp;
        }
    }

    /// <summary>
    /// Checks whether an in-flight operation is active for a publisher for testing.
    /// </summary>
    /// <param name="publisherId">The publisher ID to check.</param>
    /// <returns>True if an operation is in flight, otherwise false.</returns>
    internal bool HasInFlightOperationForTesting(string publisherId)
    {
        lock (_cacheLock)
        {
            return _inFlightOperations.ContainsKey(publisherId);
        }
    }

    /// <summary>
    /// Gets the cached catalog ID for a publisher for testing.
    /// </summary>
    /// <param name="publisherId">The publisher ID to query.</param>
    /// <returns>The cached catalog ID, or null if not cached.</returns>
    internal string? GetCachedCatalogIdForTesting(string publisherId)
    {
        lock (_cacheLock)
        {
            return _browseCache.TryGetValue(publisherId, out var state) ? state.CatalogId : null;
        }
    }

    /// <summary>
    /// Invokes CommitBrowseResultsToCache for unit testing.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="query">The search query.</param>
    /// <param name="hasMoreItems">Whether more items exist.</param>
    /// <param name="isCustomQuery">Whether query is custom.</param>
    /// <param name="append">Whether to append.</param>
    /// <param name="inFlightOp">The in-flight operation context.</param>
    /// <param name="newVms">The list of newly created view models.</param>
    /// <param name="catalogId">Optional explicit catalog identifier.</param>
    /// <returns><c>true</c> if results were committed to cache or handled; otherwise <c>false</c>.</returns>
    internal bool CommitBrowseResultsToCacheForTesting(
        string publisherId,
        ContentSearchQuery query,
        bool hasMoreItems,
        bool isCustomQuery,
        bool append,
        PublisherInFlightOperation? inFlightOp,
        List<ContentGridItemViewModel> newVms,
        string? catalogId = null)
    {
        return CommitBrowseResultsToCache(publisherId, query, hasMoreItems, isCustomQuery, append, inFlightOp, newVms, catalogId);
    }

    /// <summary>
    /// Invokes SaveOutgoingPublisherState for unit testing.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="catalogId">Optional catalog identifier.</param>
    internal void SaveOutgoingPublisherStateForTesting(string? publisherId, string? catalogId = null)
    {
        SaveOutgoingPublisherState(publisherId, catalogId);
    }

    private static void ResetUninstalledFamilyItems(
        IEnumerable<ContentGridItemViewModel> items,
        HashSet<ContentGridItemViewModel> installedItems)
    {
        foreach (var item in items)
        {
            if (installedItems.Contains(item))
            {
                continue;
            }

            foreach (var variant in item.Variants)
            {
                if (variant.CurrentState is ContentState.UpdateAvailable or ContentState.Downloaded)
                {
                    variant.CurrentState = ContentState.NotDownloaded;
                }
            }

            if (item.CurrentState is ContentState.UpdateAvailable or ContentState.Downloaded)
            {
                if (item.SelectedVariant != null)
                {
                    item.CurrentState = item.SelectedVariant.CurrentState;
                    item.IsDownloaded = item.SelectedVariant.CurrentState is ContentState.Downloaded or ContentState.UpdateAvailable;
                }
                else
                {
                    item.CurrentState = ContentState.NotDownloaded;
                    item.IsDownloaded = false;
                }
            }

            item.UpdateTargetVm = null;
            item.NotifyStateChanged();
        }
    }

    private static void ReconcileInstalledFamilyItems(
        IEnumerable<ContentGridItemViewModel> items,
        HashSet<ContentGridItemViewModel> installedItems,
        bool isGeneralsOnline)
    {
        // Same-version siblings (language/resolution variants surfaced as separate cards)
        // are not updates of each other: only a strictly newer newest item triggers updates.
        var newestItem = items
            .OrderByDescending(i => i.SearchResult.Version, Comparer<string?>.Create((a, b) => ContentStateService.CompareVersions(a, b, isGeneralsOnline)))
            .ThenByDescending(i => i.SearchResult.LastUpdated ?? DateTime.MinValue)
            .First();
        var newestVersion = newestItem.SearchResult.Version;
        bool newestNeedsDownload = !installedItems.Contains(newestItem) &&
            installedItems.Any(i => ContentStateService.CompareVersions(newestVersion, i.SearchResult.Version, isGeneralsOnline) > 0);

        foreach (var item in items)
        {
            if (!installedItems.Contains(item))
            {
                continue;
            }

            if (newestNeedsDownload && item != newestItem)
            {
                // An update to newestItem is available for this installed older release.
                foreach (var variant in item.Variants)
                {
                    if (variant.CurrentState is ContentState.Downloaded or ContentState.UpdateAvailable)
                    {
                        variant.CurrentState = ContentState.UpdateAvailable;
                    }
                }

                if (item.SelectedVariant != null)
                {
                    item.CurrentState = item.SelectedVariant.CurrentState;
                    item.IsDownloaded = item.SelectedVariant.CurrentState is ContentState.Downloaded or ContentState.UpdateAvailable;
                }
                else
                {
                    item.CurrentState = ContentState.UpdateAvailable;
                    item.IsDownloaded = true;
                }

                item.UpdateTargetVm = item.CurrentState == ContentState.UpdateAvailable ? newestItem : null;
                item.NotifyStateChanged();
            }
            else
            {
                // Either newestItem is already installed or this item IS the newest item.
                // No update can be offered.
                foreach (var variant in item.Variants)
                {
                    if (variant.CurrentState == ContentState.UpdateAvailable)
                    {
                        variant.CurrentState = ContentState.Downloaded;
                    }
                }

                if (item.CurrentState == ContentState.UpdateAvailable)
                {
                    if (item.SelectedVariant != null)
                    {
                        item.CurrentState = item.SelectedVariant.CurrentState;
                        item.IsDownloaded = item.SelectedVariant.CurrentState is ContentState.Downloaded or ContentState.UpdateAvailable;
                    }
                    else
                    {
                        item.CurrentState = ContentState.Downloaded;
                        item.IsDownloaded = true;
                    }
                }

                item.UpdateTargetVm = null;
                item.NotifyStateChanged();
            }
        }
    }

    [RelayCommand]
    private static void GoBack()
    {
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Core.Messages.ClosePublisherDetailsMessage());
    }

    private static List<List<ContentSearchResult>> GroupContentItemsByVariant(List<ContentSearchResult> items)
    {
        var grouped = new List<List<ContentSearchResult>>();
        var seenVariantGroups = new Dictionary<string, List<ContentSearchResult>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.VariantGroupId))
            {
                if (seenVariantGroups.TryGetValue(item.VariantGroupId, out var group))
                {
                    group.Add(item);
                }
                else
                {
                    var newGroup = new List<ContentSearchResult> { item };
                    seenVariantGroups[item.VariantGroupId] = newGroup;
                    grouped.Add(newGroup);
                }
            }
            else
            {
                grouped.Add([item]);
            }
        }

        return grouped
            .OrderByDescending(g => g.Any(i => i.IsFeatured))
            .ThenByDescending(g => g.Any(i => i.ContentType == ContentType.ContentBundle))
            .ToList();
    }

    private static void PopulateSynthesizedVariants(
        ContentGridItemViewModel variantVm,
        ContentSearchResult primaryItem,
        IList<ContentVariantInfo> singleVariants,
        ILogger? logger = null)
    {
        var lastSegment = primaryItem.Id?.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(lastSegment))
        {
            lastSegment = ContentConstants.DefaultContentFallbackId;
        }

        var baseContentCode = GenPatcherContentRegistry.NormalizeContentCode(lastSegment);
        if (string.IsNullOrEmpty(baseContentCode))
        {
            baseContentCode = lastSegment;
        }

        foreach (var v in singleVariants)
        {
            var provider = !string.IsNullOrWhiteSpace(primaryItem.ProviderName) ? primaryItem.ProviderName : ContentConstants.DefaultContentFallbackId;
            var variantId = !string.IsNullOrWhiteSpace(v.Id) ? v.Id : ContentConstants.DefaultContentFallbackId;
            var composedName = $"{baseContentCode}-{variantId}";
            if (composedName.All(c => !char.IsLetterOrDigit(c)))
            {
                composedName = $"{baseContentCode}-{ContentConstants.DefaultContentFallbackId}";
            }

            string manifestId = string.Empty;
            if (!string.IsNullOrEmpty(v.ManifestId) && ManifestIdValidator.IsValid(v.ManifestId, out _))
            {
                manifestId = v.ManifestId;
            }
            else
            {
                var cleanedProvider = new string(provider.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
                var safeProvider = string.IsNullOrWhiteSpace(cleanedProvider) ? ContentConstants.DefaultContentFallbackId : cleanedProvider;

                var cleanedComposedName = new string(composedName.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray()).Trim('-');
                var safeComposedName = string.IsNullOrWhiteSpace(cleanedComposedName) ? ContentConstants.DefaultContentFallbackId : cleanedComposedName;

                var candidateId = $"{ManifestConstants.DefaultManifestFormatVersion}.0.{safeProvider}.{primaryItem.ContentType.ToManifestIdString()}.{safeComposedName}";
                if (ManifestIdValidator.IsValid(candidateId, out _))
                {
                    manifestId = candidateId;
                }
                else
                {
                    logger?.LogWarning(
                        "Synthesized variant candidate ID '{CandidateId}' failed validation for provider '{Provider}', content '{ContentName}'. Falling back to default ID.",
                        candidateId,
                        provider,
                        primaryItem.Name);
                    manifestId = $"{ManifestConstants.DefaultManifestFormatVersion}.0.{safeProvider}.{primaryItem.ContentType.ToManifestIdString()}.{ContentConstants.DefaultContentFallbackId}";
                }
            }

            var baseName = !string.IsNullOrEmpty(primaryItem.VariantFamilyName) ? primaryItem.VariantFamilyName : primaryItem.Name;
            var variantName = !string.IsNullOrEmpty(v.Name) && (v.Name.StartsWith(baseName, StringComparison.OrdinalIgnoreCase) || v.Name.Contains(baseName, StringComparison.OrdinalIgnoreCase))
                ? v.Name
                : $"{baseName} - {v.Name}";

            // Clone the full result so variant swaps keep the direct download URL,
            // parsed page data, and typed payload instead of falling back to the source URL.
            var variantSr = VariantSwap.Clone(primaryItem);
            variantSr.Id = manifestId;
            variantSr.Name = variantName;
            variantSr.TargetGame = v.TargetGame ?? primaryItem.TargetGame;

            variantSr.ResolverMetadata[CatalogConstants.SelectedVariantMetadataKey] = v.Id;

            var installable = new InstallableVariant
            {
                Name = !string.IsNullOrWhiteSpace(v.Name) ? v.Name : VariantSwap.ResolveDisplayName(variantSr, v),
                ManifestId = manifestId,
                IconUrl = primaryItem.IconUrl ?? string.Empty,
                VariantType = v.VariantType ?? string.Empty,
            };

            variantVm.AddVariant(installable, variantSr);
        }
    }

    private static void PopulateSiblingVariants(
        ContentGridItemViewModel variantVm,
        IReadOnlyList<ContentSearchResult> groupItems,
        ContentSearchResult defaultVariant)
    {
        foreach (var sibling in groupItems)
        {
            var variantInfo = sibling.Variants?.FirstOrDefault(v =>
                string.Equals(v.Id, sibling.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v.ManifestId, sibling.Id, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(v.Id) && sibling.Id?.EndsWith($".{v.Id}", StringComparison.OrdinalIgnoreCase) == true) ||
                (!string.IsNullOrEmpty(v.Id) && sibling.Id?.EndsWith($"-{v.Id}", StringComparison.OrdinalIgnoreCase) == true));

            var info = variantInfo ?? new ContentVariantInfo
            {
                Id = sibling.Id ?? string.Empty,
                Name = sibling.Name ?? sibling.Id ?? "Unknown",
                ManifestId = sibling.Id ?? string.Empty,
                IsDefault = sibling == defaultVariant,
                VariantType = sibling.Variants?.FirstOrDefault(v => !string.IsNullOrEmpty(v.VariantType))?.VariantType ?? string.Empty,
            };

            var catalogKey = VariantSwap.ResolveCatalogKey(sibling, info);
            var displayName = !string.IsNullOrWhiteSpace(info.Name)
                ? info.Name
                : VariantSwap.ResolveDisplayName(sibling, info);

            var installable = new InstallableVariant
            {
                Name = displayName,
                ManifestId = catalogKey,
                IconUrl = sibling.IconUrl ?? string.Empty,
                VariantType = info.VariantType ?? string.Empty,
            };

            variantVm.AddVariant(installable, sibling);
        }
    }

    /// <summary>
    /// Seeds the sidebar with shipped/built-in providers (not user subscriptions).
    /// </summary>
    private static ObservableCollection<PublisherItemViewModel> CreateBuiltInPublishers(string? downloadedContentLabel = null)
    {
        return
        [
            new PublisherItemViewModel(
                PublisherTypeConstants.Downloaded,
                downloadedContentLabel ?? PublisherInfoConstants.DownloadedContent.Name,
                PublisherInfoConstants.DownloadedContent.LogoSource,
                ContentConstants.CategoryStatic),
            new PublisherItemViewModel(
                PublisherTypeConstants.GeneralsOnline,
                PublisherInfoConstants.GeneralsOnline.Name,
                PublisherInfoConstants.GeneralsOnline.LogoSource,
                ContentConstants.CategoryStatic),
            new PublisherItemViewModel(
                PublisherTypeConstants.TheSuperHackers,
                PublisherInfoConstants.TheSuperHackers.Name,
                PublisherInfoConstants.TheSuperHackers.LogoSource,
                ContentConstants.CategoryStatic),
            new PublisherItemViewModel(
                CommunityOutpostConstants.PublisherType,
                PublisherInfoConstants.CommunityOutpost.Name,
                PublisherInfoConstants.CommunityOutpost.LogoSource,
                ContentConstants.CategoryStatic),
            new PublisherItemViewModel(
                PublisherTypeConstants.GenLauncher,
                PublisherInfoConstants.GenLauncher.Name,
                PublisherInfoConstants.GenLauncher.LogoSource,
                ContentConstants.CategoryStatic),
            new PublisherItemViewModel(
                GitHubTopicsConstants.PublisherType,
                PublisherInfoConstants.GitHub.Name,
                PublisherInfoConstants.GitHub.LogoSource,
                ContentConstants.CategoryDynamic),
            new PublisherItemViewModel(
                CNCLabsConstants.PublisherType,
                PublisherInfoConstants.CNCLabs.Name,
                PublisherInfoConstants.CNCLabs.LogoSource,
                ContentConstants.CategoryDynamic),
            new PublisherItemViewModel(
                AODMapsConstants.PublisherType,
                PublisherInfoConstants.AODMaps.Name,
                PublisherInfoConstants.AODMaps.LogoSource,
                ContentConstants.CategoryDynamic),
            new PublisherItemViewModel(
                ModDBConstants.PublisherType,
                PublisherInfoConstants.ModDB.Name,
                PublisherInfoConstants.ModDB.LogoSource,
                ContentConstants.CategoryDynamic),
            new PublisherItemViewModel(
                SteamWorkshopConstants.PublisherType,
                PublisherInfoConstants.SteamWorkshop.Name,
                PublisherInfoConstants.SteamWorkshop.LogoSource,
                ContentConstants.CategoryDynamic),
        ];
    }

    private static void RunOnUi(Action action)
    {
        if (Avalonia.Application.Current == null || Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(action);
        }
    }

    private static List<ContentGridItemViewModel> SnapshotInFlight(PublisherInFlightOperation inFlight)
    {
        lock (inFlight.SyncRoot)
        {
            return inFlight.ResolvedItems.ToList();
        }
    }

    private static string GetVariantVersion(ContentGridItemViewModel vm, InstallableVariant v)
    {
        if (!string.IsNullOrEmpty(v.ManifestId) &&
            vm.VariantSearchResults.TryGetValue(v.ManifestId, out var sr) &&
            !string.IsNullOrEmpty(sr.Version))
        {
            return sr.Version;
        }

        return string.Empty;
    }

    private static int CompareVariantVersions(string? v1, string? v2)
    {
        if (string.IsNullOrEmpty(v1) && string.IsNullOrEmpty(v2))
        {
            return 0;
        }

        if (string.IsNullOrEmpty(v1))
        {
            return -1;
        }

        if (string.IsNullOrEmpty(v2))
        {
            return 1;
        }

        return ContentStateService.CompareVersions(v1, v2);
    }

    private static bool IsVariantUpdateCandidate(InstallableVariant candidate, InstallableVariant current)
    {
        if (string.Equals(candidate.ManifestId, current.ManifestId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(candidate.Name) &&
            !string.IsNullOrWhiteSpace(current.Name))
        {
            return string.Equals(candidate.Name, current.Name, StringComparison.OrdinalIgnoreCase);
        }

        return candidate.IsDefault && (current.IsDefault || string.IsNullOrWhiteSpace(current.Name));
    }

    private void HandleSelectedPublisherChanged(PublisherItemViewModel? value)
    {
        if (value == null)
        {
            Interlocked.Increment(ref _activeRequestId);
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
            ClearCatalogSelection();
            return;
        }

        // Save current outgoing publisher state to _browseCache before switching
        SaveOutgoingPublisherState(_lastPopulatedPublisherId, SelectedCatalog?.Id);

        Interlocked.Increment(ref _activeRequestId);
        BeginLoadCatalogsForPublisher(value);

        // Cancel any active custom search query
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        // Update selection state
        foreach (var publisher in Publishers)
        {
            publisher.IsSelected = publisher == value;
        }

        // Switch filter panel
        SwitchFilterPanel(value.PublisherId);

        if (_suppressPublisherChangedRefresh)
        {
            _lastPopulatedPublisherId = value.PublisherId;
            SelectedContent = null;
            return;
        }

        // Detach UI collection and detail view immediately so previous publisher's cards vanish from UI without disposing cached objects
        ContentItems = [];
        SelectedContent = null;

        lock (_cacheLock)
        {
            // SelectedCatalog is null while catalogs load (BeginLoadCatalogsForPublisher
            // clears it above). A surviving entry is still safe to restore: PopulateCatalogs
            // evicts and refreshes when the loaded catalog differs from the cached one.
            if (_browseCache.TryGetValue(value.PublisherId, out var cached) &&
                (SelectedCatalog == null ||
                 string.Equals(cached.CatalogId, SelectedCatalog?.Id, StringComparison.OrdinalIgnoreCase)))
            {
                // Cache hit: restore full dataset instantly without network discovery
                ContentItems = new ObservableCollection<ContentGridItemViewModel>(cached.Items);
                CurrentPage = cached.CurrentPage;
                CanLoadMore = cached.CanLoadMore;
                SearchTerm = cached.SearchTerm;
                _hasCustomQuery = cached.HasCustomQuery;
                SelectedContent = cached.ActiveDetailViewModel;
                _lastPopulatedPublisherId = value.PublisherId;
                IsLoading = false;
                _ = RefreshAndReconcileItemsAsync(cached.Items, value.PublisherId);
                logger.LogInformation(
                    "Restored {Count} cached items for {Publisher} (no refresh needed)",
                    cached.Items.Count,
                    value.DisplayName);
                return;
            }

            if (_inFlightOperations.TryGetValue(value.PublisherId, out var inFlight))
            {
                // Attach UI to ongoing in-flight background operation
                List<ContentGridItemViewModel> itemsSoFar = [];
                lock (inFlight.SyncRoot)
                {
                    inFlight.ActiveRequestId = _activeRequestId;
                    itemsSoFar = [.. inFlight.ResolvedItems];
                }

                ContentItems = new ObservableCollection<ContentGridItemViewModel>(itemsSoFar);
                CurrentPage = inFlight.Query.Page ?? 1;
                CanLoadMore = inFlight.HasMoreItems;
                SearchTerm = string.Empty;
                _hasCustomQuery = false;
                SelectedContent = null;
                _lastPopulatedPublisherId = value.PublisherId;
                IsLoading = !inFlight.IsCompleted;
                _ = RefreshAndReconcileItemsAsync(itemsSoFar, value.PublisherId);
                logger.LogInformation(
                    "Attached to in-flight operation for {Publisher} ({Count} items loaded so far)",
                    value.PublisherId,
                    itemsSoFar.Count);
                return;
            }
        }

        _hasCustomQuery = false;
        SearchTerm = string.Empty;
        CurrentPage = 1;
        CanLoadMore = false;
        SelectedContent = null;
        _lastPopulatedPublisherId = value.PublisherId;
        _ = RefreshContentAsync();
    }

    private void SaveOutgoingPublisherState(string? publisherId, string? catalogId = null)
    {
        if (string.IsNullOrEmpty(publisherId))
        {
            return;
        }

        lock (_cacheLock)
        {
            var targetCatalogId = ResolveEffectiveCatalogId(publisherId, catalogId);

            if (_browseCache.TryGetValue(publisherId, out var outgoingState))
            {
                outgoingState.ActiveDetailViewModel = SelectedContent;
                outgoingState.SearchTerm = SearchTerm;
                outgoingState.HasCustomQuery = _hasCustomQuery;
                outgoingState.CurrentPage = CurrentPage;
                outgoingState.CanLoadMore = CanLoadMore;
                if (!string.IsNullOrEmpty(targetCatalogId))
                {
                    outgoingState.CatalogId = targetCatalogId;
                }

                if (_hasCustomQuery)
                {
                    if (outgoingState.Items.Count > 0)
                    {
                        var activeItemSet = new HashSet<ContentGridItemViewModel>(ContentItems);
                        foreach (var oldItem in outgoingState.Items.Where(oldItem => !activeItemSet.Contains(oldItem)))
                        {
                            oldItem.Dispose();
                        }
                    }

                    outgoingState.Items = [.. ContentItems];
                }
            }
            else if (ContentItems.Count > 0 || SelectedContent != null)
            {
                _browseCache[publisherId] = new PublisherBrowseState
                {
                    Items = [.. ContentItems],
                    CurrentPage = CurrentPage,
                    CanLoadMore = CanLoadMore,
                    SearchTerm = SearchTerm,
                    HasCustomQuery = _hasCustomQuery,
                    ActiveDetailViewModel = SelectedContent,
                    CatalogId = targetCatalogId,
                };
            }
        }
    }

    partial void OnSelectedCatalogChanged(CatalogEntry? value)
    {
        if (_suppressCatalogChanged || value == null || SelectedPublisher == null)
        {
            return;
        }

        _ = ApplySelectedCatalogAsync(SelectedPublisher.PublisherId, value);
    }

    private void BeginLoadCatalogsForPublisher(PublisherItemViewModel publisher)
    {
        var oldCts = Interlocked.Exchange(ref _catalogsCts, null);
        oldCts?.Cancel();
        oldCts?.Dispose();
        ClearCatalogSelection();

        if (!publisher.PublisherType.Equals(CatalogConstants.SubscribedPublisherCategory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var loadId = Interlocked.Increment(ref _activeCatalogLoadId);
        _lastCatalogPublisherId = publisher.PublisherId;
        var newCts = CancellationTokenSource.CreateLinkedTokenSource(_vmCts.Token);
        var replaced = Interlocked.Exchange(ref _catalogsCts, newCts);
        replaced?.Cancel();
        replaced?.Dispose();
        _ = LoadAvailableCatalogsAsync(publisher.PublisherId, loadId, newCts.Token);
    }

    private void ClearCatalogSelection()
    {
        Interlocked.Increment(ref _activeCatalogLoadId);
        _lastCatalogPublisherId = null;
        _suppressCatalogChanged = true;
        try
        {
            AvailableCatalogs.Clear();
            SelectedCatalog = null;
            IsLoadingCatalogs = false;
        }
        finally
        {
            _suppressCatalogChanged = false;
        }

        OnPropertyChanged(nameof(HasMultipleCatalogs));
        OnPropertyChanged(nameof(CanShowCatalogSwitcher));
        OnPropertyChanged(nameof(CatalogLoadingStatusText));
        OnPropertyChanged(nameof(CanReloadCatalog));
        ReloadCatalogCommand.NotifyCanExecuteChanged();
    }

    private CatalogEntry? FindMatchingCatalogEntry(
        IReadOnlyList<CatalogEntry> catalogs,
        Core.Models.Providers.PublisherSubscription subscription)
    {
        if (!string.IsNullOrWhiteSpace(subscription.SelectedCatalogId))
        {
            var byId = catalogs.FirstOrDefault(c =>
                c.Id.Equals(subscription.SelectedCatalogId, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
            {
                return byId;
            }
        }

        return catalogs.FirstOrDefault(c =>
            c.Url.Equals(subscription.CatalogUrl, StringComparison.OrdinalIgnoreCase));
    }

    private async Task LoadAvailableCatalogsAsync(string publisherId, int loadId, CancellationToken cancellationToken)
    {
        RunOnUi(() =>
        {
            if (string.Equals(_lastCatalogPublisherId, publisherId, StringComparison.OrdinalIgnoreCase) && _activeCatalogLoadId == loadId)
            {
                IsLoadingCatalogs = true;
            }
        });

        try
        {
            var subscriptionResult = await subscriptionStore.GetSubscriptionAsync(publisherId, cancellationToken);
            var subscription = subscriptionResult.Success ? subscriptionResult.Data : null;
            if (subscription == null || string.IsNullOrWhiteSpace(subscription.DefinitionUrl))
            {
                return;
            }

            var definitionService = serviceProvider.GetService<IPublisherDefinitionService>();
            if (definitionService == null)
            {
                return;
            }

            var definitionResult = await definitionService.FetchDefinitionAsync(subscription.DefinitionUrl, cancellationToken);
            if (definitionResult.Success && definitionResult.Data != null)
            {
                var definition = definitionResult.Data;
                var updated = false;
                if (!string.IsNullOrWhiteSpace(definition.Publisher?.AvatarUrl) &&
                    !string.Equals(subscription.AvatarUrl, definition.Publisher.AvatarUrl, StringComparison.OrdinalIgnoreCase))
                {
                    subscription.AvatarUrl = definition.Publisher.AvatarUrl;
                    updated = true;
                }

                if (!string.IsNullOrWhiteSpace(definition.Publisher?.Name) &&
                    !string.Equals(subscription.PublisherName, definition.Publisher.Name, StringComparison.OrdinalIgnoreCase))
                {
                    subscription.PublisherName = definition.Publisher.Name;
                    updated = true;
                }

                if (updated)
                {
                    await subscriptionStore.UpdateSubscriptionAsync(subscription, cancellationToken);
                    RunOnUi(() =>
                    {
                        var pub = Publishers.FirstOrDefault(p => string.Equals(p.PublisherId, publisherId, StringComparison.OrdinalIgnoreCase));
                        if (pub != null)
                        {
                            if (!string.IsNullOrWhiteSpace(subscription.AvatarUrl))
                            {
                                pub.LogoSource = subscription.AvatarUrl;
                            }

                            if (!string.IsNullOrWhiteSpace(subscription.PublisherName))
                            {
                                pub.DisplayName = subscription.PublisherName;
                            }
                        }
                    });
                }
            }

            var catalogs = definitionResult.Success
                ? definitionResult.Data?.Catalogs.Where(c => !string.IsNullOrWhiteSpace(c.Url)).ToList()
                : null;
            if (cancellationToken.IsCancellationRequested
                || _activeCatalogLoadId != loadId
                || !string.Equals(_lastCatalogPublisherId, publisherId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (catalogs == null || catalogs.Count == 0)
            {
                logger.LogDebug(
                    "No browsable catalogs in definition for subscribed publisher {PublisherId}",
                    publisherId);
                return;
            }

            RunOnUi(() => PopulateCatalogs(publisherId, subscription, catalogs, loadId));
        }
        catch (OperationCanceledException)
        {
            // Superseded by another publisher selection or disposal.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load catalogs for subscribed publisher {PublisherId}", publisherId);
        }
        finally
        {
            RunOnUi(() =>
            {
                if (!cancellationToken.IsCancellationRequested &&
                    string.Equals(_lastCatalogPublisherId, publisherId, StringComparison.OrdinalIgnoreCase) &&
                    _activeCatalogLoadId == loadId)
                {
                    IsLoadingCatalogs = false;
                }
            });
        }
    }

    private void CheckAndNotifyNewCatalogs(
        string publisherId,
        Core.Models.Providers.PublisherSubscription subscription,
        List<CatalogEntry> catalogs)
    {
        if (!_knownCatalogIds.TryGetValue(publisherId, out var prevCatalogIds) || !subscription.NotifyNewReleases)
        {
            return;
        }

        var newCatalogs = catalogs.Where(c => !prevCatalogIds.Contains(c.Id)).ToList();
        if (newCatalogs.Count == 0)
        {
            return;
        }

        var title = _localizationService?.GetString("Downloads.Browser.NewCatalogNotificationTitle") ?? "New Catalog Available";
        var actionText = _localizationService?.GetString("Downloads.Browser.ViewCatalogAction") ?? "View Catalog";

        var targetCat = newCatalogs[0];
        var message = newCatalogs.Count == 1
            ? string.Format(
                _localizationService?.GetString("Downloads.Browser.NewCatalogNotificationFormat") ?? "Publisher '{0}' released a new catalog: '{1}'",
                subscription.PublisherName ?? publisherId,
                targetCat.Name)
            : string.Format(
                _localizationService?.GetString("Downloads.Browser.NewCatalogsNotificationFormat") ?? "Publisher '{0}' released {1} new catalogs.",
                subscription.PublisherName ?? publisherId,
                newCatalogs.Count);

        notificationService.Show(new NotificationMessage(
            NotificationType.Info,
            title,
            message,
            autoDismissMilliseconds: NotificationDurations.VeryLong,
            actionText: actionText,
            action: () => RunOnUi(() =>
            {
                if (string.Equals(SelectedPublisher?.PublisherId, publisherId, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedCatalog = targetCat;
                }
            })));
    }

    private void PopulateCatalogs(
        string publisherId,
        Core.Models.Providers.PublisherSubscription subscription,
        List<CatalogEntry> catalogs,
        int loadId)
    {
        if (_activeCatalogLoadId != loadId
            || !string.Equals(_lastCatalogPublisherId, publisherId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(SelectedPublisher?.PublisherId, publisherId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CheckAndNotifyNewCatalogs(publisherId, subscription, catalogs);
        _knownCatalogIds[publisherId] = new HashSet<string>(catalogs.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);

        _suppressCatalogChanged = true;
        try
        {
            var previouslySelectedId = SelectedCatalog?.Id;
            var publisherAvatar = subscription.AvatarUrl ?? SelectedPublisher?.LogoSource;
            AvailableCatalogs.Clear();
            foreach (var catalog in catalogs)
            {
                catalog.PublisherAvatarUrl = publisherAvatar;
                AvailableCatalogs.Add(catalog);
            }

            var match = AvailableCatalogs.FirstOrDefault(c => string.Equals(c.Id, previouslySelectedId, StringComparison.OrdinalIgnoreCase))
                ?? FindMatchingCatalogEntry(catalogs, subscription);
            if (match == null && !string.IsNullOrWhiteSpace(subscription.CatalogUrl))
            {
                match = new CatalogEntry
                {
                    Id = CatalogConstants.CurrentCatalogEntryId,
                    Name = _localizationService?.GetString("Downloads.Browser.CurrentCatalog") ?? "Current catalog",
                    Url = subscription.CatalogUrl,
                    PublisherAvatarUrl = publisherAvatar,
                };
                AvailableCatalogs.Insert(0, match);
            }

            SelectedCatalog = match ?? catalogs[0];
            OnPropertyChanged(nameof(HasMultipleCatalogs));
            OnPropertyChanged(nameof(CanShowCatalogSwitcher));
            OnPropertyChanged(nameof(CatalogLoadingStatusText));
            OnPropertyChanged(nameof(CanReloadCatalog));
            ReloadCatalogCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            _suppressCatalogChanged = false;
        }

        lock (_cacheLock)
        {
            if (_browseCache.TryGetValue(publisherId, out var cachedState) &&
                !string.IsNullOrEmpty(cachedState.CatalogId) &&
                !string.Equals(cachedState.CatalogId, SelectedCatalog?.Id, StringComparison.OrdinalIgnoreCase))
            {
                _browseCache.Remove(publisherId);
                foreach (var item in cachedState.Items)
                {
                    item.Dispose();
                }

                if (string.Equals(SelectedPublisher?.PublisherId, publisherId, StringComparison.OrdinalIgnoreCase))
                {
                    ContentItems = [];
                    _ = RefreshContentAsync();
                }
            }
        }
    }

    private async Task ApplySelectedCatalogAsync(string publisherId, CatalogEntry catalog)
    {
        try
        {
            if (!_subscribedDiscoverers.TryGetValue(publisherId, out var discoverer))
            {
                return;
            }

            var subscriptionResult = await subscriptionStore.GetSubscriptionAsync(publisherId, _vmCts.Token);
            var subscription = subscriptionResult.Success ? subscriptionResult.Data : null;
            if (subscription == null)
            {
                return;
            }

            var catalogUrlChanged = !string.Equals(subscription.CatalogUrl, catalog.Url, StringComparison.OrdinalIgnoreCase);
            var catalogIdChanged = !string.Equals(subscription.SelectedCatalogId, catalog.Id, StringComparison.OrdinalIgnoreCase);

            if (!catalogUrlChanged && !catalogIdChanged)
            {
                return;
            }

            subscription.CatalogUrl = catalog.Url;
            subscription.SelectedCatalogId = catalog.Id;
            subscription.CachedCatalogHash = null;
            subscription.LastFetched = null;
            var updateResult = await subscriptionStore.UpdateSubscriptionAsync(subscription, _vmCts.Token);
            if (!updateResult.Success)
            {
                logger.LogWarning(
                    "Failed to persist catalog switch for {PublisherId}: {Errors}",
                    publisherId,
                    string.Join("; ", updateResult.Errors));
                notificationService.ShowWarning(
                    _localizationService?.GetString("Downloads.Browser.SwitchCatalogFailedTitle") ?? "Could Not Switch Catalogs",
                    _localizationService?.GetString("Downloads.Browser.SwitchCatalogFailedMessage") ?? "The catalog selection could not be saved. Please try again.");
                await RevertCatalogSelectionAsync(publisherId);
                return;
            }

            lock (_cacheLock)
            {
                if (_inFlightOperations.Remove(publisherId, out var inFlightOp))
                {
                    inFlightOp.Cts.Cancel();
                    inFlightOp.Cts.Dispose();
                }

                if (_browseCache.Remove(publisherId, out var oldState))
                {
                    foreach (var item in oldState.Items)
                    {
                        item.Dispose();
                    }
                }
            }

            discoverer.Configure(subscription);
            Interlocked.Increment(ref _activeRequestId);
            var searchCts = Interlocked.Exchange(ref _searchCts, null);
            if (searchCts != null)
            {
                await searchCts.CancelAsync();
                searchCts.Dispose();
            }

            SelectedContent?.Dispose();
            SelectedContent = null;
            _hasCustomQuery = false;
            SearchTerm = string.Empty;
            CurrentPage = 1;
            CanLoadMore = false;
            _lastPopulatedPublisherId = publisherId;
            await RefreshContentAsync();
        }
        catch (OperationCanceledException)
        {
            // Superseded by another selection or disposal. This method only runs as
            // fire-and-forget work, so cancellation is swallowed rather than rethrown.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to switch catalogs for publisher {PublisherId}", publisherId);
        }
    }

    private async Task RevertCatalogSelectionAsync(string publisherId)
    {
        var freshResult = await subscriptionStore.GetSubscriptionAsync(publisherId, _vmCts.Token);
        var freshSubscription = freshResult.Success ? freshResult.Data : null;
        RunOnUi(() =>
        {
            _suppressCatalogChanged = true;
            try
            {
                if (freshSubscription != null)
                {
                    SelectedCatalog = FindMatchingCatalogEntry([.. AvailableCatalogs], freshSubscription) ?? SelectedCatalog;
                }
            }
            finally
            {
                _suppressCatalogChanged = false;
            }
        });
    }

    private void SwitchFilterPanel(string publisherId)
    {
        if (CurrentFilterViewModel != null)
        {
            CurrentFilterViewModel.FiltersApplied -= OnFiltersApplied;
            CurrentFilterViewModel.FiltersCleared -= OnFiltersCleared;
            CurrentFilterViewModel.ClearFilters();
        }

        if (_filterViewModels.TryGetValue(publisherId, out var filterVm))
        {
            CurrentFilterViewModel = filterVm;
            CurrentFilterViewModel.FiltersApplied += OnFiltersApplied;
            CurrentFilterViewModel.FiltersCleared += OnFiltersCleared;
            IsFilterPanelVisible = false;
        }
        else
        {
            CurrentFilterViewModel = null;
            IsFilterPanelVisible = false;
        }
    }

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        // Clearing filters re-runs a default query, which re-populates the cache.
        _hasCustomQuery = !string.IsNullOrWhiteSpace(SearchTerm);
        CurrentPage = 1;
        Interlocked.Increment(ref _activeRequestId);
        _ = RefreshContentAsync();
    }

    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        // Trigger content refresh when filters are applied
        _hasCustomQuery = true;
        CurrentPage = 1;
        Interlocked.Increment(ref _activeRequestId);
        _ = RefreshContentAsync();
    }

    private void OnContentStateChanged(object? sender, ContentStateChangedEventArgs e)
    {
        InvalidateDownloadedLibraryCache();
        _ = RefreshDownloadedContentCountAsync();
        RunOnUi(() => ReconcileReleaseUpdateStates(ContentItems));
    }

    /// <summary>
    /// Drops the cached offline-library browse state so the next visit re-reads the
    /// manifest pool. Skipped while the library is open: deletions purge their cards
    /// synchronously via <see cref="OnContentDeletedAsync"/>, and acquisitions appear
    /// on the next refresh, matching catalog tab freshness behavior.
    /// </summary>
    private void InvalidateDownloadedLibraryCache()
    {
        lock (_cacheLock)
        {
            if (string.Equals(SelectedPublisher?.PublisherId, PublisherTypeConstants.Downloaded, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_browseCache.TryGetValue(PublisherTypeConstants.Downloaded, out var state))
            {
                foreach (var item in state.Items)
                {
                    item.Dispose();
                }

                _browseCache.Remove(PublisherTypeConstants.Downloaded);
            }
        }
    }

    /// <summary>
    /// Removes cards for a deleted manifest from the offline library and updates its count.
    /// Catalog cards are left in place; they flip back to the download state through the
    /// content-state notification the detail view sends after a successful delete.
    /// </summary>
    /// <param name="manifestId">The deleted manifest ID.</param>
    private Task OnContentDeletedAsync(string manifestId)
    {
        if (string.IsNullOrWhiteSpace(manifestId))
        {
            return Task.CompletedTask;
        }

        RunOnUi(() =>
        {
            lock (_cacheLock)
            {
                if (_browseCache.TryGetValue(PublisherTypeConstants.Downloaded, out var state))
                {
                    var cached = state.Items
                        .Where(item => string.Equals(item.SearchResult?.Id, manifestId, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    foreach (var item in cached)
                    {
                        state.Items.Remove(item);
                        if (!ContentItems.Contains(item))
                        {
                            item.Dispose();
                        }
                    }
                }
            }

            if (string.Equals(SelectedPublisher?.PublisherId, PublisherTypeConstants.Downloaded, StringComparison.OrdinalIgnoreCase))
            {
                var visible = ContentItems
                    .Where(item => string.Equals(item.SearchResult?.Id, manifestId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var item in visible)
                {
                    ContentItems.Remove(item);
                    item.Dispose();
                }
            }
        });

        _ = RefreshDownloadedContentCountAsync();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Refreshes the offline-library sidebar badge from the manifest pool.
    /// </summary>
    private async Task RefreshDownloadedContentCountAsync()
    {
        try
        {
            var manifestPool = serviceProvider.GetService(typeof(IContentManifestPool)) as IContentManifestPool;
            if (manifestPool == null)
            {
                return;
            }

            var result = await manifestPool.GetAllManifestsAsync(_vmCts.Token);
            var count = result.Success && result.Data != null
                ? result.Data.Count(manifest => !ManifestHelper.IsLauncherManagedManifest(manifest))
                : 0;
            RunOnUi(() =>
            {
                var entry = Publishers.FirstOrDefault(p =>
                    string.Equals(p.PublisherId, PublisherTypeConstants.Downloaded, StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                {
                    entry.ContentCount = count;
                }
            });
        }
        catch (OperationCanceledException)
        {
            // View model is shutting down; badge update is irrelevant.
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to refresh downloaded content count");
        }
    }

    private void OnPublisherSubscriptionsChanged()
    {
        RunOnUi(() => _ = RefreshSubscribedPublishersAsync());
    }

    private void OnPublisherSubscriptionRemoved(string publisherId)
    {
        Interlocked.Increment(ref _subscriptionRefreshVersion);
        RunOnUi(() =>
        {
            if (!_disposed)
            {
                RemoveSubscribedPublisher(publisherId);
            }
        });
    }

    private void RemoveSubscribedPublisher(string publisherId)
    {
        var item = Publishers.FirstOrDefault(p =>
            p.PublisherType.Equals(CatalogConstants.SubscribedPublisherCategory, StringComparison.OrdinalIgnoreCase)
            && p.PublisherId.Equals(publisherId, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            return;
        }

        _subscribedDiscoverers.Remove(publisherId);
        var isSelected = string.Equals(SelectedPublisher?.PublisherId, publisherId, StringComparison.OrdinalIgnoreCase);
        if (isSelected)
        {
            _searchCts?.Cancel();
            SelectedContent?.Dispose();
            SelectedContent = null;
            foreach (var contentItem in ContentItems)
            {
                contentItem.Dispose();
            }

            ContentItems.Clear();

            // Do not save the removed publisher back into the browse cache on selection change.
            _lastPopulatedPublisherId = null;
        }

        lock (_cacheLock)
        {
            if (_inFlightOperations.Remove(publisherId, out var inFlight))
            {
                inFlight.Cts.Cancel();
                foreach (var vm in SnapshotInFlight(inFlight))
                {
                    vm.Dispose();
                }
            }

            if (_browseCache.Remove(publisherId, out var removedState))
            {
                removedState.ActiveDetailViewModel?.Dispose();
                foreach (var vm in removedState.Items)
                {
                    vm.Dispose();
                }
            }
        }

        // Remove the bound row after clearing outgoing state: selection bindings may run synchronously.
        Publishers.Remove(item);
        if (isSelected)
        {
            SelectedPublisher = Publishers.FirstOrDefault();
        }
    }

    private void OnContentLibraryCleared()
    {
        RunOnUi(() =>
        {
            lock (_cacheLock)
            {
                if (_browseCache.TryGetValue(PublisherTypeConstants.Downloaded, out var state))
                {
                    foreach (var item in state.Items.Where(item => !ContentItems.Contains(item)))
                    {
                        item.Dispose();
                    }

                    _browseCache.Remove(PublisherTypeConstants.Downloaded);
                }
            }

            if (string.Equals(SelectedPublisher?.PublisherId, PublisherTypeConstants.Downloaded, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var item in ContentItems)
                {
                    item.Dispose();
                }

                ContentItems.Clear();
                CurrentPage = 1;
                CanLoadMore = false;
                _ = RefreshContentAsync();
            }
            else
            {
                ReconcileReleaseUpdateStates(ContentItems);
            }
        });

        _ = RefreshDownloadedContentCountAsync();
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (e.PropertyName == nameof(ILocalizationService.CurrentCulture) || e.PropertyName == LocalizationConstants.IndexerPropertyName)
        {
            var entry = Publishers.FirstOrDefault(p =>
                string.Equals(p.PublisherId, PublisherTypeConstants.Downloaded, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                entry.DisplayName = ResolveDownloadedContentLabel();
            }

            OnPropertyChanged(nameof(CatalogLoadingStatusText));
        }
    }

    /// <summary>
    /// Resolves the offline-library sidebar label for the active culture.
    /// </summary>
    private string ResolveDownloadedContentLabel()
    {
        if (_localizationService is { } locService && locService.TryGetString("Downloads.Browser.MyDownloads", out var localized))
        {
            return localized;
        }

        return PublisherInfoConstants.DownloadedContent.Name;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (ModDBDiscoverer.TryNormalizeModDBUrl(SearchTerm, out _) &&
            SelectedPublisher?.PublisherId != ModDBConstants.PublisherType)
        {
            var savedSearchTerm = SearchTerm;
            var moddbPublisher = Publishers.FirstOrDefault(p => p.PublisherId == ModDBConstants.PublisherType);
            if (moddbPublisher != null)
            {
                // Restore outgoing publisher's original search term before triggering SelectedPublisher change,
                // so the outgoing publisher's cached browse state is not polluted with the pasted ModDB URL.
                string? restoredSearchTerm = null;
                lock (_cacheLock)
                {
                    if (!string.IsNullOrEmpty(_lastPopulatedPublisherId) &&
                        _browseCache.TryGetValue(_lastPopulatedPublisherId, out var state))
                    {
                        restoredSearchTerm = state.SearchTerm;
                    }
                }

                SearchTerm = restoredSearchTerm ?? string.Empty;

                try
                {
                    _suppressPublisherChangedRefresh = true;
                    SelectedPublisher = moddbPublisher;
                }
                finally
                {
                    _suppressPublisherChangedRefresh = false;
                }

                SearchTerm = savedSearchTerm;
                _hasCustomQuery = true;
                CurrentPage = 1;
                Interlocked.Increment(ref _activeRequestId);
                await RefreshContentAsync();
                return;
            }
        }

        _hasCustomQuery = !string.IsNullOrWhiteSpace(SearchTerm) || (CurrentFilterViewModel != null && CurrentFilterViewModel.HasActiveFilters);
        CurrentPage = 1;
        Interlocked.Increment(ref _activeRequestId);
        await RefreshContentAsync();
    }

    /// <summary>
    /// Reloads the available catalogs and content for the current publisher.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(CanExecute = nameof(CanReloadCatalog))]
    private async Task ReloadCatalogAsync()
    {
        if (SelectedPublisher == null || !IsSubscribedPublisher)
        {
            return;
        }

        Interlocked.Increment(ref _activeRequestId);
        var publisherId = SelectedPublisher.PublisherId;
        lock (_cacheLock)
        {
            if (_browseCache.Remove(publisherId, out var cachedState))
            {
                if (cachedState.ActiveDetailViewModel != null)
                {
                    if (ReferenceEquals(SelectedContent, cachedState.ActiveDetailViewModel))
                    {
                        SelectedContent = null;
                    }

                    cachedState.ActiveDetailViewModel.Dispose();
                }

                var currentItems = new HashSet<ContentGridItemViewModel>(ContentItems);
                foreach (var item in cachedState.Items.Where(item => !currentItems.Contains(item)))
                {
                    item.Dispose();
                }
            }
        }

        if (IsSubscribedPublisher)
        {
            var previousCts = Interlocked.Exchange(ref _catalogsCts, null);
            if (previousCts != null)
            {
                await previousCts.CancelAsync();
                previousCts.Dispose();
            }

            if (!string.Equals(SelectedPublisher?.PublisherId, publisherId, StringComparison.OrdinalIgnoreCase) || !IsSubscribedPublisher)
            {
                return;
            }

            var loadId = Interlocked.Increment(ref _activeCatalogLoadId);
            _lastCatalogPublisherId = publisherId;
            var newCts = CancellationTokenSource.CreateLinkedTokenSource(_vmCts.Token);
            var replaced = Interlocked.Exchange(ref _catalogsCts, newCts);
            if (replaced != null)
            {
                await replaced.CancelAsync();
                replaced.Dispose();
            }

            await LoadAvailableCatalogsAsync(publisherId, loadId, newCts.Token);
        }

        if (!string.Equals(SelectedPublisher?.PublisherId, publisherId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await RefreshContentAsync(reloadCatalogs: false);
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (CanLoadMore && !IsLoading)
        {
            var targetPublisherId = SelectedPublisher?.PublisherId;
            if (string.IsNullOrEmpty(targetPublisherId))
            {
                return;
            }

            var attemptedPage = ++CurrentPage;
            logger.LogInformation(
                "Loading more content for {Publisher}, page {Page}",
                targetPublisherId,
                attemptedPage);
            var success = await RefreshContentAsync(append: true);
            if (!success)
            {
                if (string.Equals(SelectedPublisher?.PublisherId, targetPublisherId, StringComparison.OrdinalIgnoreCase))
                {
                    if (CurrentPage == attemptedPage && CurrentPage > 1)
                    {
                        CurrentPage--;
                    }
                }
                else
                {
                    lock (_cacheLock)
                    {
                        if (_browseCache.TryGetValue(targetPublisherId, out var cachedState) && cachedState.CurrentPage > 1)
                        {
                            cachedState.CurrentPage--;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Refreshes the content for the currently selected publisher.
    /// </summary>
    /// <param name="append">Whether to append results to the current list instead of clearing.</param>
    /// <param name="reloadCatalogs">Whether to reload subscribed catalogs.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task<bool> RefreshContentAsync(bool append = false, bool reloadCatalogs = true)
    {
        if (SelectedPublisher == null)
        {
            return false;
        }

        var publisherId = SelectedPublisher.PublisherId;
        var requestId = _activeRequestId;
        var isCustomQuery = _hasCustomQuery;

        try
        {
            IsLoading = true;
            if (!append)
            {
                PrepareContentCollectionForRefresh(publisherId, isCustomQuery);
                if (reloadCatalogs && SelectedPublisher.PublisherType.Equals(CatalogConstants.SubscribedPublisherCategory, StringComparison.OrdinalIgnoreCase))
                {
                    var oldCts = Interlocked.Exchange(ref _catalogsCts, null);
                    if (oldCts != null)
                    {
                        await oldCts.CancelAsync();
                        oldCts.Dispose();
                    }

                    if (!string.Equals(SelectedPublisher?.PublisherId, publisherId, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    var loadId = Interlocked.Increment(ref _activeCatalogLoadId);
                    _lastCatalogPublisherId = publisherId;
                    var newCts = CancellationTokenSource.CreateLinkedTokenSource(_vmCts.Token);
                    var replaced = Interlocked.Exchange(ref _catalogsCts, newCts);
                    if (replaced != null)
                    {
                        await replaced.CancelAsync();
                        replaced.Dispose();
                    }

                    if (!string.Equals(SelectedPublisher?.PublisherId, publisherId, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    _ = LoadAvailableCatalogsAsync(publisherId, loadId, newCts.Token);
                }
            }

            // Build base query
            var effectivePageSize = publisherId == PublisherTypeConstants.TheSuperHackers
                ? SuperHackersConstants.PageSize
                : PageSize;

            var baseQuery = new ContentSearchQuery
            {
                SearchTerm = SearchTerm,
                Take = effectivePageSize,
                Page = CurrentPage,
                TargetGame = ContentConstants.DefaultGameType,
                IncludeOlderVersions = string.Equals(publisherId, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase),
            };

            // Apply active filters from filter panel
            if (CurrentFilterViewModel != null)
            {
                baseQuery = CurrentFilterViewModel.ApplyFilters(baseQuery);
            }

            return await ExecuteStreamingFetchAsync(publisherId, baseQuery, requestId, isCustomQuery, append, SelectedCatalog?.Id);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Search for {Publisher} was canceled", publisherId);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh content for publisher {Publisher}", publisherId);
            return false;
        }
    }

    private void PrepareContentCollectionForRefresh(string publisherId, bool isCustomQuery)
    {
        if (isCustomQuery)
        {
            List<ContentGridItemViewModel> itemsToDispose = [];
            lock (_cacheLock)
            {
                var cachedItemSet = new HashSet<ContentGridItemViewModel>(_browseCache.Values.SelectMany(s => s.Items));
                foreach (var inFlight in _inFlightOperations.Values)
                {
                    cachedItemSet.UnionWith(inFlight.ResolvedItems);
                }

                itemsToDispose = ContentItems.Where(item => !cachedItemSet.Contains(item)).ToList();
            }

            ContentItems.Clear();
            foreach (var item in itemsToDispose)
            {
                item.Dispose();
            }
        }
        else
        {
            lock (_cacheLock)
            {
                if (_browseCache.Remove(publisherId, out var cachedState))
                {
                    cachedState.ActiveDetailViewModel?.Dispose();
                    cachedState.ActiveDetailViewModel = null;
                    foreach (var cachedItem in cachedState.Items)
                    {
                        cachedItem.Dispose();
                    }
                }

                if (_inFlightOperations.Remove(publisherId, out var oldInFlight))
                {
                    oldInFlight.Cts.Cancel();
                    oldInFlight.Cts.Dispose();
                    List<ContentGridItemViewModel> oldSnapshot = [];
                    lock (oldInFlight.SyncRoot)
                    {
                        oldSnapshot = oldInFlight.ResolvedItems.ToList();
                    }

                    foreach (var item in oldSnapshot)
                    {
                        item.Dispose();
                    }
                }

                var retainedItems = new HashSet<ContentGridItemViewModel>(_browseCache.Values.SelectMany(s => s.Items));
                foreach (var inFlight in _inFlightOperations.Values)
                {
                    var inFlightSnapshot = SnapshotInFlight(inFlight);
                    retainedItems.UnionWith(inFlightSnapshot);
                }

                var itemsToDispose = ContentItems.Where(item => !retainedItems.Contains(item)).ToList();
                ContentItems.Clear();
                foreach (var item in itemsToDispose)
                {
                    item.Dispose();
                }
            }
        }
    }

    private async Task<bool> ExecuteStreamingFetchAsync(
        string publisherId,
        ContentSearchQuery query,
        int requestId,
        bool isCustomQuery,
        bool append,
        string? catalogId = null)
    {
        var targetCatalogId = ResolveEffectiveCatalogId(publisherId, catalogId);
        var (opCts, inFlightOp) = SetupFetchOperation(publisherId, query, isCustomQuery, append, requestId, targetCatalogId);

        // Capture the token once: a concurrent refresh can dispose the CTS while this fetch
        // is still running, and re-accessing CTS.Token afterwards throws ObjectDisposedException.
        var fetchToken = opCts.Token;

        var discoverer = GetDiscovererForPublisher(publisherId);
        if (discoverer == null)
        {
            logger.LogWarning("No discoverer found for publisher {Publisher}", publisherId);
            CleanupInFlight(publisherId, inFlightOp);
            RunOnUi(() =>
            {
                if (IsCurrentActiveOperation(requestId, publisherId, inFlightOp))
                {
                    IsLoading = false;
                }
            });
            return false;
        }

        try
        {
            var result = await discoverer.DiscoverAsync(query, fetchToken);

            if (fetchToken.IsCancellationRequested)
            {
                CleanupInFlight(publisherId, inFlightOp);
                return false;
            }

            if (result.Success && result.Data != null)
            {
                await PersistRefreshedSubscriptionUrlAsync(publisherId, discoverer, fetchToken);

                var items = result.Data.Items
                    .Where(item => !query.ContentType.HasValue || item.ContentType == query.ContentType.Value)
                    .ToList();

                var effectiveCatalogId = inFlightOp?.CatalogId ?? targetCatalogId;
                await CheckAndNotifyNewDiscoveredItemsAsync(publisherId, effectiveCatalogId, items, append, isCustomQuery, fetchToken);

                var cacheKey = $"{publisherId}:{effectiveCatalogId ?? CatalogConstants.DefaultCatalogId}";
                if (!append && !isCustomQuery)
                {
                    _knownContentItemIds[cacheKey] = new HashSet<string>(items.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
                }

                var existingIds = append ? CollectExistingContentIds() : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var groups = GroupContentItemsByVariant(items);
                var newVms = await ProcessDiscoveredGroupsAsync(groups, existingIds, inFlightOp, publisherId, requestId, fetchToken);

                if (fetchToken.IsCancellationRequested || inFlightOp?.Cts.IsCancellationRequested == true || !IsCurrentActiveOperation(requestId, publisherId, inFlightOp))
                {
                    DisposeUnretainedViewModels(newVms);

                    CleanupInFlight(publisherId, inFlightOp);
                    return false;
                }

                var committed = CommitBrowseResultsToCache(publisherId, query, result.Data.HasMoreItems, isCustomQuery, append, inFlightOp, newVms, effectiveCatalogId);
                if (!committed)
                {
                    CleanupInFlight(publisherId, inFlightOp);
                    return false;
                }

                if (isCustomQuery && (_activeRequestId != requestId || SelectedPublisher?.PublisherId != publisherId))
                {
                    CleanupInFlight(publisherId, inFlightOp);
                    return false;
                }

                RunOnUi(() =>
                {
                    if (IsCurrentActiveOperation(requestId, publisherId, inFlightOp))
                    {
                        CanLoadMore = result.Data.HasMoreItems;
                        IsLoading = false;

                        // Update GitHub author options if available
                        if (string.Equals(publisherId, GitHubTopicsConstants.PublisherType, StringComparison.OrdinalIgnoreCase) &&
                            _filterViewModels.TryGetValue(publisherId, out var filterVm) &&
                            filterVm is GitHubFilterViewModel ghFilter)
                        {
                            var authors = result.Data.Items
                                .Select(i => i.AuthorName)
                                .OfType<string>()
                                .Where(a => !string.IsNullOrWhiteSpace(a))
                                .Distinct(StringComparer.OrdinalIgnoreCase);
                            ghFilter.UpdateAvailableAuthors(authors);
                        }
                    }
                });

                logger.LogInformation(
                    "Added {AddedCount} new items out of {TotalCount} fetched for {Publisher} (page {Page}). HasMoreItems: {HasMore}",
                    newVms.Count,
                    items.Count,
                    publisherId,
                    query.Page,
                    result.Data.HasMoreItems);

                return true;
            }

            CleanupInFlight(publisherId, inFlightOp);

            RunOnUi(() =>
            {
                if (IsCurrentActiveOperation(requestId, publisherId, inFlightOp))
                {
                    CanLoadMore = false;
                    IsLoading = false;

                    if (ContentItems.Count == 0)
                    {
                        if (!result.Success)
                        {
                            var errorMsg = result.FirstError ?? localizationService.GetLocalizedString("Downloads.Notification.DiscoveryConnectionHint", "Check your connection and try again.");
                            notificationService.ShowWarning(
                                localizationService.GetLocalizedString("Downloads.Notification.DiscoveryFailed.Title", "Discovery Failed"),
                                localizationService.GetLocalizedString("Downloads.Notification.DiscoveryFailed.Message", $"{publisherId} discovery failed: {errorMsg}", publisherId, errorMsg));
                        }
                        else
                        {
                            notificationService.ShowInfo(
                                localizationService.GetLocalizedString("Downloads.Notification.NoContentLoaded.Title", "No content loaded"),
                                localizationService.GetLocalizedString("Downloads.Notification.NoContentLoaded.Message", $"{publisherId} returned no content.", publisherId));
                        }
                    }
                    else if (!result.Success)
                    {
                        var errorMsg = result.FirstError ?? localizationService.GetLocalizedString("Downloads.Notification.DiscoveryConnectionHint", "Check your connection and try again.");
                        notificationService.ShowWarning(
                            localizationService.GetLocalizedString("Downloads.Notification.LoadMoreFailed.Title", "Failed to Load More"),
                            localizationService.GetLocalizedString("Downloads.Notification.LoadMoreFailed.Message", $"Could not load more content for {publisherId}: {errorMsg}", publisherId, errorMsg));
                    }
                }
            });

            logger.LogWarning("Discovery failed or returned no data for {Publisher}. Success: {Success}", publisherId, result.Success);
            return false;
        }
        catch (OperationCanceledException ex)
        {
            CleanupInFlight(publisherId, inFlightOp);
            RunOnUi(() =>
            {
                if (IsCurrentActiveOperation(requestId, publisherId, inFlightOp))
                {
                    IsLoading = false;
                }
            });
            logger.LogInformation(ex, "Streaming fetch for {Publisher} was canceled", publisherId);
            return false;
        }
        catch (Exception ex)
        {
            CleanupInFlight(publisherId, inFlightOp);
            RunOnUi(() =>
            {
                if (IsCurrentActiveOperation(requestId, publisherId, inFlightOp))
                {
                    IsLoading = false;
                }
            });
            logger.LogError(ex, "Failed to stream content for publisher {Publisher}", publisherId);
            return false;
        }
    }

    private async Task CheckAndNotifyNewDiscoveredItemsAsync(
        string publisherId,
        string? effectiveCatalogId,
        IReadOnlyList<ContentSearchResult> items,
        bool append,
        bool isCustomQuery,
        CancellationToken fetchToken)
    {
        var cacheKey = $"{publisherId}:{effectiveCatalogId ?? CatalogConstants.DefaultCatalogId}";
        if (!_knownContentItemIds.TryGetValue(cacheKey, out var prevItemIds) || append || isCustomQuery)
        {
            return;
        }

        var newDiscoveredItems = items.Where(i => !prevItemIds.Contains(i.Id)).ToList();
        if (newDiscoveredItems.Count == 0)
        {
            return;
        }

        var subResult = await subscriptionStore.GetSubscriptionAsync(publisherId, fetchToken);
        var sub = subResult.Success ? subResult.Data : null;
        if (sub?.NotifyNewReleases == false)
        {
            return;
        }

        var title = _localizationService?.GetString("Downloads.Browser.NewContentNotificationTitle") ?? "New Content Available";
        var actionText = _localizationService?.GetString("Downloads.Browser.ViewContentAction") ?? "View";
        var catalogName = AvailableCatalogs.FirstOrDefault(c => string.Equals(c.Id, effectiveCatalogId, StringComparison.OrdinalIgnoreCase))?.Name
            ?? (string.Equals(SelectedCatalog?.Id, effectiveCatalogId, StringComparison.OrdinalIgnoreCase) ? SelectedCatalog?.Name : null)
            ?? "Catalog";

        if (newDiscoveredItems.Count == 1)
        {
            var item = newDiscoveredItems[0];
            var message = string.Format(
                _localizationService?.GetString("Downloads.Browser.NewContentNotificationFormat") ?? "New content '{0}' released in catalog '{1}'",
                item.Name,
                catalogName);

            notificationService.Show(new NotificationMessage(
                NotificationType.Info,
                title,
                message,
                autoDismissMilliseconds: NotificationDurations.VeryLong,
                actionText: actionText,
                action: () => RunOnUi(() =>
                {
                    var targetVm = ContentItems.FirstOrDefault(ci => MatchesNotificationTarget(ci, item));
                    if (targetVm != null)
                    {
                        var matchingVariant = targetVm.Variants.FirstOrDefault(v =>
                            string.Equals(v.ManifestId, item.Id, StringComparison.OrdinalIgnoreCase));
                        if (matchingVariant != null)
                        {
                            targetVm.SelectedVariant = matchingVariant;
                        }

                        ViewContent(targetVm);
                    }
                })));
        }
        else
        {
            var message = string.Format(
                _localizationService?.GetString("Downloads.Browser.NewContentsNotificationFormat") ?? "{0} new content items released in catalog '{1}'",
                newDiscoveredItems.Count,
                catalogName);

            notificationService.Show(new NotificationMessage(
                NotificationType.Info,
                title,
                message,
                autoDismissMilliseconds: NotificationDurations.VeryLong,
                actionText: actionText,
                action: () => RunOnUi(() =>
                {
                    var match = ContentItems
                        .Select(ci => new
                        {
                            Card = ci,
                            Item = newDiscoveredItems.FirstOrDefault(ni => MatchesNotificationTarget(ci, ni)),
                        })
                        .FirstOrDefault(m => m.Item != null);

                    if (match?.Item is { } item)
                    {
                        var matchingVariant = match.Card.Variants.FirstOrDefault(v =>
                            string.Equals(v.ManifestId, item.Id, StringComparison.OrdinalIgnoreCase));
                        if (matchingVariant != null)
                        {
                            match.Card.SelectedVariant = matchingVariant;
                        }

                        ViewContent(match.Card);
                    }
                })));
        }
    }

    private (CancellationTokenSource Cts, PublisherInFlightOperation? InFlightOp) SetupFetchOperation(
        string publisherId,
        ContentSearchQuery query,
        bool isCustomQuery,
        bool append,
        int requestId,
        string? catalogId = null)
    {
        CancellationTokenSource opCts = _vmCts;
        PublisherInFlightOperation? inFlightOp = null;

        if (!isCustomQuery && !append)
        {
            lock (_cacheLock)
            {
                if (_inFlightOperations.TryGetValue(publisherId, out var existingOp))
                {
                    existingOp.Cts.Cancel();
                    _inFlightOperations.Remove(publisherId);
                }

                opCts = CancellationTokenSource.CreateLinkedTokenSource(_vmCts.Token);
                inFlightOp = new PublisherInFlightOperation(publisherId, query, opCts, catalogId)
                {
                    ActiveRequestId = requestId,
                };
                _inFlightOperations[publisherId] = inFlightOp;
            }
        }
        else
        {
            _searchCts?.Cancel();
            _searchCts = CancellationTokenSource.CreateLinkedTokenSource(_vmCts.Token);
            opCts = _searchCts;
            inFlightOp = new PublisherInFlightOperation(publisherId, query, opCts, catalogId)
            {
                ActiveRequestId = requestId,
            };
        }

        return (opCts, inFlightOp);
    }

    private bool IsCurrentActiveOperation(
        int requestId,
        string publisherId,
        PublisherInFlightOperation? inFlightOp = null)
    {
        if (SelectedPublisher?.PublisherId != publisherId)
        {
            return false;
        }

        return _activeRequestId == requestId || inFlightOp?.ActiveRequestId == _activeRequestId;
    }

    private HashSet<string> CollectExistingContentIds()
    {
        var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in ContentItems)
        {
            if (!string.IsNullOrEmpty(item.Id))
            {
                existingIds.Add(item.Id);
            }

            if (item.Variants != null)
            {
                foreach (var v in item.Variants)
                {
                    if (!string.IsNullOrEmpty(v.ManifestId))
                    {
                        existingIds.Add(v.ManifestId);
                    }
                }
            }
        }

        return existingIds;
    }

    private async Task<List<ContentGridItemViewModel>> ProcessDiscoveredGroupsAsync(
        List<List<ContentSearchResult>> groups,
        HashSet<string> existingIds,
        PublisherInFlightOperation? inFlightOp,
        string publisherId,
        int requestId,
        CancellationToken ct)
    {
        var newVms = new List<ContentGridItemViewModel>();

        foreach (var group in groups)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var primaryItem = ResolveDefaultVariant(group, group[0]);
            if (existingIds.Contains(primaryItem.Id))
            {
                continue;
            }

            var vm = await CreateItemViewModelAsync(group, primaryItem, ct);
            if (vm == null)
            {
                continue;
            }

            newVms.Add(vm);

            if (inFlightOp != null)
            {
                lock (inFlightOp.SyncRoot)
                {
                    inFlightOp.ResolvedItems.Add(vm);
                }
            }

            RunOnUi(() =>
            {
                if (IsCurrentActiveOperation(requestId, publisherId, inFlightOp) &&
                    ContentItems.All(existing => !string.Equals(existing.Id, vm.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    ContentItems.Add(vm);
                }
            });

            // Delay briefly to let the UI thread layout and render each card progressively one by one
            await Task.Delay(UiConstants.ProgressiveItemRenderDelayMs, ct);
        }

        RunOnUi(() =>
        {
            if (IsCurrentActiveOperation(requestId, publisherId, inFlightOp))
            {
                ReconcileReleaseUpdateStates(ContentItems);
                if (ContentItems.Any(ci => ci.IsFeatured || ci.ContentType == ContentType.ContentBundle))
                {
                    var sorted = ContentItems
                        .OrderByDescending(ci => ci.IsFeatured)
                        .ThenByDescending(ci => ci.ContentType == ContentType.ContentBundle)
                        .ToList();

                    for (var i = 0; i < sorted.Count; i++)
                    {
                        var oldIdx = ContentItems.IndexOf(sorted[i]);
                        if (oldIdx != i && oldIdx >= 0)
                        {
                            ContentItems.Move(oldIdx, i);
                        }
                    }
                }
            }
        });

        return newVms;
    }

    private string? ResolveEffectiveCatalogId(
        string publisherId,
        string? candidateCatalogId = null,
        PublisherInFlightOperation? inFlightOp = null)
    {
        var targetCatalogId = inFlightOp?.CatalogId ?? candidateCatalogId;
        if (string.IsNullOrEmpty(targetCatalogId) && string.Equals(SelectedPublisher?.PublisherId, publisherId, StringComparison.OrdinalIgnoreCase))
        {
            targetCatalogId = SelectedCatalog?.Id;
        }

        return targetCatalogId;
    }

    private bool CommitBrowseResultsToCache(
        string publisherId,
        ContentSearchQuery query,
        bool hasMoreItems,
        bool isCustomQuery,
        bool append,
        PublisherInFlightOperation? inFlightOp,
        List<ContentGridItemViewModel> newVms,
        string? catalogId = null)
    {
        if (inFlightOp != null)
        {
            if (inFlightOp.Cts.IsCancellationRequested)
            {
                DisposeUnretainedViewModels(newVms);
                return false;
            }

            inFlightOp.IsCompleted = true;
            inFlightOp.HasMoreItems = hasMoreItems;
        }

        if (isCustomQuery)
        {
            return true;
        }

        lock (_cacheLock)
        {
            // Re-check cancellation under the lock: an op cancelled and unregistered
            // after the pre-lock check must not recreate a cache entry with a stale catalog.
            if (inFlightOp != null && inFlightOp.Cts.IsCancellationRequested)
            {
                DisposeUnretainedViewModels(newVms);
                return false;
            }

            if (inFlightOp != null && _inFlightOperations.TryGetValue(publisherId, out var activeOp) && !ReferenceEquals(activeOp, inFlightOp))
            {
                DisposeUnretainedViewModels(newVms);
                return false;
            }

            var isPublisherKnown = Publishers.Any(p => string.Equals(p.PublisherId, publisherId, StringComparison.OrdinalIgnoreCase));
            if (!isPublisherKnown)
            {
                DisposeUnretainedViewModels(newVms);

                if (_inFlightOperations.TryGetValue(publisherId, out var currentOp) && ReferenceEquals(currentOp, inFlightOp))
                {
                    _inFlightOperations.Remove(publisherId);
                }

                return false;
            }

            var targetCatalogId = ResolveEffectiveCatalogId(publisherId, catalogId, inFlightOp);
            if (_browseCache.TryGetValue(publisherId, out var existingState))
            {
                if (append)
                {
                    existingState.Items.AddRange(newVms);
                    existingState.Items = existingState.Items
                        .OrderByDescending(vm => vm.IsFeatured)
                        .ThenByDescending(vm => vm.ContentType == ContentType.ContentBundle)
                        .ToList();
                    existingState.CurrentPage = query.Page ?? existingState.CurrentPage;
                    existingState.CanLoadMore = hasMoreItems;
                }
                else
                {
                    var newVmSet = new HashSet<ContentGridItemViewModel>(newVms);
                    foreach (var oldItem in existingState.Items.Where(item => !newVmSet.Contains(item)))
                    {
                        oldItem.Dispose();
                    }

                    existingState.Items.Clear();
                    var sortedNewVms = newVms
                        .OrderByDescending(vm => vm.IsFeatured)
                        .ThenByDescending(vm => vm.ContentType == ContentType.ContentBundle)
                        .ToList();
                    existingState.Items.AddRange(sortedNewVms);
                    existingState.CurrentPage = query.Page ?? 1;
                    existingState.CanLoadMore = hasMoreItems;
                }

                if (!string.IsNullOrEmpty(targetCatalogId))
                {
                    existingState.CatalogId = targetCatalogId;
                }
            }
            else
            {
                _browseCache[publisherId] = new PublisherBrowseState
                {
                    Items = [.. newVms],
                    CurrentPage = query.Page ?? 1,
                    CanLoadMore = hasMoreItems,
                    CatalogId = targetCatalogId,
                };
            }

            if (_inFlightOperations.TryGetValue(publisherId, out var currentInFlight) && ReferenceEquals(currentInFlight, inFlightOp))
            {
                _inFlightOperations.Remove(publisherId);
            }

            return true;
        }
    }

    private async Task<ContentGridItemViewModel?> CreateItemViewModelAsync(
        IReadOnlyList<ContentSearchResult> groupItems,
        ContentSearchResult primaryItem,
        CancellationToken ct)
    {
        if (groupItems.Count == 1 &&
            string.IsNullOrEmpty(primaryItem.VariantGroupId) &&
            (primaryItem.Variants == null || primaryItem.Variants.Count <= 1))
        {
            return await CreateSingletonItemViewModelAsync(primaryItem, ct);
        }

        var defaultVariant = ResolveDefaultVariant(groupItems, primaryItem);
        if (groupItems.Any(i => i.IsFeatured))
        {
            defaultVariant.IsFeatured = true;
            if (string.IsNullOrWhiteSpace(defaultVariant.FeaturedBadge))
            {
                defaultVariant.FeaturedBadge = groupItems.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.FeaturedBadge))?.FeaturedBadge;
            }
        }

        var variantVm = CreateBaseGridItemViewModel(defaultVariant);
        try
        {
            if (groupItems.Count == 1 && primaryItem.Variants is { Count: > 0 } singleVariants)
            {
                PopulateSynthesizedVariants(variantVm, primaryItem, singleVariants, logger);
            }
            else
            {
                PopulateSiblingVariants(variantVm, groupItems, defaultVariant);
            }

            SelectDefaultVariant(variantVm, groupItems, primaryItem, defaultVariant);

            await variantVm.RefreshVariantStatesAsync();
            var targetState = variantVm.SelectedVariant?.CurrentState ?? await contentStateService.GetStateAsync(defaultVariant, ct);
            if (targetState == ContentState.NotDownloaded && variantVm.Variants.Any(v => v.CurrentState is ContentState.Downloaded or ContentState.UpdateAvailable))
            {
                var downloadedVariants = variantVm.Variants
                    .Where(v => v.CurrentState is ContentState.Downloaded or ContentState.UpdateAvailable)
                    .ToList();

                var downloadedVariant = downloadedVariants.Count > 1
                    ? downloadedVariants
                        .OrderByDescending(
                            v => GetVariantVersion(variantVm, v),
                            Comparer<string>.Create(CompareVariantVersions))
                        .FirstOrDefault() ?? downloadedVariants[0]
                    : downloadedVariants.FirstOrDefault();

                if (downloadedVariant != null)
                {
                    variantVm.SelectedVariant = downloadedVariant;
                    targetState = downloadedVariant.CurrentState;
                }
            }

            variantVm.CurrentState = targetState;
            variantVm.IsDownloaded = targetState is ContentState.Downloaded or ContentState.UpdateAvailable ||
                                     variantVm.Variants.Any(v => v.CurrentState is ContentState.Downloaded or ContentState.UpdateAvailable);

            var targetItem = defaultVariant;
            if (variantVm.SelectedVariant != null &&
                !string.IsNullOrEmpty(variantVm.SelectedVariant.ManifestId) &&
                variantVm.VariantSearchResults.TryGetValue(variantVm.SelectedVariant.ManifestId, out var variantSr))
            {
                targetItem = variantSr;
            }

            if (variantVm.CurrentState == ContentState.Downloaded && (string.IsNullOrEmpty(targetItem.Id) || !ManifestIdValidator.IsValid(targetItem.Id, out _)))
            {
                var manifestId = await contentStateService.GetLocalManifestIdAsync(targetItem, ct);
                if (!string.IsNullOrEmpty(manifestId))
                {
                    targetItem.UpdateId(manifestId);
                }
            }

            variantVm.NotifyStateChanged();
            await Task.WhenAny(variantVm.EnsureIconsLoadedAsync(ct), Task.Delay(UiConstants.ProgressiveItemRenderDelayMs, ct));
            return variantVm;
        }
        catch
        {
            variantVm.Dispose();
            throw;
        }
    }

    private async Task<ContentGridItemViewModel> CreateSingletonItemViewModelAsync(ContentSearchResult primaryItem, CancellationToken ct)
    {
        var vm = CreateBaseGridItemViewModel(primaryItem);
        try
        {
            var singletonState = await contentStateService.GetStateAsync(primaryItem, ct);
            vm.CurrentState = singletonState;
            vm.IsDownloaded = singletonState is ContentState.Downloaded or ContentState.UpdateAvailable;

            if (vm.CurrentState == ContentState.Downloaded && (string.IsNullOrEmpty(primaryItem.Id) || !ManifestIdValidator.IsValid(primaryItem.Id, out _)))
            {
                var manifestId = await contentStateService.GetLocalManifestIdAsync(primaryItem, ct);
                if (!string.IsNullOrEmpty(manifestId))
                {
                    primaryItem.UpdateId(manifestId);
                }
            }

            vm.NotifyStateChanged();
            await Task.WhenAny(vm.EnsureIconsLoadedAsync(ct), Task.Delay(UiConstants.ProgressiveItemRenderDelayMs, ct));
            return vm;
        }
        catch
        {
            vm.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Updates the specified content item to its prospective newer version.
    /// </summary>
    [RelayCommand]
    private async Task<bool> UpdateContentAsync(ContentGridItemViewModel? item, CancellationToken cancellationToken = default)
    {
        if (item == null)
        {
            logger.LogWarning("UpdateContentAsync called with null item");
            return false;
        }

        if (item.IsDownloading)
        {
            return false;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_vmCts.Token, cancellationToken);
        if (item.HasBundleComponents)
        {
            try
            {
                item.IsDownloading = true;
                return await UpdateBundleComponentsAsync(item, linkedCts.Token);
            }
            finally
            {
                item.IsDownloading = false;
            }
        }

        var ct = linkedCts.Token;

        var targetItem = item.UpdateTargetVm ?? item;
        var publisherId = ResolveUpdatePublisherId(item, targetItem);

        var (reconcilerHandled, reconcilerSuccess) = await TryReconcileViaPublisherReconcilerAsync(publisherId, targetItem, ct);
        if (reconcilerHandled)
        {
            return reconcilerSuccess;
        }

        var (shouldCancelUpdate, promptResult) = await PromptForUpdateOptionAsync(targetItem);
        if (shouldCancelUpdate)
        {
            return false;
        }

        var oldManifestId = await ResolveLocalInstalledManifestIdAsync(item, ct).ConfigureAwait(false);
        var previousVariant = targetItem.SelectedVariant;

        if (ReferenceEquals(targetItem, item) && item.Variants.Count > 1)
        {
            var updateVariant = item.Variants.FirstOrDefault(v => v.CurrentState == ContentState.NotDownloaded &&
                (item.SelectedVariant == null || IsVariantUpdateCandidate(v, item.SelectedVariant)));
            if (updateVariant != null)
            {
                targetItem.SelectedVariant = updateVariant;
            }
            else
            {
                logger.LogWarning("No eligible update variant found for {Item}", item.SearchResult?.Name ?? "Item");
                return false;
            }
        }

        var downloadSuccess = await DownloadContentAsync(targetItem, ct);
        if (!downloadSuccess)
        {
            targetItem.SelectedVariant = previousVariant;
            if (ct.IsCancellationRequested ||
                string.Equals(targetItem.DownloadStatus, ContentConstants.DownloadCancelledStatusMessage, StringComparison.Ordinal))
            {
                return false;
            }

            var failedPublisherId = publisherId ?? targetItem.SearchResult?.ProviderName ?? SelectedPublisher?.PublisherId ?? DefaultPublisherName;
            _telemetryService?.TrackEvent(TelemetryConstants.Events.ContentUpdateFailed, new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.PublisherId] = failedPublisherId,
                [TelemetryConstants.Properties.ContentName] = targetItem.Name,
                [TelemetryConstants.Properties.ContentId] = oldManifestId ?? targetItem.Id,
                [TelemetryConstants.Properties.Author] = targetItem.SearchResult?.AuthorName ?? TelemetryConstants.DownloadAttribution.Unknown,
                [TelemetryConstants.Properties.ErrorMessage] = "Download failed during update",
            });
            return false;
        }

        var newManifestId = await ResolveLocalInstalledManifestIdAsync(targetItem, ct).ConfigureAwait(false);

        var reconciliationProceeded = await ApplyPostUpdateReconciliationAsync(
            targetItem,
            publisherId,
            oldManifestId,
            newManifestId,
            promptResult,
            ct);

        if (!reconciliationProceeded)
        {
            return false;
        }

        var activeNotificationService = notificationService ?? serviceProvider.GetService<INotificationService>();
        activeNotificationService?.ShowSuccess(
            localizationService.GetLocalizedString("Content.Notification.UpdateCompleted.Title", "Update Completed"),
            localizationService.GetLocalizedString("Downloads.Notification.ItemUpdated.Message", $"Updated {targetItem.Name} to latest version.", targetItem.Name),
            NotificationDurations.Medium);

        if (SelectedPublisher != null)
        {
            await RefreshAndReconcileItemsAsync(ContentItems, SelectedPublisher.PublisherId);
        }

        return true;
    }

    private string? ResolveUpdatePublisherId(ContentGridItemViewModel item, ContentGridItemViewModel targetItem)
    {
        var publisherId = item.SearchResult?.ProviderName;
        if ((string.IsNullOrEmpty(publisherId) || ContentStateService.IsGitHubPublisher(publisherId)) &&
            item.SearchResult?.ResolverMetadata != null &&
            item.SearchResult.ResolverMetadata.TryGetValue(GitHubConstants.OwnerMetadataKey, out var owner) &&
            !string.IsNullOrWhiteSpace(owner))
        {
            publisherId = owner;
        }

        return publisherId ?? targetItem.SearchResult?.ProviderName ?? SelectedPublisher?.PublisherId;
    }

    private async Task<(bool Handled, bool Success)> TryReconcileViaPublisherReconcilerAsync(
        string? publisherId,
        ContentGridItemViewModel targetItem,
        CancellationToken ct)
    {
        var registry = _reconcilerRegistry ?? serviceProvider.GetService<IPublisherReconcilerRegistry>();
        var reconciler = !string.IsNullOrEmpty(publisherId) ? registry?.GetReconciler(publisherId) : null;

        if (reconciler == null)
        {
            return (false, false);
        }

        var result = await reconciler.CheckAndReconcileIfNeededAsync(string.Empty, ct);
        if (result.Success && result.Data)
        {
            if (SelectedPublisher != null)
            {
                await RefreshAndReconcileItemsAsync(ContentItems, SelectedPublisher.PublisherId);
            }

            return (true, true);
        }

        if (!result.Success)
        {
            logger.LogWarning("Reconciler failed for {PublisherId}: {Error}", publisherId, result.FirstError);
            targetItem.DownloadStatus = $"{ContentConstants.ErrorStatusPrefix}{result.FirstError ?? ContentConstants.UpdateFailedStatusMessage}";
            return (true, false);
        }

        logger.LogInformation(
            "Reconciler reported no update work for {PublisherId}; continuing with explicit update flow",
            publisherId);
        return (false, false);
    }

    private async Task<(bool ShouldCancel, UpdateDialogResult? Result)> PromptForUpdateOptionAsync(ContentGridItemViewModel targetItem)
    {
        var dialogService = serviceProvider.GetService<IDialogService>();
        if (dialogService == null)
        {
            return (false, null);
        }

        var version = targetItem.SearchResult?.Version;
        var title = _localizationService?.GetString("Downloads.UpdateDialog.Title", targetItem.Name)
            ?? $"{targetItem.Name} Update Available";

        var message = !string.IsNullOrWhiteSpace(version)
            ? _localizationService?.GetString("Downloads.UpdateDialog.MessageWithVersion", targetItem.Name, version)
                ?? $"{targetItem.Name} has an update available ({version}).\n\nHow do you want to apply this update?"
            : _localizationService?.GetString("Downloads.UpdateDialog.Message", targetItem.Name)
                ?? $"{targetItem.Name} has an update available.\n\nHow do you want to apply this update?";

        var promptResult = await dialogService.ShowUpdateOptionDialogAsync(title, message, initialDeleteOldVersions: true);
        if (promptResult == null || string.Equals(promptResult.Action, "Skip", StringComparison.OrdinalIgnoreCase))
        {
            return (true, null);
        }

        return (false, promptResult);
    }

    private async Task<bool> ApplyPostUpdateReconciliationAsync(
        ContentGridItemViewModel targetItem,
        string? publisherId,
        string? oldManifestId,
        string? newManifestId,
        UpdateDialogResult? promptResult,
        CancellationToken ct)
    {
        var activeProfileManager = profileManager ?? serviceProvider.GetService<IGameProfileManager>();
        var reconciliationService = serviceProvider.GetService<IContentReconciliationService>();
        var manifestPool = serviceProvider.GetService<IContentManifestPool>();

        if (activeProfileManager == null || reconciliationService == null || manifestPool == null)
        {
            return true;
        }

        var targetPublisherId = publisherId ?? targetItem.SearchResult?.ProviderName ?? SelectedPublisher?.PublisherId ?? DefaultPublisherName;
        var strategy = promptResult?.Strategy ?? UpdateStrategy.CreateNewProfile;
        var shouldDelete = promptResult?.DeleteOldVersions == true;

        var fromVersion = string.Empty;
        try
        {
            var (oldManifests, newManifests, mapping) = await LoadReconciliationManifestsAsync(
                manifestPool,
                oldManifestId,
                newManifestId,
                ct);

            if (oldManifests.Count > 0)
            {
                fromVersion = oldManifests[0].Version;
            }

            var helperContext = new PublisherReconciliationContext(
                activeProfileManager,
                reconciliationService,
                notificationService,
                logger,
                targetItem.SearchResult?.ProviderName ?? DefaultPublisherName,
                "[Downloads Update]",
                localizationService);

            var updateOutcome = await PublisherReconcilerHelper.ApplyUpdateStrategyAsync(
                new UpdateStrategyExecutionArgs(
                    strategy,
                    oldManifests,
                    newManifests,
                    mapping,
                    targetItem.SearchResult?.Version ?? string.Empty,
                    shouldDelete,
                    null),
                helperContext,
                ct);

            if (updateOutcome.ShouldDeleteOldVersions && !updateOutcome.AnyFailure)
            {
                await reconciliationService.ScheduleGarbageCollectionAsync(false, ct);
            }

            var toVersion = targetItem.SearchResult?.Version ?? string.Empty;

            if (!updateOutcome.Proceed)
            {
                var error = updateOutcome.Error ?? ContentConstants.UpdateFailedStatusMessage;
                targetItem.DownloadStatus = $"{ContentConstants.ErrorStatusPrefix}{error}";
                TrackUpdateTelemetry(
                    TelemetryConstants.Events.ContentUpdateFailed,
                    targetPublisherId,
                    targetItem,
                    newManifestId,
                    fromVersion,
                    toVersion,
                    strategy.ToString(),
                    errorMessage: error);
                return false;
            }

            TrackUpdateTelemetry(
                TelemetryConstants.Events.ContentUpdateApplied,
                targetPublisherId,
                targetItem,
                newManifestId,
                fromVersion,
                toVersion,
                strategy.ToString(),
                profilesUpdated: updateOutcome.ProfilesUpdated,
                success: !updateOutcome.AnyFailure);

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to apply update strategy for {OldManifestId} -> {NewManifestId}", oldManifestId, newManifestId);
            targetItem.DownloadStatus = $"{ContentConstants.ErrorStatusPrefix}{ex.Message}";
            TrackUpdateTelemetry(
                TelemetryConstants.Events.ContentUpdateFailed,
                targetPublisherId,
                targetItem,
                newManifestId,
                fromVersion,
                targetItem.SearchResult?.Version ?? string.Empty,
                strategy.ToString(),
                errorMessage: ex.Message);
            return false;
        }
    }

    private async Task<(List<ContentManifest> OldManifests, List<ContentManifest> NewManifests, Dictionary<string, string> Mapping)> LoadReconciliationManifestsAsync(
        IContentManifestPool manifestPool,
        string? oldManifestId,
        string? newManifestId,
        CancellationToken ct)
    {
        var oldManifest = !string.IsNullOrEmpty(oldManifestId)
            ? await manifestPool.GetManifestAsync(oldManifestId, ct)
            : null;
        var newManifest = !string.IsNullOrEmpty(newManifestId)
            ? await manifestPool.GetManifestAsync(newManifestId, ct)
            : null;

        var oldManifests = oldManifest is { Success: true, Data: not null }
            ? new List<ContentManifest> { oldManifest.Data }
            : new List<ContentManifest>();
        var newManifests = newManifest is { Success: true, Data: not null }
            ? new List<ContentManifest> { newManifest.Data }
            : new List<ContentManifest>();

        var mapping = (!string.IsNullOrEmpty(oldManifestId) && !string.IsNullOrEmpty(newManifestId))
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [oldManifestId] = newManifestId }
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return (oldManifests, newManifests, mapping);
    }

    private void TrackUpdateTelemetry(
        string eventName,
        string publisherId,
        ContentGridItemViewModel targetItem,
        string? manifestId,
        string fromVersion,
        string toVersion,
        string strategy,
        string? errorMessage = null,
        int profilesUpdated = 0,
        bool success = true)
    {
        var props = new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.PublisherId] = publisherId,
            [TelemetryConstants.Properties.ContentName] = targetItem.Name,
            [TelemetryConstants.Properties.ContentId] = manifestId ?? targetItem.Id,
            [TelemetryConstants.Properties.Author] = targetItem.SearchResult?.AuthorName ?? TelemetryConstants.DownloadAttribution.Unknown,
            [TelemetryConstants.Properties.FromVersion] = fromVersion,
            [TelemetryConstants.Properties.ToVersion] = toVersion,
            [TelemetryConstants.Properties.Strategy] = strategy,
        };

        if (!string.IsNullOrEmpty(errorMessage))
        {
            props[TelemetryConstants.Properties.ErrorMessage] = errorMessage;
        }

        if (eventName == TelemetryConstants.Events.ContentUpdateApplied)
        {
            props[TelemetryConstants.Properties.ProfilesUpdated] = profilesUpdated;
            props[TelemetryConstants.Properties.Success] = success;
        }

        _telemetryService?.TrackEvent(eventName, props);
    }

    private async Task RefreshAndReconcileItemsAsync(IReadOnlyList<ContentGridItemViewModel> items, string publisherId)
    {
        if (_disposed || _vmCts.IsCancellationRequested)
        {
            return;
        }

        using var throttler = new SemaphoreSlim(4);
        var tasks = items.Select(async item =>
        {
            if (_disposed || _vmCts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await throttler.WaitAsync(_vmCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                item.ClearInactiveDownloadStatus();
                try
                {
                    await item.RefreshVariantStatesAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to refresh variant states for item {Id}", item.Id);
                }
            }
            finally
            {
                throttler.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        RunOnUi(() =>
        {
            if (!_disposed && !_vmCts.IsCancellationRequested && SelectedPublisher?.PublisherId == publisherId)
            {
                foreach (var item in items)
                {
                    _ = item.EnsureIconsLoadedAsync(_vmCts.Token);
                }

                ReconcileReleaseUpdateStates(ContentItems);
            }
        });
    }

    private ContentGridItemViewModel CreateBaseGridItemViewModel(ContentSearchResult item)
    {
        var vm = new ContentGridItemViewModel(
            item,
            contentStateService,
            loggerFactory.CreateLogger<ContentGridItemViewModel>(),
            _downloadCoordinator,
            _localizationService)
        {
            ViewCommand = ViewContentCommand,
            DownloadCommand = DownloadContentCommand,
            AddToProfileCommand = AddContentToProfileCommand,
            UpdateCommand = UpdateContentCommand,
            InstallBuildCommand = InstallBuildContentCommand,
        };

        vm.Initialize();
        return vm;
    }

    /// <summary>
    /// Persists a catalog URL refreshed from the publisher definition during discovery,
    /// so publisher renames and hosting moves survive the next subscription sync.
    /// </summary>
    /// <param name="publisherId">The publisher ID.</param>
    /// <param name="discoverer">The discoverer that ran the fetch.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    private async Task PersistRefreshedSubscriptionUrlAsync(
        string publisherId,
        IContentDiscoverer discoverer,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (discoverer is not GenericCatalogDiscoverer genericCatalogDiscoverer)
        {
            return;
        }

        var refreshedCatalogUrl = genericCatalogDiscoverer.TakeRefreshedCatalogUrl();
        var refreshedAvatarUrl = genericCatalogDiscoverer.TakeRefreshedAvatarUrl();
        if (string.IsNullOrWhiteSpace(refreshedCatalogUrl) && string.IsNullOrWhiteSpace(refreshedAvatarUrl))
        {
            return;
        }

        try
        {
            var storedResult = await subscriptionStore.GetSubscriptionAsync(publisherId, cancellationToken);
            if (!storedResult.Success || storedResult.Data == null)
            {
                return;
            }

            bool changed = false;
            if (!string.IsNullOrWhiteSpace(refreshedCatalogUrl) &&
                !string.Equals(storedResult.Data.CatalogUrl, refreshedCatalogUrl, StringComparison.OrdinalIgnoreCase))
            {
                storedResult.Data.CatalogUrl = refreshedCatalogUrl;
                storedResult.Data.CachedCatalogHash = null;
                storedResult.Data.LastFetched = null;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(refreshedAvatarUrl) &&
                !string.Equals(storedResult.Data.AvatarUrl, refreshedAvatarUrl, StringComparison.OrdinalIgnoreCase))
            {
                storedResult.Data.AvatarUrl = refreshedAvatarUrl;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            var updateResult = await subscriptionStore.UpdateSubscriptionAsync(storedResult.Data, cancellationToken);
            if (updateResult.Success)
            {
                logger.LogInformation("Persisted refreshed subscription data for {PublisherId}", publisherId);
                if (!string.IsNullOrWhiteSpace(refreshedAvatarUrl))
                {
                    RunOnUi(() =>
                    {
                        var existing = Publishers.FirstOrDefault(p =>
                            p.PublisherId.Equals(publisherId, StringComparison.OrdinalIgnoreCase));
                        if (existing != null)
                        {
                            existing.LogoSource = refreshedAvatarUrl;
                        }
                    });
                }

                WeakReferenceMessenger.Default.Send(new PublisherSubscriptionsChangedMessage(publisherId));
            }
            else
            {
                logger.LogWarning(
                    "Failed to persist refreshed subscription data for {PublisherId}: {Errors}",
                    publisherId,
                    string.Join("; ", updateResult.Errors));
            }
        }
        catch (OperationCanceledException)
        {
            // Best-effort persistence; results were already delivered.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist refreshed subscription data for {PublisherId}", publisherId);
        }
    }

    /// <returns>The discoverer for the specified publisher, or null if not found.</returns>
    private IContentDiscoverer? GetDiscovererForPublisher(string publisherId)
    {
        return publisherId switch
        {
            PublisherTypeConstants.Downloaded => contentDiscoverers.OfType<DownloadedContentDiscoverer>().FirstOrDefault(),
            PublisherTypeConstants.GeneralsOnline => contentDiscoverers.OfType<GeneralsOnlineDiscoverer>().FirstOrDefault(),
            PublisherTypeConstants.TheSuperHackers => contentDiscoverers.OfType<GenHub.Features.Content.Services.GitHub.GitHubReleasesDiscoverer>().FirstOrDefault(),
            CommunityOutpostConstants.PublisherType => contentDiscoverers.OfType<GenHub.Features.Content.Services.CommunityOutpost.CommunityOutpostDiscoverer>().FirstOrDefault(),
            PublisherTypeConstants.GenLauncher => contentDiscoverers.OfType<GenHub.Features.Content.Services.GenLauncher.GenLauncherDiscoverer>().FirstOrDefault(),
            GitHubTopicsConstants.PublisherType => contentDiscoverers.OfType<GenHub.Features.Content.Services.ContentDiscoverers.GitHubTopicsDiscoverer>().FirstOrDefault(),
            CNCLabsConstants.PublisherType => contentDiscoverers.OfType<CNCLabsMapDiscoverer>().FirstOrDefault(),
            AODMapsConstants.PublisherType => contentDiscoverers.OfType<AODMapsDiscoverer>().FirstOrDefault(),
            ModDBConstants.PublisherType => contentDiscoverers.OfType<ModDBDiscoverer>().FirstOrDefault(),
            SteamWorkshopConstants.PublisherType => contentDiscoverers.OfType<SteamWorkshopDiscoverer>().FirstOrDefault(),

            // User-subscribed GenHub catalogs (and later definition-resolved endpoints)
            _ => _subscribedDiscoverers.TryGetValue(publisherId, out var subscribed) ? subscribed : null,
        };
    }

    [RelayCommand]
    private void ViewContent(ContentGridItemViewModel item)
    {
        if (item?.SearchResult != null)
        {
            var contentLogger = serviceProvider.GetService(typeof(ILogger<ContentDetailViewModel>)) as ILogger<ContentDetailViewModel>
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ContentDetailViewModel>.Instance;

            var parsers = (serviceProvider.GetService(typeof(IEnumerable<IWebPageParser>)) as IEnumerable<IWebPageParser> ?? []).ToList();
            var tabProviderRegistry = serviceProvider.GetService(typeof(ITabProviderRegistry)) as ITabProviderRegistry
                ?? throw new InvalidOperationException("ITabProviderRegistry not registered");
            var coordinator = _downloadCoordinator ?? serviceProvider.GetRequiredService<IContentDownloadCoordinator>();
            var manifestPool = serviceProvider.GetRequiredService<IContentManifestPool>();
            var dialogService = serviceProvider.GetService(typeof(IDialogService)) as IDialogService;

            var selectedVariantId = item.SelectedVariant?.ManifestId ?? item.SelectedVariant?.Name;

            // Pass a snapshot: the card keeps swapping its live result on variant changes and
            // the detail view rewrites IDs during downloads. Sharing one instance corrupts the
            // Releases tab (parsed page data lost) and the card's selected variant.
            var vm = new ContentDetailViewModel(
                VariantSwap.Clone(item.SearchResult),
                parsers,
                profileContentService,
                profileManager,
                notificationService,
                tabProviderRegistry,
                contentStateService,
                coordinator,
                manifestPool,
                loggerFactory,
                contentLogger,
                CloseDetail,
                item.VariantSearchResults,
                updateTargetSearchResult: item.UpdateTargetVm?.SearchResult,
                updateAction: ct => UpdateContentAsync(item, ct),
                isUpdateAvailable: item.CurrentState == ContentState.UpdateAvailable,
                initialVariantManifestId: selectedVariantId,
                localizationService: serviceProvider.GetService(typeof(ILocalizationService)) as ILocalizationService,
                dialogService: dialogService,
                deletedAction: OnContentDeletedAsync,
                artworkService: serviceProvider.GetService<IContentArtworkService>(),
                gitHubApiClient: serviceProvider.GetService(typeof(IGitHubApiClient)) as IGitHubApiClient,
                workspaceManager: serviceProvider.GetService<IWorkspaceManager>(),
                patchNotesService: serviceProvider.GetService(typeof(IGeneralsOnlinePatchNotesService)) as IGeneralsOnlinePatchNotesService,
                userSettingsService: serviceProvider.GetService<IUserSettingsService>(),
                onDescriptionEnriched: (version, desc) =>
                {
                    if (GeneralsOnlinePatchNotesHelper.VersionsMatch(item.SearchResult.Version, version))
                    {
                        item.UpdateDescription(desc);
                    }
                },
                installBuildAction: (manifestId, name, ct) => InstallBuildContentByIdAsync(manifestId ?? item.Id, name ?? item.Name, ct));

            if (item.HasBundleComponents)
            {
                vm.AttachBundleComponents(item.BundleComponents);
            }

            if (item.IsDownloading)
            {
                vm.IsDownloading = true;
                vm.DownloadProgress = item.DownloadProgress;
                vm.DownloadStatusMessage = item.DownloadStatus;
            }

            vm.Initialize();

            if (!string.IsNullOrEmpty(selectedVariantId))
            {
                vm.SelectVariantByManifestId(selectedVariantId);
            }

            SelectedContent?.Dispose();
            SelectedContent = vm;
            if (!string.IsNullOrEmpty(_lastPopulatedPublisherId))
            {
                lock (_cacheLock)
                {
                    if (_browseCache.TryGetValue(_lastPopulatedPublisherId, out var state))
                    {
                        state.ActiveDetailViewModel = vm;
                    }
                }
            }
        }
    }

    [RelayCommand]
    private void CloseDetail()
    {
        var viewedSearchResult = SelectedContent?.SearchResult;
        var selectedVariantId = SelectedContent?.SelectedVariant?.ManifestId ?? viewedSearchResult?.Id;

        if (!string.IsNullOrEmpty(_lastPopulatedPublisherId))
        {
            lock (_cacheLock)
            {
                if (_browseCache.TryGetValue(_lastPopulatedPublisherId, out var state))
                {
                    state.ActiveDetailViewModel = null;
                }
            }
        }

        SelectedContent?.Dispose();
        SelectedContent = null;

        if (viewedSearchResult != null)
        {
            var match = ContentItems.FirstOrDefault(i =>
                ReferenceEquals(i.SearchResult, viewedSearchResult) ||
                i.SearchResult.Id == viewedSearchResult.Id ||
                (!string.IsNullOrEmpty(i.SearchResult?.VariantGroupId) && !string.IsNullOrEmpty(viewedSearchResult?.VariantGroupId) &&
                 string.Equals(i.SearchResult.VariantGroupId, viewedSearchResult.VariantGroupId, StringComparison.OrdinalIgnoreCase)) ||
                (i.VariantSearchResults != null && !string.IsNullOrEmpty(selectedVariantId) &&
                 i.VariantSearchResults.ContainsKey(selectedVariantId)));

            if (match != null && !string.IsNullOrWhiteSpace(selectedVariantId))
            {
                match.SelectVariantByManifestId(selectedVariantId);
            }

            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        if (match != null)
                        {
                            var state = await contentStateService.GetStateAsync(match.SearchResult, _vmCts.Token);
                            var isDownloaded = state is ContentState.Downloaded or ContentState.UpdateAvailable;
                            if (state == ContentState.Downloaded && (string.IsNullOrEmpty(match.SearchResult.Id) || !ManifestIdValidator.IsValid(match.SearchResult.Id, out _)))
                            {
                                var manifestId = await contentStateService.GetLocalManifestIdAsync(match.SearchResult, _vmCts.Token);
                                if (!string.IsNullOrEmpty(manifestId))
                                {
                                    match.SearchResult.UpdateId(manifestId);
                                }
                            }

                            await match.RefreshVariantStatesAsync().ConfigureAwait(false);
                            RunOnUi(() =>
                            {
                                match.CurrentState = state;
                                match.IsDownloaded = isDownloaded;
                                match.NotifyStateChanged();
                                ReconcileReleaseUpdateStates(ContentItems);
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to refresh grid item state after closing detail");
                    }
                },
                _vmCts.Token);
        }
    }

    /// <summary>
    /// Syncs sidebar + discoverer map with <c>subscriptions.json</c> (catalog-direct today).
    /// </summary>
    private async Task RefreshSubscribedPublishersAsync()
    {
        var refreshVersion = Interlocked.Increment(ref _subscriptionRefreshVersion);
        try
        {
            var result = await subscriptionStore.GetSubscriptionsAsync(_vmCts.Token);
            if (!result.Success || result.Data == null)
            {
                logger.LogWarning(
                    "Could not load publisher subscriptions: {Errors}",
                    string.Join("; ", result.Errors));
                return;
            }

            void ApplySubscriptions()
            {
                if (_disposed || refreshVersion != Volatile.Read(ref _subscriptionRefreshVersion))
                {
                    return;
                }

                var subscriptions = result.Data.ToList();
                var subscribedIds = new HashSet<string>(subscriptions.Select(s => s.PublisherId), StringComparer.OrdinalIgnoreCase);
                var removed = Publishers.Where(p =>
                    p.PublisherType.Equals(CatalogConstants.SubscribedPublisherCategory, StringComparison.OrdinalIgnoreCase)
                    && !subscribedIds.Contains(p.PublisherId)).Select(p => p.PublisherId).ToList();
                foreach (var publisherId in removed)
                {
                    RemoveSubscribedPublisher(publisherId);
                }

                foreach (var subscription in subscriptions)
                {
                    if (string.IsNullOrWhiteSpace(subscription.PublisherId)
                        || string.IsNullOrWhiteSpace(subscription.CatalogUrl))
                    {
                        continue;
                    }

                    var existing = Publishers.FirstOrDefault(p =>
                        p.PublisherId.Equals(subscription.PublisherId, StringComparison.OrdinalIgnoreCase));

                    if (existing == null)
                    {
                        Publishers.Add(new PublisherItemViewModel(
                            subscription.PublisherId,
                            subscription.PublisherName,
                            subscription.AvatarUrl,
                            CatalogConstants.SubscribedPublisherCategory));
                    }
                    else
                    {
                        existing.DisplayName = subscription.PublisherName;
                        existing.LogoSource = subscription.AvatarUrl;
                    }

                    // Transient discoverer configured for this catalog URL (generic GenHub schema)
                    var discoverer = serviceProvider.GetRequiredService<GenericCatalogDiscoverer>();
                    discoverer.Configure(subscription);
                    _subscribedDiscoverers[subscription.PublisherId] = discoverer;

                    if (_browseCache.Remove(subscription.PublisherId, out var oldState))
                    {
                        var isCurrentlySelected = string.Equals(SelectedPublisher?.PublisherId, subscription.PublisherId, StringComparison.OrdinalIgnoreCase);
                        if (!isCurrentlySelected)
                        {
                            oldState.ActiveDetailViewModel?.Dispose();
                            oldState.ActiveDetailViewModel = null;
                            foreach (var oldVm in oldState.Items)
                            {
                                oldVm.Dispose();
                            }
                        }
                        else
                        {
                            var activeItemSet = new HashSet<ContentGridItemViewModel>(ContentItems);
                            foreach (var oldVm in oldState.Items.Where(oldVm => !activeItemSet.Contains(oldVm)))
                            {
                                oldVm.Dispose();
                            }
                        }
                    }
                }

                logger.LogDebug(
                    "Synced {Count} subscribed publisher(s) into Downloads sidebar",
                    subscriptions.Count);
            }

            if (Avalonia.Application.Current == null || Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                ApplySubscriptions();
            }
            else
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(ApplySubscriptions);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh subscribed publishers");
        }
    }

    private void InitializeFilterViewModels()
    {
        // Offline downloaded-content library filters (content type + game)
        _filterViewModels[PublisherTypeConstants.Downloaded] = new DownloadedContentFilterViewModel(_localizationService);

        // Dynamic publisher filters
        _filterViewModels[GitHubTopicsConstants.PublisherType] = new GitHubFilterViewModel();
        _filterViewModels[CNCLabsConstants.PublisherType] = new CNCLabsFilterViewModel();
        _filterViewModels[AODMapsConstants.PublisherType] = new AODMapsFilterViewModel();
        _filterViewModels[ModDBConstants.PublisherType] = new ModDBFilterViewModel();
        _filterViewModels[SteamWorkshopConstants.PublisherType] = new SteamWorkshopFilterViewModel(_localizationService);
        _filterViewModels[PublisherTypeConstants.GenLauncher] = new StaticPublisherFilterViewModel(PublisherTypeConstants.GenLauncher)
        {
            LocalizationService = _localizationService,
        };
    }

    [RelayCommand]
    private async Task<bool> DownloadContentAsync(ContentGridItemViewModel item, CancellationToken cancellationToken = default)
    {
        if (item == null || item.IsDownloading)
        {
            return false;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _vmCts.Token);
        var effectiveToken = linkedCts.Token;

        try
        {
            item.IsDownloading = true;
            item.DownloadProgress = 0;
            item.DownloadStatus = ContentConstants.StartingDownloadStatusMessage;

            if (item.HasBundleComponents)
            {
                await DownloadBundleComponentsAsync(item, effectiveToken);
                return item.AreBundleComponentsReadyForProfile;
            }

            logger.LogInformation("Starting download for content: {Name} ({Provider})", item.Name, item.ProviderName);

            // Use the ContentOrchestrator to properly acquire content
            // This handles ZIP extraction, manifest factory processing, and proper file storage
            var progress = new Progress<ContentAcquisitionProgress>(p =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    // Progress is posted asynchronously. Ignore callbacks queued before a
                    // completed or failed acquisition cleared the active download state.
                    if (!item.IsDownloading)
                    {
                        return;
                    }

                    item.DownloadProgress = (int)p.ProgressPercentage;
                    item.DownloadStatus = p.FormatProgressStatus();
                });
            });

            // Terminal toasts are owned by the coordinator (or the fallback scope below),
            // and the grid only mirrors inline progress so a download never toasts twice.
            var result = _downloadCoordinator != null
                ? await _downloadCoordinator.DownloadContentAsync(item.SearchResult, progress, effectiveToken)
                : await AcquireWithNotificationsAsync(item.SearchResult, progress, item.Name, effectiveToken);

            if (result.Success && result.Data != null)
            {
                await HandleSuccessfulAcquisitionAsync(item, result.Data);
                return true;
            }

            var errorMsg = result.FirstError ?? "Unknown error";
            logger.LogError("Failed to download {ItemName}: {Error}", item.Name, errorMsg);
            item.DownloadStatus = $"{ContentConstants.ErrorStatusPrefix}{errorMsg}";
            return false;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Download cancelled for: {Name}", item.Name);
            item.DownloadStatus = ContentConstants.DownloadCancelledStatusMessage;
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error downloading content: {Name}", item.Name);
            item.DownloadStatus = $"{ContentConstants.ErrorStatusPrefix}{ex.Message}";
            return false;
        }
        finally
        {
            item.IsDownloading = false;
            if (item.IsDownloaded)
            {
                item.ClearInactiveDownloadStatus();
            }
        }
    }

    private async Task HandleSuccessfulAcquisitionAsync(ContentGridItemViewModel item, ContentManifest manifest)
    {
        logger.LogInformation("Successfully downloaded and stored content: {ManifestId}", manifest.Id.Value);

        item.DownloadProgress = 100;
        item.DownloadStatus = string.Empty;

        // Remember the pre-download catalog ID before rewriting SearchResult.Id so
        // variant dropdown matching and ContentStateService session maps stay keyed
        // by the stable catalog identity (parity with ContentDownloadCoordinator).
        var originalContentId = item.SearchResult.Id ?? string.Empty;
        if (item.SelectedVariant != null && !string.IsNullOrEmpty(item.SelectedVariant.ManifestId))
        {
            originalContentId = item.SelectedVariant.ManifestId;
        }

        item.SearchResult.UpdateId(manifest.Id.Value);
        item.MarkVariantDownloaded(originalContentId, manifest.Id.Value);

        // Update the item's state to Downloaded so the UI switches from "Download" to "Add to Profile"
        item.CurrentState = ContentState.Downloaded;
        item.IsDownloaded = true;

        var moddbId = item.SearchResult.GetModDbId();

        // Notify ContentStateService that state has changed (catalog ID + manifest ID)
        contentStateService.NotifyStateChanged(originalContentId, ContentState.Downloaded, manifest.Id.Value, moddbId);

        // Re-read every sibling so checkmarks stay accurate if acquisition produced
        // a different on-disk identity than the catalog key (e.g. SuperHackers).
        await item.RefreshVariantStatesAsync();
        RunOnUi(() => ReconcileReleaseUpdateStates(ContentItems));

        if (_downloadCoordinator == null)
        {
            try
            {
                var message = new ContentAcquiredMessage(manifest);
                WeakReferenceMessenger.Default.Send(message);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send ContentAcquiredMessage");
            }
        }
    }

    /// <summary>
    /// Acquires content directly through the orchestrator with the standard notification
    /// lifecycle. Used only when no download coordinator is available.
    /// </summary>
    private async Task<OperationResult<ContentManifest>> AcquireWithNotificationsAsync(
        ContentSearchResult searchResult,
        IProgress<ContentAcquisitionProgress> progress,
        string contentName,
        CancellationToken cancellationToken)
    {
        var localization = serviceProvider.GetService<ILocalizationService>();
        using var scope = new DownloadNotificationScope(notificationService, contentName, null, progress, localization);
        try
        {
            var result = await contentOrchestrator.AcquireContentAsync(searchResult, scope, cancellationToken);
            if (result.Success && result.Data != null)
            {
                scope.CompleteSuccess();
            }
            else
            {
                scope.CompleteFailure(result.FirstError);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            scope.CompleteCanceled();
            throw;
        }
        catch (Exception ex)
        {
            scope.CompleteFailure(ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Resolves the user-facing status shown when only the selected bundle members
    /// downloaded. Falls back to English when localization is unavailable.
    /// </summary>
    private string GetDownloadedSelectedContentStatus() =>
        _localizationService?.GetString("Downloads.Card.DownloadedSelectedContent") ?? "Downloaded selected content";

    /// <summary>
    /// Acquires every required bundle member that is not yet downloaded for the current selection.
    /// </summary>
    private async Task DownloadBundleComponentsAsync(
        ContentGridItemViewModel item,
        CancellationToken cancellationToken)
    {
        var targets = BundleComponentViewModel.GetRequiredDownloadTargets(item.BundleComponents);
        if (targets.Count == 0)
        {
            item.DownloadStatus = ContentConstants.AllSelectedContentLoadedStatusMessage;
            await item.RefreshBundleComponentStatesAsync();
            return;
        }

        logger.LogInformation(
            "Downloading {Count} missing bundle member(s) for {Name}",
            targets.Count,
            item.Name);

        var (acquired, success) = await AcquireBundleMemberTargetsAsync(item, targets, cancellationToken);
        if (acquired.Count > 0)
        {
            await item.RefreshBundleComponentStatesAsync();
        }

        if (!success)
        {
            return;
        }

        item.DownloadProgress = 100;
        item.DownloadStatus = item.AreBundleComponentsReadyForProfile
            ? ContentConstants.DownloadCompleteStatusMessage
            : GetDownloadedSelectedContentStatus();
    }

    /// <summary>
    /// Updates every acquired bundle member that reports a newer version, asking first via
    /// the same update dialog used for single items and reconciling game profiles with the
    /// old-to-new member manifest mapping afterwards.
    /// </summary>
    /// <param name="item">The bundle card to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when all outdated members were updated; otherwise false.</returns>
    private async Task<bool> UpdateBundleComponentsAsync(
        ContentGridItemViewModel item,
        CancellationToken cancellationToken)
    {
        var targets = BundleComponentViewModel.GetRequiredUpdateTargets(item.BundleComponents);
        if (targets.Count == 0)
        {
            await item.RefreshBundleComponentStatesAsync();
            return true;
        }

        logger.LogInformation(
            "Updating {Count} outdated bundle member(s) for {Name}",
            targets.Count,
            item.Name);

        var dialogService = serviceProvider.GetService<IDialogService>();
        UpdateDialogResult? promptResult = null;
        if (dialogService != null)
        {
            var title = _localizationService?.GetString("Downloads.UpdateDialog.Title", item.Name)
                ?? $"{item.Name} Update Available";
            var message = _localizationService?.GetString("Downloads.UpdateDialog.BundleMessage", item.Name, targets.Count)
                ?? $"{item.Name} has {targets.Count} bundle member(s) with updates available.\n\nHow do you want to apply these updates?";
            promptResult = await dialogService.ShowUpdateOptionDialogAsync(title, message, initialDeleteOldVersions: true);

            if (promptResult == null || string.Equals(promptResult.Action, "Skip", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var (acquired, success) = await AcquireBundleMemberTargetsAsync(item, targets, cancellationToken);
        if (acquired.Count > 0)
        {
            await item.RefreshBundleComponentStatesAsync();
            var updateOutcome = await ReconcileBundleMemberUpdatesAsync(item, acquired, promptResult, cancellationToken);
            if (updateOutcome.HasValue && (!updateOutcome.Value.Proceed || updateOutcome.Value.AnyFailure))
            {
                var errorMessage = updateOutcome.Value.Error ?? ContentConstants.UpdateFailedStatusMessage;
                item.DownloadStatus = $"{ContentConstants.ErrorStatusPrefix}{errorMessage}";
                var targetPublisherId = item.SearchResult?.ProviderName ?? SelectedPublisher?.PublisherId ?? DefaultPublisherName;
                _telemetryService?.TrackEvent(TelemetryConstants.Events.ContentUpdateFailed, new Dictionary<string, object?>
                {
                    [TelemetryConstants.Properties.PublisherId] = targetPublisherId,
                    [TelemetryConstants.Properties.ContentName] = item.Name,
                    [TelemetryConstants.Properties.ContentId] = item.Id,
                    [TelemetryConstants.Properties.Author] = item.SearchResult?.AuthorName ?? TelemetryConstants.DownloadAttribution.Unknown,
                    [TelemetryConstants.Properties.ToVersion] = item.SearchResult?.Version ?? string.Empty,
                    [TelemetryConstants.Properties.Strategy] = promptResult?.Strategy.ToString() ?? UpdateStrategy.CreateNewProfile.ToString(),
                    [TelemetryConstants.Properties.ErrorMessage] = errorMessage,
                });
                return false;
            }
        }

        if (!success)
        {
            return false;
        }

        item.DownloadProgress = 100;
        item.DownloadStatus = item.AreBundleComponentsReadyForProfile
            ? ContentConstants.DownloadCompleteStatusMessage
            : GetDownloadedSelectedContentStatus();

        var activeNotificationService = notificationService ?? serviceProvider.GetService<INotificationService>();
        activeNotificationService?.ShowSuccess(
            _localizationService?.GetLocalizedString("Content.Notification.UpdateCompleted.Title", "Update Completed") ?? "Update Completed",
            _localizationService?.GetLocalizedString("Downloads.Notification.ItemUpdated.Message", $"Updated {item.Name} to latest version.", item.Name) ?? $"Updated {item.Name} to latest version.",
            NotificationDurations.Medium);

        if (SelectedPublisher != null)
        {
            await RefreshAndReconcileItemsAsync(ContentItems, SelectedPublisher.PublisherId);
        }

        return true;
    }

    /// <summary>
    /// Downloads one bundle member target per entry with aggregated progress and a single
    /// terminal notification. Captures each member's pre-download manifest so callers can
    /// reconcile profiles afterwards.
    /// </summary>
    /// <param name="item">The bundle card owning the members.</param>
    /// <param name="targets">Member search results to acquire in order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The per-member acquisitions and whether all targets succeeded.</returns>
    private async Task<(IReadOnlyList<(ContentSearchResult Target, string OriginalContentId, string? OldManifestId, string NewManifestId)> Acquired, bool Success)> AcquireBundleMemberTargetsAsync(
        ContentGridItemViewModel item,
        IReadOnlyList<ContentSearchResult> targets,
        CancellationToken cancellationToken)
    {
        // One aggregated notification covers every member so a bundle never toasts per member.
        var localization = serviceProvider.GetService<ILocalizationService>();
        using var scope = new DownloadNotificationScope(notificationService, item.Name, localization: localization);

        var acquired = new List<(ContentSearchResult Target, string OriginalContentId, string? OldManifestId, string NewManifestId)>();
        var completed = 0;
        try
        {
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                item.DownloadStatus = $"{ContentConstants.DownloadingStatusPrefix}{target.Name} ({completed + 1}/{targets.Count})...";
                item.DownloadProgress = (int)(completed * 100.0 / targets.Count);

                var progress = new Progress<ContentAcquisitionProgress>(p =>
                {
                    var slice = 100.0 / targets.Count;
                    var overall = (completed * slice) + (p.ProgressPercentage * slice / 100.0);
                    scope.ReportFraction(overall / 100.0, $"{target.Name}: {p.FormatProgressStatus()}");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!item.IsDownloading)
                        {
                            return;
                        }

                        item.DownloadProgress = (int)overall;
                        item.DownloadStatus = $"{target.Name}: {p.FormatProgressStatus()}";
                    });
                });

                var originalContentId = target.Id ?? string.Empty;
                var oldManifestId = await contentStateService.GetLocalManifestIdAsync(target, cancellationToken);
                var result = _downloadCoordinator != null
                    ? await _downloadCoordinator.DownloadContentAsync(target, progress, cancellationToken, suppressNotifications: true)
                    : await contentOrchestrator.AcquireContentAsync(target, progress, cancellationToken);
                if (!result.Success || result.Data == null)
                {
                    var errorMsg = result.FirstError ?? "Unknown error";
                    logger.LogError("Failed to download bundle member {ItemName}: {Error}", target.Name, errorMsg);
                    item.DownloadStatus = $"{ContentConstants.ErrorStatusPrefix}{errorMsg}";
                    scope.CompleteFailure(errorMsg);
                    return (acquired, false);
                }

                target.UpdateId(result.Data.Id.Value);
                foreach (var component in item.BundleComponents)
                {
                    component.MarkDownloaded(originalContentId, result.Data.Id.Value);
                }

                var moddbId = target.GetModDbId();

                contentStateService.NotifyStateChanged(originalContentId, ContentState.Downloaded, result.Data.Id.Value, moddbId);
                acquired.Add((target, originalContentId, oldManifestId, result.Data.Id.Value));
                completed++;
            }
        }
        catch (OperationCanceledException)
        {
            scope.CompleteCanceled();
            throw;
        }

        if (item.AreBundleComponentsReadyForProfile)
        {
            scope.CompleteSuccess();
        }

        return (acquired, true);
    }

    /// <summary>
    /// Reconciles game profiles with the old-to-new manifest mapping of updated bundle members.
    /// </summary>
    /// <param name="item">The bundle card that was updated.</param>
    /// <param name="acquired">Per-member acquisitions from the update run.</param>
    /// <param name="promptResult">The update dialog outcome, if the user was prompted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The strategy execution outcome, or null if reconciliation was not performed.</returns>
    private async Task<(bool Proceed, string? Error, int ProfilesUpdated, bool AnyFailure, bool ShouldDeleteOldVersions, string? TargetProfileId)?> ReconcileBundleMemberUpdatesAsync(
        ContentGridItemViewModel item,
        IReadOnlyList<(ContentSearchResult Target, string OriginalContentId, string? OldManifestId, string NewManifestId)> acquired,
        UpdateDialogResult? promptResult,
        CancellationToken cancellationToken)
    {
        var activeProfileManager = profileManager ?? serviceProvider.GetService<IGameProfileManager>();
        var reconciliationService = serviceProvider.GetService<IContentReconciliationService>();
        var manifestPool = serviceProvider.GetService<IContentManifestPool>();

        if (activeProfileManager == null || reconciliationService == null || manifestPool == null)
        {
            return null;
        }

        try
        {
            var oldManifests = new List<ContentManifest>();
            var newManifests = new List<ContentManifest>();
            var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in acquired)
            {
                if (string.IsNullOrEmpty(entry.OldManifestId) ||
                    string.Equals(entry.OldManifestId, entry.NewManifestId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var oldManifest = await manifestPool.GetManifestAsync(entry.OldManifestId, cancellationToken);
                var newManifest = await manifestPool.GetManifestAsync(entry.NewManifestId, cancellationToken);
                if (oldManifest?.Success == true && oldManifest.Data != null &&
                    newManifest?.Success == true && newManifest.Data != null)
                {
                    oldManifests.Add(oldManifest.Data);
                    newManifests.Add(newManifest.Data);
                    mapping[entry.OldManifestId] = entry.NewManifestId;
                }
            }

            if (newManifests.Count == 0)
            {
                return null;
            }

            var strategy = promptResult?.Strategy ?? UpdateStrategy.CreateNewProfile;
            var shouldDelete = promptResult?.DeleteOldVersions == true;

            var helperContext = new PublisherReconciliationContext(
                activeProfileManager,
                reconciliationService,
                notificationService,
                logger,
                item.SearchResult?.ProviderName ?? DefaultPublisherName,
                "[Downloads Bundle Update]",
                localizationService);

            var updateOutcome = await PublisherReconcilerHelper.ApplyUpdateStrategyAsync(
                new UpdateStrategyExecutionArgs(
                    strategy,
                    oldManifests,
                    newManifests,
                    mapping,
                    item.SearchResult?.Version ?? string.Empty,
                    shouldDelete,
                    null),
                helperContext,
                cancellationToken);

            if (updateOutcome.ShouldDeleteOldVersions && !updateOutcome.AnyFailure)
            {
                await reconciliationService.ScheduleGarbageCollectionAsync(false, cancellationToken);
            }

            return updateOutcome;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to apply bundle update strategy for {Name}", item.Name);
            return (false, ex.Message, 0, true, false, null);
        }
    }

    /// <summary>
    /// Installs a downloaded GenHub application build and restarts the application.
    /// </summary>
    /// <param name="item">The content item representing the GenHub build.</param>
    [RelayCommand]
    private async Task InstallBuildContentAsync(ContentGridItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        var resolvedManifestId = await ResolveLocalInstalledManifestIdAsync(item, _vmCts.Token) ?? item.Id;
        await InstallBuildContentByIdAsync(resolvedManifestId, item.Name, _vmCts.Token);
    }

    private async Task InstallBuildContentByIdAsync(string contentId, string contentName, CancellationToken cancellationToken)
    {
        var velopackManager = serviceProvider.GetService<IVelopackUpdateManager>();
        if (velopackManager == null)
        {
            logger.LogWarning("IVelopackUpdateManager is not available to install build");
            return;
        }

        var storageService = serviceProvider.GetService<IContentStorageService>();
        if (storageService == null)
        {
            logger.LogWarning("IContentStorageService is not available to retrieve build files");
            return;
        }

        var locService = serviceProvider.GetService<ILocalizationService>();
        string? tempDir = null;

        try
        {
            var prepTitle = locService?.GetLocalizedString("Downloads.Notification.InstallBuild.Preparing.Title", "Preparing Installation") ?? "Preparing Installation";
            var prepMessage = locService != null
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, locService.GetLocalizedString("Downloads.Notification.InstallBuild.Preparing.Message", "Preparing {0} for installation..."), contentName)
                : $"Preparing {contentName} for installation...";

            notificationService.Show(new NotificationMessage(
                NotificationType.Info,
                prepTitle,
                prepMessage,
                NotificationDurations.Medium));

            tempDir = Path.Combine(Path.GetTempPath(), $"genhub-build-install-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var retrieveResult = await storageService.RetrieveContentAsync(
                ManifestId.Create(contentId),
                tempDir,
                cancellationToken);

            if (!retrieveResult.Success)
            {
                logger.LogError("Failed to retrieve build files for {ContentId}: {Error}", contentId, retrieveResult.FirstError);
                var failedTitle = locService?.GetLocalizedString("Downloads.Notification.InstallBuild.Failed.Title", "Installation Failed") ?? "Installation Failed";
                var failedMsg = retrieveResult.FirstError ?? "Failed to retrieve build files from storage.";
                notificationService.Show(new NotificationMessage(
                    NotificationType.Error,
                    failedTitle,
                    failedMsg,
                    NotificationDurations.Long));
                return;
            }

            var progress = new Progress<UpdateProgress>(p =>
            {
                if (!string.IsNullOrEmpty(p.Status))
                {
                    logger.LogInformation("Build install progress: {Percent}% - {Status}", p.PercentComplete, p.Status);
                }
            });

            await velopackManager.InstallDownloadedBuildAsync(
                tempDir,
                contentName,
                progress,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Installation of GenHub build {ContentId} was cancelled", contentId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to install GenHub build {ContentId}", contentId);
            var failedTitle = locService?.GetLocalizedString("Downloads.Notification.InstallBuild.Failed.Title", "Installation Failed") ?? "Installation Failed";
            notificationService.Show(new NotificationMessage(
                NotificationType.Error,
                failedTitle,
                ex.Message,
                NotificationDurations.Long));
        }
        finally
        {
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to clean up build install temp directory: {Dir}", tempDir);
                }
            }
        }
    }

    /// <summary>
    /// Adds the content to a compatible profile. Shows a profile selection dialog.
    /// </summary>
    [RelayCommand]
    private async Task AddContentToProfileAsync(ContentGridItemViewModel item, CancellationToken cancellationToken = default)
    {
        if (item == null)
        {
            logger.LogWarning("AddContentToProfileAsync called with null item");
            return;
        }

        if (item.IsGenHubBuild)
        {
            var loc = serviceProvider.GetService<ILocalizationService>();
            notificationService.ShowWarning(
                loc?.GetLocalizedString("Downloads.Notification.InstallBuild.InvalidAction.Title", "Invalid Action") ?? "Invalid Action",
                loc?.GetLocalizedString("Downloads.Notification.InstallBuild.InvalidAction.Message", "GenHub application builds cannot be added to game profiles. Use the Install Build option instead.") ?? "GenHub application builds cannot be added to game profiles. Use the Install Build option instead.");
            return;
        }

        var isReady = item.HasBundleComponents
            ? item.AreBundleComponentsReadyForProfile
            : item.EffectiveIsDownloaded || item.EffectiveCurrentState is ContentState.Downloaded or ContentState.UpdateAvailable;

        if (!isReady)
        {
            item.DownloadStatus = ContentConstants.PleaseDownloadFirstStatusMessage;
            notificationService.ShowError(
                localizationService.GetLocalizedString("Downloads.Notification.CannotAddToProfile.Title", "Cannot Add to Profile"),
                localizationService.GetLocalizedString("Downloads.Notification.CannotAddToProfile.DownloadFirstMessage", "Please download the content first before adding it to a profile."));
            logger.LogWarning("Cannot add content to profile: content '{Name}' is not downloaded", item.Name);
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_vmCts.Token, cancellationToken);
        var ct = cts.Token;

        try
        {
            var (manifestId, additionalManifestIds, errorHandled) = await ResolveProfileManifestIdsAsync(item, ct);
            if (errorHandled || string.IsNullOrEmpty(manifestId))
            {
                return;
            }

            logger.LogInformation("Adding content '{ContentName}' (Manifest: {ManifestId}) to profile", item.Name, manifestId);

            // Show profile selection dialog
            item.DownloadStatus = ContentConstants.SelectingProfileStatusMessage;

            var manifestPool = serviceProvider.GetRequiredService(typeof(IContentManifestPool)) as IContentManifestPool
                ?? throw new InvalidOperationException("IContentManifestPool service not found");

            using var profileSelectionVm = new ProfileSelectionViewModel(
                serviceProvider.GetService(typeof(ILogger<ProfileSelectionViewModel>)) as ILogger<ProfileSelectionViewModel>
                    ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProfileSelectionViewModel>.Instance,
                profileManager,
                profileContentService,
                manifestPool,
                notificationService,
                localizationService ?? _localizationService,
                serviceProvider.GetService<IUserSettingsService>());

            // Load profiles for the target game
            await profileSelectionVm.LoadProfilesAsync(
                item.TargetGame,
                manifestId,
                item.Name,
                additionalManifestIds,
                ct);

            // Show the dialog
            var dialog = new Views.ProfileSelectionView(profileSelectionVm);

            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

            if (mainWindow != null)
            {
                await dialog.ShowDialog(mainWindow);
            }
            else
            {
                logger.LogWarning("No main window found to show profile selection dialog");
                item.DownloadStatus = ContentConstants.ErrorNoWindowStatusMessage;
                return;
            }

            await HandleProfileSelectionResultAsync(item, profileSelectionVm);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // View disposal/cancellation is expected.
        }
        catch (Exception ex)
        {
            item.DownloadStatus = $"{ContentConstants.ErrorStatusPrefix}{ex.Message}";
            notificationService.ShowError(
                localizationService.GetLocalizedString("Downloads.Notification.AddToProfileError.Title", "Error Adding to Profile"),
                localizationService.GetLocalizedString("Downloads.Notification.AddToProfileError.Message", $"An unexpected error occurred: {ex.Message}", ex.Message));
            logger.LogError(ex, "Exception adding content '{ContentName}' to profile", item.Name);
        }
    }

    private async Task<(string? PrimaryManifestId, IReadOnlyList<string> AdditionalManifestIds, bool ErrorHandled)> ResolveProfileManifestIdsAsync(
        ContentGridItemViewModel item,
        CancellationToken ct)
    {
        if (item.HasBundleComponents)
        {
            var bundleIds = await BundleComponentViewModel.GetRequiredProfileManifestIdsAsync(
                item.BundleComponents,
                contentStateService,
                ct);
            if (bundleIds.Count == 0)
            {
                item.DownloadStatus = ContentConstants.PleaseDownloadFirstStatusMessage;
                notificationService.ShowError(
                    localizationService.GetLocalizedString("Downloads.Notification.CannotAddToProfile.Title", "Cannot Add to Profile"),
                    localizationService.GetLocalizedString("Downloads.Notification.CannotAddToProfile.BundleMessage", "Download every selected bundle item (including the chosen variants) before adding them to a profile."));
                logger.LogWarning(
                    "Cannot add bundle to profile: missing acquired members for '{ContentName}'",
                    item.Name);
                return (null, [], true);
            }

            return (bundleIds[0], [.. bundleIds.Skip(1)], false);
        }

        var searchResultToMatch = item.SearchResult;
        if (item.SelectedVariant != null &&
            !string.IsNullOrEmpty(item.SelectedVariant.ManifestId) &&
            item.VariantSearchResults.TryGetValue(item.SelectedVariant.ManifestId, out var variantSr))
        {
            searchResultToMatch = variantSr;
        }

        var manifestId = searchResultToMatch.Id;
        if (string.IsNullOrEmpty(manifestId) && item.SelectedVariant != null)
        {
            manifestId = item.SelectedVariant.ManifestId;
        }

        var trustSearchResultId = !string.IsNullOrEmpty(manifestId)
            && ManifestIdValidator.IsValid(manifestId, out _)
            && await contentStateService.GetStateByManifestIdAsync(manifestId, ct) == ContentState.Downloaded;

        if (!trustSearchResultId && !string.IsNullOrEmpty(item.SelectedVariant?.ManifestId))
        {
            manifestId = item.SelectedVariant.ManifestId;
            trustSearchResultId = ManifestIdValidator.IsValid(manifestId, out _)
                && await contentStateService.GetStateByManifestIdAsync(manifestId, ct) == ContentState.Downloaded;
        }

        if (!trustSearchResultId)
        {
            logger.LogDebug("SearchResult ID '{Id}' is not an acquired manifest, looking up from pool", searchResultToMatch.Id);
            manifestId = await contentStateService.GetLocalManifestIdAsync(searchResultToMatch, ct);
        }

        if (string.IsNullOrEmpty(manifestId))
        {
            item.DownloadStatus = ContentConstants.PleaseDownloadFirstStatusMessage;
            notificationService.ShowError(
                localizationService.GetLocalizedString("Downloads.Notification.CannotAddToProfile.Title", "Cannot Add to Profile"),
                localizationService.GetLocalizedString("Downloads.Notification.CannotAddToProfile.DownloadFirstMessage", "Please download the content first before adding it to a profile."));
            logger.LogWarning("Cannot add content to profile: no manifest found for '{ContentName}'", item.Name);
            return (null, [], true);
        }

        return (manifestId, [], false);
    }

    private async Task HandleProfileSelectionResultAsync(
        ContentGridItemViewModel item,
        ProfileSelectionViewModel profileSelectionVm)
    {
        if (profileSelectionVm.WasSuccessful && !string.IsNullOrEmpty(profileSelectionVm.SelectedProfileName))
        {
            item.DownloadStatus = $"{ContentConstants.AddedToProfileStatusPrefix}{profileSelectionVm.SelectedProfileName}";

            try
            {
                var profilesResult = await profileManager.GetAllProfilesAsync(CancellationToken.None);
                var selectedProfile = profilesResult.Data?.FirstOrDefault(p => p.Name == profileSelectionVm.SelectedProfileName);
                if (selectedProfile != null)
                {
                    var message = new ProfileUpdatedMessage(selectedProfile);
                    WeakReferenceMessenger.Default.Send(message);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send ProfileUpdatedMessage");
            }
        }
        else if (!profileSelectionVm.WasSuccessful && !profileSelectionVm.WasCancelled && !string.IsNullOrEmpty(profileSelectionVm.ErrorMessage))
        {
            item.DownloadStatus = $"{ContentConstants.FailedStatusPrefix}{profileSelectionVm.ErrorMessage}";
            logger.LogError("Failed to add content to profile: {Error}", profileSelectionVm.ErrorMessage);
        }
        else
        {
            item.ClearInactiveDownloadStatus();
            logger.LogInformation("User cancelled profile selection for '{ContentName}'", item.Name);
        }
    }

    [RelayCommand]
    private async Task ImportSubscriptionAsync()
    {
        try
        {
            var importVm = new ImportSubscriptionViewModel(serviceProvider);
            var dialog = new ImportSubscriptionDialog
            {
                DataContext = importVm,
            };

            var mainWindow = (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWindow != null)
            {
                await dialog.ShowDialog(mainWindow);
                if (importVm.LastConfirmResult == true)
                {
                    await InitializeAsync();
                    notificationService.ShowSuccess(
                        localizationService?.GetString("Downloads.Subscription.SubscribedNotificationTitle") ?? "Subscribed",
                        localizationService?.GetString("Downloads.Subscription.SubscribedNotificationMessage") ?? "Successfully subscribed to content catalog.");
                }
            }
            else
            {
                dialog.Show();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to show Import Subscription dialog");
            notificationService.ShowError(
                localizationService?.GetString("Downloads.ImportSubscription.ImportErrorTitle") ?? "Import Error",
                localizationService?.GetString("Downloads.ImportSubscription.ImportErrorBody", ex.Message) ?? $"Failed to open import dialog: {ex.Message}");
        }
    }

    private async Task<string?> ResolveLocalInstalledManifestIdAsync(ContentGridItemViewModel gridItem, CancellationToken cancellationToken)
    {
        var targetResult = gridItem.SelectedVariant != null &&
            !string.IsNullOrEmpty(gridItem.SelectedVariant.ManifestId) &&
            gridItem.VariantSearchResults.TryGetValue(gridItem.SelectedVariant.ManifestId, out var variantResult)
            ? variantResult
            : gridItem.SearchResult;

        return await contentStateService.GetLocalManifestIdAsync(targetResult, cancellationToken).ConfigureAwait(false);
    }
}
