using System;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Notifications;
using GenHub.Features.AppUpdate.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Info.Services;
using GenHub.Features.Tools.MapManager.ViewModels;
using GenHub.Features.Tools.ReplayManager.ViewModels;
using GenHub.Infrastructure.Imaging;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// Factory for creating demo ViewModels with mock data for interactive demos.
/// </summary>
public static class DemoViewModelFactory
{
    /// <summary>
    /// Creates a demo GameProfileItemViewModel with sample data.
    /// </summary>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <param name="showSteamHighlight">Whether to show the highlight on the Steam button.</param>
    /// <param name="showShortcutHighlight">Whether to show the highlight on the Create Shortcut button.</param>
    /// <returns>A configured demo profile view model.</returns>
    public static GameProfileItemViewModel CreateDemoProfileCard(INotificationService? notificationService = null, bool showSteamHighlight = false, bool showShortcutHighlight = false)
    {
        // Create a mock GameProfile
        var mockProfile = new GameProfile
        {
            Id = "demo-profile-001",
            Name = "Zero Hour Demo",
            Description = "This is a sample profile for demonstration purposes.",
            ThemeColor = "#00A3FF",
            WorkspaceStrategy = WorkspaceStrategy.SymlinkOnly,
            GameClient = new GameClient
            {
                Id = "1.104.steam.gameclient.zerohour",
                Name = "Zero Hour",
                Version = "v1.04",
                GameType = GameType.ZeroHour,
                PublisherType = PublisherTypeConstants.Steam,
            },
            GameInstallationId = "mock-steam-installation", // Required to switch IsSteamInstallation to true so the button appears
            UseSteamLaunch = false, // Explicitly start disabled so the first toggle turns it ON
        };

        GameProfileItemViewModel vm = new(mockProfile.Id, mockProfile, UriConstants.ZeroHourIconUri, "avares://GenHub/Assets/Covers/usa-cover.png");

        // Wire up demo actions that show notifications instead of real operations
        vm.LaunchAction = async _ =>
            {
                notificationService?.Show(new NotificationMessage(
                    NotificationType.Info,
                    "Demo",
                    "Simulating game launch process...",
                    2000));

                await Task.Delay(1500);

                notificationService?.Show(new NotificationMessage(
                    NotificationType.Success,
                    "Demo",
                    "Zero Hour launched successfully! (Simulated)",
                    3000));
            };

        vm.EditProfileAction = async _ =>
            {
                notificationService?.Show(new NotificationMessage(
                    NotificationType.Info,
                    "Demo",
                    "Opening the Profile Editor... (Simulated)",
                    3000));
                await Task.CompletedTask;
            };

        vm.DeleteProfileAction = async _ =>
            {
                notificationService?.Show(new NotificationMessage(
                    NotificationType.Warning,
                    "Demo",
                    "Deleting profiles is restricted in this interactive guide.",
                    3000));
                await Task.CompletedTask;
            };

        vm.CreateShortcutAction = async _ =>
            {
                notificationService?.Show(new NotificationMessage(
                    NotificationType.Success,
                    "Demo",
                    "Desktop Shortcut created successfully on your desktop! (Simulated)",
                    4000));
                await Task.CompletedTask;
            };

        vm.ToggleSteamLaunchAction = async _ =>
            {
                vm.UseSteamLaunch = !vm.UseSteamLaunch;
                notificationService?.Show(new NotificationMessage(
                    NotificationType.Success,
                    "Demo",
                    vm.UseSteamLaunch ? "Steam Integration Enabled: Track hours and use the Overlay." : "Steam Integration Disabled.",
                    3000));
                await Task.CompletedTask;
            };

        // Enable specific visual highlights requested for the demos
        // Explicitly set these to ensure no default state bleed
        vm.IsDemoSteamHighlightVisible = showSteamHighlight;
        vm.IsDemoShortcutHighlightVisible = showShortcutHighlight;

        return vm;
    }

    /// <summary>
    /// Creates a demo UpdateNotificationViewModel with sample data.
    /// </summary>
    /// <param name="notificationService">Optional notification service for demo feedback.</param>
    /// <returns>A configured demo update view model.</returns>
    public static GenHub.Features.AppUpdate.ViewModels.UpdateNotificationViewModel CreateDemoUpdateViewModel(INotificationService? notificationService = null)
    {
        var mockVelopack = new MockVelopackUpdateManager(notificationService);
        var mockSettings = new MockUserSettingsService();
        var mockLogger = new MockLogger<GenHub.Features.AppUpdate.ViewModels.UpdateNotificationViewModel>();

        UpdateNotificationViewModel vm = new(mockVelopack, mockLogger, mockSettings);

        // Manually configure the state to look like an update is available
        vm.IsChecking = false;
        vm.IsUpdateAvailable = true;
        vm.LatestVersion = "1.2.0";
        vm.StatusMessage = "New feature update available!";
        vm.ReleaseNotesUrl = "https://github.com/undead2146/GeneralsHub/releases";

        // Enable PAT features for demo to show "Browse Builds" tab
        vm.HasPat = true;

        // Pre-load dummy data directly to ensure it appears in the demo
        vm.AvailablePullRequests.Clear();
        foreach (var pr in new[]
        {
            new GenHub.Core.Models.AppUpdate.PullRequestInfo { Number = 101, Title = "Feature: Enhanced Profile Management", Author = "undead2146", BranchName = "feature/profile-mgmt", State = "open" },
            new GenHub.Core.Models.AppUpdate.PullRequestInfo { Number = 102, Title = "Fix: Application crash on startup", Author = "Bravo15", BranchName = "fix/startup-crash", State = "open" },
            new GenHub.Core.Models.AppUpdate.PullRequestInfo { Number = 105, Title = "Refactor: Move settings to central storage", Author = "GenHubBot", BranchName = "refactor/settings-storage", State = "open" },
        })
        {
            vm.AvailablePullRequests.Add(pr);
        }

        vm.AvailableBranches.Clear();
        foreach (var branch in new[] { "main", "dev", "v1.2-beta", "feature/ui-rework" })
        {
            vm.AvailableBranches.Add(branch);
        }

        return vm;
    }

    /// <summary>
    /// Creates a demo GameSettingsViewModel with mock data.
    /// </summary>
    /// <returns>A configured demo settings view model.</returns>
    public static GameSettingsViewModel CreateDemoGameSettingsViewModel()
    {
        try
        {
            var mockService = new MockGameSettingsService();
            var mockLogger = new MockLogger<GameSettingsViewModel>();

            var vm = new GameSettingsViewModel(mockService, mockLogger);

            // Initialize with default mock data
            // Use fire-and-forget but safer
            _ = Task.Run(() => vm.InitializeForProfileAsync(null, null));

            // Manually populate with interesting data for the demo
            vm.ResolutionWidth = 2560;
            vm.ResolutionHeight = 1440;
            vm.GoCameraMaxHeightOnlyWhenLobbyHost = 550;
            vm.Windowed = false;

            // vm.PoolSize = 1024; // Not available
            vm.TextureQuality = GenHub.Core.Models.Enums.TextureQuality.High;
            vm.Shadows = true;

            vm.ParticleEffects = true;
            vm.ExtraAnimations = true;

            return vm;
        }
        catch (Exception)
        {
            // Fallback
            var mockService = new MockGameSettingsService();
            var mockLogger = new MockLogger<GameSettingsViewModel>();
            return new(mockService, mockLogger);
        }
    }

    /// <summary>
    /// Creates a demo ReplayManagerViewModel with mock data.
    /// </summary>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <returns>A configured demo replay manager view model.</returns>
    public static ReplayManagerViewModel CreateDemoReplayManager(INotificationService? notificationService = null)
    {
        try
        {
            var mockDir = new MockReplayDirectoryService();
            var mockImport = new MockReplayImportService();
            var mockExport = new MockReplayExportService();
            var mockHistory = new MockUploadHistoryService();

            // Use the provided notification service or fall back to mock
            var mockNotify = notificationService ?? new MockNotificationService();
            var mockLogger = new MockLogger<ReplayManagerViewModel>();

            var vm = new ReplayManagerViewModel(
                mockDir,
                mockImport,
                mockExport,
                mockHistory,
                mockNotify,
                mockLogger);

            _ = Task.Run(() => vm.InitializeAsync());
            return vm;
        }
        catch
        {
            // Fail safe
            return null!;
        }
    }

    /// <summary>
    /// Creates a demo MapManagerViewModel with mock data.
    /// </summary>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <returns>A configured demo map manager view model.</returns>
    public static MapManagerViewModel CreateDemoMapManager(INotificationService? notificationService = null)
    {
        try
        {
            var mockDir = new MockMapDirectoryService();
            var mockImport = new MockMapImportService();
            var mockExport = new MockMapExportService();
            var mockPack = new MockMapPackService();
            var mockHistory = new MockUploadHistoryService();

            // Use the provided notification service or fall back to mock
            var mockNotify = notificationService ?? new MockNotificationService();
            var mockLogger = new MockLogger<MapManagerViewModel>();

            // Provide a real mocked logger for the parser too
            var parserLogger = new MockLogger<TgaImageParser>();
            var parser = new TgaImageParser(parserLogger);

            var vm = new MapManagerViewModel(
                mockDir,
                mockImport,
                mockExport,
                mockPack,
                mockHistory,
                mockNotify,
                parser,
                mockLogger)
            {
                IsMapPackPanelOpen = false,
                IsHistoryOpen = false,
            };

            _ = Task.Run(() => vm.InitializeAsync());
            return vm;
        }
        catch
        {
             return null!;
        }
    }

    /// <summary>
    /// Creates a demo AddLocalContentViewModel with mock data.
    /// </summary>
    /// <returns>A configured demo add local content view model.</returns>
    public static AddLocalContentViewModel CreateDemoAddLocalContent()
    {
        var mockService = new MockLocalContentService();
        var mockLogger = new MockLogger<AddLocalContentViewModel>();

        return new AddLocalContentViewModel(mockService, mockLogger);
    }

    /// <summary>
    /// Creates a demo WorkspaceDemoViewModel with mock data.
    /// </summary>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <returns>A configured demo workspace view model.</returns>
    public static WorkspaceDemoViewModel CreateDemoWorkspaceViewModel(INotificationService? notificationService = null)
    {
        return new WorkspaceDemoViewModel(notificationService);
    }

    /// <summary>
    /// Creates a demo GameProfileSettingsViewModel with the Content tab selected and visible.
    /// </summary>
    /// <returns>A configured demo profile settings view model for the Content tab demo.</returns>
    public static GameProfileSettingsViewModel CreateDemoProfileSettingsViewModel_ContentTab()
    {
        var mockProfileManager = new MockGameProfileManager();
        var mockGameSettings = new MockGameSettingsService();
        var mockConfig = new MockConfigurationProviderService();
        var mockLoader = new MockProfileContentLoader();
        var mockNotify = new MockNotificationService();
        var mockManifests = new MockContentManifestPool();
        var mockStorage = new MockContentStorageService();
        var mockLocalContent = new MockLocalContentService();
        var mockLogger = new MockLogger<GameProfileSettingsViewModel>();
        var mockSettingsLogger = new MockLogger<GameSettingsViewModel>();

        // Use the dedicated Demo subclass that overrides content loading logic
        // This guarantees mock data appears regardless of service state or race conditions
        DemoGameProfileSettingsViewModel vm = new(
            mockProfileManager,
            mockGameSettings,
            mockConfig,
            mockLoader,
            null, // profileResourceService
            mockNotify,
            mockManifests,
            mockStorage,
            mockLocalContent,
            mockLogger,
            mockSettingsLogger);

        // Set the Content tab as selected (index 0)
        vm.SelectedTabIndex = 0;

        // Ensure dialog is closed immediately
        vm.IsAddLocalContentDialogOpen = false;

        return vm;
    }

    /// <summary>
    /// Creates a demo GameProfileSettingsViewModel with the Settings tab selected and visible.
    /// </summary>
    /// <returns>A configured demo profile settings view model for the Settings tab demo.</returns>
    public static GameProfileSettingsViewModel CreateDemoProfileSettingsViewModel_SettingsTab()
    {
        var mockProfileManager = new MockGameProfileManager();
        var mockGameSettings = new MockGameSettingsService();
        var mockConfig = new MockConfigurationProviderService();
        var mockLoader = new MockProfileContentLoader();
        var mockNotify = new MockNotificationService();
        var mockManifests = new MockContentManifestPool();
        var mockStorage = new MockContentStorageService();
        var mockLocalContent = new MockLocalContentService();
        var mockLogger = new MockLogger<GameProfileSettingsViewModel>();
        var mockSettingsLogger = new MockLogger<GameSettingsViewModel>();

        // Use the dedicated Demo subclass that overrides content loading logic
        // This guarantees mock data appears regardless of service state or race conditions
        DemoGameProfileSettingsViewModel vm = new(
            mockProfileManager,
            mockGameSettings,
            mockConfig,
            mockLoader,
            null, // profileResourceService
            mockNotify,
            mockManifests,
            mockStorage,
            mockLocalContent,
            mockLogger,
            mockSettingsLogger);

        // Set the Game Settings tab as selected (index 2)
        vm.SelectedTabIndex = 2;

        // Ensure dialog is closed immediately
        vm.IsAddLocalContentDialogOpen = false;

        return vm;
    }

    /// <summary>
    /// Creates a demo GameProfileSettingsViewModel with mock data.
    /// </summary>
    /// <returns>A configured demo profile settings view model.</returns>
    [Obsolete("Use CreateDemoProfileSettingsViewModel_ContentTab() or CreateDemoProfileSettingsViewModel_SettingsTab() instead to ensure proper demo context")]
    public static GameProfileSettingsViewModel CreateDemoProfileSettingsViewModel()
    {
        try
        {
            var mockProfileManager = new MockGameProfileManager();
            var mockGameSettings = new MockGameSettingsService();
            var mockConfig = new MockConfigurationProviderService();
            var mockLoader = new MockProfileContentLoader();
            var mockNotify = new MockNotificationService();
            var mockManifests = new MockContentManifestPool();
            var mockStorage = new MockContentStorageService();
            var mockLocalContent = new MockLocalContentService();
            var mockLogger = new MockLogger<GameProfileSettingsViewModel>();
            var mockSettingsLogger = new MockLogger<GameSettingsViewModel>();

            // Use the dedicated Demo subclass that overrides content loading logic
            // This guarantees mock data appears regardless of service state or race conditions
            DemoGameProfileSettingsViewModel vm = new(
                mockProfileManager,
                mockGameSettings,
                mockConfig,
                mockLoader,
                null, // profileResourceService
                mockNotify,
                mockManifests,
                mockStorage,
                mockLocalContent,
                mockLogger,
                mockSettingsLogger);

            // The Demo subclass handles its own initialization in the constructor
            // and overrides RefreshVisibleFiltersAsync and LoadAvailableContentAsync
            // to provide instant mock data without service calls.

            // Ensure dialog is closed immediately
            vm.IsAddLocalContentDialogOpen = false;

            return vm;
        }
        catch
        {
            return null!;
        }
    }
}
