using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.AppUpdate;
using GenHub.Features.AppUpdate.Interfaces;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace GenHub.Features.AppUpdate.ViewModels;

/// <summary>
/// ViewModel for the update notification dialog powered by Velopack.
/// </summary>
public partial class UpdateNotificationViewModel : ObservableObject, IDisposable
{
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
    /// Gets or sets a value indicating whether an update download is in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isDownloading;

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
    public IReadOnlyList<string> AvailableSortOptions { get; } =
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
    /// Gets or sets a value indicating whether GitHub PAT is available.
    /// </summary>
    [ObservableProperty]
    private bool _hasPat;

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
                return AppUpdateConstants.LoadingVersionsMessage;
            }

            return AvailableVersions.Count > 0
                ? AppUpdateConstants.SelectVersionMessage
                : AppUpdateConstants.NoVersionsFoundMessage;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether a merged/closed PR warning should be shown.
    /// </summary>
    [ObservableProperty]
    private bool _showPrMergedWarning;

    /// <summary>
    /// Gets a value indicating whether the user is subscribed to either a PR or a branch.
    /// </summary>
    public bool IsSubscribedToAny => SubscribedPr != null || !string.IsNullOrEmpty(SubscribedBranch);

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
        if (HasPat)
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
    /// <param name="gitHubTokenStorage">The GitHub token storage.</param>
    public UpdateNotificationViewModel(
        IVelopackUpdateManager velopackUpdateManager,
        ILogger<UpdateNotificationViewModel> logger,
        IUserSettingsService userSettingsService,
        IGitHubTokenStorage? gitHubTokenStorage = null)
    {
        _velopackUpdateManager = velopackUpdateManager ?? throw new ArgumentNullException(nameof(velopackUpdateManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
        _cancellationTokenSource = new CancellationTokenSource();

        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !IsChecking);
        ManualRefreshCommand = new AsyncRelayCommand(ManualRefreshAsync, () => !IsChecking);
        DismissCommand = new RelayCommand(DismissUpdate);

        // check if pat is available
        HasPat = gitHubTokenStorage?.HasToken() == true;

        _logger.LogInformation("UpdateNotificationViewModel initialized with Velopack (HasPat={HasPat})", HasPat);

        // monitor collection changes to update placeholder text
        AvailableVersions.CollectionChanged += (s, e) => OnPropertyChanged(nameof(VersionPlaceholderText));

        // automatically check for updates and load prs when dialog opens
        _ = InitializeAsync();
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

        if (targetPrNumber == null && string.IsNullOrEmpty(targetBranch))
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
        AvailableVersions.Clear();
        SelectedVersion = null;

        try
        {
            var artifacts = await FetchSubscribedArtifactsAsync(targetPrNumber, targetBranch, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            PopulateAvailableVersions(artifacts);
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
        CancellationToken token)
    {
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
        // load subscribed pr and branch from settings
        var settings = _userSettingsService.Get();
        if (settings.SubscribedPrNumber.HasValue)
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
            _logger.LogInformation("Loaded subscribed PR #{PrNumber} from settings", prNumber);
        }

        if (!string.IsNullOrEmpty(settings.SubscribedBranch))
        {
            SubscribedBranch = settings.SubscribedBranch;
            _logger.LogInformation("Loaded subscribed branch '{Branch}' from settings", settings.SubscribedBranch);
        }

        // load data if we have a pat
        if (HasPat)
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
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel property bound to UI elements")]
    public bool CanDownloadUpdate => (IsUpdateAvailable || SelectedVersion != null) && !IsInstalling && !IsChecking && !IsLoadingVersions;

    /// <summary>
    /// Gets a value indicating whether the check button should be enabled.
    /// </summary>
    public bool IsCheckButtonEnabled => !IsChecking;

    /// <summary>
    /// Gets a value indicating whether an operation is currently loading versions, checking updates, or installing.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel property bound to UI elements")]
    public bool IsLoadingOrInstalling => IsLoadingVersions || IsChecking || IsInstalling;

    /// <summary>
    /// Gets the text for the install button.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel property bound to UI elements")]
    public string InstallButtonText
    {
        get
        {
            if (IsInstalling)
            {
                return AppUpdateConstants.InstallingMessage;
            }

            if (IsChecking || IsLoadingVersions)
            {
                return AppUpdateConstants.LoadingMessage;
            }

            return AppUpdateConstants.InstallUpdateAction;
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
        _loadArtifactsCts?.Cancel();
        _loadArtifactsCts?.Dispose();
        _loadArtifactsCts = null;

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ProcessPrArtifactUpdate(ArtifactUpdateInfo artifact, int prNumber)
    {
        var currentVersionBase = CurrentAppVersion.Split('+')[0];
        var prVersionBase = artifact.Version.Split('+')[0];

        if (AppUpdateVersionHelper.IsArtifactVersionNewer(prVersionBase, currentVersionBase))
        {
            var settings = _userSettingsService.Get();
            if (!string.Equals(prVersionBase, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                IsUpdateAvailable = true;
                LatestVersion = prVersionBase;
                ReleaseNotesUrl = $"{AppConstants.GitHubRepositoryUrl}/pull/{prNumber}";
                StatusMessage = $"New PR build available: {artifact.DisplayVersion}";
                _logger.LogInformation("Subscribed to PR #{PrNumber}, new build available: {Version}", prNumber, artifact.DisplayVersion);
                return;
            }

            StatusMessage = $"You dismissed the update for PR #{prNumber}";
            return;
        }

        IsUpdateAvailable = false;
        StatusMessage = $"You are on the latest build for PR #{prNumber}";
    }

    private void ProcessBranchArtifactUpdate(ArtifactUpdateInfo artifact, string branch)
    {
        var currentVersionBase = CurrentAppVersion.Split('+')[0];
        var branchVersionBase = artifact.Version.Split('+')[0];

        if (AppUpdateVersionHelper.IsArtifactVersionNewer(branchVersionBase, currentVersionBase))
        {
            var settings = _userSettingsService.Get();
            if (!string.Equals(branchVersionBase, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                IsUpdateAvailable = true;
                LatestVersion = branchVersionBase;
                ReleaseNotesUrl = $"{AppConstants.GitHubRepositoryUrl}/tree/{branch}";
                StatusMessage = $"New {branch} build available: {artifact.DisplayVersion}";
                _logger.LogInformation("Branch '{Branch}' has new build: {Version}", branch, LatestVersion);
                return;
            }

            StatusMessage = $"You dismissed the update for branch '{branch}'";
            return;
        }

        IsUpdateAvailable = false;
        StatusMessage = $"You are on the latest build for {branch}";
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

        if (AppUpdateVersionHelper.IsArtifactVersionNewer(selectedVersionBase, currentVersionBase))
        {
            var settings = _userSettingsService.Get();
            if (!string.Equals(selectedVersionBase, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
            {
                IsUpdateAvailable = true;
                LatestVersion = selectedVersionBase;
                if (value.PullRequestNumber.HasValue)
                {
                    ReleaseNotesUrl = $"{AppConstants.GitHubRepositoryUrl}/pull/{value.PullRequestNumber.Value}";
                    StatusMessage = $"New PR build available: {value.DisplayVersion}";
                }
                else if (!string.IsNullOrEmpty(SubscribedBranch))
                {
                    ReleaseNotesUrl = $"{AppConstants.GitHubRepositoryUrl}/tree/{SubscribedBranch}";
                    StatusMessage = $"New {SubscribedBranch} build available: {value.DisplayVersion}";
                }
                else
                {
                    StatusMessage = $"New build available: {value.DisplayVersion}";
                }

                return;
            }

            IsUpdateAvailable = false;
            LatestVersion = string.Empty;
            ReleaseNotesUrl = string.Empty;
            StatusMessage = $"You dismissed update {value.DisplayVersion}";
            return;
        }

        var currentRun = AppUpdateVersionHelper.ExtractRunNumber(currentVersionBase);
        var selectedRun = AppUpdateVersionHelper.ExtractRunNumber(selectedVersionBase);

        if (currentRun > 0 && selectedRun > 0 && currentRun == selectedRun)
        {
            IsUpdateAvailable = false;
            if (value.PullRequestNumber.HasValue)
            {
                StatusMessage = $"You are on the latest build for PR #{value.PullRequestNumber.Value}";
            }
            else if (!string.IsNullOrEmpty(SubscribedBranch))
            {
                StatusMessage = $"You are on the latest build for {SubscribedBranch}";
            }
            else
            {
                StatusMessage = $"You are on the latest build ({value.DisplayVersion})";
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
            StatusMessage = "Checking for updates...";
            IsUpdateAvailable = false;
            ShowPrMergedWarning = false;

            _logger.LogInformation("Starting Velopack update check");

            // check if subscribed to a pr
            if (SubscribedPr != null)
            {
                if (!HasPat)
                {
                    _logger.LogInformation("Subscribed to PR #{PrNumber} but GitHub PAT is not configured", SubscribedPr.Number);
                    StatusMessage = AppUpdateConstants.PatRequiredForArtifactsMessage;
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
                    if (HasPat)
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
                    if (!HasPat)
                    {
                        _logger.LogInformation("Subscribed to branch '{Branch}' but GitHub PAT is not configured", SubscribedBranch);
                        StatusMessage = AppUpdateConstants.PatRequiredForArtifactsMessage;
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
                    StatusMessage = $"Update available: v{LatestVersion}";
                    _logger.LogInformation("Update available from UpdateManager: {Version}", LatestVersion);
                }
                else
                {
                    StatusMessage = "You're up to date!";
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
                    StatusMessage = $"Update available: v{LatestVersion}";
                    _logger.LogInformation("Update available from GitHub API: {Version}", LatestVersion);
                }
                else
                {
                    StatusMessage = "You're up to date!";
                }
            }
            else
            {
                IsUpdateAvailable = false;
                LatestVersion = string.Empty;
                StatusMessage = "You're up to date!";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update check failed");
            HasError = true;
            ErrorMessage = $"Failed to check for updates: {ex.Message}";
            StatusMessage = "Update check failed";
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
        if (HasPat)
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
            HasError = false;
            ErrorMessage = string.Empty;
            StatusMessage = AppUpdateConstants.DownloadingUpdateMessage;
            InstallationProgress = new UpdateProgress { Status = AppUpdateConstants.DownloadingUpdateMessage, PercentComplete = 0 };

            var progress = new Progress<UpdateProgress>(p =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    InstallationProgress = p;
                    StatusMessage = p.Status;
                    DownloadProgress = p.PercentComplete;
                });
            });

            await _velopackUpdateManager.DownloadUpdatesAsync(_currentUpdateInfo, progress, _cancellationTokenSource.Token);

            StatusMessage = AppUpdateConstants.UpdateDownloadedRestartingMessage;
            InstallationProgress = new UpdateProgress
            {
                Status = AppUpdateConstants.UpdateCompleteRestartingMessage,
                PercentComplete = 100,
                IsCompleted = true,
            };

            await Task.Delay(1500); // Brief delay to show completion message

            _velopackUpdateManager.ApplyUpdatesAndRestart(_currentUpdateInfo);
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
            IsInstalling = false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the branch artifact can be installed.
    /// </summary>
    public bool CanInstallBranchArtifact => !string.IsNullOrEmpty(SubscribedBranch) && !IsInstalling;

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

            var progress = new Progress<UpdateProgress>(p =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    InstallationProgress = p;
                    StatusMessage = p.Status;
                    DownloadProgress = p.PercentComplete;
                });
            });

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
            StatusMessage = "PR installation failed";
            InstallationProgress = new UpdateProgress
            {
                Status = "Installation failed",
                HasError = true,
                ErrorMessage = ex.Message,
            };
        }
        finally
        {
            IsInstalling = false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the PR artifact can be installed.
    /// </summary>
    public bool CanInstallPrArtifact => SubscribedPr != null && !IsInstalling;

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

            var progress = new Progress<UpdateProgress>(p =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    InstallationProgress = p;
                    StatusMessage = p.Status;
                    DownloadProgress = p.PercentComplete;
                });
            });

            // clear cache to force fresh check
            _velopackUpdateManager.ClearCache();

            // check for latest artifact for the subscribed branch
            var artifactUpdate = await _velopackUpdateManager.CheckForArtifactUpdatesAsync(_cancellationTokenSource.Token);
            if (artifactUpdate == null)
            {
                _logger.LogWarning("No artifact found for branch '{Branch}'", SubscribedBranch);
                HasError = true;
                ErrorMessage = $"No artifact found for branch '{SubscribedBranch}'";
                StatusMessage = "No artifact available";
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
            StatusMessage = "Branch installation failed";
            InstallationProgress = new UpdateProgress
            {
                Status = "Installation failed",
                HasError = true,
                ErrorMessage = ex.Message,
            };
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private async Task InstallArtifactAsync(ArtifactUpdateInfo artifact)
    {
        IsInstalling = true;
        HasError = false;
        ErrorMessage = string.Empty;
        DownloadProgress = 0;

        try
        {
            _logger.LogInformation("Installing artifact: {Name} ({Version})", artifact.ArtifactName, artifact.Version);

            var progress = new Progress<UpdateProgress>(p =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    InstallationProgress = p;
                    StatusMessage = p.Status;
                    DownloadProgress = p.PercentComplete;
                });
            });

            await _velopackUpdateManager.InstallArtifactAsync(artifact, progress, _cancellationTokenSource.Token);

            // app will restart
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install artifact");
            HasError = true;
            ErrorMessage = $"Installation failed: {ex.Message}";
            StatusMessage = "Installation failed";
            InstallationProgress = new UpdateProgress
            {
                Status = "Installation failed",
                HasError = true,
                ErrorMessage = ex.Message,
            };
        }
        finally
        {
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
            _userSettingsService.Update(s => s.DismissedUpdateVersion = LatestVersion);
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
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateCommandStates();
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(UpdateCommandStates);
        }
    }

    partial void OnIsInstallingChanged(bool value)
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
        if (!HasPat || IsLoadingPullRequests) return;

        IsLoadingPullRequests = true;
        AvailablePullRequests.Clear();

        try
        {
            _logger.LogInformation("Loading open pull requests with artifacts");
            var prs = await _velopackUpdateManager.GetOpenPullRequestsAsync(_cancellationTokenSource.Token);

            await Dispatcher.UIThread.InvokeAsync(() =>
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
        if (!HasPat || IsLoadingBranches) return;

        IsLoadingBranches = true;
        AvailableBranches.Clear();

        try
        {
            _logger.LogInformation("Loading repository branches");
            var branches = await _velopackUpdateManager.GetBranchesAsync(_cancellationTokenSource.Token);

            await Dispatcher.UIThread.InvokeAsync(() =>
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
        _velopackUpdateManager.SubscribedPrNumber = prNumber;
        _velopackUpdateManager.SubscribedBranch = null;
        SubscribedBranch = null;
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
        });
        _ = _userSettingsService.SaveAsync(CancellationToken.None);

        StatusMessage = $"Subscribed to PR #{prNumber}: {SubscribedPr.Title}";
        _logger.LogInformation("Subscribed to PR #{PrNumber}", prNumber);
    }

    [RelayCommand]
    private void SubscribeToBranch(string branchName)
    {
        if (string.IsNullOrEmpty(branchName)) return;

        _velopackUpdateManager.SubscribedPrNumber = null;
        _velopackUpdateManager.SubscribedBranch = branchName;
        SubscribedPr = null;
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
        });
        _ = _userSettingsService.SaveAsync(CancellationToken.None);

        _logger.LogInformation("Unsubscribed from dev builds, switched to MAIN");
        _ = CheckForUpdatesAsync();
    }

    [RelayCommand]
    private void UnsubscribeFromPr() => Unsubscribe();
}
