using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Parsers;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Features.Downloads.Services;
using GenHub.Features.Downloads.ViewModels.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Downloads browser: built-in publishers plus user-subscribed GenHub catalogs.
/// </summary>
/// <remarks>
/// Built-in entries (GeneralsOnline, ModDB, …) use specialized discoverers.
/// Subscribed entries come from <see cref="IPublisherSubscriptionStore"/> and use
/// <see cref="GenericCatalogDiscoverer"/> so any schema-valid <c>catalog.json</c> is browsable
/// without a custom publisher class. After <c>genhub://subscribe</c> confirms,
/// <see cref="InitializeAsync"/> refreshes only the subscribed sidebar rows.
/// </remarks>
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
    IPublisherSubscriptionStore subscriptionStore) : ObservableObject, IDisposable
{
    private const string CategoryStatic = "static";
    private const string CategoryDynamic = "dynamic";

    private readonly Dictionary<string, IFilterPanelViewModel> _filterViewModels = [];
    private readonly Dictionary<string, PublisherBrowseState> _browseCache = [];
    private readonly Dictionary<string, PublisherInFlightOperation> _inFlightOperations = [];
    private readonly object _cacheLock = new();

    // TODO: [Architecture] Abstract concrete GenericCatalogDiscoverer instances behind an ICatalogDiscoveryService interface
    // to decouple ViewModel layer from discoverer instantiation and lifetime management.
    private readonly Dictionary<string, GenericCatalogDiscoverer> _subscribedDiscoverers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly CancellationTokenSource _vmCts = new();
    private CancellationTokenSource? _searchCts;
    private int _activeRequestId;
    private string? _lastPopulatedPublisherId;
    private bool _hasCustomQuery;
    private bool _disposed;
    private bool _builtInPublishersInitialized;

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private bool _isFilterPanelVisible;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<PublisherItemViewModel> _publishers = [];

    private PublisherItemViewModel? _selectedPublisher;

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
                HandleSelectedPublisherChanged(value);
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowFilters))]
    [NotifyPropertyChangedFor(nameof(CanSearchOrFilter))]
    private IFilterPanelViewModel? _currentFilterViewModel;

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
        PublisherTypeConstants.TheSuperHackers;

    /// <summary>
    /// Gets a value indicating whether search or filter UI controls are available for the current publisher.
    /// </summary>
    public bool CanSearchOrFilter => CanSearch || CanShowFilters;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailViewVisible))]
    private ContentDetailViewModel? _selectedContent;

    [ObservableProperty]
    private ObservableCollection<ContentGridItemViewModel> _contentItems = [];

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private bool _canLoadMore;

    /// <summary>
    /// Gets a value indicating whether the detail view is currently visible.
    /// </summary>
    public bool IsDetailViewVisible => SelectedContent != null;

    [ObservableProperty]
    private int _pageSize = 24;

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
            Publishers = CreateBuiltInPublishers();
            InitializeFilterViewModels();
            _builtInPublishersInitialized = true;
        }

        await RefreshSubscribedPublishersAsync();
    }

    /// <summary>
    /// Called when the Downloads tab is activated.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task OnTabActivatedAsync()
    {
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
            _ = item.EnsureIconsLoadedAsync();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            // Unsubscribe from event handlers
            if (CurrentFilterViewModel != null)
            {
                CurrentFilterViewModel.FiltersApplied -= OnFiltersApplied;
                CurrentFilterViewModel.FiltersCleared -= OnFiltersCleared;
            }

            _vmCts.Cancel();
            _vmCts.Dispose();

            _searchCts?.Cancel();
            _searchCts?.Dispose();

            lock (_cacheLock)
            {
                foreach (var op in _inFlightOperations.Values)
                {
                    op.Cts.Cancel();
                    op.Cts.Dispose();
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

        return grouped;
    }

    private static ContentSearchResult ResolveDefaultVariant(IReadOnlyList<ContentSearchResult> groupItems, ContentSearchResult primaryItem)
    {
        return groupItems.FirstOrDefault(i =>
            i.Variants?.Any(v => v.IsDefault && (v.ManifestId == i.Id || i.Id?.EndsWith($".{v.ManifestId}", StringComparison.OrdinalIgnoreCase) == true)) == true)
            ?? groupItems.FirstOrDefault(i =>
                i.ContentType == ContentType.GameClient &&
                (i.ProviderName?.Contains("SuperHacker", StringComparison.OrdinalIgnoreCase) == true || i.ResolverId?.Contains("github", StringComparison.OrdinalIgnoreCase) == true) &&
                i.TargetGame == GameType.ZeroHour)
            ?? groupItems.FirstOrDefault(i => i.Variants?.Any(v => v.IsDefault) == true)
            ?? primaryItem;
    }

    private static void PopulateSynthesizedVariants(
        ContentGridItemViewModel variantVm,
        ContentSearchResult primaryItem,
        IList<ContentVariantInfo> singleVariants)
    {
        var lastSegment = primaryItem.Id?.Split('.').LastOrDefault() ?? "content";
        foreach (var v in singleVariants)
        {
            var manifestId = !string.IsNullOrEmpty(v.ManifestId)
                ? v.ManifestId
                : $"1.0.{primaryItem.ProviderName.ToLowerInvariant()}.{primaryItem.ContentType.ToString().ToLowerInvariant()}.{lastSegment}-{v.Id}";

            var variantSr = new ContentSearchResult
            {
                Id = manifestId,
                Name = string.IsNullOrEmpty(primaryItem.VariantFamilyName) ? $"{primaryItem.Name} - {v.Name}" : $"{primaryItem.VariantFamilyName} - {v.Name}",
                Description = primaryItem.Description,
                Version = primaryItem.Version,
                ContentType = primaryItem.ContentType,
                TargetGame = primaryItem.TargetGame,
                ProviderName = primaryItem.ProviderName,
                AuthorName = primaryItem.AuthorName,
                IconUrl = primaryItem.IconUrl,
                SourceUrl = primaryItem.SourceUrl,
                DownloadSize = primaryItem.DownloadSize,
                RequiresResolution = primaryItem.RequiresResolution,
                ResolverId = primaryItem.ResolverId,
                VariantGroupId = primaryItem.VariantGroupId,
                VariantFamilyName = primaryItem.VariantFamilyName,
                Variants = primaryItem.Variants,
            };

            foreach (var kvp in primaryItem.ResolverMetadata)
            {
                variantSr.ResolverMetadata[kvp.Key] = kvp.Value;
            }

            var installable = new InstallableVariant
            {
                Name = VariantSwap.ResolveDisplayName(variantSr, v),
                ManifestId = VariantSwap.ResolveCatalogKey(variantSr, v),
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
                (!string.IsNullOrEmpty(v.Id) && sibling.Id?.EndsWith($".{v.Id}", StringComparison.OrdinalIgnoreCase) == true));

            var info = variantInfo ?? new ContentVariantInfo
            {
                Id = sibling.Id ?? string.Empty,
                Name = sibling.Name ?? sibling.Id ?? "Unknown",
                ManifestId = sibling.Id ?? string.Empty,
                IsDefault = sibling == defaultVariant,
            };

            var catalogKey = VariantSwap.ResolveCatalogKey(sibling, info);
            var installable = new InstallableVariant
            {
                Name = VariantSwap.ResolveDisplayName(sibling, info),
                ManifestId = catalogKey,
                IconUrl = sibling.IconUrl ?? string.Empty,
                VariantType = info.VariantType ?? string.Empty,
            };

            variantVm.AddVariant(installable, sibling);
        }
    }

    private static void SelectDefaultVariant(
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

        variantVm.SelectedVariant = defaultSelection
            ?? variantVm.Variants.FirstOrDefault(v => string.Equals(v.ManifestId, defaultVariant.Id, StringComparison.OrdinalIgnoreCase))
            ?? variantVm.Variants.FirstOrDefault(v => v.Name.Contains("Zero Hour", StringComparison.OrdinalIgnoreCase))
            ?? variantVm.Variants[^1];
    }

    /// <summary>
    /// Seeds the sidebar with shipped/built-in providers (not user subscriptions).
    /// </summary>
    private static ObservableCollection<PublisherItemViewModel> CreateBuiltInPublishers()
    {
        return
        [
            new PublisherItemViewModel(
                PublisherTypeConstants.GeneralsOnline,
                "Generals Online",
                "avares://GenHub/Assets/Logos/generalsonline-logo.png",
                CategoryStatic),
            new PublisherItemViewModel(
                PublisherTypeConstants.TheSuperHackers,
                "TheSuperHackers",
                "avares://GenHub/Assets/Logos/thesuperhackers-logo.png",
                CategoryStatic),
            new PublisherItemViewModel(
                CommunityOutpostConstants.PublisherType,
                "CommunityOutpost",
                "avares://GenHub/Assets/Logos/communityoutpost-logo.png",
                CategoryStatic),
            new PublisherItemViewModel(
                ModDBConstants.PublisherType,
                "ModDB",
                "avares://GenHub/Assets/Logos/moddb-logo.png",
                CategoryDynamic),
            new PublisherItemViewModel(
                CNCLabsConstants.PublisherType,
                "CNC Labs",
                "avares://GenHub/Assets/Logos/cnclabs-logo.png",
                CategoryDynamic),
            new PublisherItemViewModel(
                GitHubTopicsConstants.PublisherType,
                "GitHub",
                "avares://GenHub/Assets/Logos/github-logo.png",
                CategoryDynamic),
            new PublisherItemViewModel(
                AODMapsConstants.PublisherType,
                "AOD Maps",
                "avares://GenHub/Assets/Logos/aodmaps-logo.png",
                CategoryDynamic),
        ];
    }

    private static void RunOnUi(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess() || Avalonia.Application.Current == null)
        {
            action();
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(action);
        }
    }

    private void HandleSelectedPublisherChanged(PublisherItemViewModel? value)
    {
        if (value == null)
        {
            return;
        }

        Interlocked.Increment(ref _activeRequestId);

        // Cancel any active custom search query
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        // Save current outgoing publisher state to _browseCache before switching
        if (!string.IsNullOrEmpty(_lastPopulatedPublisherId))
        {
            lock (_cacheLock)
            {
                if (_browseCache.TryGetValue(_lastPopulatedPublisherId, out var outgoingState))
                {
                    outgoingState.ActiveDetailViewModel = SelectedContent;
                    outgoingState.SearchTerm = SearchTerm;
                    outgoingState.HasCustomQuery = _hasCustomQuery;
                    outgoingState.CurrentPage = CurrentPage;
                    outgoingState.CanLoadMore = CanLoadMore;
                    if (_hasCustomQuery)
                    {
                        outgoingState.Items = [.. ContentItems];
                    }
                }
                else if (ContentItems.Count > 0 || SelectedContent != null)
                {
                    _browseCache[_lastPopulatedPublisherId] = new PublisherBrowseState
                    {
                        Items = [.. ContentItems],
                        CurrentPage = CurrentPage,
                        CanLoadMore = CanLoadMore,
                        SearchTerm = SearchTerm,
                        HasCustomQuery = _hasCustomQuery,
                        ActiveDetailViewModel = SelectedContent,
                    };
                }
            }
        }

        // Update selection state
        foreach (var publisher in Publishers)
        {
            publisher.IsSelected = publisher == value;
        }

        // Clear previous filter state
        if (CurrentFilterViewModel != null)
        {
            CurrentFilterViewModel.FiltersApplied -= OnFiltersApplied;
            CurrentFilterViewModel.FiltersCleared -= OnFiltersCleared;
            CurrentFilterViewModel.ClearFilters();
        }

        // Switch filter panel
        if (_filterViewModels.TryGetValue(value.PublisherId, out var filterVm))
        {
            CurrentFilterViewModel = filterVm;
            CurrentFilterViewModel.FiltersApplied += OnFiltersApplied;
            CurrentFilterViewModel.FiltersCleared += OnFiltersCleared;
            IsFilterPanelVisible = string.Equals(
                value.PublisherId,
                AODMapsConstants.PublisherType,
                StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            CurrentFilterViewModel = null;
            IsFilterPanelVisible = false;
        }

        // Detach UI collection and detail view immediately so previous publisher's cards vanish from UI without disposing cached objects
        ContentItems = [];
        SelectedContent = null;

        lock (_cacheLock)
        {
            if (_browseCache.TryGetValue(value.PublisherId, out var cached))
            {
                // Cache hit: restore full dataset instantly without network discovery
                foreach (var item in cached.Items)
                {
                    item.ClearInactiveDownloadStatus();
                    _ = item.RefreshVariantStatesAsync();
                    _ = item.EnsureIconsLoadedAsync();
                }

                ContentItems = new ObservableCollection<ContentGridItemViewModel>(cached.Items);
                CurrentPage = cached.CurrentPage;
                CanLoadMore = cached.CanLoadMore;
                SearchTerm = cached.SearchTerm;
                _hasCustomQuery = cached.HasCustomQuery;
                SelectedContent = cached.ActiveDetailViewModel;
                _lastPopulatedPublisherId = value.PublisherId;
                IsLoading = false;
                logger.LogInformation(
                    "Restored {Count} cached items for {Publisher} (no refresh needed)",
                    cached.Items.Count,
                    value.PublisherId);
                return;
            }

            if (_inFlightOperations.TryGetValue(value.PublisherId, out var inFlight))
            {
                // Attach UI to ongoing in-flight background operation
                var itemsSoFar = inFlight.ResolvedItems.ToList();
                foreach (var item in itemsSoFar)
                {
                    item.ClearInactiveDownloadStatus();
                    _ = item.RefreshVariantStatesAsync();
                    _ = item.EnsureIconsLoadedAsync();
                }

                ContentItems = new ObservableCollection<ContentGridItemViewModel>(itemsSoFar);
                CurrentPage = inFlight.Query.Page ?? 1;
                CanLoadMore = inFlight.HasMoreItems;
                SearchTerm = string.Empty;
                _hasCustomQuery = false;
                SelectedContent = null;
                _lastPopulatedPublisherId = value.PublisherId;
                IsLoading = !inFlight.IsCompleted;
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

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        // Clearing filters re-runs a default query, which re-populates the cache.
        _hasCustomQuery = !string.IsNullOrWhiteSpace(SearchTerm);
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

    [RelayCommand]
    private async Task SearchAsync()
    {
        _hasCustomQuery = !string.IsNullOrWhiteSpace(SearchTerm) || (CurrentFilterViewModel?.HasActiveFilters == true);
        CurrentPage = 1;
        Interlocked.Increment(ref _activeRequestId);
        await RefreshContentAsync();
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (CanLoadMore && !IsLoading)
        {
            CurrentPage++;
            logger.LogInformation(
                "Loading more content for {Publisher}, page {Page}",
                SelectedPublisher?.PublisherId ?? "Unknown",
                CurrentPage);
            var success = await RefreshContentAsync(append: true);
            if (!success)
            {
                CurrentPage--;
            }
        }
    }

    /// <param name="append">Whether to append results to the current list instead of clearing.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task<bool> RefreshContentAsync(bool append = false)
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
                if (isCustomQuery)
                {
                    lock (_cacheLock)
                    {
                        var cachedItemSet = new HashSet<ContentGridItemViewModel>(_browseCache.Values.SelectMany(s => s.Items));
                        foreach (var inFlight in _inFlightOperations.Values)
                        {
                            cachedItemSet.UnionWith(inFlight.ResolvedItems);
                        }

                        foreach (var item in ContentItems)
                        {
                            if (!cachedItemSet.Contains(item))
                            {
                                item.Dispose();
                            }
                        }
                    }

                    ContentItems.Clear();
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
                            foreach (var item in oldInFlight.ResolvedItems)
                            {
                                item.Dispose();
                            }
                        }
                    }

                    ContentItems.Clear();
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
                TargetGame = GameType.ZeroHour, // Global default
            };

            // Apply active filters from filter panel
            if (CurrentFilterViewModel != null)
            {
                baseQuery = CurrentFilterViewModel.ApplyFilters(baseQuery);
            }

            return await ExecuteStreamingFetchAsync(publisherId, baseQuery, requestId, isCustomQuery, append);
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

    private async Task<bool> ExecuteStreamingFetchAsync(
        string publisherId,
        ContentSearchQuery query,
        int requestId,
        bool isCustomQuery,
        bool append)
    {
        var discoverer = GetDiscovererForPublisher(publisherId);
        if (discoverer == null)
        {
            logger.LogWarning("No discoverer found for publisher {Publisher}", publisherId);
            RunOnUi(() =>
            {
                if (_activeRequestId == requestId && SelectedPublisher?.PublisherId == publisherId)
                {
                    IsLoading = false;
                }
            });
            return false;
        }

        var (opCts, inFlightOp) = SetupFetchOperation(publisherId, query, isCustomQuery, append);

        try
        {
            var result = await discoverer.DiscoverAsync(query, opCts.Token);

            if (opCts.Token.IsCancellationRequested)
            {
                CleanupInFlight(publisherId, inFlightOp);
                return false;
            }

            if (result.Success && result.Data != null)
            {
                var items = result.Data.Items
                    .Where(item => !query.ContentType.HasValue || item.ContentType == query.ContentType.Value)
                    .ToList();

                var existingIds = append ? CollectExistingContentIds() : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var groups = GroupContentItemsByVariant(items);
                var newVms = await ProcessDiscoveredGroupsAsync(groups, existingIds, inFlightOp, publisherId, requestId, opCts.Token);

                if (opCts.Token.IsCancellationRequested)
                {
                    CleanupInFlight(publisherId, inFlightOp);
                    return false;
                }

                CommitBrowseResultsToCache(publisherId, query, result.Data.HasMoreItems, isCustomQuery, append, inFlightOp, newVms);

                RunOnUi(() =>
                {
                    if (_activeRequestId == requestId && SelectedPublisher?.PublisherId == publisherId)
                    {
                        CanLoadMore = result.Data.HasMoreItems;
                        IsLoading = false;

                        // Update GitHub author options if available
                        if (publisherId.Equals(GitHubTopicsConstants.PublisherType, StringComparison.OrdinalIgnoreCase) &&
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

                        // Cloudflare notice for ModDB
                        if (result.Data.ChallengeDetected)
                        {
                            notificationService.ShowInfo(
                                "ModDB is waiting for verification",
                                "A browser window opened for ModDB's bot check. Complete it, then press Search to load the full catalogue.",
                                autoDismissMs: 8000);
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
                if (_activeRequestId == requestId && SelectedPublisher?.PublisherId == publisherId)
                {
                    CanLoadMore = false;
                    IsLoading = false;

                    if (result.Data?.ChallengeDetected == true)
                    {
                        notificationService.ShowWarning(
                            "ModDB is waiting for verification",
                            "A browser window opened for ModDB's bot check. Click \"I am not a robot\" in that window, then open ModDB again. You only need to do this once.");
                    }
                    else if (ContentItems.Count == 0)
                    {
                        notificationService.ShowInfo(
                            "No content loaded",
                            $"{publisherId} returned no content. Check your connection and try again.");
                    }
                }
            });

            logger.LogWarning("Discovery failed or returned no data for {Publisher}. Success: {Success}", publisherId, result.Success);
            return false;
        }
        catch (OperationCanceledException)
        {
            CleanupInFlight(publisherId, inFlightOp);
            RunOnUi(() =>
            {
                if (_activeRequestId == requestId && SelectedPublisher?.PublisherId == publisherId)
                {
                    IsLoading = false;
                }
            });
            logger.LogInformation("Streaming fetch for {Publisher} was canceled", publisherId);
            return false;
        }
        catch (Exception ex)
        {
            CleanupInFlight(publisherId, inFlightOp);
            RunOnUi(() =>
            {
                if (_activeRequestId == requestId && SelectedPublisher?.PublisherId == publisherId)
                {
                    IsLoading = false;
                }
            });
            logger.LogError(ex, "Failed to stream content for publisher {Publisher}", publisherId);
            return false;
        }
    }

    private (CancellationTokenSource Cts, PublisherInFlightOperation? InFlightOp) SetupFetchOperation(
        string publisherId,
        ContentSearchQuery query,
        bool isCustomQuery,
        bool append)
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
                inFlightOp = new PublisherInFlightOperation(publisherId, query, opCts);
                _inFlightOperations[publisherId] = inFlightOp;
            }
        }
        else
        {
            _searchCts?.Cancel();
            _searchCts = CancellationTokenSource.CreateLinkedTokenSource(_vmCts.Token);
            opCts = _searchCts;
        }

        return (opCts, inFlightOp);
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

            var primaryItem = group[0];
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
                if (_activeRequestId == requestId && SelectedPublisher?.PublisherId == publisherId)
                {
                    ContentItems.Add(vm);
                }
            });
        }

        return newVms;
    }

    private void CommitBrowseResultsToCache(
        string publisherId,
        ContentSearchQuery query,
        bool hasMoreItems,
        bool isCustomQuery,
        bool append,
        PublisherInFlightOperation? inFlightOp,
        List<ContentGridItemViewModel> newVms)
    {
        if (isCustomQuery)
        {
            return;
        }

        lock (_cacheLock)
        {
            if (_browseCache.TryGetValue(publisherId, out var existingState))
            {
                if (append)
                {
                    existingState.Items.AddRange(newVms);
                }
            }
            else
            {
                _browseCache[publisherId] = new PublisherBrowseState
                {
                    Items = [.. newVms],
                    CurrentPage = query.Page ?? 1,
                    CanLoadMore = hasMoreItems,
                };
            }

            if (inFlightOp != null)
            {
                inFlightOp.IsCompleted = true;
                inFlightOp.HasMoreItems = hasMoreItems;
                _inFlightOperations.Remove(publisherId);
            }
        }
    }

    private void CleanupInFlight(string publisherId, PublisherInFlightOperation? inFlightOp)
    {
        if (inFlightOp != null)
        {
            lock (_cacheLock)
            {
                _inFlightOperations.Remove(publisherId);
            }

            foreach (var item in inFlightOp.ResolvedItems)
            {
                item.Dispose();
            }
        }
    }

    private async Task<ContentGridItemViewModel?> CreateItemViewModelAsync(
        IReadOnlyList<ContentSearchResult> groupItems,
        ContentSearchResult primaryItem,
        CancellationToken ct)
    {
        if (groupItems.Count == 1 && string.IsNullOrEmpty(primaryItem.VariantGroupId))
        {
            return await CreateSingletonItemViewModelAsync(primaryItem, ct);
        }

        var defaultVariant = ResolveDefaultVariant(groupItems, primaryItem);
        var variantVm = CreateBaseGridItemViewModel(defaultVariant);

        if (groupItems.Count == 1 && primaryItem.Variants is { Count: > 0 } singleVariants)
        {
            PopulateSynthesizedVariants(variantVm, primaryItem, singleVariants);
        }
        else
        {
            PopulateSiblingVariants(variantVm, groupItems, defaultVariant);
        }

        SelectDefaultVariant(variantVm, groupItems, primaryItem, defaultVariant);

        await variantVm.RefreshVariantStatesAsync();
        variantVm.CurrentState = await contentStateService.GetStateAsync(defaultVariant, ct);

        return variantVm;
    }

    private async Task<ContentGridItemViewModel> CreateSingletonItemViewModelAsync(ContentSearchResult primaryItem, CancellationToken ct)
    {
        var vm = CreateBaseGridItemViewModel(primaryItem);
        var singletonState = await contentStateService.GetStateAsync(primaryItem, ct);
        vm.CurrentState = singletonState;
        return vm;
    }

    private ContentGridItemViewModel CreateBaseGridItemViewModel(ContentSearchResult item)
    {
        var vm = new ContentGridItemViewModel(
            item,
            contentStateService,
            loggerFactory.CreateLogger<ContentGridItemViewModel>())
        {
            ViewCommand = ViewContentCommand,
            DownloadCommand = DownloadContentCommand,
            AddToProfileCommand = AddContentToProfileCommand,
            UpdateCommand = DownloadContentCommand,
        };

        vm.Initialize();
        return vm;
    }

    /// <returns>The discoverer for the specified publisher, or null if not found.</returns>
    private IContentDiscoverer? GetDiscovererForPublisher(string publisherId)
    {
        return publisherId switch
        {
            PublisherTypeConstants.GeneralsOnline => contentDiscoverers.OfType<GeneralsOnlineDiscoverer>().FirstOrDefault(),
            PublisherTypeConstants.TheSuperHackers => contentDiscoverers.OfType<GenHub.Features.Content.Services.GitHub.GitHubReleasesDiscoverer>().FirstOrDefault(),
            CommunityOutpostConstants.PublisherType => contentDiscoverers.OfType<GenHub.Features.Content.Services.CommunityOutpost.CommunityOutpostDiscoverer>().FirstOrDefault(),
            ModDBConstants.PublisherType => contentDiscoverers.OfType<GenHub.Features.Content.Services.ContentDiscoverers.ModDBDiscoverer>().FirstOrDefault(),
            CNCLabsConstants.PublisherType => contentDiscoverers.OfType<GenHub.Features.Content.Services.ContentDiscoverers.CNCLabsMapDiscoverer>().FirstOrDefault(),
            GitHubTopicsConstants.PublisherType => contentDiscoverers.OfType<GenHub.Features.Content.Services.ContentDiscoverers.GitHubTopicsDiscoverer>().FirstOrDefault(),
            AODMapsConstants.PublisherType => contentDiscoverers.OfType<GenHub.Features.Content.Services.ContentDiscoverers.AODMapsDiscoverer>().FirstOrDefault(),

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
            var downloadCoordinator = serviceProvider.GetRequiredService<IContentDownloadCoordinator>();
            var manifestPool = serviceProvider.GetRequiredService<IContentManifestPool>();

            var vm = new ContentDetailViewModel(
                item.SearchResult,
                parsers,
                profileContentService,
                profileManager,
                notificationService,
                tabProviderRegistry,
                contentStateService,
                downloadCoordinator,
                manifestPool,
                loggerFactory,
                contentLogger,
                CloseDetail,
                item.VariantSearchResults);

            if (item.HasBundleComponents)
            {
                vm.AttachBundleComponents(item.BundleComponents);
            }

            vm.Initialize();

            if (item.SelectedVariant != null && !string.IsNullOrEmpty(item.SelectedVariant.ManifestId))
            {
                vm.SelectVariantByManifestId(item.SelectedVariant.ManifestId);
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
            var match = ContentItems.FirstOrDefault(i => ReferenceEquals(i.SearchResult, viewedSearchResult) || i.SearchResult.Id == viewedSearchResult.Id);
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
                            Avalonia.Threading.Dispatcher.UIThread.Post(() => match.CurrentState = state);
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

            var subscriptions = result.Data.ToList();
            var subscribedIds = new HashSet<string>(
                subscriptions.Select(s => s.PublisherId),
                StringComparer.OrdinalIgnoreCase);

            // Drop sidebar rows / caches for unsubscribed catalogs only
            var removed = Publishers
                .Where(p => p.PublisherType.Equals(CatalogConstants.SubscribedPublisherCategory, StringComparison.OrdinalIgnoreCase)
                            && !subscribedIds.Contains(p.PublisherId))
                .ToList();

            foreach (var item in removed)
            {
                Publishers.Remove(item);
                _subscribedDiscoverers.Remove(item.PublisherId);
                if (_browseCache.Remove(item.PublisherId, out var removedState))
                {
                    removedState.ActiveDetailViewModel?.Dispose();
                    removedState.ActiveDetailViewModel = null;
                    foreach (var oldVm in removedState.Items)
                    {
                        oldVm.Dispose();
                    }
                }

                if (SelectedPublisher?.PublisherId == item.PublisherId)
                {
                    foreach (var contentItem in ContentItems)
                    {
                        contentItem.Dispose();
                    }

                    ContentItems.Clear();
                    SelectedPublisher = Publishers.FirstOrDefault();
                }
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

                // Transient discoverer configured for this catalog URL (generic GenHub schema)
                var discoverer = serviceProvider.GetRequiredService<GenericCatalogDiscoverer>();
                discoverer.Configure(subscription);
                _subscribedDiscoverers[subscription.PublisherId] = discoverer;

                if (_browseCache.Remove(subscription.PublisherId, out var oldState))
                {
                    oldState.ActiveDetailViewModel?.Dispose();
                    oldState.ActiveDetailViewModel = null;
                    foreach (var oldVm in oldState.Items)
                    {
                        oldVm.Dispose();
                    }
                }
            }

            logger.LogDebug(
                "Synced {Count} subscribed publisher(s) into Downloads sidebar",
                subscriptions.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh subscribed publishers");
        }
    }

    private void InitializeFilterViewModels()
    {
        // Dynamic publisher filters
        _filterViewModels[ModDBConstants.PublisherType] = new ModDBFilterViewModel();
        _filterViewModels[CNCLabsConstants.PublisherType] = new CNCLabsFilterViewModel();
        _filterViewModels[GitHubTopicsConstants.PublisherType] = new GitHubFilterViewModel();
        _filterViewModels[AODMapsConstants.PublisherType] = new AODMapsFilterViewModel();
    }

    [RelayCommand]
    private async Task DownloadContentAsync(ContentGridItemViewModel item)
    {
        if (item == null || item.IsDownloading)
        {
            return;
        }

        CancellationToken cancellationToken = default; // We might want to support cancellation later

        try
        {
            item.IsDownloading = true;
            item.DownloadProgress = 0;
            item.DownloadStatus = "Starting download...";

            if (item.HasBundleComponents)
            {
                await DownloadBundleComponentsAsync(item, cancellationToken);
                return;
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

            var result = await contentOrchestrator.AcquireContentAsync(item.SearchResult, progress, cancellationToken);

            if (result.Success && result.Data != null)
            {
                var manifest = result.Data;
                logger.LogInformation("Successfully downloaded and stored content: {ManifestId}", manifest.Id.Value);

                item.DownloadProgress = 100;
                item.DownloadStatus = "Download complete!";

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

                // Notify ContentStateService that state has changed (catalog ID + manifest ID)
                contentStateService.NotifyStateChanged(originalContentId, ContentState.Downloaded, manifest.Id.Value);

                // Re-read every sibling so checkmarks stay accurate if acquisition produced
                // a different on-disk identity than the catalog key (e.g. SuperHackers).
                await item.RefreshVariantStatesAsync();

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

                notificationService.ShowSuccess("Download Complete", $"Downloaded {item.Name}");
            }
            else
            {
                var errorMsg = result.FirstError ?? "Unknown error";
                logger.LogError("Failed to download {ItemName}: {Error}", item.Name, errorMsg);
                item.DownloadStatus = $"Error: {errorMsg}";
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Download cancelled for: {Name}", item.Name);
            item.DownloadStatus = "Download cancelled";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error downloading content: {Name}", item.Name);
            item.DownloadStatus = $"Error: {ex.Message}";
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
            item.DownloadStatus = "All selected content is already downloaded";
            await item.RefreshBundleComponentStatesAsync();
            return;
        }

        logger.LogInformation(
            "Downloading {Count} missing bundle member(s) for {Name}",
            targets.Count,
            item.Name);

        var completed = 0;
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            item.DownloadStatus = $"Downloading {target.Name} ({completed + 1}/{targets.Count})...";
            item.DownloadProgress = (int)(completed * 100.0 / targets.Count);

            var progress = new Progress<ContentAcquisitionProgress>(p =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!item.IsDownloading)
                    {
                        return;
                    }

                    var slice = 100.0 / targets.Count;
                    item.DownloadProgress = (int)((completed * slice) + (p.ProgressPercentage * slice / 100.0));
                    item.DownloadStatus = $"{target.Name}: {p.FormatProgressStatus()}";
                });
            });

            var result = await contentOrchestrator.AcquireContentAsync(target, progress, cancellationToken);
            if (!result.Success || result.Data == null)
            {
                var errorMsg = result.FirstError ?? "Unknown error";
                logger.LogError("Failed to download bundle member {ItemName}: {Error}", target.Name, errorMsg);
                item.DownloadStatus = $"Error: {errorMsg}";
                notificationService.ShowError("Download Failed", $"Failed to download {target.Name}: {errorMsg}");
                return;
            }

            var originalContentId = target.Id ?? string.Empty;
            target.UpdateId(result.Data.Id.Value);
            foreach (var component in item.BundleComponents)
            {
                component.MarkDownloaded(originalContentId, result.Data.Id.Value);
            }

            contentStateService.NotifyStateChanged(originalContentId, ContentState.Downloaded, result.Data.Id.Value);
            completed++;
        }

        await item.RefreshBundleComponentStatesAsync();
        item.DownloadProgress = 100;
        item.DownloadStatus = item.AreBundleComponentsReadyForProfile
            ? "Download complete!"
            : "Downloaded selected content";

        if (item.AreBundleComponentsReadyForProfile)
        {
            notificationService.ShowSuccess("Download Complete", $"Downloaded {item.Name}");
        }
    }

    /// <summary>
    /// Adds the content to a compatible profile. Shows a profile selection dialog.
    /// </summary>
    [RelayCommand]
    private async Task AddContentToProfileAsync(ContentGridItemViewModel item)
    {
        if (item == null)
        {
            logger.LogWarning("AddContentToProfileAsync called with null item");
            return;
        }

        try
        {
            string? manifestId;
            IReadOnlyList<string> additionalManifestIds = [];

            if (item.HasBundleComponents)
            {
                var bundleIds = await BundleComponentViewModel.GetRequiredProfileManifestIdsAsync(
                    item.BundleComponents,
                    contentStateService,
                    CancellationToken.None);
                if (bundleIds.Count == 0)
                {
                    item.DownloadStatus = "Please download first";
                    notificationService.ShowError(
                        "Cannot Add to Profile",
                        "Download every selected bundle item (including the chosen variants) before adding them to a profile.");
                    logger.LogWarning(
                        "Cannot add bundle to profile: missing acquired members for '{ContentName}'",
                        item.Name);
                    return;
                }

                manifestId = bundleIds[0];
                additionalManifestIds = [.. bundleIds.Skip(1)];
            }
            else
            {
                // Get the manifest ID - first try from SearchResult, then look up from manifest pool
                manifestId = item.SearchResult.Id;

                // A SearchResult ID may be manifest-shaped (5 segments) but still NOT be the on-disk
                // manifest ID — publishers such as GitHub encode a different content-name in the stored
                // manifest than the catalog card carries. Validate that the manifest is actually acquired
                // before trusting the ID; otherwise fall back to the provenance-aware pool lookup.
                var trustSearchResultId = !string.IsNullOrEmpty(manifestId)
                    && ManifestIdValidator.IsValid(manifestId, out _)
                    && await contentStateService.GetStateByManifestIdAsync(manifestId!, CancellationToken.None) == ContentState.Downloaded;

                if (!trustSearchResultId)
                {
                    logger.LogDebug("SearchResult ID '{Id}' is not an acquired manifest, looking up from pool", manifestId);
                    manifestId = await contentStateService.GetLocalManifestIdAsync(item.SearchResult, CancellationToken.None);
                }

                if (string.IsNullOrEmpty(manifestId))
                {
                    // Content hasn't been downloaded yet
                    item.DownloadStatus = "Please download first";
                    notificationService.ShowError("Cannot Add to Profile", "Please download the content first before adding it to a profile.");
                    logger.LogWarning("Cannot add content to profile: no manifest found for '{ContentName}'", item.Name);
                    return;
                }
            }

            logger.LogInformation("Adding content '{ContentName}' (Manifest: {ManifestId}) to profile", item.Name, manifestId);

            // Show profile selection dialog
            item.DownloadStatus = "Selecting profile...";

            var manifestPool = serviceProvider.GetRequiredService(typeof(IContentManifestPool)) as IContentManifestPool
                ?? throw new InvalidOperationException("IContentManifestPool service not found");

            var profileSelectionVm = new ProfileSelectionViewModel(
                serviceProvider.GetService(typeof(ILogger<ProfileSelectionViewModel>)) as ILogger<ProfileSelectionViewModel>
                    ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProfileSelectionViewModel>.Instance,
                profileManager,
                profileContentService,
                manifestPool,
                notificationService);

            // Load profiles for the target game
            await profileSelectionVm.LoadProfilesAsync(
                item.TargetGame,
                manifestId,
                item.Name,
                additionalManifestIds,
                CancellationToken.None);

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
                item.DownloadStatus = "Error: No window";
                return;
            }

            // Check the result
            if (profileSelectionVm.WasSuccessful && !string.IsNullOrEmpty(profileSelectionVm.SelectedProfileName))
            {
                item.DownloadStatus = $"Added to {profileSelectionVm.SelectedProfileName}";
                notificationService.ShowSuccess(
                    "Added to Profile",
                    $"'{item.Name}' has been added to profile '{profileSelectionVm.SelectedProfileName}'.");

                // Send profile updated message to notify other components
                try
                {
                    // Get the updated profile to send in the message
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
            else if (!profileSelectionVm.WasSuccessful && !string.IsNullOrEmpty(profileSelectionVm.ErrorMessage))
            {
                item.DownloadStatus = $"Failed: {profileSelectionVm.ErrorMessage}";
                notificationService.ShowError(
                    "Failed to Add to Profile",
                    profileSelectionVm.ErrorMessage);
                logger.LogError("Failed to add content to profile: {Error}", profileSelectionVm.ErrorMessage);
            }
            else
            {
                // Dismissing the profile picker does not cancel an acquisition. Leave the
                // downloaded card in its normal actionable state instead of presenting a
                // misleading persistent cancellation message.
                item.ClearInactiveDownloadStatus();
                logger.LogInformation("User cancelled profile selection for '{ContentName}'", item.Name);
            }
        }
        catch (Exception ex)
        {
            item.DownloadStatus = $"Error: {ex.Message}";
            notificationService.ShowError(
                "Error Adding to Profile",
                $"An unexpected error occurred: {ex.Message}");
            logger.LogError(ex, "Exception adding content '{ContentName}' to profile", item?.Name);
        }
    }

    /// <summary>
    /// Opens the manifests storage directory in the file explorer.
    /// </summary>
    [RelayCommand]
    private void OpenManifestsFolder()
    {
        try
        {
            var configProvider = serviceProvider.GetRequiredService<IConfigurationProviderService>();
            var path = configProvider.GetManifestsPath();

            logger.LogInformation("Opening manifests directory: {Path}", path);

            if (!Directory.Exists(path))
            {
                logger.LogWarning("Manifests directory not found at {Path}, creating it", path);
                Directory.CreateDirectory(path);
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open manifests directory");
            notificationService.ShowError("Error", $"Failed to open manifests directory: {ex.Message}", 5000);
        }
    }

    /// <summary>
    /// Tracks an in-flight background default browse operation so switching away
    /// allows the fetch to complete into cache, and switching back can attach to it.
    /// </summary>
    private sealed class PublisherInFlightOperation(
        string publisherId,
        ContentSearchQuery query,
        CancellationTokenSource cts)
    {
        /// <summary>Gets the publisher identifier.</summary>
        public string PublisherId { get; } = publisherId;

        /// <summary>Gets the search query used for this operation.</summary>
        public ContentSearchQuery Query { get; } = query;

        /// <summary>Gets the cancellation token source for this operation.</summary>
        public CancellationTokenSource Cts { get; } = cts;

        /// <summary>Gets the sync root for thread-safe list operations.</summary>
        public object SyncRoot { get; } = new();

        /// <summary>Gets the list of items resolved so far.</summary>
        public List<ContentGridItemViewModel> ResolvedItems { get; } = [];

        /// <summary>Gets or sets a value indicating whether the operation has completed.</summary>
        public bool IsCompleted { get; set; }

        /// <summary>Gets or sets a value indicating whether more items are available from the provider.</summary>
        public bool HasMoreItems { get; set; }
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
    }
}
