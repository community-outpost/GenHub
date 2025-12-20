using System;
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
    /// Gets or sets the status message.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "Checking for updates...";

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
    private ObservableCollection<PullRequestInfo> _availablePullRequests = new();

    /// <summary>
    /// Gets or sets the currently subscribed PR.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLatestVersion))]
    private PullRequestInfo? _subscribedPr;

    /// <summary>
    /// Gets or sets a value indicating whether PR list is currently loading.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingPullRequests;

    /// <summary>
    /// Gets or sets a value indicating whether GitHub PAT is available.
    /// </summary>
    [ObservableProperty]
    private bool _hasPat;

    /// <summary>
    /// Gets or sets a value indicating whether a merged/closed PR warning should be shown.
    /// </summary>
    [ObservableProperty]
    private bool _showPrMergedWarning;

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
        DismissCommand = new RelayCommand(DismissUpdate);

        // Check if PAT is available
        HasPat = gitHubTokenStorage?.HasToken() ?? false;

        _logger.LogInformation("UpdateNotificationViewModel initialized with Velopack (HasPat={HasPat})", HasPat);

        // Automatically check for updates and load PRs when dialog opens
        _ = InitializeAsync();
    }

    /// <summary>
    /// Initializes the view model by checking for updates and loading PRs.
    /// </summary>
    private async Task InitializeAsync()
    {
        // Load subscribed PR from settings
        var settings = _userSettingsService.Get();
        if (settings.SubscribedPrNumber.HasValue)
        {
            _velopackUpdateManager.SubscribedPrNumber = settings.SubscribedPrNumber;
            _logger.LogInformation("Loaded subscribed PR #{PrNumber} from settings", settings.SubscribedPrNumber);
        }

        // Load PRs FIRST so SubscribedPr object is populated before update check
        if (HasPat)
        {
            await LoadPullRequestsAsync();
        }

        // Now check for updates - SubscribedPr will be properly populated
        await CheckForUpdatesAsync();
    }

    /// <summary>
    /// Gets the command to check for updates.
    /// </summary>
    public ICommand CheckForUpdatesCommand { get; }

    /// <summary>
    /// Gets the command to dismiss the update notification.
    /// </summary>
    public ICommand DismissCommand { get; }

    /// <summary>
    /// Gets the current application version.
    /// </summary>
    public string CurrentAppVersion => AppConstants.AppVersion;

    /// <summary>
    /// Gets a value indicating whether an update is available and can be downloaded.
    /// </summary>
    public bool CanDownloadUpdate => IsUpdateAvailable && !IsInstalling;

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
                return "Unknown";
            }

            // If we are subscribed to a PR and the update matches that PR's latest artifact
            if (SubscribedPr?.LatestArtifact != null &&
                string.Equals(SubscribedPr.LatestArtifact.Version, LatestVersion, StringComparison.OrdinalIgnoreCase))
            {
                return SubscribedPr.LatestArtifact.DisplayVersion;
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

            // Check if subscribed to a PR - this takes precedence over main branch releases
            if (SubscribedPr?.LatestArtifact != null)
            {
                // For subscribed PRs, compare versions without build metadata
                // Strip everything after '+' to ignore build hashes
                var currentVersionBase = CurrentAppVersion.Split('+')[0];
                var prVersionBase = SubscribedPr.LatestArtifact.Version.Split('+')[0];

                if (!string.Equals(prVersionBase, currentVersionBase, StringComparison.OrdinalIgnoreCase))
                {
                    // Check if this version was already dismissed
                    var settings = _userSettingsService.Get();
                    if (!string.Equals(prVersionBase, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        IsUpdateAvailable = true;
                        LatestVersion = prVersionBase;
                        ReleaseNotesUrl = $"{AppConstants.GitHubRepositoryUrl}/pull/{SubscribedPr.Number}";
                        StatusMessage = $"New PR build available: {SubscribedPr.LatestArtifact.DisplayVersion}";
                        _logger.LogInformation(
                            "Subscribed to PR #{PrNumber}, new build available: {Version}",
                            SubscribedPr.Number,
                            LatestVersion);
                        return; // Exit early - PR update takes priority
                    }
                    else
                    {
                        _logger.LogInformation("PR update {Version} was previously dismissed", prVersionBase);
                        StatusMessage = $"You dismissed the update for PR #{SubscribedPr.Number}";
                        return;
                    }
                }
                else
                {
                    // We are on the latest PR build
                    IsUpdateAvailable = false;
                    StatusMessage = $"You are on the latest build for PR #{SubscribedPr.Number}";
                    _logger.LogInformation("Already on latest PR #{PrNumber} build", SubscribedPr.Number);
                    return; // Exit early - no need to check main branch
                }
            }

            // Check main branch releases (only if not subscribed to PR)
            _currentUpdateInfo = await _velopackUpdateManager.CheckForUpdatesAsync(_cancellationTokenSource.Token);

            // Check both UpdateInfo (for installed app with working Velopack) and GitHub flag (for installed app where Velopack has issues)
            if (_currentUpdateInfo != null)
            {
                var version = _currentUpdateInfo.TargetFullRelease.Version.ToString();

                // Check if this version was already dismissed
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
                    _logger.LogInformation("Update {Version} was previously dismissed", version);
                    StatusMessage = "You're up to date!";
                }
            }
            else if (_velopackUpdateManager.HasUpdateAvailableFromGitHub)
            {
                // GitHub API detected update but UpdateManager couldn't confirm
                var githubVersion = _velopackUpdateManager.LatestVersionFromGitHub;
                _logger.LogDebug(
                    "GitHub update detected: HasUpdate={HasUpdate}, Version='{Version}'",
                    _velopackUpdateManager.HasUpdateAvailableFromGitHub,
                    githubVersion ?? "NULL");

                // Check if this version was already dismissed
                var settings = _userSettingsService.Get();
                if (!string.Equals(githubVersion, settings.DismissedUpdateVersion, StringComparison.OrdinalIgnoreCase))
                {
                    IsUpdateAvailable = true;
                    LatestVersion = githubVersion ?? "Unknown";
                    ReleaseNotesUrl = AppConstants.GitHubRepositoryUrl + "/releases/tag/v" + LatestVersion;
                    StatusMessage = $"Update available: v{LatestVersion}";
                    _logger.LogInformation("Update available from GitHub API: {Version}", LatestVersion);
                }
                else
                {
                    _logger.LogInformation("GitHub update {Version} was previously dismissed", githubVersion);
                    StatusMessage = "You're up to date!";
                }
            }
            else
            {
                IsUpdateAvailable = false;
                LatestVersion = string.Empty;
                StatusMessage = "You're up to date!";
                _logger.LogInformation("No updates available from Velopack/GitHub");
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

        // 1. Handle PR Artifact Update
        // If we are subscribed to a PR and the LatestVersion matches the PR artifact, install that instead
        if (SubscribedPr?.LatestArtifact != null &&
            string.Equals(SubscribedPr.LatestArtifact.Version, LatestVersion, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Installing PR artifact update via InstallUpdateAsync override");
            await InstallPrArtifactAsync();
            return;
        }

        // 2. Handle Standard Velopack Update

        // If we don't have UpdateInfo, we need to show error that installed app is required
        if (_currentUpdateInfo == null)
        {
            _logger.LogError("Cannot install update - UpdateInfo is null (app not installed via Setup.exe)");
            HasError = true;
            ErrorMessage = $"Update installation requires the app to be installed.\n\n" +
                          $"You are running from: {AppDomain.CurrentDomain.BaseDirectory}\n\n" +
                          $"To enable updates:\n" +
                          $"1. Download GenHub-win-Setup.exe from GitHub releases\n" +
                          $"2. Run Setup.exe to install GenHub properly\n" +
                          $"3. Launch the installed version (will be in %LOCALAPPDATA%\\GenHub)\n\n" +
                          $"Update available: v{LatestVersion}";
            StatusMessage = "Cannot install from this location";
            return;
        }

        try
        {
            IsInstalling = true;
            HasError = false;
            ErrorMessage = string.Empty;
            StatusMessage = "Downloading update...";
            InstallationProgress = new UpdateProgress { Status = "Downloading...", PercentComplete = 0 };

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

            StatusMessage = "Update downloaded! Restarting application...";
            InstallationProgress = new UpdateProgress
            {
                Status = "Update complete! Restarting...",
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
            StatusMessage = "Update failed";
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
    /// Installs the subscribed PR artifact.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstallPrArtifact))]
    private async Task InstallPrArtifactAsync()
    {
        if (SubscribedPr == null || SubscribedPr.LatestArtifact == null)
        {
            _logger.LogWarning("Cannot install PR artifact - no PR subscribed or no artifact available");
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

            await _velopackUpdateManager.InstallPrArtifactAsync(SubscribedPr, progress, _cancellationTokenSource.Token);

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
    public bool CanInstallPrArtifact => SubscribedPr?.LatestArtifact != null && !IsInstalling;

    /// <summary>
    /// Dismisses the update notification and persists the dismissed version.
    /// </summary>
    private void DismissUpdate()
    {
        // Persist the dismissed version to prevent showing it again
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

    // Add method to handle property changes that affect command state
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
        // Ensure command updates happen on UI thread - but avoid recursion
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
        OnPropertyChanged(nameof(DisplayLatestVersion));
        OnPropertyChanged(nameof(InstallButtonText));
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Loads the list of open pull requests with available artifacts.
    /// </summary>
    [RelayCommand]
    private async Task LoadPullRequestsAsync()
    {
        if (!HasPat || IsLoadingPullRequests)
        {
            return;
        }

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

            // Check if we had a subscribed PR that got merged/closed
            if (_velopackUpdateManager.IsPrMergedOrClosed && _velopackUpdateManager.SubscribedPrNumber.HasValue)
            {
                ShowPrMergedWarning = true;
                StatusMessage = $"PR #{_velopackUpdateManager.SubscribedPrNumber} has been merged. Select a new PR or switch to MAIN.";
                _logger.LogInformation("Subscribed PR has been merged/closed, showing warning");
            }

            // Update subscribed PR info
            if (_velopackUpdateManager.SubscribedPrNumber.HasValue)
            {
                SubscribedPr = AvailablePullRequests.FirstOrDefault(p => p.Number == _velopackUpdateManager.SubscribedPrNumber);
            }

            _logger.LogInformation("Loaded {Count} open PRs", AvailablePullRequests.Count);
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

    /// <summary>
    /// Subscribes to updates from a specific PR.
    /// </summary>
    /// <param name="prNumber">The PR number to subscribe to.</param>
    [RelayCommand]
    private void SubscribeToPr(int prNumber)
    {
        _velopackUpdateManager.SubscribedPrNumber = prNumber;
        SubscribedPr = AvailablePullRequests.FirstOrDefault(p => p.Number == prNumber);
        ShowPrMergedWarning = false;

        // Persist to settings
        _userSettingsService.Update(s => s.SubscribedPrNumber = prNumber);
        _ = _userSettingsService.SaveAsync();

        if (SubscribedPr != null)
        {
            StatusMessage = $"Subscribed to PR #{prNumber}: {SubscribedPr.Title}";
            _logger.LogInformation("Subscribed to PR #{PrNumber}", prNumber);
        }
    }

    partial void OnSubscribedPrChanged(PullRequestInfo? value)
    {
        OnPropertyChanged(nameof(CanInstallPrArtifact));
        InstallPrArtifactCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Unsubscribes from PR updates and switches to MAIN branch.
    /// </summary>
    [RelayCommand]
    private void UnsubscribeFromPr()
    {
        _velopackUpdateManager.SubscribedPrNumber = null;
        SubscribedPr = null;
        ShowPrMergedWarning = false;
        StatusMessage = "Switched to MAIN branch updates";

        // Persist to settings
        _userSettingsService.Update(s => s.SubscribedPrNumber = null);
        _ = _userSettingsService.SaveAsync();

        _logger.LogInformation("Unsubscribed from PR, switched to MAIN");
    }
}
