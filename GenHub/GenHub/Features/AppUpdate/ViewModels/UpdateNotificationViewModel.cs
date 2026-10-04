using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Velopack;
using Velopack.Sources;

namespace GenHub.Features.AppUpdate.ViewModels;

/// <summary>
/// ViewModel for the update notification dialog powered by Velopack.
/// </summary>
public partial class UpdateNotificationViewModel : ObservableObject, IDisposable
{
    private const string InstallationFailedLocalizationKey = "Updates.Status.InstallationFailed";

    private static readonly Lazy<string> CachedCurrentAppVersion = new(() =>
    {
        try
        {
            // get actual installed version from velopack
            var updateManager = new UpdateManager(new SimpleWebSource(string.Empty));
            var currentVersion = updateManager.CurrentVersion;
            return currentVersion?.ToString() ?? AppConstants.AppVersion;
        }
        catch
        {
            // fallback to compile-time version if velopack fails
            return AppConstants.AppVersion;
        }
    });

    /// <summary>
    /// Gets the current application version.
    /// </summary>
    public static string CurrentAppVersion => CachedCurrentAppVersion.Value;

    /// <summary>
    /// Gets the formatted display string of the currently installed application version.
    /// </summary>
    public static string DisplayCurrentVersion
    {
        get
        {
            var version = CurrentAppVersion;
            if (string.IsNullOrWhiteSpace(version))
            {
                return "0.0.0";
            }

            var cleanVersion = version.Split('+')[0].TrimStart('v', 'V');
            return $"v{cleanVersion}";
        }
    }

    /// <summary>
    /// Gets the formatted display string of the currently installed application version for instance data binding.
    /// </summary>
    public string InstalledVersionDisplay => DisplayCurrentVersion;

    private readonly IVelopackUpdateManager _velopackUpdateManager;
    private readonly ILogger<UpdateNotificationViewModel> _logger;
    private readonly IUserSettingsService _userSettingsService;
    private readonly ILocalizationService? _localizationService;
    private readonly IGitHubAuthService? _gitHubAuthService;
    private readonly IPublisherSubscriptionStore? _publisherSubscriptionStore;
    private readonly IPublisherCatalogParser? _publisherCatalogParser;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly List<PullRequestInfo> _allPullRequests = [];
    private CancellationTokenSource? _loadArtifactsCts;
    private UpdateInfo? _currentUpdateInfo;
    private bool _disposed;

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = $"GenHub {AppConstants.AppVersion} - {AppUpdateConstants.CheckingForUpdatesMessage}";

    /// <summary>
    /// Gets or sets a value indicating whether an update check is in progress.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCheckButtonEnabled))]
    [NotifyPropertyChangedFor(nameof(DisplayLatestVersion))]
    [NotifyPropertyChangedFor(nameof(CanDownloadUpdate))]
    [NotifyPropertyChangedFor(nameof(InstallButtonText))]
    [NotifyPropertyChangedFor(nameof(IsLoadingOrInstalling))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool _isChecking;

    /// <summary>
    /// Gets or sets the download progress percentage.
    /// </summary>
    [ObservableProperty]
    private double _downloadProgress;

    /// <summary>
    /// Gets or sets a value indicating whether an update is available.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    [NotifyPropertyChangedFor(nameof(DisplayLatestVersion))]
    [NotifyPropertyChangedFor(nameof(CanDownloadUpdate))]
    private bool _isUpdateAvailable;

    /// <summary>
    /// Gets or sets the latest version string.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLatestVersion))]
    private string _latestVersion = string.Empty;

    /// <summary>
    /// Gets or sets the release notes URL.
    /// </summary>
    [ObservableProperty]
    private string _releaseNotesUrl = string.Empty;

    [ObservableProperty]
    private UpdateProgress _installationProgress = new() { Status = "Ready", PercentComplete = 0 };

    /// <summary>
    /// Gets or sets a value indicating whether an update download is in progress.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallButtonText))]
    [NotifyPropertyChangedFor(nameof(CanDownloadUpdate))]
    [NotifyPropertyChangedFor(nameof(IsLoadingOrInstalling))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool _isDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallButtonText))]
    [NotifyPropertyChangedFor(nameof(CanDownloadUpdate))]
    [NotifyPropertyChangedFor(nameof(IsLoadingOrInstalling))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool _isInstalling;

    /// <summary>
    /// Gets or sets a value indicating whether there is an error.
    /// </summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Gets or sets the list of available pull requests with artifacts.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PullRequestInfo> _availablePullRequests = [];

    /// <summary>
    /// Gets or sets the selected tab index (0 = Update, 1 = Browse Builds).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowseTabSelected))]
    private int _selectedTabIndex;

    /// <summary>
    /// Gets a value indicating whether the browse builds tab is selected.
    /// </summary>
    public bool IsBrowseTabSelected => SelectedTabIndex == AppUpdateConstants.BrowseBuildsTabIndex;

    /// <summary>
    /// Gets the list of available sort options for pull requests.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<string> _availableSortOptions =
    [
        AppUpdateConstants.SortOptionLastUpdated,
        AppUpdateConstants.SortOptionPrNumberDesc,
        AppUpdateConstants.SortOptionPrNumberAsc,
    ];

    /// <summary>
    /// Gets or sets the selected sort option for pull requests.
    /// </summary>
    [ObservableProperty]
    private string _selectedSortOption = AppUpdateConstants.SortOptionLastUpdated;

    partial void OnSelectedSortOptionChanged(string value)
    {
        ApplyPullRequestSorting();
    }

    /// <summary>
    /// Gets or sets the currently subscribed PR.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLatestVersion))]
    [NotifyPropertyChangedFor(nameof(IsSubscribedToAny))]
    private PullRequestInfo? _subscribedPr;

    /// <summary>
    /// Gets or sets a value indicating whether PR list is currently loading.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingPullRequests;

    /// <summary>
    /// Gets or sets the list of available branches.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<string> _availableBranches = [];

    /// <summary>
    /// Gets or sets the currently subscribed branch.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLatestVersion))]
    [NotifyPropertyChangedFor(nameof(IsSubscribedToAny))]
    private string? _subscribedBranch;

    /// <summary>
    /// Gets or sets a value indicating whether branches are currently loading.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingBranches;

    /// <summary>
    /// Gets or sets a value indicating whether GitHub authentication is available.
    /// </summary>
    [ObservableProperty]
    private bool _isAuthenticated;

    /// <summary>
    /// Gets or sets the list of available versions (artifacts) for the subscribed item.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ArtifactUpdateInfo> _availableVersions = [];

    /// <summary>
    /// Gets or sets the currently selected version (artifact) to install.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    [NotifyPropertyChangedFor(nameof(CanDownloadUpdate))]
    private ArtifactUpdateInfo? _selectedVersion;

    /// <summary>
    /// Gets or sets a value indicating whether versions are currently loading.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionPlaceholderText))]
    [NotifyPropertyChangedFor(nameof(CanDownloadUpdate))]
    [NotifyPropertyChangedFor(nameof(InstallButtonText))]
    [NotifyPropertyChangedFor(nameof(IsLoadingOrInstalling))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool _isLoadingVersions;

    /// <summary>
    /// Gets the text to display as a placeholder in the version selection combo box.
    /// </summary>
    public string VersionPlaceholderText
    {
        get
        {
            if (IsLoadingVersions)
            {
                return _localizationService?.GetString("Updates.Placeholder.LoadingVersions") ?? AppUpdateConstants.LoadingVersionsMessage;
            }

            return AvailableVersions.Count > 0
                ? (_localizationService?.GetString("Updates.Placeholder.SelectVersion") ?? AppUpdateConstants.SelectVersionMessage)
                : (_localizationService?.GetString("Updates.Placeholder.NoVersionsFound") ?? AppUpdateConstants.NoVersionsFoundMessage);
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether a merged/closed PR warning should be shown.
    /// </summary>
    [ObservableProperty]
    private bool _showPrMergedWarning;

    /// <summary>
    /// Gets or sets the ID of the publisher for the subscribed custom build.
    /// </summary>
    [ObservableProperty]
    private string? _subscribedCustomBuildPublisherId;

    /// <summary>
    /// Gets or sets the content ID of the subscribed custom build.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSubscribedToCustomBuild))]
    [NotifyPropertyChangedFor(nameof(IsSubscribedToAny))]
    private string? _subscribedCustomBuildContentId;

    /// <summary>
    /// Gets or sets the friendly name of the subscribed custom build.
    /// </summary>
    [ObservableProperty]
    private string? _subscribedCustomBuildName;

    /// <summary>
    /// Gets or sets the installed version of the subscribed custom build.
    /// </summary>
    [ObservableProperty]
    private string? _subscribedCustomBuildVersion;

    /// <summary>
    /// Gets a value indicating whether the user is subscribed to a custom build or community fork.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Property is bound to UI in Avalonia XAML.")]
    public bool IsSubscribedToCustomBuild => !string.IsNullOrEmpty(SubscribedCustomBuildContentId);

    /// <summary>
    /// Gets the list of available custom builds discovered from active publisher subscriptions.
    /// </summary>
    public ObservableCollection<CustomBuildSubscriptionItem> AvailableCustomBuilds { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether any custom builds are available.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomBuildsColumnWidth))]
    private bool _hasCustomBuilds;

    /// <summary>
    /// Gets the column width for the custom builds column in the browse builds grid.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Property is bound to UI in Avalonia XAML.")]
    public GridLength CustomBuildsColumnWidth => HasCustomBuilds ? new GridLength(1.2, GridUnitType.Star) : new GridLength(0);

    /// <summary>
    /// Gets a value indicating whether the user is subscribed to either a PR, a branch, or a custom build.
    /// </summary>
    public bool IsSubscribedToAny => SubscribedPr != null || !string.IsNullOrEmpty(SubscribedBranch) || IsSubscribedToCustomBuild;

    /// <summary>
    /// Gets the display string for the subscribed PR number.
    /// </summary>
    public string SubscribedPrNumberDisplay => SubscribedPr?.Number.ToString() ?? AppUpdateConstants.NotAvailable;

    /// <summary>
    /// Gets the display string for the subscribed PR title.
    /// </summary>
    public string SubscribedPrTitleDisplay => SubscribedPr?.Title ?? AppUpdateConstants.NotAvailable;

    /// <summary>
    /// Gets the display string for the subscribed PR latest version.
    /// </summary>
    public string SubscribedPrLatestVersionDisplay => SubscribedPr?.LatestArtifact?.DisplayVersion ?? AppUpdateConstants.NotAvailable;

    /// <summary>
    /// Forces a manual refresh of updates and artifacts.
    /// </summary>
    [RelayCommand]
    private async Task ForceRefresh()
    {
        await CheckForUpdatesAsync();

        // also refresh prs and branches if in browse mode
        if (IsAuthenticated)
        {
            await LoadPullRequestsAsync();
            await LoadBranchesAsync();
        }

        // refresh artifacts for current subscription
        if (IsSubscribedToAny)
        {
            await LoadArtifactsForSubscribedItemAsync();
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateNotificationViewModel"/> class.
    /// </summary>
    /// <param name="velopackUpdateManager">The Velopack update manager.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="userSettingsService">The user settings service.</param>
    /// <param name="gitHubAuthService">The GitHub authentication service.</param>
    /// <param name="localizationService">The optional localization service.</param>
    /// <param name="publisherSubscriptionStore">The optional publisher subscription store.</param>
    /// <param name="publisherCatalogParser">The optional publisher catalog parser.</param>
    /// <param name="httpClientFactory">The optional HTTP client factory.</param>
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "ViewModel requires multiple dependencies for updates and custom builds.")]
    public UpdateNotificationViewModel(
        IVelopackUpdateManager velopackUpdateManager,
        ILogger<UpdateNotificationViewModel> logger,
        IUserSettingsService userSettingsService,
        IGitHubAuthService? gitHubAuthService = null,
        ILocalizationService? localizationService = null,
        IPublisherSubscriptionStore? publisherSubscriptionStore = null,
        IPublisherCatalogParser? publisherCatalogParser = null,
        IHttpClientFactory? httpClientFactory = null)
    {
        _velopackUpdateManager = velopackUpdateManager ?? throw new ArgumentNullException(nameof(velopackUpdateManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
        _localizationService = localizationService;
        _gitHubAuthService = gitHubAuthService;
        _publisherSubscriptionStore = publisherSubscriptionStore;
        _publisherCatalogParser = publisherCatalogParser;
        _httpClientFactory = httpClientFactory;
        _cancellationTokenSource = new CancellationTokenSource();

        if (_localizationService != null)
        {
            _localizationService.PropertyChanged += OnLocalizationPropertyChanged;
        }

        if (_gitHubAuthService != null)
        {
            _gitHubAuthService.AuthStateChanged += OnGitHubAuthStateChanged;
        }

        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !IsChecking);
        ManualRefreshCommand = new AsyncRelayCommand(ManualRefreshAsync, () => !IsChecking);
        DismissCommand = new RelayCommand(DismissUpdate);

        // check if GitHub authentication is available
        IsAuthenticated = gitHubAuthService is { IsAuthenticated: true };

        _logger.LogInformation("UpdateNotificationViewModel initialized with Velopack (IsAuthenticated={IsAuthenticated})", IsAuthenticated);

        // monitor collection changes to update placeholder text
        AvailableVersions.CollectionChanged += (s, e) => OnPropertyChanged(nameof(VersionPlaceholderText));
        AvailableCustomBuilds.CollectionChanged += (s, e) => HasCustomBuilds = AvailableCustomBuilds.Count > 0;

        // automatically check for updates and load prs when dialog opens
        _ = InitializeAsync();
    }

    /// <summary>
    /// Creates the progress reporter shared by every install path. Reports marshal to the UI
    /// thread and mirror installation state into the dialog bindings.
    /// </summary>
    private static Progress<UpdateProgress> CreateInstallationProgress(UpdateNotificationViewModel viewModel)
    {
        return new Progress<UpdateProgress>(p =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                viewModel.InstallationProgress = p;
                viewModel.StatusMessage = p.Status;
                viewModel.DownloadProgress = p.PercentComplete;

                if (!string.IsNullOrEmpty(p.Status) &&
                    p.Status.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase))
                {
                    viewModel.IsDownloading = true;
                }
                else if (!string.IsNullOrEmpty(p.Status) &&
                         (p.Status.StartsWith("Extracting", StringComparison.OrdinalIgnoreCase) ||
                          p.Status.StartsWith("Preparing", StringComparison.OrdinalIgnoreCase) ||
                          p.Status.StartsWith("Applying", StringComparison.OrdinalIgnoreCase) ||
                          p.Status.StartsWith("Launching", StringComparison.OrdinalIgnoreCase) ||
                          p.Status.StartsWith("Installing", StringComparison.OrdinalIgnoreCase)))
                {
                    viewModel.IsDownloading = false;
                }
            });
        });
    }

    private static void RunOnUi(Action action)
    {
        if (Avalonia.Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private static async Task RunOnUiAsync(Action action)
    {
        if (Avalonia.Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(action);
        }
    }

    private async Task LoadArtifactsForSubscribedItemAsync()
    {
        await CancelPreviousArtifactLoadAsync();

        if (_disposed || _cancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        var targetPr = SubscribedPr;
        var targetPrNumber = targetPr?.Number ?? _velopackUpdateManager.SubscribedPrNumber;
        var targetBranch = SubscribedBranch;
        var targetCustomBuild = SubscribedCustomBuildContentId;

        if (targetPrNumber == null && string.IsNullOrEmpty(targetBranch) && string.IsNullOrEmpty(targetCustomBuild))
        {
            IsLoadingVersions = false;
            AvailableVersions.Clear();
            SelectedVersion = null;
            return;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
        _loadArtifactsCts = cts;
        var token = cts.Token;

        IsLoadingVersions = true;
        await RunOnUiAsync(() =>
        {
            AvailableVersions.Clear();
            SelectedVersion = null;
        });

        try
        {
            var artifacts = await FetchSubscribedArtifactsAsync(targetPrNumber, targetBranch, targetCustomBuild, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            await RunOnUiAsync(() => PopulateAvailableVersions(artifacts));
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Artifact loading cancelled for subscription change");
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                _logger.LogError(ex, "Failed to load available versions");
            }
        }
        finally
        {
            if (ReferenceEquals(_loadArtifactsCts, cts))
            {
                IsLoadingVersions = false;
            }
        }
    }

    private async Task CancelPreviousArtifactLoadAsync()
    {
        var oldCts = Interlocked.Exchange(ref _loadArtifactsCts, null);
        if (oldCts != null)
        {
            await oldCts.CancelAsync();
            oldCts.Dispose();
        }
    }

    private async Task<IReadOnlyList<ArtifactUpdateInfo>> FetchSubscribedArtifactsAsync(
        int? targetPrNumber,
        string? targetBranch,
        string? targetCustomBuild,
        CancellationToken token)
    {
        if (!string.IsNullOrEmpty(targetCustomBuild))
        {
            _logger.LogInformation("Loading artifacts for custom build '{ContentId}'", targetCustomBuild);
            var item = await FindSubscribedCatalogItemAsync(targetCustomBuild, SubscribedCustomBuildPublisherId, updateSubscribedPublisherId: false, token);
            return MapCatalogReleasesToArtifactUpdateInfos(item);
        }

        if (targetPrNumber.HasValue)
        {
            _logger.LogInformation("Loading artifacts for PR #{PrNumber}", targetPrNumber.Value);
            return await _velopackUpdateManager.GetArtifactsForPullRequestAsync(targetPrNumber.Value, token);
        }

        if (!string.IsNullOrEmpty(targetBranch))
        {
            _logger.LogInformation("Loading artifacts for branch '{Branch}'", targetBranch);
            return await _velopackUpdateManager.GetArtifactsForBranchAsync(targetBranch, token);
        }

        return [];
    }

    private async Task<CatalogContentItem?> FindSubscribedCatalogItemAsync(
        string? contentId,
        string? publisherId,
        bool updateSubscribedPublisherId,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(contentId) || _publisherSubscriptionStore == null)
        {
            return null;
        }

        var subsResult = await _publisherSubscriptionStore.GetSubscriptionsAsync(token).ConfigureAwait(false);
        if (!subsResult.Success || subsResult.Data == null)
        {
            return null;
        }

        var candidateSubs = string.IsNullOrWhiteSpace(publisherId)
            ? subsResult.Data
            : subsResult.Data.Where(s => string.Equals(s.PublisherId, publisherId, StringComparison.OrdinalIgnoreCase));

        foreach (var sub in candidateSubs)
        {
            var catalog = await FetchCatalogForSubscriptionAsync(sub, token).ConfigureAwait(false);
            var item = catalog?.Content.FirstOrDefault(c =>
                string.Equals(c.Id, contentId, StringComparison.OrdinalIgnoreCase) &&
                c.ContentType == ContentType.GenHubBuild);

            if (item != null)
            {
                if (updateSubscribedPublisherId && string.IsNullOrWhiteSpace(SubscribedCustomBuildPublisherId) && !string.IsNullOrWhiteSpace(sub.PublisherId))
                {
                    SubscribedCustomBuildPublisherId = sub.PublisherId;
                }

                return item;
            }
        }

        return null;
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private IReadOnlyList<ArtifactUpdateInfo> MapCatalogReleasesToArtifactUpdateInfos(CatalogContentItem? item)
    {
        if (item == null)
        {
            return [];
        }

        var list = new List<ArtifactUpdateInfo>();
        var releaseIdCounter = 1L;

        foreach (var rel in item.Releases
            .OrderByDescending(r => r.Version, Comparer<string>.Create((a, b) =>
            {
                if (AppUpdateVersionHelper.IsArtifactVersionNewer(a, b, allowCrossChannel: true)) return 1;
                if (AppUpdateVersionHelper.IsArtifactVersionNewer(b, a, allowCrossChannel: true)) return -1;
                return 0;
            }))
            .ThenByDescending(r => r.ReleaseDate))
        {
            var art = rel.Artifacts.FirstOrDefault(a => a.IsPrimary && !string.IsNullOrWhiteSpace(a.DownloadUrl)) ??
                      rel.Artifacts.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.DownloadUrl));
            if (art == null)
            {
                continue;
            }

            list.Add(new ArtifactUpdateInfo(
                Version: rel.Version,
                GitHash: string.Empty,
                PullRequestNumber: null,
                WorkflowRunId: 0,
                WorkflowRunUrl: string.Empty,
                ArtifactId: releaseIdCounter++,
                ArtifactName: art.Filename ?? rel.Version,
                CreatedAt: rel.ReleaseDate ?? DateTime.UtcNow,
                DownloadUrl: art.DownloadUrl,
                Size: art.Size));
        }

        return list;
    }

    private async Task CheckSubscribedCustomBuildUpdatesAsync()
    {
        var buildDisplayName = SubscribedCustomBuildName ?? SubscribedCustomBuildContentId ?? "Custom Build";
        _logger.LogInformation("Checking updates for subscribed custom build '{Name}' ({Id})", SubscribedCustomBuildName, SubscribedCustomBuildContentId);
        StatusMessage = string.Format(
            CultureInfo.InvariantCulture,
            GetLocalizedString("Updates.Status.CheckingCustomBuildUpdates", "Checking updates for {0}..."),
            buildDisplayName);

        var matchedItem = await FindSubscribedCatalogItemAsync(
            SubscribedCustomBuildContentId,
            SubscribedCustomBuildPublisherId,
            updateSubscribedPublisherId: true,
            _cancellationTokenSource.Token);

        if (matchedItem == null)
        {
            StatusMessage = string.Format(
                CultureInfo.InvariantCulture,
                GetLocalizedString("Updates.Status.CustomBuildNotFound", "Subscribed build '{0}' not found in publisher subscriptions."),
                buildDisplayName);
            IsUpdateAvailable = false;
            return;
        }

        var latestRelease = matchedItem.Releases
            .Where(r => r.Artifacts.Any(a => !string.IsNullOrWhiteSpace(a.DownloadUrl)))
            .OrderByDescending(r => r.Version, Comparer<string>.Create((a, b) =>
            {
                if (AppUpdateVersionHelper.IsArtifactVersionNewer(a, b, allowCrossChannel: true)) return 1;
                if (AppUpdateVersionHelper.IsArtifactVersionNewer(b, a, allowCrossChannel: true)) return -1;
                return 0;
            }))
            .ThenByDescending(r => r.ReleaseDate)
            .FirstOrDefault();

        if (latestRelease == null || string.IsNullOrWhiteSpace(latestRelease.Version))
        {
            StatusMessage = GetLocalizedString("Updates.Status.CustomBuildNoReleases", "No releases available for this build.");
            IsUpdateAvailable = false;
            return;
        }

        var latestVersion = latestRelease.Version.TrimStart('v', 'V');
        var isNotInstalledYet = string.IsNullOrWhiteSpace(SubscribedCustomBuildVersion);
        var installedVersion = (SubscribedCustomBuildVersion ?? CurrentAppVersion).TrimStart('v', 'V').Split('+')[0];

        var isNewer = AppUpdateVersionHelper.IsArtifactVersionNewer(latestVersion, installedVersion, allowCrossChannel: true);
        if (isNotInstalledYet || isNewer)
        {
            var isDismissed = string.Equals(latestVersion, _userSettingsService.Get().DismissedUpdateVersion?.TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase);
            if (isDismissed && !isNotInstalledYet)
            {
                IsUpdateAvailable = false;
                _logger.LogInformation("Update {Version} was previously dismissed by user", latestRelease.Version);
                return;
            }

            IsUpdateAvailable = true;
            LatestVersion = latestRelease.Version ?? string.Empty;
            StatusMessage = isNotInstalledYet
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    GetLocalizedString("Updates.Status.CustomBuildReadyToInstall", "Ready to install: {0} ({1})"),
                    buildDisplayName,
                    latestRelease.Version)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    GetLocalizedString("Updates.Status.CustomBuildUpdateAvailable", "Update available: {0} for {1}"),
                    latestRelease.Version,
                    buildDisplayName);
            _logger.LogInformation("Custom build update available: {Version}", LatestVersion);
        }
        else
        {
            IsUpdateAvailable = false;
            LatestVersion = string.Empty;
            SelectedVersion = null;
            StatusMessage = string.Format(
                CultureInfo.InvariantCulture,
                GetLocalizedString("Updates.Status.CustomBuildUpToDate", "{0} is up to date (v{1})"),
                buildDisplayName,
                installedVersion);
        }
    }

    /// <summary>
    /// Loads discoverable custom builds and forks across subscribed publisher catalogs.
    /// </summary>
    private async Task LoadCustomBuildsAsync()
    {
        if (_publisherSubscriptionStore == null)
        {
            return;
        }

        try
        {
            var subsResult = await _publisherSubscriptionStore.GetSubscriptionsAsync(_cancellationTokenSource.Token);
            if (!subsResult.Success || subsResult.Data == null)
            {
                return;
            }

            AvailableCustomBuilds.Clear();
            HasCustomBuilds = false;

            foreach (var sub in subsResult.Data)
            {
                var catalog = await FetchCatalogForSubscriptionAsync(sub, _cancellationTokenSource.Token);
                if (catalog?.Content == null)
                {
                    continue;
                }

                foreach (var item in catalog.Content.Where(c => c.ContentType == ContentType.GenHubBuild))
                {
                    var latestRel = item.Releases
                        .Where(r => r.Artifacts.Any(a => !string.IsNullOrWhiteSpace(a.DownloadUrl)))
                        .OrderByDescending(r => r.Version, Comparer<string>.Create((a, b) =>
                        {
                            if (AppUpdateVersionHelper.IsArtifactVersionNewer(a, b, allowCrossChannel: true)) return 1;
                            if (AppUpdateVersionHelper.IsArtifactVersionNewer(b, a, allowCrossChannel: true)) return -1;
                            return 0;
                        }))
                        .ThenByDescending(r => r.ReleaseDate)
                        .FirstOrDefault();
                    AvailableCustomBuilds.Add(new CustomBuildSubscriptionItem
                    {
                        PublisherId = sub.PublisherId,
                        PublisherName = sub.PublisherName ?? sub.PublisherId,
                        ContentId = item.Id,
                        Name = item.Name,
                        Description = item.Description,
                        LatestVersion = latestRel?.Version ?? GenHubBuildConstants.DefaultVersion,
                        ReleaseDate = latestRel?.ReleaseDate,
                        Category = latestRel?.Category ?? GenHubBuildConstants.CategoryCustomFork,
                        IsSubscribed = string.Equals(SubscribedCustomBuildContentId, item.Id, StringComparison.OrdinalIgnoreCase) &&
                                       string.Equals(SubscribedCustomBuildPublisherId, sub.PublisherId, StringComparison.OrdinalIgnoreCase),
                    });
                }
            }

            HasCustomBuilds = AvailableCustomBuilds.Count > 0;
        }
        catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load custom builds from publisher subscriptions");
        }
    }

    private async Task<PublisherCatalog?> FetchCatalogForSubscriptionAsync(PublisherSubscription subscription, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subscription.CatalogUrl) || _publisherCatalogParser == null)
        {
            return null;
        }

        using var client = _httpClientFactory?.CreateClient(CatalogConstants.CatalogHttpClientName) ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
        });
        return await CatalogDocumentReader.FetchAndParseCatalogAsync(client, _publisherCatalogParser, subscription.CatalogUrl, _logger, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Subscribes to a custom community build or fork.
    /// </summary>
    /// <param name="item">The build item to subscribe to.</param>
    [RelayCommand]
    private async Task SubscribeToCustomBuildAsync(CustomBuildSubscriptionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.Equals(SubscribedCustomBuildContentId, item.ContentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(SubscribedCustomBuildPublisherId, item.PublisherId, StringComparison.OrdinalIgnoreCase))
        {
            Unsubscribe();
            return;
        }

        _velopackUpdateManager.SubscribedPrNumber = null;
        _velopackUpdateManager.SubscribedBranch = null;
        _velopackUpdateManager.ClearCache();

        SubscribedPr = null;
        SubscribedBranch = null;
        SubscribedCustomBuildPublisherId = item.PublisherId;
        SubscribedCustomBuildContentId = item.ContentId;
        SubscribedCustomBuildName = item.Name;
        SubscribedCustomBuildVersion = null;

        _userSettingsService.Update(settings =>
        {
            settings.SubscribedPrNumber = null;
            settings.SubscribedBranch = null;
            settings.SubscribedCustomBuildPublisherId = item.PublisherId;
            settings.SubscribedCustomBuildContentId = item.ContentId;
            settings.SubscribedCustomBuildName = item.Name;
            settings.SubscribedCustomBuildVersion = null;
        });
        _ = _userSettingsService.SaveAsync(CancellationToken.None);

        foreach (var build in AvailableCustomBuilds)
        {
            build.IsSubscribed = string.Equals(build.ContentId, item.ContentId, StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(build.PublisherId, item.PublisherId, StringComparison.OrdinalIgnoreCase);
        }

        OnPropertyChanged(nameof(IsSubscribedToAny));
        OnPropertyChanged(nameof(IsSubscribedToCustomBuild));

        await CheckForUpdatesAsync();
        await LoadArtifactsForSubscribedItemAsync();
    }

    private void PopulateAvailableVersions(IReadOnlyList<ArtifactUpdateInfo> artifacts)
    {
        _logger.LogInformation("Received {Count} platform-compatible artifacts from update manager", artifacts.Count);

        var addedArtifactIds = new HashSet<long>();
        foreach (var artifact in artifacts)
        {
            if (addedArtifactIds.Add(artifact.ArtifactId))
            {
                AvailableVersions.Add(artifact);
                _logger.LogDebug("Added artifact: {Version} ({Hash}) - ID: {Id}", artifact.DisplayVersion, artifact.GitHash, artifact.ArtifactId);
            }
            else
            {
                _logger.LogWarning("Duplicate artifact detected in ViewModel: {Version} ({Hash}) - ID: {Id}", artifact.DisplayVersion, artifact.GitHash, artifact.ArtifactId);
            }
        }

        _logger.LogInformation("Loaded {Count} artifacts into AvailableVersions", AvailableVersions.Count);

        if (AvailableVersions.Count > 0)
        {
            SelectedVersion = AvailableVersions[0];
        }
    }

    /// <summary>
    /// Initializes the view model by checking for updates and loading PRs.
    /// </summary>
    private async Task InitializeAsync()
    {
        // load subscribed pr, branch, or custom build from settings with strict mutual exclusivity
        var settings = _userSettingsService.Get();
        if (!string.IsNullOrWhiteSpace(settings.SubscribedCustomBuildContentId))
        {
            SubscribedCustomBuildContentId = settings.SubscribedCustomBuildContentId;
            SubscribedCustomBuildName = settings.SubscribedCustomBuildName;
            SubscribedCustomBuildPublisherId = settings.SubscribedCustomBuildPublisherId;
            SubscribedCustomBuildVersion = settings.SubscribedCustomBuildVersion;
            _velopackUpdateManager.SubscribedPrNumber = null;
            SubscribedPr = null;
            SubscribedBranch = null;
            _logger.LogInformation("Loaded subscribed custom build '{Name}' ({Id}) from settings", SubscribedCustomBuildName, SubscribedCustomBuildContentId);
        }
        else if (settings.SubscribedPrNumber.HasValue)
        {
            var prNumber = settings.SubscribedPrNumber.Value;
            _velopackUpdateManager.SubscribedPrNumber = prNumber;
            SubscribedPr = new PullRequestInfo
            {
                Number = prNumber,
                Title = $"PR #{prNumber}",
                BranchName = "unknown",
                Author = "unknown",
                State = "open",
            };
            SubscribedBranch = null;
            SubscribedCustomBuildContentId = null;
            SubscribedCustomBuildName = null;
            SubscribedCustomBuildPublisherId = null;
            SubscribedCustomBuildVersion = null;
            _logger.LogInformation("Loaded subscribed PR #{PrNumber} from settings", prNumber);
        }
        else if (!string.IsNullOrEmpty(settings.SubscribedBranch))
        {
            SubscribedBranch = settings.SubscribedBranch;
            _velopackUpdateManager.SubscribedPrNumber = null;
            SubscribedPr = null;
            SubscribedCustomBuildContentId = null;
            SubscribedCustomBuildName = null;
            SubscribedCustomBuildPublisherId = null;
            SubscribedCustomBuildVersion = null;
            _logger.LogInformation("Loaded subscribed branch '{Branch}' from settings", settings.SubscribedBranch);
        }

        await LoadCustomBuildsAsync();

        // load data if we are authenticated
        if (IsAuthenticated)
        {
            // initial check and load
            await Task.WhenAll(
                LoadPullRequestsAsync(),
                LoadBranchesAsync());
        }

        // check for updates after subscriptions are populated
        await CheckForUpdatesAsync();
    }

    /// <summary>
    /// Gets the command to check for updates.
    /// </summary>
    public ICommand CheckForUpdatesCommand { get; }

    /// <summary>
    /// Gets the command to manually refresh all update data (clears cache).
    /// </summary>
    public ICommand ManualRefreshCommand { get; }

    /// <summary>
    /// Gets the command to dismiss the update notification.
    /// </summary>
    public ICommand DismissCommand { get; }

    /// <summary>
    /// Gets a value indicating whether an update is available and can be downloaded.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel property bound to UI elements")]
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel property bound to UI elements")]
    public bool CanDownloadUpdate => (IsUpdateAvailable || SelectedVersion != null) && !IsInstalling && !IsDownloading && !IsChecking && !IsLoadingVersions;

    /// <summary>
    /// Gets a value indicating whether the check button should be enabled.
    /// </summary>
    public bool IsCheckButtonEnabled => !IsChecking;

    /// <summary>
    /// Gets a value indicating whether an operation is currently loading versions, checking updates, or installing.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel property bound to UI elements")]
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel property bound to UI elements")]
    public bool IsLoadingOrInstalling => IsLoadingVersions || IsChecking || IsInstalling || IsDownloading;

    /// <summary>
    /// Gets the text for the install button.
    /// </summary>
    public string InstallButtonText
    {
        get
        {
            if (IsDownloading)
            {
                return _localizationService?.GetString("Downloads.Status.Downloading") ?? "Downloading...";
            }

            if (IsInstalling)
            {
                return _localizationService?.GetString("Updates.Button.Installing") ?? AppUpdateConstants.InstallingMessage;
            }

            if (IsChecking || IsLoadingVersions)
            {
                return _localizationService?.GetString("Tools.Status.Loading") ?? AppUpdateConstants.LoadingMessage;
            }

            return _localizationService?.GetString("Updates.Button.InstallUpdate") ?? AppUpdateConstants.InstallUpdateAction;
        }
    }

    /// <summary>
    /// Gets the latest version string, ensuring it has a 'v' prefix for display.
    /// </summary>
    public string DisplayLatestVersion
    {
        get
        {
            if (IsChecking)
            {
                return "Checking...";
            }

            if (string.IsNullOrEmpty(LatestVersion))
            {
                return GameClientConstants.UnknownVersion;
            }

            // 1. pr update takes precedence
            if (SubscribedPr?.LatestArtifact != null &&
                string.Equals(SubscribedPr.LatestArtifact.Version, LatestVersion, StringComparison.OrdinalIgnoreCase))
            {
                return SubscribedPr.LatestArtifact.DisplayVersion;
            }

            // 2. branch update
            if (!string.IsNullOrEmpty(SubscribedBranch))
            {
                return LatestVersion.StartsWith(SubscribedBranch, StringComparison.OrdinalIgnoreCase)
                    ? LatestVersion
                    : $"{SubscribedBranch} build {LatestVersion}";
            }

            // 3. custom build update
            if (IsSubscribedToCustomBuild)
            {
                var cleanVersion = LatestVersion.TrimStart('v', 'V');
                return $"{SubscribedCustomBuildName ?? "Custom Build"} v{cleanVersion}";
            }

            return LatestVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                ? LatestVersion
                : $"v{LatestVersion}";
        }
    }

    /// <summary>
    /// Disposes of managed resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_localizationService != null)
        {
            _localizationService.PropertyChanged -= OnLocalizationPropertyChanged;
        }

        if (_gitHubAuthService != null)
        {
            _gitHubAuthService.AuthStateChanged -= OnGitHubAuthStateChanged;
        }

        _loadArtifactsCts?.Cancel();
        _loadArtifactsCts?.Dispose();
        _loadArtifactsCts = null;

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        GC.SuppressFinalize(this);
    }

    private string GetLocalizedString(string key, string fallback)
    {
        return _localizationService?.GetString(key) ?? fallback;
    }

    private string FormatLocalizedString(string key, string fallbackFormat, params object[] args)
    {
        var pattern = _localizationService?.GetString(key);
        if (string.IsNullOrEmpty(pattern))
        {
            return string.Format(fallbackFormat, args);
        }

        return string.Format(pattern, args);
    }

    private void ProcessPrArtifactUpdate(ArtifactUpdateInfo artifact, int prNumber)
    {
        var currentVersionBase = CurrentAppVersion.Split('+')[0];
        var prVersionBase = artifact.Version.Split('+')[0];

        if (AppUpdateVersionHelper.IsArtifactVersionNewer(prVersionBase, currentVersionBase, allowCrossChannel: true))
        {
            var settings = _userSettingsService.Get();
            if (!string.Equals(prVersionBase, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                IsUpdateAvailable = true;
                LatestVersion = prVersionBase;
                ReleaseNotesUrl = $"{AppConstants.GitHubRepositoryUrl}/pull/{prNumber}";
                StatusMessage = FormatLocalizedString("Updates.Status.NewPrBuild", "New PR build available: {0}", artifact.DisplayVersion);
                _logger.LogInformation("Subscribed to PR #{PrNumber}, new build available: {Version}", prNumber, artifact.DisplayVersion);
                return;
            }

            StatusMessage = FormatLocalizedString("Updates.Status.PrDismissed", "You dismissed the update for PR #{0}", prNumber);
            return;
        }

        IsUpdateAvailable = false;
        StatusMessage = FormatLocalizedString("Updates.Status.PrLatest", "You are on the latest build for PR #{0}", prNumber);
    }

    private void ProcessBranchArtifactUpdate(ArtifactUpdateInfo artifact, string branch)
    {
        var currentVersionBase = CurrentAppVersion.Split('+')[0];
        var branchVersionBase = artifact.Version.Split('+')[0];

        if (AppUpdateVersionHelper.IsArtifactVersionNewer(branchVersionBase, currentVersionBase, allowCrossChannel: true))
        {
            var settings = _userSettingsService.Get();
            if (!string.Equals(branchVersionBase, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                IsUpdateAvailable = true;
                LatestVersion = branchVersionBase;
                ReleaseNotesUrl = $"{AppConstants.GitHubRepositoryUrl}/tree/{branch}";
                StatusMessage = FormatLocalizedString("Updates.Status.NewBranchBuild", "New {0} build available: {1}", branch, artifact.DisplayVersion);
                _logger.LogInformation("Branch '{Branch}' has new build: {Version}", branch, LatestVersion);
                return;
            }

            StatusMessage = FormatLocalizedString("Updates.Status.BranchDismissed", "You dismissed the update for branch '{0}'", branch);
            return;
        }

        IsUpdateAvailable = false;
        StatusMessage = FormatLocalizedString("Updates.Status.BranchLatest", "You are on the latest build for {0}", branch);
    }

    partial void OnSelectedVersionChanged(ArtifactUpdateInfo? value)
    {
        UpdateCommandStates();

        if (value == null)
        {
            return;
        }

        var currentVersionBase = CurrentAppVersion.Split('+')[0];
        var selectedVersionBase = value.Version.Split('+')[0];

        if (AppUpdateVersionHelper.IsArtifactVersionNewer(selectedVersionBase, currentVersionBase, allowCrossChannel: true))
        {
            var settings = _userSettingsService.Get();
            if (!string.Equals(selectedVersionBase, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                IsUpdateAvailable = true;
                LatestVersion = selectedVersionBase;
                if (!string.IsNullOrEmpty(SubscribedBranch))
                {
                    ReleaseNotesUrl = $"{AppConstants.GitHubRepositoryUrl}/tree/{SubscribedBranch}";
                    StatusMessage = FormatLocalizedString("Updates.Status.NewBranchBuild", "New {0} build available: {1}", SubscribedBranch, value.DisplayVersion);
                }
                else if (value.PullRequestNumber.HasValue)
                {
                    ReleaseNotesUrl = $"{AppConstants.GitHubRepositoryUrl}/pull/{value.PullRequestNumber.Value}";
                    StatusMessage = FormatLocalizedString("Updates.Status.NewPrBuild", "New PR build available: {0}", value.DisplayVersion);
                }
                else
                {
                    StatusMessage = FormatLocalizedString("Updates.Status.NewBuild", "New build available: {0}", value.DisplayVersion);
                }

                return;
            }

            IsUpdateAvailable = false;
            LatestVersion = string.Empty;
            ReleaseNotesUrl = string.Empty;
            StatusMessage = FormatLocalizedString("Updates.Status.BuildDismissed", "You dismissed update {0}", value.DisplayVersion);
            return;
        }

        var currentRun = AppUpdateVersionHelper.ExtractRunNumber(currentVersionBase);
        var selectedRun = AppUpdateVersionHelper.ExtractRunNumber(selectedVersionBase);

        if (currentRun > 0 && selectedRun > 0 && currentRun == selectedRun)
        {
            IsUpdateAvailable = false;
            if (value.PullRequestNumber.HasValue)
            {
                StatusMessage = FormatLocalizedString("Updates.Status.PrLatest", "You are on the latest build for PR #{0}", value.PullRequestNumber.Value);
            }
            else if (!string.IsNullOrEmpty(SubscribedBranch))
            {
                StatusMessage = FormatLocalizedString("Updates.Status.BranchLatest", "You are on the latest build for {0}", SubscribedBranch);
            }
            else
            {
                StatusMessage = FormatLocalizedString("Updates.Status.BuildLatest", "You are on the latest build ({0})", value.DisplayVersion);
            }
        }
        else
        {
            IsUpdateAvailable = false;
            StatusMessage = $"Selected build: {value.DisplayVersion}";
        }
    }

    /// <summary>
    /// Checks for updates asynchronously using Velopack.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    private async Task CheckForUpdatesAsync()
    {
        if (IsChecking)
        {
            return;
        }

        try
        {
            IsChecking = true;
            HasError = false;
            ErrorMessage = string.Empty;
            StatusMessage = GetLocalizedString("Updates.Status.CheckingForUpdates", "Checking for updates...");
            IsUpdateAvailable = false;
            ShowPrMergedWarning = false;

            _logger.LogInformation("Starting Velopack update check");

            // check custom build updates if subscribed
            if (IsSubscribedToCustomBuild)
            {
                await CheckSubscribedCustomBuildUpdatesAsync();
                return;
            }

            // check if subscribed to a pr
            if (SubscribedPr != null)
            {
                if (!IsAuthenticated)
                {
                    _logger.LogInformation("Subscribed to PR #{PrNumber} but GitHub authentication is not configured", SubscribedPr.Number);
                    StatusMessage = GetLocalizedString("Updates.Status.AuthRequiredForArtifacts", AppUpdateConstants.AuthRequiredForArtifactsMessage);
                    IsUpdateAvailable = false;
                    return;
                }

                if (SubscribedPr.LatestArtifact != null)
                {
                    ProcessPrArtifactUpdate(SubscribedPr.LatestArtifact, SubscribedPr.Number);
                    return;
                }

                // try to fetch artifact for update check
                _logger.LogInformation("PR #{PrNumber} has no cached artifact, fetching for update check", SubscribedPr.Number);
                var prArtifact = await _velopackUpdateManager.CheckForArtifactUpdatesAsync(_cancellationTokenSource.Token);
                if (prArtifact != null)
                {
                    ProcessPrArtifactUpdate(prArtifact, SubscribedPr.Number);
                    return;
                }

                if (_velopackUpdateManager.IsPrMergedOrClosed)
                {
                    ShowPrMergedWarning = true;
                    StatusMessage = string.Format(AppUpdateConstants.PrMergedStatusMessageFormat, SubscribedPr.Number);
                    IsUpdateAvailable = false;
                    _logger.LogInformation("Subscribed PR #{PrNumber} is merged or closed", SubscribedPr.Number);
                    return;
                }

                // if subscribed to pr but no artifact found, do not fall through to main release
                _logger.LogInformation("Subscribed to PR #{PrNumber} but no artifact available yet", SubscribedPr.Number);
                StatusMessage = $"Waiting for PR #{SubscribedPr.Number} build...";
                IsUpdateAvailable = false;
                return;
            }

            // check branch updates if subscribed
            if (!string.IsNullOrEmpty(SubscribedBranch))
            {
                if (string.Equals(SubscribedBranch, AppUpdateConstants.MainBranch, StringComparison.OrdinalIgnoreCase))
                {
                    if (IsAuthenticated)
                    {
                        _logger.LogInformation("Checking for artifact updates on main branch");
                        var mainArtifact = await _velopackUpdateManager.CheckForArtifactUpdatesAsync(_cancellationTokenSource.Token);
                        if (mainArtifact != null)
                        {
                            ProcessBranchArtifactUpdate(mainArtifact, SubscribedBranch);
                            return;
                        }
                    }

                    _logger.LogInformation("Subscribed to main branch; proceeding to release check");
                }
                else
                {
                    if (!IsAuthenticated)
                    {
                        _logger.LogInformation("Subscribed to branch '{Branch}' but GitHub authentication is not configured", SubscribedBranch);
                        StatusMessage = GetLocalizedString("Updates.Status.AuthRequiredForArtifacts", AppUpdateConstants.AuthRequiredForArtifactsMessage);
                        IsUpdateAvailable = false;
                        return;
                    }

                    _logger.LogInformation("Checking for artifact updates on branch: {Branch}", SubscribedBranch);
                    var branchArtifact = await _velopackUpdateManager.CheckForArtifactUpdatesAsync(_cancellationTokenSource.Token);

                    if (branchArtifact != null)
                    {
                        ProcessBranchArtifactUpdate(branchArtifact, SubscribedBranch);
                        return;
                    }

                    // if subscribed to branch but no artifact found, do not fall through to main release
                    _logger.LogInformation("Subscribed to branch '{Branch}' but no artifact available yet", SubscribedBranch);
                    StatusMessage = string.Equals(SubscribedBranch, AppUpdateConstants.DevelopmentBranch, StringComparison.OrdinalIgnoreCase)
                        ? $"Waiting for {SubscribedBranch} build..."
                        : string.Format(AppUpdateConstants.BranchStaleStatusMessageFormat, SubscribedBranch);
                    IsUpdateAvailable = false;
                    return;
                }
            }

            // check main branch releases
            _currentUpdateInfo = await _velopackUpdateManager.CheckForUpdatesAsync(_cancellationTokenSource.Token);

            if (_currentUpdateInfo != null)
            {
                var version = _currentUpdateInfo.TargetFullRelease.Version.ToString();
                var settings = _userSettingsService.Get();
                if (!string.Equals(version, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
                {
                    IsUpdateAvailable = true;
                    LatestVersion = version;
                    ReleaseNotesUrl = AppConstants.GitHubRepositoryUrl + "/releases/tag/v" + LatestVersion;
                    StatusMessage = FormatLocalizedString("Updates.Status.UpdateAvailable", "Update available: {0}", $"v{LatestVersion}");
                    _logger.LogInformation("Update available from UpdateManager: {Version}", LatestVersion);
                }
                else
                {
                    StatusMessage = GetLocalizedString("Updates.Status.UpToDate", "You're up to date!");
                }
            }
            else if (_velopackUpdateManager.HasUpdateAvailableFromGitHub)
            {
                var githubVersion = _velopackUpdateManager.LatestVersionFromGitHub;
                var settings = _userSettingsService.Get();
                if (!string.Equals(githubVersion, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
                {
                    IsUpdateAvailable = true;
                    LatestVersion = githubVersion ?? GameClientConstants.UnknownVersion;
                    ReleaseNotesUrl = AppConstants.GitHubRepositoryUrl + "/releases/tag/v" + LatestVersion;
                    StatusMessage = FormatLocalizedString("Updates.Status.UpdateAvailable", "Update available: {0}", $"v{LatestVersion}");
                    _logger.LogInformation("Update available from GitHub API: {Version}", LatestVersion);
                }
                else
                {
                    StatusMessage = GetLocalizedString("Updates.Status.UpToDate", "You're up to date!");
                }
            }
            else
            {
                IsUpdateAvailable = false;
                LatestVersion = string.Empty;
                StatusMessage = GetLocalizedString("Updates.Status.UpToDate", "You're up to date!");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update check failed");
            HasError = true;
            ErrorMessage = $"Failed to check for updates: {ex.Message}";
            StatusMessage = GetLocalizedString("Updates.Status.UpdateCheckFailed", "Update check failed");
            IsUpdateAvailable = false;
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// Manually refreshes all update data, clearing the cache and dismissing status.
    /// </summary>
    private async Task ManualRefreshAsync()
    {
        if (IsChecking) return;

        _logger.LogInformation("Manual refresh requested - clearing cache and dismissal status");

        // clear dismissal status in settings so the user can see the update again
        var settings = _userSettingsService.Get();
        if (!string.IsNullOrEmpty(settings.DismissedUpdateVersion))
        {
            _userSettingsService.Update(s => s.DismissedUpdateVersion = string.Empty);
            await _userSettingsService.SaveAsync(CancellationToken.None);
        }

        // clear manager cache
        _velopackUpdateManager.ClearCache();

        // reload data
        if (IsAuthenticated)
        {
            await Task.WhenAll(
                LoadPullRequestsAsync(),
                LoadBranchesAsync());
        }

        await CheckForUpdatesAsync();
    }

    /// <summary>
    /// Shows the update tab.
    /// </summary>
    [RelayCommand]
    private void ShowUpdateTab()
    {
        SelectedTabIndex = AppUpdateConstants.UpdateTabIndex;
    }

    /// <summary>
    /// Shows the browse builds tab.
    /// </summary>
    [RelayCommand]
    private void ShowBrowseBuildsTab()
    {
        SelectedTabIndex = AppUpdateConstants.BrowseBuildsTabIndex;
    }

    /// <summary>
    /// Selects the specified tab by index (0 = Update, 1 = Browse Builds).
    /// </summary>
    /// <param name="parameter">The tab index to select.</param>
    [RelayCommand]
    private void SelectTab(object? parameter)
    {
        if (parameter is int i)
        {
            SelectedTabIndex = Math.Clamp(i, AppUpdateConstants.UpdateTabIndex, AppUpdateConstants.MaxTabIndex);
        }
        else if (parameter is string s && int.TryParse(s, out var parsed))
        {
            SelectedTabIndex = Math.Clamp(parsed, AppUpdateConstants.UpdateTabIndex, AppUpdateConstants.MaxTabIndex);
        }
    }

    /// <summary>
    /// Opens the release notes in the default browser.
    /// </summary>
    [RelayCommand]
    private void ViewReleaseNotes()
    {
        if (!string.IsNullOrEmpty(ReleaseNotesUrl))
        {
            try
            {
                Process.Start(new ProcessStartInfo(ReleaseNotesUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open browser for release notes");
            }
        }
    }

    /// <summary>
    /// Opens the specified pull request in the default browser.
    /// </summary>
    /// <param name="prNumber">The PR number to open.</param>
    [RelayCommand]
    private void OpenPullRequestUrl(int prNumber)
    {
        if (prNumber <= 0)
        {
            return;
        }

        var url = $"{AppConstants.GitHubRepositoryUrl}/pull/{prNumber}";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open browser for PR #{PrNumber}", prNumber);
        }
    }

    /// <summary>
    /// Downloads and applies the update using Velopack.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownloadUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (!CanDownloadUpdate)
        {
            return;
        }

        // 0. handle explicitly selected version
        if (SelectedVersion != null)
        {
            _logger.LogInformation("Installing selected artifact version: {Version}", SelectedVersion.DisplayVersion);
            await InstallArtifactAsync(SelectedVersion);
            return;
        }

        // 0.5 handle custom build update
        if (IsSubscribedToCustomBuild)
        {
            if (AvailableVersions.Count == 0)
            {
                await LoadArtifactsForSubscribedItemAsync();
            }

            if (AvailableVersions.Count > 0)
            {
                var targetArtifact = AvailableVersions.FirstOrDefault(a =>
                    string.Equals(a.Version, LatestVersion, StringComparison.OrdinalIgnoreCase)) ?? AvailableVersions[0];
                _logger.LogInformation("Installing custom build update: {Version}", targetArtifact.DisplayVersion);
                await InstallArtifactAsync(targetArtifact);
                return;
            }

            _logger.LogError("Cannot install custom build - no versions available");
            HasError = true;
            ErrorMessage = GetLocalizedString("Updates.Error.NoVersionsAvailable", "No versions available for the subscribed custom build");
            StatusMessage = AppUpdateConstants.UpdateFailedMessage;
            return;
        }

        // 1. handle pr artifact update
        if (SubscribedPr?.LatestArtifact != null &&
            string.Equals(SubscribedPr.LatestArtifact.Version, LatestVersion, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Installing PR artifact update via InstallUpdateAsync override");
            await InstallPrArtifactAsync();
            return;
        }

        // 1.5 handle branch artifact update
        if (!string.IsNullOrEmpty(SubscribedBranch))
        {
            _logger.LogInformation("Installing Branch '{Branch}' artifact update", SubscribedBranch);
            await InstallBranchArtifactAsync();
            return;
        }

        // 2. handle standard velopack update
        if (_currentUpdateInfo == null)
        {
            _logger.LogError("Cannot install update - UpdateInfo is null (app not installed via Setup.exe)");
            HasError = true;
            ErrorMessage = string.Format(AppUpdateConstants.UpdateInstallationRequiresAppInstalledMessage, AppDomain.CurrentDomain.BaseDirectory, LatestVersion);
            StatusMessage = AppUpdateConstants.CannotInstallFromLocationMessage;
            return;
        }

        try
        {
            IsInstalling = true;
            IsDownloading = true;
            HasError = false;
            ErrorMessage = string.Empty;
            StatusMessage = AppUpdateConstants.DownloadingUpdateMessage;
            InstallationProgress = new UpdateProgress { Status = AppUpdateConstants.DownloadingUpdateMessage, PercentComplete = 0 };

            var progress = CreateInstallationProgress(this);

            await _velopackUpdateManager.DownloadUpdatesAsync(_currentUpdateInfo, progress, _cancellationTokenSource.Token);

            StatusMessage = AppUpdateConstants.UpdateDownloadedRestartingMessage;
            InstallationProgress = new UpdateProgress
            {
                Status = AppUpdateConstants.UpdateCompleteRestartingMessage,
                PercentComplete = 100,
                IsCompleted = true,
            };

            await Task.Delay(1500); // Brief delay to show completion message

            await _velopackUpdateManager.ApplyUpdatesAndRestartAsync(_currentUpdateInfo, _cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install update");
            HasError = true;
            ErrorMessage = $"Update failed: {ex.Message}";
            StatusMessage = AppUpdateConstants.UpdateFailedMessage;
            InstallationProgress = new UpdateProgress
            {
                Status = AppUpdateConstants.InstallationFailedMessage,
                HasError = true,
                ErrorMessage = ex.Message,
            };
        }
        finally
        {
            IsDownloading = false;
            IsInstalling = false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the branch artifact can be installed.
    /// </summary>
    public bool CanInstallBranchArtifact => !string.IsNullOrEmpty(SubscribedBranch) && !IsInstalling && !IsDownloading;

    /// <summary>
    /// Installs the subscribed PR artifact.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstallPrArtifact))]
    private async Task InstallPrArtifactAsync()
    {
        if (SubscribedPr == null)
        {
            _logger.LogWarning("Cannot install PR artifact - no PR subscribed");
            return;
        }

        IsInstalling = true;
        HasError = false;
        ErrorMessage = string.Empty;
        DownloadProgress = 0;

        try
        {
            _logger.LogInformation("Installing PR #{Number} artifact", SubscribedPr.Number);

            var progress = CreateInstallationProgress(this);

            ArtifactUpdateInfo? artifactToInstall = SubscribedPr.LatestArtifact;
            if (artifactToInstall == null)
            {
                // clear cache to force fresh check
                _velopackUpdateManager.ClearCache();

                // try to fetch the latest artifact for the pr
                artifactToInstall = await _velopackUpdateManager.CheckForArtifactUpdatesAsync(_cancellationTokenSource.Token);
                if (artifactToInstall == null)
                {
                    _logger.LogWarning("No artifact found for PR #{Number}", SubscribedPr.Number);
                    HasError = true;
                    ErrorMessage = $"No artifact found for PR #{SubscribedPr.Number}";
                    StatusMessage = AppUpdateConstants.NoArtifactAvailableMessage;
                    return;
                }
            }

            await _velopackUpdateManager.InstallArtifactAsync(artifactToInstall, progress, _cancellationTokenSource.Token);

            // app will restart, this code will not execute
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install PR artifact");
            HasError = true;
            ErrorMessage = $"PR installation failed: {ex.Message}";
            StatusMessage = GetLocalizedString(InstallationFailedLocalizationKey, AppUpdateConstants.InstallationFailedMessage);
            InstallationProgress = new UpdateProgress
            {
                Status = GetLocalizedString(InstallationFailedLocalizationKey, AppUpdateConstants.InstallationFailedMessage),
                HasError = true,
                ErrorMessage = ex.Message,
            };
        }
        finally
        {
            IsDownloading = false;
            IsInstalling = false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the PR artifact can be installed.
    /// </summary>
    public bool CanInstallPrArtifact => SubscribedPr != null && !IsInstalling && !IsDownloading;

    /// <summary>
    /// Installs the subscribed branch artifact.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstallBranchArtifact))]
    private async Task InstallBranchArtifactAsync()
    {
        if (string.IsNullOrEmpty(SubscribedBranch))
        {
            _logger.LogWarning("Cannot install branch artifact - no branch subscribed");
            return;
        }

        IsInstalling = true;
        HasError = false;
        ErrorMessage = string.Empty;
        DownloadProgress = 0;

        try
        {
            _logger.LogInformation("Installing branch '{Branch}' artifact", SubscribedBranch);

            var progress = CreateInstallationProgress(this);

            // clear cache to force fresh check
            _velopackUpdateManager.ClearCache();

            // check for latest artifact for the subscribed branch
            var artifactUpdate = await _velopackUpdateManager.CheckForArtifactUpdatesAsync(_cancellationTokenSource.Token);
            if (artifactUpdate == null)
            {
                _logger.LogWarning("No artifact found for branch '{Branch}'", SubscribedBranch);
                HasError = true;
                ErrorMessage = $"No artifact found for branch '{SubscribedBranch}'";
                StatusMessage = GetLocalizedString("Updates.Status.NoArtifactAvailable", AppUpdateConstants.NoArtifactAvailableMessage);
                return;
            }

            await _velopackUpdateManager.InstallArtifactAsync(artifactUpdate, progress, _cancellationTokenSource.Token);

            // app will restart, this code will not execute
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install branch artifact");
            HasError = true;
            ErrorMessage = $"Branch installation failed: {ex.Message}";
            StatusMessage = GetLocalizedString(InstallationFailedLocalizationKey, AppUpdateConstants.InstallationFailedMessage);
            InstallationProgress = new UpdateProgress
            {
                Status = GetLocalizedString(InstallationFailedLocalizationKey, AppUpdateConstants.InstallationFailedMessage),
                HasError = true,
                ErrorMessage = ex.Message,
            };
        }
        finally
        {
            IsDownloading = false;
            IsInstalling = false;
        }
    }

    private async Task InstallArtifactAsync(ArtifactUpdateInfo artifact)
    {
        IsInstalling = true;
        IsDownloading = true;
        HasError = false;
        ErrorMessage = string.Empty;
        DownloadProgress = 0;

        var previousCustomBuildVersion = SubscribedCustomBuildVersion;
        try
        {
            _logger.LogInformation("Installing artifact: {Name} ({Version})", artifact.ArtifactName, artifact.Version);

            if (IsSubscribedToCustomBuild && !string.IsNullOrWhiteSpace(artifact.Version))
            {
                SubscribedCustomBuildVersion = artifact.Version;
                _userSettingsService.Update(settings =>
                {
                    settings.SubscribedCustomBuildVersion = artifact.Version;
                });
                await _userSettingsService.SaveAsync(CancellationToken.None);
            }

            var progress = CreateInstallationProgress(this);

            await _velopackUpdateManager.InstallArtifactAsync(artifact, progress, _cancellationTokenSource.Token);

            // app will restart
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install artifact");
            if (IsSubscribedToCustomBuild)
            {
                SubscribedCustomBuildVersion = previousCustomBuildVersion;
                _userSettingsService.Update(settings =>
                {
                    settings.SubscribedCustomBuildVersion = previousCustomBuildVersion;
                });
                await _userSettingsService.SaveAsync(CancellationToken.None);
            }

            HasError = true;
            ErrorMessage = $"Installation failed: {ex.Message}";
            StatusMessage = GetLocalizedString(InstallationFailedLocalizationKey, AppUpdateConstants.InstallationFailedMessage);
            InstallationProgress = new UpdateProgress
            {
                Status = GetLocalizedString(InstallationFailedLocalizationKey, AppUpdateConstants.InstallationFailedMessage),
                HasError = true,
                ErrorMessage = ex.Message,
            };
        }
        finally
        {
            IsDownloading = false;
            IsInstalling = false;
        }
    }

    /// <summary>
    /// Dismisses the update notification and persists the dismissed version.
    /// </summary>
    private void DismissUpdate()
    {
        if (!string.IsNullOrEmpty(LatestVersion))
        {
            _userSettingsService.Update(s => s.DismissedUpdateVersion = LatestVersion.TrimStart('v', 'V'));
            _ = _userSettingsService.SaveAsync(CancellationToken.None);
            _logger.LogInformation("Dismissed update version {Version}", LatestVersion);
        }

        IsUpdateAvailable = false;
        _currentUpdateInfo = null;
        StatusMessage = "Update dismissed";
        HasError = false;
        ErrorMessage = string.Empty;
        LatestVersion = string.Empty;
    }

    partial void OnIsCheckingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCheckButtonEnabled));
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateCommandStates();
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(UpdateCommandStates);
        }
    }

    partial void OnIsLoadingVersionsChanged(bool value)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateCommandStates();
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(UpdateCommandStates);
        }
    }

    partial void OnIsUpdateAvailableChanged(bool value)
    {
        RunOnUi(UpdateCommandStates);
    }

    partial void OnIsDownloadingChanged(bool value)
    {
        RunOnUi(UpdateCommandStates);
    }

    partial void OnIsInstallingChanged(bool value)
    {
        RunOnUi(UpdateCommandStates);
    }

    private void UpdateCommandStates()
    {
        OnPropertyChanged(nameof(CanDownloadUpdate));
        OnPropertyChanged(nameof(CanInstallPrArtifact));
        OnPropertyChanged(nameof(CanInstallBranchArtifact));
        OnPropertyChanged(nameof(DisplayLatestVersion));
        OnPropertyChanged(nameof(InstallButtonText));
        OnPropertyChanged(nameof(IsLoadingOrInstalling));
        InstallUpdateCommand.NotifyCanExecuteChanged();
        InstallPrArtifactCommand.NotifyCanExecuteChanged();
        InstallBranchArtifactCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task LoadPullRequestsAsync()
    {
        if (_disposed || !IsAuthenticated || IsLoadingPullRequests) return;

        IsLoadingPullRequests = true;
        await RunOnUiAsync(() => AvailablePullRequests.Clear());

        try
        {
            _logger.LogInformation("Loading open pull requests with artifacts");
            var prs = await _velopackUpdateManager.GetOpenPullRequestsAsync(_cancellationTokenSource.Token);
            if (_disposed || !IsAuthenticated)
            {
                return;
            }

            await RunOnUiAsync(() =>
            {
                _allPullRequests.Clear();
                _allPullRequests.AddRange(prs);
                ApplyPullRequestSorting();
            });

            if (_velopackUpdateManager.IsPrMergedOrClosed && _velopackUpdateManager.SubscribedPrNumber.HasValue)
            {
                ShowPrMergedWarning = true;
                StatusMessage = string.Format(AppUpdateConstants.PrMergedStatusMessageFormat, _velopackUpdateManager.SubscribedPrNumber.Value);
                _logger.LogInformation("Subscribed PR has been merged/closed, showing warning");
            }

            if (_velopackUpdateManager.SubscribedPrNumber.HasValue)
            {
                var matchingPr = AvailablePullRequests.FirstOrDefault(p => p.Number == _velopackUpdateManager.SubscribedPrNumber.Value);
                if (matchingPr != null && (SubscribedPr == null || SubscribedPr.Number == matchingPr.Number))
                {
                    SubscribedPr = matchingPr;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load pull requests");
            StatusMessage = "Failed to load PRs";
        }
        finally
        {
            IsLoadingPullRequests = false;
        }
    }

    private void ApplyPullRequestSorting()
    {
        if (Avalonia.Application.Current != null && !Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ApplyPullRequestSorting);
            return;
        }

        if (_allPullRequests.Count == 0 && AvailablePullRequests.Count == 0)
        {
            return;
        }

        if (_allPullRequests.Count == 0 && AvailablePullRequests.Count > 0)
        {
            _allPullRequests.AddRange(AvailablePullRequests);
        }

        IEnumerable<PullRequestInfo> sorted = SelectedSortOption switch
        {
            AppUpdateConstants.SortOptionPrNumberDesc => _allPullRequests.OrderByDescending(p => p.Number),
            AppUpdateConstants.SortOptionPrNumberAsc => _allPullRequests.OrderBy(p => p.Number),
            _ => _allPullRequests.OrderByDescending(p => p.UpdatedAt ?? DateTimeOffset.MinValue),
        };

        var sortedList = sorted.ToList();
        AvailablePullRequests.Clear();
        foreach (var pr in sortedList)
        {
            AvailablePullRequests.Add(pr);
        }
    }

    [RelayCommand]
    private async Task LoadBranchesAsync()
    {
        if (_disposed || !IsAuthenticated || IsLoadingBranches) return;

        IsLoadingBranches = true;
        await RunOnUiAsync(() => AvailableBranches.Clear());

        try
        {
            _logger.LogInformation("Loading repository branches");
            var branches = await _velopackUpdateManager.GetBranchesAsync(_cancellationTokenSource.Token);
            if (_disposed || !IsAuthenticated)
            {
                return;
            }

            await RunOnUiAsync(() =>
            {
                foreach (var branch in branches)
                {
                    AvailableBranches.Add(branch);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load branches");
            StatusMessage = "Failed to load branches";
        }
        finally
        {
            IsLoadingBranches = false;
        }
    }

    [RelayCommand]
    private void SubscribeToPr(int prNumber)
    {
        if (SubscribedPr?.Number == prNumber)
        {
            Unsubscribe();
            return;
        }

        _velopackUpdateManager.SubscribedPrNumber = prNumber;
        _velopackUpdateManager.SubscribedBranch = null;
        SubscribedBranch = null;
        SubscribedCustomBuildPublisherId = null;
        SubscribedCustomBuildContentId = null;
        SubscribedCustomBuildName = null;
        SubscribedCustomBuildVersion = null;
        foreach (var build in AvailableCustomBuilds)
        {
            build.IsSubscribed = false;
        }

        OnPropertyChanged(nameof(IsSubscribedToCustomBuild));

        ShowPrMergedWarning = false;
        IsUpdateAvailable = false;
        SelectedVersion = null;
        LatestVersion = string.Empty;
        ReleaseNotesUrl = string.Empty;
        _currentUpdateInfo = null;

        SubscribedPr = AvailablePullRequests.FirstOrDefault(p => p.Number == prNumber) ?? new PullRequestInfo
        {
            Number = prNumber,
            Title = $"PR #{prNumber}",
            BranchName = "unknown",
            Author = "unknown",
            State = "open",
        };

        // clear artifact cache to force fresh check
        _velopackUpdateManager.ClearCache();

        _userSettingsService.Update(settings =>
        {
            settings.SubscribedPrNumber = prNumber;
            settings.SubscribedBranch = null;
            settings.SubscribedCustomBuildPublisherId = null;
            settings.SubscribedCustomBuildContentId = null;
            settings.SubscribedCustomBuildName = null;
            settings.SubscribedCustomBuildVersion = null;
        });
        _ = _userSettingsService.SaveAsync(CancellationToken.None);

        StatusMessage = $"Subscribed to PR #{prNumber}: {SubscribedPr.Title}";
        _logger.LogInformation("Subscribed to PR #{PrNumber}", prNumber);
    }

    [RelayCommand]
    private void SubscribeToBranch(string branchName)
    {
        if (string.IsNullOrEmpty(branchName)) return;

        if (string.Equals(SubscribedBranch, branchName, StringComparison.Ordinal))
        {
            Unsubscribe();
            return;
        }

        _velopackUpdateManager.SubscribedPrNumber = null;
        _velopackUpdateManager.SubscribedBranch = branchName;
        SubscribedPr = null;
        SubscribedCustomBuildPublisherId = null;
        SubscribedCustomBuildContentId = null;
        SubscribedCustomBuildName = null;
        SubscribedCustomBuildVersion = null;
        foreach (var build in AvailableCustomBuilds)
        {
            build.IsSubscribed = false;
        }

        OnPropertyChanged(nameof(IsSubscribedToCustomBuild));

        ShowPrMergedWarning = false;
        IsUpdateAvailable = false;
        SelectedVersion = null;
        LatestVersion = string.Empty;
        ReleaseNotesUrl = string.Empty;
        _currentUpdateInfo = null;

        SubscribedBranch = branchName;

        // clear artifact cache to force fresh check
        _velopackUpdateManager.ClearCache();

        _userSettingsService.Update(settings =>
        {
            settings.SubscribedBranch = branchName;
            settings.SubscribedPrNumber = null;
            settings.SubscribedCustomBuildPublisherId = null;
            settings.SubscribedCustomBuildContentId = null;
            settings.SubscribedCustomBuildName = null;
            settings.SubscribedCustomBuildVersion = null;
        });
        _ = _userSettingsService.SaveAsync(CancellationToken.None);

        StatusMessage = $"Subscribed to branch: {branchName}";
        _logger.LogInformation("Subscribed to branch '{Branch}'", branchName);
    }

    partial void OnSubscribedBranchChanged(string? value)
    {
        _velopackUpdateManager.SubscribedBranch = value;
        _ = LoadArtifactsForSubscribedItemAsync();
        OnPropertyChanged(nameof(IsSubscribedToAny));
        UpdateCommandStates();
    }

    partial void OnSubscribedPrChanged(PullRequestInfo? value)
    {
        _ = LoadArtifactsForSubscribedItemAsync();
        OnPropertyChanged(nameof(IsSubscribedToAny));
        OnPropertyChanged(nameof(SubscribedPrNumberDisplay));
        OnPropertyChanged(nameof(SubscribedPrTitleDisplay));
        OnPropertyChanged(nameof(SubscribedPrLatestVersionDisplay));
        UpdateCommandStates();
    }

    [RelayCommand]
    private void Unsubscribe()
    {
        _velopackUpdateManager.SubscribedPrNumber = null;
        _velopackUpdateManager.SubscribedBranch = null;
        SubscribedPr = null;
        SubscribedBranch = null;
        SubscribedCustomBuildContentId = null;
        SubscribedCustomBuildName = null;
        SubscribedCustomBuildPublisherId = null;
        SubscribedCustomBuildVersion = null;
        SelectedVersion = null;
        ShowPrMergedWarning = false;
        IsUpdateAvailable = false;
        LatestVersion = string.Empty;
        ReleaseNotesUrl = string.Empty;
        _currentUpdateInfo = null;
        StatusMessage = "Switched to MAIN branch updates";

        _userSettingsService.Update(settings =>
        {
            settings.SubscribedPrNumber = null;
            settings.SubscribedBranch = null;
            settings.SubscribedCustomBuildContentId = null;
            settings.SubscribedCustomBuildName = null;
            settings.SubscribedCustomBuildPublisherId = null;
            settings.SubscribedCustomBuildVersion = null;
        });
        _ = _userSettingsService.SaveAsync(CancellationToken.None);

        foreach (var build in AvailableCustomBuilds)
        {
            build.IsSubscribed = false;
        }

        OnPropertyChanged(nameof(IsSubscribedToCustomBuild));
        OnPropertyChanged(nameof(IsSubscribedToAny));

        _logger.LogInformation("Unsubscribed from dev builds, switched to MAIN");
        _ = CheckForUpdatesAsync();
    }

    [RelayCommand]
    private void UnsubscribeFromPr() => Unsubscribe();

    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ILocalizationService.CurrentCulture) && e.PropertyName != LocalizationConstants.IndexerPropertyName)
        {
            return;
        }

        OnPropertyChanged(nameof(InstallButtonText));
        OnPropertyChanged(nameof(VersionPlaceholderText));
        OnPropertyChanged(nameof(DisplayLatestVersion));
        OnPropertyChanged(nameof(InstalledVersionDisplay));

        if (AvailablePullRequests.Count > 0)
        {
            var prs = AvailablePullRequests.ToList();
            AvailablePullRequests.Clear();
            foreach (var pr in prs)
            {
                AvailablePullRequests.Add(pr);
            }
        }

        AvailableSortOptions =
        [
            AppUpdateConstants.SortOptionLastUpdated,
            AppUpdateConstants.SortOptionPrNumberDesc,
            AppUpdateConstants.SortOptionPrNumberAsc,
        ];
        OnPropertyChanged(nameof(SelectedSortOption));
    }

    private void OnGitHubAuthStateChanged(object? sender, GitHubAuthStateChangedEventArgs e)
    {
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            RefreshAuthenticationState(e.IsAuthenticated);
        }
        else
        {
            var isAuthenticated = e.IsAuthenticated;
            Dispatcher.UIThread.Post(() => RefreshAuthenticationState(isAuthenticated));
        }
    }

    private void RefreshAuthenticationState(bool isAuthenticated)
    {
        if (_disposed)
        {
            return;
        }

        IsAuthenticated = isAuthenticated;
        if (isAuthenticated)
        {
            _ = Task.WhenAll(LoadPullRequestsAsync(), LoadBranchesAsync());
        }
        else
        {
            _allPullRequests.Clear();
            AvailablePullRequests.Clear();
            AvailableBranches.Clear();
        }
    }
}
