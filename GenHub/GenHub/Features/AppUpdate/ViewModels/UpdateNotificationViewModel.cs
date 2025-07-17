using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.AppUpdate.ViewModels;

/// <summary>
/// ViewModel for the update notification dialog.
/// </summary>
public partial class UpdateNotificationViewModel : ObservableObject
{
    private readonly IAppUpdateService _updateService;
    private readonly IUpdateInstaller _updateInstaller;
    private readonly ILogger<UpdateNotificationViewModel> _logger;

    /// <summary>
    /// Gets or sets the repository owner.
    /// </summary>
    [ObservableProperty]
    private string _repositoryOwner = "Community-Outpost";

    /// <summary>
    /// Gets or sets the repository name.
    /// </summary>
    [ObservableProperty]
    private string _repositoryName = "GenHub";

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "Ready to check for updates";

    /// <summary>
    /// Gets or sets a value indicating whether an update check is in progress.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCheckButtonEnabled))]
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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    [NotifyPropertyChangedFor(nameof(DisplayLatestVersion))]
    private UpdateCheckResult? _updateCheckResult;

    [ObservableProperty]
    private UpdateProgress _installationProgress;

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
    /// <param name="updateService">The update service.</param>
    /// <param name="updateInstaller">The update installer.</param>
    /// <param name="logger">The logger.</param>
    public UpdateNotificationViewModel(
        IAppUpdateService updateService,
        IUpdateInstaller updateInstaller,
        ILogger<UpdateNotificationViewModel> logger)
    {
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
        _updateInstaller = updateInstaller ?? throw new ArgumentNullException(nameof(updateInstaller));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize properties to prevent null binding errors
        _installationProgress = new UpdateProgress { Status = "Ready", PercentComplete = 0 };

        // Initialize with default non-null values to prevent binding errors
        _updateCheckResult = new UpdateCheckResult
        {
            IsUpdateAvailable = false,
            ReleaseTitle = "Ready to check for updates",
            ReleaseNotes = "Click 'Check For Updates' to begin.",
            CurrentVersion = "0.0.0",
            LatestVersion = "0.0.0",
            UpdateUrl = string.Empty,
        };

        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        DismissCommand = new RelayCommand(DismissUpdate);

        _logger.LogInformation("UpdateNotificationViewModel initialized");
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
    public string CurrentAppVersion => _updateService.GetCurrentVersion();

    /// <summary>
    /// Gets helper property to expose release assets from UpdateCheckResult.
    /// </summary>
    public IEnumerable<GitHubReleaseAsset> UpdateAssets =>
        UpdateCheckResult?.Assets ?? Enumerable.Empty<GitHubReleaseAsset>();

    /// <summary>
    /// Gets a value indicating whether an update is available and can be downloaded.
    /// </summary>
    public bool CanDownloadUpdate =>
        UpdateCheckResult?.IsUpdateAvailable == true &&
        UpdateAssets.Any() &&
        !IsInstalling;

    /// <summary>
    /// Gets a value indicating whether the check button should be enabled.
    /// </summary>
    public bool IsCheckButtonEnabled => !IsChecking;

    /// <summary>
    /// Gets the text for the install button.
    /// </summary>
    public string InstallButtonText => IsInstalling ? "Installing..." : "Install Update";

    /// <summary>
    /// Gets a value indicating whether the update result is not null.
    /// </summary>
    public bool HasUpdateResult => UpdateCheckResult is not null;

    /// <summary>
    /// Gets the latest version string, ensuring it has a 'v' prefix for display.
    /// </summary>
    public string DisplayLatestVersion
    {
        get
        {
            var version = UpdateCheckResult?.LatestVersion;
            if (string.IsNullOrEmpty(version))
            {
                return "v0.0.0";
            }

            return version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : $"v{version}";
        }
    }

    /// <summary>
    /// Checks for updates asynchronously.
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
            // Ensure UI updates happen on UI thread
            IsChecking = true;
            HasError = false;
            ErrorMessage = string.Empty;
            StatusMessage = "Checking for updates...";

            // Update the current result to show checking state
            UpdateCheckResult = new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                ReleaseTitle = "Checking for updates...",
                ReleaseNotes = "Please wait while we check for updates.",
                CurrentVersion = UpdateCheckResult?.CurrentVersion ?? "0.0.0",
                LatestVersion = UpdateCheckResult?.LatestVersion ?? "0.0.0",
                UpdateUrl = string.Empty,
            };

            _logger.LogInformation("Starting update check");

            // Perform the actual update check (this can be on background thread)
            var result = await _updateService.CheckForUpdatesAsync(RepositoryOwner, RepositoryName);

            // Update UI properties back on UI thread
            // Ensure we always have a non-null result
            if (result == null)
            {
                UpdateCheckResult = UpdateCheckResult.NoUpdateAvailable(
                    UpdateCheckResult.CurrentVersion,
                    UpdateCheckResult.CurrentVersion);
            }
            else
            {
                UpdateCheckResult = result;
            }

            StatusMessage = UpdateCheckResult.IsUpdateAvailable
                ? $"Update available: {UpdateCheckResult.LatestVersion}"
                : "No updates available";

            _logger.LogInformation("Update check completed. Update available: {IsUpdateAvailable}", UpdateCheckResult.IsUpdateAvailable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update check failed");
            HasError = true;
            ErrorMessage = $"Failed to check for updates: {ex.Message}";
            StatusMessage = "Update check failed";

            // Create error result that's still non-null
            UpdateCheckResult = UpdateCheckResult.Error($"Failed to check for updates: {ex.Message}");
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
        var url = UpdateCheckResult?.UpdateUrl;
        if (!string.IsNullOrEmpty(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open browser for release notes.");
            }
        }
    }

    /// <summary>
    /// Downloads and applies the update. This is the single entry point for the installation process.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownloadUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (!CanDownloadUpdate)
        {
            return;
        }

        try
        {
            IsInstalling = true;
            HasError = false;
            ErrorMessage = string.Empty;
            StatusMessage = "Preparing to download update...";
            InstallationProgress = new UpdateProgress { Status = "Preparing download...", PercentComplete = 0 };

            var downloadUrl = _updateInstaller.GetPlatformDownloadUrl(UpdateAssets);
            if (string.IsNullOrEmpty(downloadUrl))
            {
                StatusMessage = "No compatible update package found for your platform.";
                InstallationProgress = new UpdateProgress
                {
                    Status = "No compatible update package found",
                    HasError = true,
                    ErrorMessage = "No compatible update package found for your platform.",
                };
                return;
            }

            var progress = new Progress<UpdateProgress>(p =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    InstallationProgress = p;
                    StatusMessage = p.Status;
                    DownloadProgress = p.PercentComplete;
                });
            });

            var success = await _updateInstaller.DownloadAndInstallAsync(downloadUrl, progress);

            if (success)
            {
                StatusMessage = "Update prepared successfully! The application will restart automatically.";
                InstallationProgress = new UpdateProgress
                {
                    Status = "Update prepared successfully! The application will restart automatically.",
                    PercentComplete = 100,
                    IsCompleted = true,
                };

                await Task.Delay(2000);
            }
            else
            {
                StatusMessage = "Update installation failed.";
                InstallationProgress = new UpdateProgress
                {
                    Status = "Installation failed",
                    HasError = true,
                    ErrorMessage = "Update installation failed.",
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install update");
            HasError = true;
            ErrorMessage = $"Update failed: {ex.Message}";
            StatusMessage = "Update failed";
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
        UpdateCheckResult = new UpdateCheckResult
        {
            IsUpdateAvailable = false,
            ReleaseTitle = string.Empty,
            ReleaseNotes = string.Empty,
            CurrentVersion = UpdateCheckResult?.CurrentVersion ?? "0.0.0",
            LatestVersion = UpdateCheckResult?.LatestVersion ?? "0.0.0",
            UpdateUrl = string.Empty,
        };

        StatusMessage = string.Empty;
        HasError = false;
        ErrorMessage = string.Empty;
    }

    /// <summary>
    /// Handles update progress reporting.
    /// </summary>
    /// <param name="progress">The update progress.</param>
    private void OnUpdateProgress(UpdateProgress progress)
    {
        if (progress == null)
        {
            return;
        }

        DownloadProgress = progress.PercentComplete;
        StatusMessage = progress.Status ?? string.Empty;

        if (progress.HasError)
        {
            HasError = true;
            ErrorMessage = progress.ErrorMessage ?? "An unknown error occurred";
        }

        _logger.LogDebug("Update progress: {Percent}% - {Status}", progress.PercentComplete, progress.Status);
    }

    // Add method to handle property changes that affect command state
    partial void OnIsCheckingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCheckButtonEnabled));
    }

    partial void OnUpdateCheckResultChanged(UpdateCheckResult? value)
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
        OnPropertyChanged(nameof(UpdateAssets));
        OnPropertyChanged(nameof(CanDownloadUpdate));
        OnPropertyChanged(nameof(HasUpdateResult));
        OnPropertyChanged(nameof(DisplayLatestVersion));
        OnPropertyChanged(nameof(InstallButtonText));
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }
}
