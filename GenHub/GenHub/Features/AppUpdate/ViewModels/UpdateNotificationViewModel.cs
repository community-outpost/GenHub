using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
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
    private readonly IVelopackUpdateManager _velopackUpdateManager;
    private readonly ILogger<UpdateNotificationViewModel> _logger;
    private readonly IUserSettingsService _userSettingsService;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private UpdateInfo? _currentUpdateInfo;

    /// <summary>
    /// Gets the current application version.
    /// </summary>
    public static string CurrentAppVersion
    {
        get
        {
            try
            {
                // Get actual installed version from Velopack
                var updateManager = new UpdateManager(new SimpleWebSource(string.Empty));
                var currentVersion = updateManager.CurrentVersion;
                return currentVersion?.ToString() ?? AppConstants.AppVersion;
            }
            catch
            {
                // Fallback to compile-time version if Velopack fails
                return AppConstants.AppVersion;
            }
        }
    }

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
    private bool _isLoadingVersions;

    /// <summary>
    /// Gets the text to display as a placeholder in the version selection combo box.
    /// </summary>
    public string VersionPlaceholderText => IsLoadingVersions ? AppUpdateConstants.LoadingVersionsMessage : (AvailableVersions.Count > 0 ? AppUpdateConstants.SelectVersionMessage : AppUpdateConstants.NoVersionsFoundMessage);

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

        // Also refresh PRs/Branches if in browse mode
        if (HasPat)
        {
            await LoadPullRequestsAsync();
            await LoadBranchesAsync();
        }

        // Refresh artifacts for current subscription
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

        // Check if PAT is available
        HasPat = gitHubTokenStorage?.HasToken() ?? false;

        _logger.LogInformation("UpdateNotificationViewModel initialized with Velopack (HasPat={HasPat})", HasPat);

        // Monitor collection changes to update placeholder text
        AvailableVersions.CollectionChanged += (s, e) => OnPropertyChanged(nameof(VersionPlaceholderText));

        // Automatically check for updates and load PRs when dialog opens
        _ = InitializeAsync();
    }

    private async Task LoadArtifactsForSubscribedItemAsync()
    {
        // Cancel any previous loading if possible, or just guard
        if (IsLoadingVersions) return; // Simple guard, could be improved with cancellation token

        IsLoadingVersions = true;
        AvailableVersions.Clear();
        SelectedVersion = null;

        try
        {
            IReadOnlyList<ArtifactUpdateInfo> artifacts = [];

            if (SubscribedPr != null)
            {
                artifacts = await _velopackUpdateManager.GetArtifactsForPullRequestAsync(SubscribedPr.Number, _cancellationTokenSource.Token);
            }
            else if (!string.IsNullOrEmpty(SubscribedBranch))
            {
                artifacts = await _velopackUpdateManager.GetArtifactsForBranchAsync(SubscribedBranch, _cancellationTokenSource.Token);
            }

            _logger.LogInformation("Received {Count} platform-compatible artifacts from update manager", artifacts.Count);

            // Use HashSet to prevent duplicates based on artifact ID
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

            // Don't auto-select to avoid duplicate display in ComboBox
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load available versions");
        }
        finally
        {
            IsLoadingVersions = false;
        }
    }

    /// <summary>
    /// Initializes the view model by checking for updates and loading PRs.
    /// </summary>
    private async Task InitializeAsync()
    {
        // Load subscribed PR and Branch from settings
        var settings = _userSettingsService.Get();
        if (settings.SubscribedPrNumber.HasValue)
        {
            _velopackUpdateManager.SubscribedPrNumber = settings.SubscribedPrNumber;
            _logger.LogInformation("Loaded subscribed PR #{PrNumber} from settings", settings.SubscribedPrNumber);
        }

        if (!string.IsNullOrEmpty(settings.SubscribedBranch))
        {
            SubscribedBranch = settings.SubscribedBranch;
            _logger.LogInformation("Loaded subscribed branch '{Branch}' from settings", settings.SubscribedBranch);
        }

        // Load data if we have a PAT
        if (HasPat)
        {
            // Initial check/load
            await Task.WhenAll(
                LoadPullRequestsAsync(),
                LoadBranchesAsync());
        }

        // Now check for updates - subscriptions will be properly populated
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
    public bool CanDownloadUpdate => (IsUpdateAvailable || SelectedVersion != null) && !IsInstalling;

    /// <summary>
    /// Gets a value indicating whether the check button should be enabled.
    /// </summary>
    public bool IsCheckButtonEnabled => !IsChecking;

    /// <summary>
    /// Gets the text for the install button.
    /// </summary>
    public string InstallButtonText => IsInstalling ? "Installing..." : "Install Update";

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

            // 1. PR Update takes precedence
            if (SubscribedPr?.LatestArtifact != null &&
                string.Equals(SubscribedPr.LatestArtifact.Version, LatestVersion, StringComparison.OrdinalIgnoreCase))
            {
                return SubscribedPr.LatestArtifact.DisplayVersion;
            }

            // 2. Branch Update
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
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Extracts the workflow run number from a version string like "0.0.641-pr241".
    /// </summary>
    private static int ExtractRunNumber(string version)
    {
        // Try to extract the run number before the PR suffix
        var match = System.Text.RegularExpressions.Regex.Match(version, @"(\d+)(?:-pr\d+|-\w+)?$");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var runNumber))
        {
            return runNumber;
        }

        // Fallback: try to parse the entire version as a number
        var parts = version.Split('.', '-', '+');
        foreach (var part in parts.Reverse())
        {
            if (int.TryParse(part, out var number))
            {
                return number;
            }
        }

        return 0;
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

            _logger.LogInformation("Starting Velopack update check");

            // Check if subscribed to a PR
            if (SubscribedPr != null)
            {
                if (SubscribedPr.LatestArtifact != null)
                {
                    var currentVersionBase = CurrentAppVersion.Split('+')[0];
                    var prVersionBase = SubscribedPr.LatestArtifact.Version.Split('+')[0];

                    // Extract run numbers for numeric comparison
                    var currentRun = ExtractRunNumber(currentVersionBase);
                    var prRun = ExtractRunNumber(prVersionBase);

                    _logger.LogDebug("Comparing PR #{PrNumber} versions: current run #{CurrentRun} vs new run #{PrRun}", SubscribedPr.Number, currentRun, prRun);

                    if (prRun > currentRun)
                    {
                        var settings = _userSettingsService.Get();
                        if (!string.Equals(prVersionBase, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
                        {
                            IsUpdateAvailable = true;
                            LatestVersion = prVersionBase;
                            ReleaseNotesUrl = $"{AppConstants.GitHubRepositoryUrl}/pull/{SubscribedPr.Number}";
                            StatusMessage = $"New PR build available: {SubscribedPr.LatestArtifact.DisplayVersion}";
                            _logger.LogInformation("Subscribed to PR #{PrNumber}, new build available: run #{PrRun} (current: #{CurrentRun})", SubscribedPr.Number, prRun, currentRun);
                            return;
                        }
                        else
                        {
                            StatusMessage = $"You dismissed the update for PR #{SubscribedPr.Number}";
                            return;
                        }
                    }
                    else
                    {
                        IsUpdateAvailable = false;
                        StatusMessage = $"You are on the latest build for PR #{SubscribedPr.Number}";
                        return;
                    }
                }
                else
                {
                    // Try to fetch artifact for update check
                    _logger.LogInformation("PR #{PrNumber} has no cached artifact, fetching for update check", SubscribedPr.Number);
                    var prArtifact = await _velopackUpdateManager.CheckForArtifactUpdatesAsync(_cancellationTokenSource.Token);
                    if (prArtifact != null)
                    {
                        var currentVersionBase = CurrentAppVersion.Split('+')[0];
                        var prVersionBase = prArtifact.Version.Split('+')[0];

                        // Extract run numbers for numeric comparison
                        var currentRun = ExtractRunNumber(currentVersionBase);
                        var prRun = ExtractRunNumber(prVersionBase);

                        _logger.LogDebug("Comparing fetched PR #{PrNumber} versions: current run #{CurrentRun} vs new run #{PrRun}", SubscribedPr.Number, currentRun, prRun);

                        if (prRun > currentRun)
                        {
                            var settings = _userSettingsService.Get();
                            if (!string.Equals(prVersionBase, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
                            {
                                IsUpdateAvailable = true;
                                LatestVersion = prVersionBase;
                                ReleaseNotesUrl = $"{AppConstants.GitHubRepositoryUrl}/pull/{SubscribedPr.Number}";
                                StatusMessage = $"New PR build available: {prArtifact.DisplayVersion}";
                                _logger.LogInformation("Fetched PR #{PrNumber} artifact, new build available: run #{PrRun} (current: #{CurrentRun})", SubscribedPr.Number, prRun, currentRun);
                                return;
                            }
                            else
                            {
                                StatusMessage = $"You dismissed the update for PR #{SubscribedPr.Number}";
                                return;
                            }
                        }
                        else
                        {
                            IsUpdateAvailable = false;
                            StatusMessage = $"You are on the latest build for PR #{SubscribedPr.Number}";
                            return;
                        }
                    }

                    // If subscribed to PR but no artifact found, don't fall through to main release
                    _logger.LogInformation("Subscribed to PR #{PrNumber} but no artifact available yet", SubscribedPr.Number);
                    StatusMessage = $"Waiting for PR #{SubscribedPr.Number} build...";
                    IsUpdateAvailable = false;
                    return;
                }
            }

            // Check Branch updates if subscribed
            if (!string.IsNullOrEmpty(SubscribedBranch))
            {
                _logger.LogInformation("Checking for artifact updates on branch: {Branch}", SubscribedBranch);
                var branchArtifact = await _velopackUpdateManager.CheckForArtifactUpdatesAsync(_cancellationTokenSource.Token);

                if (branchArtifact != null)
                {
                    var currentVersionBase = CurrentAppVersion.Split('+')[0];
                    var artifactVersionBase = branchArtifact.Version.Split('+')[0];

                    if (!string.Equals(artifactVersionBase, currentVersionBase, StringComparison.OrdinalIgnoreCase))
                    {
                        var settings = _userSettingsService.Get();
                        if (!string.Equals(artifactVersionBase, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
                        {
                            IsUpdateAvailable = true;
                            LatestVersion = artifactVersionBase;
                            ReleaseNotesUrl = $"{AppConstants.GitHubRepositoryUrl}/tree/{SubscribedBranch}";
                            StatusMessage = $"New {SubscribedBranch} build available: {branchArtifact.Version}";
                            _logger.LogInformation("Branch '{Branch}' has new build: {Version}", SubscribedBranch, LatestVersion);
                            return;
                        }
                    }
                    else
                    {
                        IsUpdateAvailable = false;
                        StatusMessage = $"You are on the latest build for {SubscribedBranch}";
                        return;
                    }
                }

                // If subscribed to branch but no artifact found, don't fall through to main release
                _logger.LogInformation("Subscribed to branch '{Branch}' but no artifact available yet", SubscribedBranch);
                StatusMessage = $"Waiting for {SubscribedBranch} build...";
                IsUpdateAvailable = false;
                return;
            }

            // Check main branch releases
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

        // Clear dismissal status in settings so the user can see the update again
        var settings = _userSettingsService.Get();
        if (!string.IsNullOrEmpty(settings.DismissedUpdateVersion))
        {
            _userSettingsService.Update(s => s.DismissedUpdateVersion = string.Empty);
            await _userSettingsService.SaveAsync();
        }

        // Clear manager cache
        _velopackUpdateManager.ClearCache();

        // Reload data
        if (HasPat)
        {
            await Task.WhenAll(
                LoadPullRequestsAsync(),
                LoadBranchesAsync());
        }

        await CheckForUpdatesAsync();
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
    /// Downloads and applies the update using Velopack.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownloadUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (!CanDownloadUpdate)
        {
            return;
        }

        // 0. Handle Explicitly Selected Version
        if (SelectedVersion != null)
        {
            _logger.LogInformation("Installing selected artifact version: {Version}", SelectedVersion.DisplayVersion);
            await InstallArtifactAsync(SelectedVersion);
            return;
        }

        // 1. Handle PR Artifact Update (Auto-latest)
        if (SubscribedPr?.LatestArtifact != null &&
            string.Equals(SubscribedPr.LatestArtifact.Version, LatestVersion, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Installing PR artifact update via InstallUpdateAsync override");
            await InstallPrArtifactAsync();
            return;
        }

        // 1.5 Handle Branch Artifact Update (Auto-latest)
        if (!string.IsNullOrEmpty(SubscribedBranch))
        {
            _logger.LogInformation("Installing Branch '{Branch}' artifact update", SubscribedBranch);
            await InstallBranchArtifactAsync();
            return;
        }

        // 2. Handle Standard Velopack Update
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
                // Clear cache to force fresh check
                _velopackUpdateManager.ClearCache();

                // Try to fetch the latest artifact for the PR
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

            // App will restart, this code won't execute
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

            // Clear cache to force fresh check
            _velopackUpdateManager.ClearCache();

            // Check for latest artifact for the subscribed branch
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

            // App will restart, this code won't execute
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

            // App will restart
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
            _ = _userSettingsService.SaveAsync();
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
                foreach (var pr in prs)
                {
                    AvailablePullRequests.Add(pr);
                }
            });

            if (_velopackUpdateManager.IsPrMergedOrClosed && _velopackUpdateManager.SubscribedPrNumber.HasValue)
            {
                ShowPrMergedWarning = true;
                StatusMessage = $"PR #{_velopackUpdateManager.SubscribedPrNumber} has been merged. Select a new PR or switch to MAIN.";
                _logger.LogInformation("Subscribed PR has been merged/closed, showing warning");
            }

            if (_velopackUpdateManager.SubscribedPrNumber.HasValue && SubscribedPr == null)
            {
                SubscribedPr = AvailablePullRequests.FirstOrDefault(p => p.Number == _velopackUpdateManager.SubscribedPrNumber);
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
        SubscribedPr = AvailablePullRequests.FirstOrDefault(p => p.Number == prNumber);
        SubscribedBranch = null;
        ShowPrMergedWarning = false;

        // Clear artifact cache to force fresh check
        _velopackUpdateManager.ClearCache();

        _userSettingsService.Update(settings =>
        {
            settings.SubscribedPrNumber = prNumber;
            settings.SubscribedBranch = null;
        });
        _ = _userSettingsService.SaveAsync();

        if (SubscribedPr != null)
        {
            StatusMessage = $"Subscribed to PR #{prNumber}: {SubscribedPr.Title}";
            _logger.LogInformation("Subscribed to PR #{PrNumber}", prNumber);
        }
    }

    [RelayCommand]
    private void SubscribeToBranch(string branchName)
    {
        if (string.IsNullOrEmpty(branchName)) return;

        SubscribedBranch = branchName;
        _velopackUpdateManager.SubscribedPrNumber = null;
        SubscribedPr = null;
        ShowPrMergedWarning = false;

        // Clear artifact cache to force fresh check
        _velopackUpdateManager.ClearCache();

        _userSettingsService.Update(settings =>
        {
            settings.SubscribedBranch = branchName;
            settings.SubscribedPrNumber = null;
        });
        _ = _userSettingsService.SaveAsync();

        StatusMessage = $"Subscribed to branch: {branchName}";
        _logger.LogInformation("Subscribed to branch '{Branch}'", branchName);
    }

    partial void OnSubscribedBranchChanged(string? value)
    {
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
        SubscribedPr = null;
        SubscribedBranch = null;
        ShowPrMergedWarning = false;
        StatusMessage = "Switched to MAIN branch updates";

        _userSettingsService.Update(settings =>
        {
            settings.SubscribedPrNumber = null;
            settings.SubscribedBranch = null;
        });
        _ = _userSettingsService.SaveAsync();

        _logger.LogInformation("Unsubscribed from dev builds, switched to MAIN");
    }

    [RelayCommand]
    private void UnsubscribeFromPr() => Unsubscribe();
}
