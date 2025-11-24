using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
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
    /// Initializes a new instance of the <see cref="UpdateNotificationViewModel"/> class.
    /// </summary>
    /// <param name="velopackUpdateManager">The Velopack update manager.</param>
    /// <param name="logger">The logger.</param>
    public UpdateNotificationViewModel(
        IVelopackUpdateManager velopackUpdateManager,
        ILogger<UpdateNotificationViewModel> logger)
    {
        _velopackUpdateManager = velopackUpdateManager ?? throw new ArgumentNullException(nameof(velopackUpdateManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cancellationTokenSource = new CancellationTokenSource();

        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !IsChecking);
        DismissCommand = new RelayCommand(DismissUpdate);

        _logger.LogInformation("UpdateNotificationViewModel initialized with Velopack");

        // Automatically check for updates when dialog opens
        _ = CheckForUpdatesAsync();
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

            _currentUpdateInfo = await _velopackUpdateManager.CheckForUpdatesAsync(_cancellationTokenSource.Token);

            // Check BOTH UpdateInfo (for installed app with working Velopack) AND GitHub flag (for installed app where Velopack has issues)
            if (_currentUpdateInfo != null)
            {
                IsUpdateAvailable = true;
                LatestVersion = _currentUpdateInfo.TargetFullRelease.Version.ToString();
                ReleaseNotesUrl = AppConstants.GitHubRepositoryUrl + "/releases/tag/v" + LatestVersion;
                StatusMessage = $"Update available: v{LatestVersion}";
                _logger.LogInformation("Update available from UpdateManager: {Version}", LatestVersion);
            }
            else if (_velopackUpdateManager.HasUpdateAvailableFromGitHub)
            {
                // GitHub API detected update but UpdateManager couldn't confirm
                var githubVersion = _velopackUpdateManager.LatestVersionFromGitHub;
                _logger.LogDebug(
                    "GitHub update detected: HasUpdate={HasUpdate}, Version='{Version}'",
                    _velopackUpdateManager.HasUpdateAvailableFromGitHub,
                    githubVersion ?? "NULL");

                IsUpdateAvailable = true;
                LatestVersion = githubVersion ?? "Unknown";
                ReleaseNotesUrl = AppConstants.GitHubRepositoryUrl + "/releases/tag/v" + LatestVersion;
                StatusMessage = $"Update available: v{LatestVersion}";
                _logger.LogInformation("Update available from GitHub API: {Version}", LatestVersion);
            }
            else
            {
                IsUpdateAvailable = false;
                LatestVersion = string.Empty;
                StatusMessage = "You're up to date!";
                _logger.LogInformation("No updates available");
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
    /// Dismisses the update notification.
    /// </summary>
    private void DismissUpdate()
    {
        IsUpdateAvailable = false;
        _currentUpdateInfo = null;
        StatusMessage = "Ready to check for updates";
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
}
