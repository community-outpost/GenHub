using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Features.AppUpdate.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Info.Services;
using GenHub.Features.Tools.GenHotkeys.Services;
using GenHub.Features.Tools.GenHotkeys.ViewModels;
using GenHub.Features.Tools.MapManager.ViewModels;
using GenHub.Features.Tools.ModBuilder.ViewModels;
using GenHub.Features.Tools.ReplayManager.ViewModels;
using GenHub.Features.Tools.ViewModels;
using GenHub.Features.Tools.WndEditor.Services;
using GenHub.Features.Tools.WndEditor.ViewModels;
using GenHub.Infrastructure.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

        GameProfileItemViewModel vm = new(mockProfile.Id, mockProfile, UriConstants.ZeroHourIconUri, "avares://GenHub/Assets/Covers/usa-cover.jpg")
        {
            // Wire up demo actions that show notifications instead of real operations
            LaunchAction = async _ =>
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
                    NotificationDurations.Short));
            },

            EditProfileAction = async _ =>
            {
                notificationService?.Show(new NotificationMessage(
                    NotificationType.Info,
                    "Demo",
                    "Opening the Profile Editor... (Simulated)",
                    NotificationDurations.Short));
                await Task.CompletedTask;
            },

            DeleteProfileAction = async _ =>
            {
                notificationService?.Show(new NotificationMessage(
                    NotificationType.Warning,
                    "Demo",
                    "Deleting profiles is restricted in this interactive guide.",
                    NotificationDurations.Short));
                await Task.CompletedTask;
            },

            CreateShortcutAction = async _ =>
            {
                notificationService?.Show(new NotificationMessage(
                    NotificationType.Success,
                    "Demo",
                    "Desktop Shortcut created successfully on your desktop! (Simulated)",
                    4000));
                await Task.CompletedTask;
            },

            ShareProfileAction = async _ =>
            {
                notificationService?.Show(new NotificationMessage(
                    NotificationType.Info,
                    "Demo",
                    "Sharing profiles is simulated in this interactive guide.",
                    NotificationDurations.Short));
                await Task.CompletedTask;
            },

            // Enable specific visual highlights requested for the demos
            // Explicitly set these to ensure no default state bleed
            IsDemoSteamHighlightVisible = showSteamHighlight,
            IsDemoShortcutHighlightVisible = showShortcutHighlight,
        };

        vm.ToggleSteamLaunchAction = async _ =>
        {
            vm.UseSteamLaunch = !vm.UseSteamLaunch;
            notificationService?.Show(new NotificationMessage(
                NotificationType.Success,
                "Demo",
                vm.UseSteamLaunch ? "Steam Integration Enabled: Track hours and use the Overlay." : "Steam Integration Disabled.",
                NotificationDurations.Short));
            await Task.CompletedTask;
        };

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

        UpdateNotificationViewModel vm = new(mockVelopack, mockLogger, mockSettings)
        {
            // Manually configure the state to look like an update is available
            IsChecking = false,
            IsUpdateAvailable = true,
            LatestVersion = "1.2.0",
            StatusMessage = "New feature update available!",
            ReleaseNotesUrl = "https://github.com/undead2146/GeneralsHub/releases",

            // Enable authenticated features for demo to show "Browse Builds" tab
            IsAuthenticated = true,
        };

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
        foreach (var branch in new[] { "main", "development", "v1.2-beta", "feature/ui-rework" })
        {
            vm.AvailableBranches.Add(branch);
        }

        return vm;
    }

    /// <summary>
    /// Creates a demo ReplayManagerViewModel with mock data.
    /// </summary>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <param name="localizationService">Optional localization service for dynamic string translation.</param>
    /// <returns>A configured demo replay manager view model.</returns>
    public static ReplayManagerViewModel CreateDemoReplayManager(
        INotificationService? notificationService = null,
        ILocalizationService? localizationService = null)
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

            var mockCheckpoint = new MockReplayCheckpointService();
            var demoRecoveryProfile = CreateDemoRecoveryProfile();
            var mockProfileManager = new MockGameProfileManager([demoRecoveryProfile]);

            var vm = new ReplayManagerViewModel(
                mockDir,
                mockCheckpoint,
                mockProfileManager,
                mockImport,
                mockExport,
                mockHistory,
                mockNotify,
                mockLogger,
                localizationService: localizationService);

            _ = SeedDemoAsync(() => vm.InitializeAsync(), "replay manager");
            return vm;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create full demo replay manager: {ex}");

            // Fail safe with minimal mocks
            var demoRecoveryProfile = CreateDemoRecoveryProfile();

            return new ReplayManagerViewModel(
                new MockReplayDirectoryService(),
                new MockReplayCheckpointService(),
                new MockGameProfileManager([demoRecoveryProfile]),
                new MockReplayImportService(),
                new MockReplayExportService(),
                new MockUploadHistoryService(),
                new MockNotificationService(),
                new MockLogger<ReplayManagerViewModel>(),
                localizationService: localizationService);
        }
    }

    /// <summary>
    /// Creates a demo MapManagerViewModel with mock data.
    /// </summary>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <param name="localizationService">Optional localization service for dynamic string translation.</param>
    /// <returns>A configured demo map manager view model.</returns>
    public static MapManagerViewModel CreateDemoMapManager(
        INotificationService? notificationService = null,
        ILocalizationService? localizationService = null)
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
                mockLogger,
                localizationService: localizationService)
            {
                IsMapPackPanelOpen = false,
                IsHistoryOpen = false,
            };

            _ = SeedDemoAsync(() => vm.InitializeAsync(), "map manager");
            return vm;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create full demo map manager: {ex}");

            // Fail safe with minimal mocks
            return new MapManagerViewModel(
                new MockMapDirectoryService(),
                new MockMapImportService(),
                new MockMapExportService(),
                new MockMapPackService(),
                new MockUploadHistoryService(),
                new MockNotificationService(),
                new TgaImageParser(new MockLogger<TgaImageParser>()),
                new MockLogger<MapManagerViewModel>(),
                localizationService: localizationService);
        }
    }

    /// <summary>
    /// Creates the actual WND editor view model with a sample main menu layout loaded from text.
    /// Uses the real document parser with mock asset and installation services so nothing touches disk.
    /// </summary>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <param name="localizationService">Optional localization service for dynamic string translation.</param>
    /// <returns>A configured WND editor view model with sample content.</returns>
    public static WndEditorViewModel CreateDemoWndEditor(
        INotificationService? notificationService = null,
        ILocalizationService? localizationService = null)
    {
        var vm = new WndEditorViewModel(
            new WndDocumentService(new MockLogger<WndDocumentService>()),
            notificationService ?? new MockNotificationService(),
            localizationService ?? new MockLocalizationService(),
            new MockDialogService(),
            new MockWndGameInstallationService(),
            new MockWndEditorAssetService(),
            new MockWndTextureImportService(),
            new MockChallengeMedalService(),
            new MockLogger<WndEditorViewModel>());

        _ = SeedDemoAsync(SeedWndDemoAsync, "WND document");

        return vm;

        async Task SeedWndDemoAsync()
        {
            // Start zoomed out so the whole sample menu fits the demo viewport on first paint.
            vm.Zoom = 0.5;
            await vm.LoadFromTextAsync(BuildSampleMainMenuDocument(), "MainMenu.wnd");
            vm.SelectedAssetInstallation ??= vm.AvailableInstallations.FirstOrDefault();
        }
    }

    /// <summary>
    /// Creates the actual ModBuilder view model with mock build and project services.
    /// </summary>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <param name="localizationService">Optional localization service for dynamic string translation.</param>
    /// <returns>A configured ModBuilder view model.</returns>
    public static ModBuilderViewModel CreateDemoModBuilder(
        INotificationService? notificationService = null,
        ILocalizationService? localizationService = null)
    {
        var notify = notificationService ?? new MockNotificationService();
        var loc = localizationService ?? new MockLocalizationService();
        var fileManager = new FileManagerViewModel(
            new MockGameInstallationService(),
            notify,
            new WndDocumentService(new MockLogger<WndDocumentService>()),
            loc,
            new MockLogger<FileManagerViewModel>());

        var configLoader = new MockConfigurationLoaderService();
        var vm = new ModBuilderViewModel(
            new MockBuildEngineService(),
            new MockProjectConfigService(),
            configLoader,
            new MockProjectStructureGenerator(),
            notify,
            loc,
            fileManager,
            NullLoggerFactory.Instance,
            new MockLogger<ModBuilderViewModel>(),
            new MockDialogService(),
            null);

        _ = SeedDemoAsync(SeedModBuilderDemoAsync, "ModBuilder");

        return vm;

        async Task SeedModBuilderDemoAsync()
        {
            await vm.InitializeAsync();
            var project = new ModBuilderProject
            {
                Name = "Demo Mod",
                Version = "1.0.0",
                Description = "Sample project for the interactive guide.",
                Author = "GenHub Guide",
                TargetGame = GameType.ZeroHour,
                ContentType = ContentType.Mod,
                ProjectDir = "demo-mod-project",
                ConfigFiles = ["configs/build.json", "configs/bundles.json"],
                BundleConfigs = ["configs/bundles.json"],
            };
            project.Configuration = await configLoader.LoadProjectConfigurationAsync(project.ProjectDir);
            await vm.HandleNewProjectCreatedAsync("demo-mod-project/DemoMod.mbproj", project.Name, project, announceCreation: false);
        }
    }

    /// <summary>
    /// Creates the actual GenHotkeys view model with the real tech tree and an in-memory sample profile.
    /// </summary>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <param name="localizationService">Optional localization service for dynamic string translation.</param>
    /// <returns>A configured GenHotkeys view model with sample content.</returns>
    public static GenHotkeysViewModel CreateDemoGenHotkeys(
        INotificationService? notificationService = null,
        ILocalizationService? localizationService = null)
    {
        var vm = new GenHotkeysViewModel(
            new TechTreeService(new MockLogger<TechTreeService>()),
            new MockHotkeyProfileStorageService(),
            new MockHotkeyPackageService(),
            new MockLogger<GenHotkeysViewModel>(),
            notificationService ?? new MockNotificationService(),
            new MockGameProfileManager(),
            null,
            null,
            null,
            new MockDialogService(),
            localizationService);

        _ = SeedDemoAsync(() => vm.InitializeAsync(), "GenHotkeys");

        return vm;
    }

    /// <summary>
    /// Creates the actual Publisher Studio view model with an in-memory sample project.
    /// Nothing is read from or written to disk.
    /// </summary>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <param name="localizationService">Optional localization service for dynamic string translation.</param>
    /// <returns>A configured Publisher Studio view model with sample content.</returns>
    public static PublisherStudioViewModel CreateDemoPublisherStudio(
        INotificationService? notificationService = null,
        ILocalizationService? localizationService = null)
    {
        var notify = notificationService ?? new MockNotificationService();
        var dialogService = new MockPublisherStudioDialogService();
        var studioLogger = new MockLogger<PublisherStudioViewModel>();
        var vm = new PublisherStudioViewModel(
            studioLogger,
            new MockPublisherStudioService(),
            dialogService,
            new MockHostingProviderFactory(),
            new MockHostingStateManager(),
            notify,
            new MockConfigurationProviderService(),
            localizationService,
            new MockHostingCredentialStore(),
            catalogParser: null,
            subscriptionStore: new MockPublisherSubscriptionStore());

        var project = CreateSamplePublisherProject();
        project.ProjectPath = "demo-publisher-project.json";
        vm.CurrentProject = project;

        _ = SeedDemoAsync(SeedPublisherStudioDemoAsync, "Publisher Studio");

        return vm;

        async Task SeedPublisherStudioDemoAsync()
        {
            await vm.InitializeChildViewModelsAsync();
            vm.PublishShareViewModel?.ReloadHostingProviders();
        }
    }

    /// <summary>
    /// Creates a demo AddLocalContentViewModel with mock data.
    /// </summary>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <returns>A configured demo add local content view model.</returns>
    public static DemoAddLocalContentViewModel CreateDemoAddLocalContent(INotificationService? notificationService = null)
    {
        var mockService = new MockLocalContentService();
        var mockLogger = new MockLogger<AddLocalContentViewModel>();

        return new DemoAddLocalContentViewModel(mockService, null, notificationService, mockLogger);
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
        return CreateBaseDemoProfileSettingsViewModel(0);
    }

    /// <summary>
    /// Creates a demo GameProfileSettingsViewModel with the Profile Settings tab selected and visible.
    /// </summary>
    /// <returns>A configured demo profile settings view model for the Profile Settings tab demo.</returns>
    public static GameProfileSettingsViewModel CreateDemoProfileSettingsViewModel_ProfileTab()
    {
        return CreateBaseDemoProfileSettingsViewModel(1);
    }

    /// <summary>
    /// Creates a demo GameProfileSettingsViewModel with the Settings tab selected and visible.
    /// </summary>
    /// <returns>A configured demo profile settings view model for the Settings tab demo.</returns>
    public static GameProfileSettingsViewModel CreateDemoProfileSettingsViewModel_SettingsTab()
    {
        return CreateBaseDemoProfileSettingsViewModel(2);
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
            return CreateBaseDemoProfileSettingsViewModel(0);
        }
        catch
        {
            // Fallback for deprecated method
            var mockLogger = new MockLogger<GameProfileSettingsViewModel>();
            var mockSettingsLogger = new MockLogger<GameSettingsViewModel>();

            return new DemoGameProfileSettingsViewModel(
               new MockGameProfileManager(),
               new MockGameSettingsService(),
               new MockConfigurationProviderService(),
               new MockProfileContentLoader(),
               null,
               new MockNotificationService(),
               new MockContentManifestPool(),
               new MockContentStorageService(),
               new MockLocalContentService(),
               null, // genLauncherNormalizationService
               null, // dialogService
               mockLogger,
               mockSettingsLogger)
            {
                IsAddLocalContentDialogOpen = false,
            };
        }
    }

    /// <summary>
    /// Creates a demo ScanWizardDemoViewModel with mock detected games.
    /// </summary>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <param name="scanDelayMs">Simulated scan delay in milliseconds.</param>
    /// <param name="delayProvider">Optional custom delay provider for deterministic testing.</param>
    /// <returns>A configured demo scan wizard view model.</returns>
    public static ScanWizardDemoViewModel CreateDemoScanWizard(
        INotificationService? notificationService = null,
        int scanDelayMs = 1000,
        Func<CancellationToken, Task>? delayProvider = null)
    {
        return new ScanWizardDemoViewModel(notificationService, scanDelayMs, delayProvider);
    }

    private static DemoGameProfileSettingsViewModel CreateBaseDemoProfileSettingsViewModel(int selectedTabIndex)
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

        return new DemoGameProfileSettingsViewModel(
            mockProfileManager,
            mockGameSettings,
            mockConfig,
            mockLoader,
            null, // profileResourceService
            mockNotify,
            mockManifests,
            mockStorage,
            mockLocalContent,
            null, // genLauncherNormalizationService
            null, // dialogService
            mockLogger,
            mockSettingsLogger)
        {
            SelectedTabIndex = selectedTabIndex,
            IsAddLocalContentDialogOpen = false,
        };
    }

    private static PublisherStudioProject CreateSamplePublisherProject()
    {
        var catalog = new PublisherCatalog
        {
            Publisher = new PublisherProfile
            {
                Id = "demo-publisher",
                Name = "Demo Publisher",
                Description = "Sample publisher for the interactive guide.",
            },
            LastUpdated = DateTime.UtcNow,
            Content =
            [
                new CatalogContentItem
                {
                    Id = "demo-mod",
                    Name = "Demo Mod",
                    Description = "A sample mod entry with one release.",
                    ContentType = ContentType.Mod,
                    TargetGame = GameType.ZeroHour,
                    Releases =
                    [
                        new ContentRelease
                        {
                            Title = "Demo Mod",
                            Version = "1.0.0",
                            ReleaseDate = DateTime.UtcNow.AddDays(-7),
                            IsLatest = true,
                            Changelog = "Initial demo release.",
                        },
                    ],
                    Tags = ["demo", "sample"],
                },
                new CatalogContentItem
                {
                    Id = "demo-map-pack",
                    Name = "Demo Map Pack",
                    Description = "A sample map pack entry with one release.",
                    ContentType = ContentType.MapPack,
                    TargetGame = GameType.ZeroHour,
                    Releases =
                    [
                        new ContentRelease
                        {
                            Title = "Demo Map Pack",
                            Version = "2.1.0",
                            ReleaseDate = DateTime.UtcNow.AddDays(-2),
                            IsLatest = true,
                            Changelog = "Added two tournament maps.",
                        },
                    ],
                    Tags = ["demo", "maps"],
                },
            ],
        };

        var project = new PublisherStudioProject
        {
            ProjectName = "Demo Publisher",
            Catalog = catalog,
            LastModified = DateTime.UtcNow,
        };
        project.Catalogs.Add(new NamedCatalog
        {
            Id = "demo-catalog",
            Name = "Demo Catalog",
            Description = "Sample catalog for the interactive guide.",
            Catalog = catalog,
            FileName = "catalog-demo.json",
        });

        return project;
    }

    private static GameProfile CreateDemoRecoveryProfile() => new()
    {
        Id = "demo-recovery-profile",
        Name = "Zero Hour 1.04 (Recovery)",
        GameClient = new GameClient
        {
            Id = "1.0.local.gameclient.generalszh-recovery",
            Name = "Zero Hour Recovery Client",
            GameType = GameType.ZeroHour,
            PublisherType = PublisherTypeConstants.TheSuperHackers,
            Capabilities = GameClientCapabilities.AllRecoveryFeatures,
        },
    };

    /// <summary>
    /// Seeds a demo view model on the caller's synchronization context so bound collections
    /// are populated on the UI thread instead of a thread-pool thread. Failures are logged
    /// without surfacing to the caller.
    /// </summary>
    /// <param name="seed">The seeding operation to run.</param>
    /// <param name="demoName">The demo name used in the failure log.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous seeding operation.</returns>
    private static async Task SeedDemoAsync(Func<Task> seed, string demoName)
    {
        try
        {
            await seed();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to seed demo {demoName}: {ex}");
        }
    }

    private static string BuildSampleMainMenuDocument()
    {
        var buttonArt = ThreePieceDrawData("MenuButtonLeft", "MenuButtonMiddle", "MenuButtonRight");
        var backdropArt = SingleDrawData("MenuBackdrop");
        var builder = new StringBuilder();
        builder.AppendLine("FILE_VERSION = 2;");
        builder.AppendLine("STARTLAYOUTBLOCK");
        builder.AppendLine("  LAYOUTINIT = MainMenuInit;");
        builder.AppendLine("ENDLAYOUTBLOCK");
        builder.AppendLine("WINDOW");
        builder.AppendLine("  WINDOWTYPE = USER;");
        builder.AppendLine("  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 800 600, CREATIONRESOLUTION: 800 600;");
        builder.AppendLine("  NAME = \"MainMenu.wnd:MainMenu\";");
        builder.AppendLine("  STATUS = ENABLED;");
        builder.AppendLine($"  ENABLEDDRAWDATA = {backdropArt};");
        builder.AppendLine("  CHILD");
        AppendWindow(builder, WndConstants.ControlTypes.StaticText, "200 40", "600 90", "MainMenu.wnd:TitleText", "GAMETEXT:Menu_Title");
        AppendWindow(builder, WndConstants.ControlTypes.PushButton, "300 150", "500 190", "MainMenu.wnd:ButtonSinglePlayer", "GAMETEXT:GUI_SinglePlayer", buttonArt);
        AppendWindow(builder, WndConstants.ControlTypes.PushButton, "300 200", "500 240", "MainMenu.wnd:ButtonMultiplayer", "GAMETEXT:GUI_Multiplayer", buttonArt);
        AppendWindow(builder, WndConstants.ControlTypes.PushButton, "300 250", "500 290", "MainMenu.wnd:ButtonOptions", "GAMETEXT:GUI_Options", buttonArt);
        AppendWindow(builder, WndConstants.ControlTypes.PushButton, "300 300", "500 340", "MainMenu.wnd:ButtonExit", "GAMETEXT:GUI_Exit", buttonArt);
        AppendWindow(builder, WndConstants.ControlTypes.StaticText, "600 570", "790 595", "MainMenu.wnd:VersionText", "Version 1.0 (Demo)");
        builder.AppendLine("  ENDALLCHILDREN");
        builder.AppendLine("END");
        builder.AppendLine("WINDOW");
        builder.AppendLine("  WINDOWTYPE = USER;");
        builder.AppendLine("  SCREENRECT = UPPERLEFT: 150 120, BOTTOMRIGHT: 650 500, CREATIONRESOLUTION: 800 600;");
        builder.AppendLine("  NAME = \"MainMenu.wnd:OptionsPanel\";");
        builder.AppendLine("  STATUS = HIDDEN;");
        builder.AppendLine("  CHILD");
        AppendWindow(builder, WndConstants.ControlTypes.CheckBox, "180 160", "420 190", "MainMenu.wnd:FullscreenCheck", "GAMETEXT:GUI_Fullscreen");
        AppendWindow(builder, WndConstants.ControlTypes.RadioButton, "180 200", "420 230", "MainMenu.wnd:EasyRadio", "Easy");
        AppendWindow(builder, WndConstants.ControlTypes.RadioButton, "180 235", "420 265", "MainMenu.wnd:NormalRadio", "Normal");
        AppendWindow(builder, WndConstants.ControlTypes.PushButton, "180 440", "330 475", "MainMenu.wnd:BackButton", "GAMETEXT:GUI_Back", buttonArt);
        builder.AppendLine("  ENDALLCHILDREN");
        builder.AppendLine("END");
        return builder.ToString();
    }

    private static void AppendWindow(StringBuilder builder, string windowType, string upperLeft, string bottomRight, string name, string text, string? drawData = null)
    {
        builder.AppendLine("  WINDOW");
        builder.AppendLine($"    WINDOWTYPE = {windowType};");
        builder.AppendLine($"    SCREENRECT = UPPERLEFT: {upperLeft}, BOTTOMRIGHT: {bottomRight}, CREATIONRESOLUTION: 800 600;");
        builder.AppendLine($"    NAME = \"{name}\";");
        builder.AppendLine($"    TEXT = \"{text}\";");
        if (!string.IsNullOrEmpty(drawData))
        {
            builder.AppendLine($"    ENABLEDDRAWDATA = {drawData};");
        }

        builder.AppendLine("  END");
    }

    private static string ThreePieceDrawData(string left, string middle, string right)
    {
        return DrawDataWith((left, 0), (middle, 5), (right, 6));
    }

    private static string SingleDrawData(string image)
    {
        return DrawDataWith((image, 0));
    }

    private static string DrawDataWith(params (string Name, int Index)[] images)
    {
        var entries = new List<WndDrawDataEntry>();
        for (var i = 0; i < WndConstants.DrawData.EntryCount; i++)
        {
            entries.Add(WndDrawDataEntry.Empty);
        }

        foreach (var (name, index) in images)
        {
            entries[index] = new WndDrawDataEntry(name, new WndRgbaColor(10, 20, 30, 255), new WndRgbaColor(40, 50, 60, 255));
        }

        return new WndDrawDataSet(entries).ToString();
    }
}
