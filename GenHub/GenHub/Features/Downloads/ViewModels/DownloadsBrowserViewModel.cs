using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Features.Downloads.Services;
using GenHub.Features.Downloads.ViewModels.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static GenHub.Features.Downloads.Services.ContentState;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for the redesigned Downloads browser with sidebar navigation and content grid.
/// </summary>
public partial class DownloadsBrowserViewModel(
    IServiceProvider serviceProvider,
    ILogger<DownloadsBrowserViewModel> logger,
    IReadOnlyList<IContentDiscoverer> contentDiscoverers,
    IContentStateService contentStateService,
    IContentOrchestrator contentOrchestrator,
    IProfileContentService profileContentService,
    IGameProfileManager profileManager,
    INotificationService notificationService) : ObservableObject, IDisposable
{
    private const string CategoryMerged = "merged";
    private const string CategoryStatic = "static";
    private const string CategoryDynamic = "dynamic";

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    private readonly Dictionary<string, IFilterPanelViewModel> _filterViewModels = [];
    private CancellationTokenSource? _lastSearchCts;
    private bool _disposed;

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    [ObservableProperty]
    private bool _isFilterPanelVisible;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<PublisherItemViewModel> _publishers = [];

    [ObservableProperty]
    private PublisherItemViewModel? _selectedPublisher;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowFilters))]
    private IFilterPanelViewModel? _currentFilterViewModel;

    /// <summary>
    /// Gets a value indicating whether filters are available for the current publisher.
    /// </summary>
    public bool CanShowFilters => CurrentFilterViewModel != null;

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
    /// Performs asynchronous initialization.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task InitializeAsync()
    {
        InitializePublishers();
        InitializeFilterViewModels();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the Downloads tab is activated.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task OnTabActivatedAsync()
    {
        if (ContentItems.Count == 0 && !IsLoading)
        {
            await RefreshContentAsync();
        }
    }

    [RelayCommand]
    private static void GoBack()
    {
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Core.Messages.ClosePublisherDetailsMessage());
    }

    partial void OnSelectedPublisherChanged(PublisherItemViewModel? value)
    {
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
        if (value != null && _filterViewModels.TryGetValue(value.PublisherId, out var filterVm))
        {
            CurrentFilterViewModel = filterVm;
            CurrentFilterViewModel.FiltersApplied += OnFiltersApplied;
            CurrentFilterViewModel.FiltersCleared += OnFiltersCleared;
        }
        else
        {
            CurrentFilterViewModel = null;
        }

        // Trigger content refresh
        CurrentPage = 1;
        CanLoadMore = false;

        // Close detail view
        SelectedContent = null;

        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshContentAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error refreshing content for publisher {Publisher}", value?.PublisherId ?? "Unknown");
            }
        });
    }

    private void OnFiltersCleared(object? sender, EventArgs e)
    {
        // Trigger content refresh when filters are cleared
        _ = RefreshContentAsync();
    }

    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        // Trigger content refresh when filters are applied
        CurrentPage = 1;
        _ = RefreshContentAsync();
    }

    [RelayCommand]
    private void SelectPublisher(PublisherItemViewModel publisher)
    {
        SelectedPublisher = publisher;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        CurrentPage = 1;
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
            await RefreshContentAsync(append: true);
        }
    }

    /// <param name="append">Whether to append results to the current list instead of clearing.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task RefreshContentAsync(bool append = false)
    {
        if (SelectedPublisher == null)
        {
            return;
        }

        // Cancel previous search if still running
        _lastSearchCts?.Cancel();
        _lastSearchCts = new CancellationTokenSource();
        var ct = _lastSearchCts.Token;

        try
        {
            IsLoading = true;
            if (!append)
            {
                ContentItems.Clear();
            }

            // Build base query
            var baseQuery = new ContentSearchQuery
            {
                SearchTerm = SearchTerm,
                Take = PageSize,
                Page = CurrentPage,
                TargetGame = GameType.ZeroHour, // Global default
            };

            // Apply active filters from filter panel
            if (CurrentFilterViewModel != null)
            {
                baseQuery = CurrentFilterViewModel.ApplyFilters(baseQuery);
            }

            if (SelectedPublisher.PublisherId == PublisherTypeConstants.All)
            {
                await RefreshAllPublishersAsync(baseQuery, ct);
            }
            else
            {
                await RefreshSinglePublisherAsync(SelectedPublisher.PublisherId, baseQuery, ct);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Search for {Publisher} was canceled", SelectedPublisher.PublisherId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh content for publisher {Publisher}", SelectedPublisher.PublisherId);
        }
        finally
        {
            // Only stop "main" loading if we aren't canceled
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private async Task RefreshSinglePublisherAsync(string publisherId, ContentSearchQuery query, CancellationToken ct)
    {
        var discoverer = GetDiscovererForPublisher(publisherId);
        if (discoverer == null)
        {
            logger.LogWarning("No discoverer found for publisher {Publisher}", publisherId);
            return;
        }

        var result = await discoverer.DiscoverAsync(query, ct);
        if (ct.IsCancellationRequested)
        {
            return;
        }

        if (result.Success && result.Data != null)
        {
            var items = result.Data.Items.ToList();

            // Track existing IDs to prevent duplicates
            var existingIds = new HashSet<string>(ContentItems.Select(x => x.SearchResult.Id ?? string.Empty));

            var addedCount = 0;
            foreach (var item in items)
            {
                var itemId = item.Id ?? string.Empty;
                if (!existingIds.Contains(itemId))
                {
                    var vm = new ContentGridItemViewModel(item)
                    {
                        ViewCommand = ViewContentCommand,
                        DownloadCommand = DownloadContentCommand,
                        AddToProfileCommand = AddContentToProfileCommand,
                        UpdateCommand = DownloadContentCommand,
                    };

                    // Determine the current state using the content state service
                    var state = await contentStateService.GetStateAsync(item, ct);
                    vm.CurrentState = state;

                    ContentItems.Add(vm);
                    existingIds.Add(itemId);
                    addedCount++;
                }
            }

            logger.LogInformation(
                "Added {AddedCount} new items out of {TotalCount} fetched for {Publisher} (page {Page}). HasMoreItems: {HasMore}",
                addedCount,
                items.Count,
                publisherId,
                query.Page,
                result.Data.HasMoreItems);

            // Update CanLoadMore based on the discoverer's explicit signal
            CanLoadMore = result.Data.HasMoreItems;
        }
        else
        {
            CanLoadMore = false;
            logger.LogWarning("Discovery failed or returned no data for {Publisher}. Success: {Success}", publisherId, result.Success);
        }
    }

    private async Task RefreshAllPublishersAsync(ContentSearchQuery query, CancellationToken ct)
    {
        // Prioritized loading order as requested:
        // 1. GeneralsOnline (first, single item)
        // 2. CommunityOutpost/Apatch (second, single item)
        // 3. TheSuperHackers GitHub (most recent release only)
        // 4. Then: 5 from other GitHub repositories
        // 5. Then: 5 from CNCLabs
        // 6. Then: 5 from ModDB
        // 7. Then: 5 from AODMaps
        // After initial load, "Load More" gets 20 at a time from remaining sources
        var anyProviderHasMore = false;
        var lockObj = new object();
        var existingIds = new HashSet<string>(ContentItems.Select(x => x.SearchResult.Id ?? string.Empty));

        async Task LoadFromDiscovererAsync(IContentDiscoverer discoverer, int take, string label)
        {
            try
            {
                var limitedQuery = new ContentSearchQuery
                {
                    SearchTerm = query.SearchTerm,
                    Take = take,
                    Page = query.Page,
                    TargetGame = query.TargetGame,
                    ContentType = query.ContentType,
                };

                logger.LogDebug("Loading {Take} items from {Source}", take, label);
                var result = await discoverer.DiscoverAsync(limitedQuery, ct);
                if (ct.IsCancellationRequested) return;

                if (result.Success && result.Data != null)
                {
                    var vmItems = new List<ContentGridItemViewModel>();
                    foreach (var item in result.Data.Items)
                    {
                        var itemId = item.Id ?? string.Empty;
                        if (existingIds.Contains(itemId)) continue;

                        var vm = new ContentGridItemViewModel(item)
                        {
                            ViewCommand = ViewContentCommand,
                            DownloadCommand = DownloadContentCommand,
                            AddToProfileCommand = AddContentToProfileCommand,
                            UpdateCommand = DownloadContentCommand,
                        };

                        var state = await contentStateService.GetStateAsync(item, ct);
                        vm.CurrentState = state;

                        vmItems.Add(vm);
                        lock (lockObj)
                        {
                            existingIds.Add(itemId);
                        }
                    }

                    if (result.Data.HasMoreItems)
                    {
                        lock (lockObj)
                        {
                            anyProviderHasMore = true;
                        }
                    }

                    // Add to collection on main thread
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var vm in vmItems)
                        {
                            ContentItems.Add(vm);
                        }
                    });

                    logger.LogDebug("Added {Count} items from {Source}", vmItems.Count, label);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load from {Source}", label);
            }
        }

        // Page 1 = initial prioritized load
        // Page > 1 = Load More (batch load from remaining sources)
        if ((query.Page ?? 1) == 1)
        {
            // Priority 1: GeneralsOnline (1 item)
            var goDiscoverer = GetDiscovererForPublisher(PublisherTypeConstants.GeneralsOnline);
            if (goDiscoverer != null)
            {
                await LoadFromDiscovererAsync(goDiscoverer, 1, "GeneralsOnline");
            }

            // Priority 2: CommunityOutpost (1 item)
            var coDiscoverer = GetDiscovererForPublisher(CommunityOutpostConstants.PublisherType);
            if (coDiscoverer != null)
            {
                await LoadFromDiscovererAsync(coDiscoverer, 1, "CommunityOutpost");
            }

            // Priority 3: TheSuperHackers GitHub (1 latest release)
            var tshDiscoverer = GetDiscovererForPublisher(PublisherTypeConstants.TheSuperHackers);
            if (tshDiscoverer != null)
            {
                await LoadFromDiscovererAsync(tshDiscoverer, 1, "TheSuperHackers");
            }

            // Priority 4: Other GitHub repositories (5 items) - via GitHubTopics discoverer if available
            var ghTopicsDiscoverer = GetDiscovererForPublisher(GitHubTopicsConstants.PublisherType);
            if (ghTopicsDiscoverer != null)
            {
                await LoadFromDiscovererAsync(ghTopicsDiscoverer, 5, "GitHub Repositories");
            }

            // Priority 5: CNCLabs (5 items)
            var cncDiscoverer = GetDiscovererForPublisher(CNCLabsConstants.PublisherType);
            if (cncDiscoverer != null)
            {
                await LoadFromDiscovererAsync(cncDiscoverer, 5, "CNCLabs");
            }

            // Priority 6: ModDB (5 items)
            var moddbDiscoverer = GetDiscovererForPublisher(ModDBConstants.PublisherType);
            if (moddbDiscoverer != null)
            {
                await LoadFromDiscovererAsync(moddbDiscoverer, 5, "ModDB");
            }

            // Priority 7: AODMaps (5 items)
            var aodDiscoverer = GetDiscovererForPublisher(AODMapsConstants.PublisherType);
            if (aodDiscoverer != null)
            {
                await LoadFromDiscovererAsync(aodDiscoverer, 5, "AODMaps");
            }

            // Always show "Load More" on initial load since there's likely more content
            anyProviderHasMore = true;
        }
        else
        {
            // Load More: batch load 20 items total from all sources proportionally
            var perSourceSize = 4; // 5 sources * 4 = 20 items

            var loadMoreTasks = new List<Task>();

            // Load from all non-priority sources in parallel
            var discoverers = new[]
            {
                (GetDiscovererForPublisher(PublisherTypeConstants.TheSuperHackers), "TheSuperHackers"),
                (GetDiscovererForPublisher(GitHubTopicsConstants.PublisherType), "GitHub Repositories"),
                (GetDiscovererForPublisher(CNCLabsConstants.PublisherType), "CNCLabs"),
                (GetDiscovererForPublisher(ModDBConstants.PublisherType), "ModDB"),
                (GetDiscovererForPublisher(AODMapsConstants.PublisherType), "AODMaps"),
            };

            foreach (var (discoverer, label) in discoverers)
            {
                if (discoverer != null)
                {
                    loadMoreTasks.Add(LoadFromDiscovererAsync(discoverer, perSourceSize, label));
                }
            }

            await Task.WhenAll(loadMoreTasks);
        }

        // Enable Load More if at least one provider has more content
        CanLoadMore = !ct.IsCancellationRequested && anyProviderHasMore;
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
            _ => null,
        };
    }

    [RelayCommand]
    private void ViewContent(ContentGridItemViewModel item)
    {
        if (item?.SearchResult != null)
        {
            var contentLogger = serviceProvider.GetService(typeof(ILogger<ContentDetailViewModel>)) as ILogger<ContentDetailViewModel>
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ContentDetailViewModel>.Instance;

            var parsers = serviceProvider.GetService(typeof(IEnumerable<IWebPageParser>)) as IEnumerable<IWebPageParser> ?? [];

            var vm = new ContentDetailViewModel(
                item.SearchResult,
                serviceProvider,
                parsers,
                profileContentService,
                profileManager,
                notificationService,
                contentLogger,
                CloseDetail);

            vm.Initialize();
            SelectedContent = vm;
        }
    }

    [RelayCommand]
    private void CloseDetail()
    {
        SelectedContent = null;
    }

    private void InitializePublishers()
    {
        Publishers =
        [
            new PublisherItemViewModel(
                PublisherTypeConstants.All,
                "All Publishers",
                "avares://GenHub/Assets/Logos/generalsonline-logo.png", // Use a generic logo for now
                CategoryMerged),
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

        // Select first publisher by default
        if (Publishers.Count > 0)
        {
            SelectedPublisher = Publishers[0];
        }
    }

    private void InitializeFilterViewModels()
    {
        // Static publisher filters
        _filterViewModels[PublisherTypeConstants.GeneralsOnline] =
            new StaticPublisherFilterViewModel(PublisherTypeConstants.GeneralsOnline);

        // Using the updated CommunityOutpost filter
        _filterViewModels[CommunityOutpostConstants.PublisherType] =
            new CommunityOutpostFilterViewModel();

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

            logger.LogInformation("Starting download for content: {Name} ({Provider})", item.Name, item.ProviderName);

            // Use the ContentOrchestrator to properly acquire content
            // This handles ZIP extraction, manifest factory processing, and proper file storage
            var progress = new Progress<ContentAcquisitionProgress>(p =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
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
                item.IsDownloaded = true;

                // Store the manifest ID in the SearchResult for later use when adding to profile
                item.SearchResult.UpdateId(manifest.Id.Value);

                // Update the item's state to Downloaded so the UI switches from "Download" to "Add to Profile"
                item.CurrentState = ContentState.Downloaded;

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
            // Get the manifest ID - first try from SearchResult, then look up from manifest pool
            string? manifestId = item.SearchResult.Id;

            // Check if the ID looks like a valid manifest ID (5 segments: schema.version.publisher.type.name)
            var segments = manifestId?.Split('.') ?? [];
            if (segments.Length != 5 || !segments[0].All(char.IsDigit))
            {
                // Not a valid manifest ID, look up from the manifest pool using ContentStateService
                logger.LogDebug("SearchResult ID '{Id}' is not a manifest ID, looking up from pool", manifestId);
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

            logger.LogInformation("Adding content '{ContentName}' (Manifest: {ManifestId}) to profile", item.Name, manifestId);

            // Show profile selection dialog
            item.DownloadStatus = "Selecting profile...";

            var profileSelectionVm = new ProfileSelectionViewModel(
                serviceProvider.GetService(typeof(ILogger<ProfileSelectionViewModel>)) as ILogger<ProfileSelectionViewModel>
                    ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProfileSelectionViewModel>.Instance,
                profileManager,
                profileContentService,
                serviceProvider.GetRequiredService(typeof(IContentManifestPool)) as IContentManifestPool
                    ?? throw new InvalidOperationException("IContentManifestPool service not found"),
                notificationService);

            // Load profiles for the target game
            await profileSelectionVm.LoadProfilesAsync(item.TargetGame, manifestId, item.Name, CancellationToken.None);

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
                // User cancelled
                item.DownloadStatus = "Cancelled";
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

            // Cancel and dispose of search cancellation token
            _lastSearchCts?.Cancel();
            _lastSearchCts?.Dispose();

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
