using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.GameInstallations;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for launching game profiles.
/// </summary>
public partial class GameProfileLauncherViewModel(
    IGameInstallationService? installationService,
    ILogger<GameProfileLauncherViewModel>? logger) : ViewModelBase
{
    private readonly IGameInstallationService? _installationService = installationService;
    private readonly ILogger<GameProfileLauncherViewModel>? _logger = logger;

    [ObservableProperty]
    private ObservableCollection<GameProfileItemViewModel> _profiles = new();

    [ObservableProperty]
    private bool _isLaunching;
    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileLauncherViewModel"/> class for design-time or test usage.
    /// </summary>
    public GameProfileLauncherViewModel()
        : this(null, null)
    {
    }

    /// <summary>
    /// Gets the command to create a shortcut for the selected profile.
    /// </summary>
    public IRelayCommand CreateShortcutCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets the command to edit the selected profile.
    /// </summary>
    public IRelayCommand EditProfileCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets the command to delete the selected profile.
    /// </summary>
    public IRelayCommand DeleteProfileCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets the command to toggle edit mode.
    /// </summary>
    public IRelayCommand ToggleEditModeCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets the command to save profiles.
    /// </summary>
    public IRelayCommand SaveProfilesCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets the command to scan for games.
    /// </summary>
    public IRelayCommand CreateNewProfileCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Performs asynchronous initialization for the Downloads tab.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public virtual async Task InitializeAsync()
    {
        try
        {
            StatusMessage = "Loading profiles...";
            Profiles.Clear();

            // TODO: Wire up UI to load profiles from IGameProfileManager in a future UI-focused PR.
            // This involves integrating the profile service into the view model initialization.
            await Task.CompletedTask;
            StatusMessage = "Profiles loaded";
            _logger?.LogInformation("Game profile launcher initialized");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error initializing profiles");
            StatusMessage = "Error loading profiles";
        }
    }

    /// <summary>
    /// Scans for games and refreshes installations.
    /// </summary>
    [RelayCommand]
    private async Task ScanForGames()
    {
        if (_installationService == null)
        {
            StatusMessage = "Game installation service not available";
            return;
        }

        if (_logger == null)
        {
            StatusMessage = "Logger not available - scan may proceed without detailed logging";
        }

        try
        {
            StatusMessage = "Scanning for games...";

            // Scan for all installations
            var installations = await _installationService.GetAllInstallationsAsync();
            if (installations.Success && installations.Data != null)
            {
                StatusMessage = $"Scan complete. Found {installations.Data.Count} game installations";
                _logger?.LogInformation("Game scan completed successfully. Found {Count} installations", installations.Data.Count);
            }
            else
            {
                StatusMessage = $"Scan failed: {string.Join(", ", installations.Errors)}";
                _logger?.LogWarning("Game scan failed: {Errors}", string.Join(", ", installations.Errors));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error scanning for games");
            StatusMessage = "Error during scan";
        }
    }

    /// <summary>
    /// Launches the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile to launch.</param>
    [RelayCommand]
    private void LaunchProfile(GameProfileItemViewModel profile)
    {
        // TODO:  Wire up actual launch logic in a future UI-focused PR.
    }
}