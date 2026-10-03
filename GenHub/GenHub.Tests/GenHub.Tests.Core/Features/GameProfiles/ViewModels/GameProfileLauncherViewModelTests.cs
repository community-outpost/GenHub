using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Events;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Publishers;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.GameProfiles.ViewModels.Wizard;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Specialized;
using System.Reflection;
using System.Resources;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Contains unit tests for <see cref="GameProfileLauncherViewModel"/>.
/// </summary>
public class GameProfileLauncherViewModelTests
{
    private delegate bool TryGetLocalizedString(string key, out string? value, object?[] arguments);

    /// <summary>
    /// Verifies that the constructor initializes properties correctly.
    /// </summary>
    [Fact]
    public void Constructor_WithValidParameters_InitializesCorrectly()
    {
        var installationService = new Mock<IGameInstallationService>();
        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<IProfileLauncherFacade>().Object,
            new GameProfileSettingsViewModel(
                new Mock<IGameProfileManager>().Object,
                new Mock<IGameSettingsService>().Object,
                new Mock<IConfigurationProviderService>().Object,
                new Mock<IProfileContentLoader>().Object,
                CreateProfileResourceService(),
                new Mock<INotificationService>().Object,
                null,
                new Mock<IContentStorageService>().Object,
                null, // ILocalContentService
                null, // IGenLauncherNormalizationService
                null, // IDialogService
                NullLogger<GameProfileSettingsViewModel>.Instance,
                NullLogger<GameSettingsViewModel>.Instance),
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IGameProcessManager>().Object,
            new Mock<IShortcutService>().Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            CreateLocalizationService());

        Assert.NotNull(vm);
        Assert.Empty(vm.Profiles);
        Assert.False(vm.IsLaunching);
        Assert.False(vm.IsEditMode);
        Assert.Equal(string.Empty, vm.StatusMessage);
    }

    /// <summary>
    /// Verifies that InitializeAsync loads profiles successfully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_LoadsProfiles_SuccessfullyAsync()
    {
        var installationService = new Mock<IGameInstallationService>();
        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<IProfileLauncherFacade>().Object,
            new GameProfileSettingsViewModel(
                new Mock<IGameProfileManager>().Object,
                new Mock<IGameSettingsService>().Object,
                new Mock<IConfigurationProviderService>().Object,
                new Mock<IProfileContentLoader>().Object,
                CreateProfileResourceService(),
                new Mock<INotificationService>().Object,
                null,
                new Mock<IContentStorageService>().Object,
                null, // ILocalContentService
                null, // IGenLauncherNormalizationService
                null, // IDialogService
                NullLogger<GameProfileSettingsViewModel>.Instance,
                NullLogger<GameSettingsViewModel>.Instance),
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IGameProcessManager>().Object,
            new Mock<IShortcutService>().Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            CreateLocalizationService());

        await vm.InitializeAsync();

        Assert.Empty(vm.Profiles); // No profiles returned by mock
    }

    /// <summary>
    /// Verifies that an out-of-range saved sort mode falls back to last-played ordering.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_OutOfRangeSortMode_FallsBackToLastPlayedAsync()
    {
        var settingsService = new Mock<IUserSettingsService>();
        settingsService.Setup(x => x.Get()).Returns(new UserSettings { ProfileSortMode = (ProfileSortMode)99 });
        var profileManager = new Mock<IGameProfileManager>();
        profileManager
            .Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));
        var vm = new GameProfileLauncherViewModel(
            new Mock<IGameInstallationService>().Object,
            profileManager.Object,
            new Mock<IProfileLauncherFacade>().Object,
            null!,
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IGameProcessManager>().Object,
            new Mock<IShortcutService>().Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            CreateLocalizationService(),
            userSettingsService: settingsService.Object);

        await vm.InitializeAsync();

        Assert.Equal(ProfileSortMode.LastPlayed, vm.SelectedSortMode);
        Assert.Equal(ProfileSortMode.LastPlayed, vm.SelectedSortModeItem?.Mode);
    }

    /// <summary>
    /// Verifies that archive-name game inference detects Generals mods like directory names do.
    /// </summary>
    /// <param name="fileName">The archive file name to classify.</param>
    /// <param name="expected">The expected inferred game type.</param>
    [Theory]
    [InlineData("generals_mod.zip", GameType.Generals)]
    [InlineData("GeneralsZeroHour.big", GameType.ZeroHour)]
    [InlineData("shockwave_mod.zip", GameType.Unknown)]
    public void InferGameTypeFromArchiveName_ClassifiesExpectedGame(string fileName, GameType expected)
    {
        var method = typeof(GameProfileLauncherViewModel).GetMethod(
            "InferGameTypeFromArchiveName",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var actual = (GameType)method.Invoke(null, [fileName, GameType.Unknown])!;

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies that ScanForGamesCommand shows success on successful scan.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanForGamesCommand_WithSuccessfulScan_ShowsSuccessAsync()
    {
        var installationService = new Mock<IGameInstallationService>();
        var installations = new List<GameInstallation>
        {
            new("C:\\Steam\\Games", GameInstallationType.Steam, new Mock<ILogger<GameInstallation>>().Object),
            new("C:\\EA\\Games", GameInstallationType.EaApp, new Mock<ILogger<GameInstallation>>().Object),
        };

        installationService.Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess(installations));

        var shortcutService = new Mock<IShortcutService>();
        var notificationService = new Mock<INotificationService>();
        var publisherOrchestrator = new Mock<IPublisherProfileOrchestrator>();
        var profileManager = new Mock<IGameProfileManager>();
        var editorFacade = new Mock<IProfileEditorFacade>();

        var setupWizardService = new Mock<ISetupWizardService>();
        setupWizardService.Setup(x => x.RunSetupWizardAsync(It.IsAny<IEnumerable<GameInstallation>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SetupWizardResult { Confirmed = true });

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            profileManager.Object,
            null!,
            null!,
            editorFacade.Object,
            null!,
            null!,
            shortcutService.Object,
            publisherOrchestrator.Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            notificationService.Object,
            setupWizardService.Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            CreateLocalizationService());

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        // Updated to match actual message format that includes manifest generation and profile creation
        Assert.Equal("Scan complete. Found 2 installations, created 0 profiles", vm.StatusMessage);
    }

    /// <summary>
    /// Verifies that ScanForGamesCommand shows failure on failed scan.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanForGamesCommand_WithFailedScan_ShowsFailureAsync()
    {
        var installationService = new Mock<IGameInstallationService>();
        const string expectedError = "Detection service unavailable";

        installationService.Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateFailure(expectedError));

        var shortcutService = new Mock<IShortcutService>();

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            shortcutService.Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            CreateLocalizationService());

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        Assert.Equal($"Scan failed: {expectedError}", vm.StatusMessage);
    }

    /// <summary>
    /// Verifies that ScanForGamesCommand handles exceptions gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanForGamesCommand_WithException_HandlesGracefullyAsync()
    {
        var installationService = new Mock<IGameInstallationService>();
        installationService.Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        var shortcutService = new Mock<IShortcutService>();

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<IProfileLauncherFacade>().Object,
            null!, // SettingsVM
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IGameProcessManager>().Object,
            shortcutService.Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            CreateLocalizationService());

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        // Should handle exception gracefully by setting an error message
        Assert.Contains("Error during scan", vm.StatusMessage);
    }

    /// <summary>
    /// Verifies that ScanForGamesCommand does nothing when service is not available.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanForGamesCommand_WithoutService_ShowsErrorAsync()
    {
        var installationService = new Mock<IGameInstallationService>();
        var shortcutService = new Mock<IShortcutService>();

        // Setup to return failure
        installationService.Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateFailure("Service unavailable"));

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            shortcutService.Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            CreateLocalizationService());

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        // Service returns failure, so we should get a scan failed message
        Assert.Contains("Scan failed", vm.StatusMessage);
    }

    /// <summary>
    /// Verifies that ScanForGamesCommand handles exceptions thrown by the setup wizard gracefully and resets scanning state.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanForGamesCommand_WhenSetupWizardThrows_ResetsScanningAndSetsErrorMessageAsync()
    {
        var installationService = new Mock<IGameInstallationService>();
        var installations = new List<GameInstallation>
        {
            new("C:\\Steam\\Games", GameInstallationType.Steam, new Mock<ILogger<GameInstallation>>().Object),
        };

        installationService.Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess(installations));

        var setupWizardService = new Mock<ISetupWizardService>();
        const string expectedError = "Unrecognized cursor type 'Default'.";
        setupWizardService.Setup(x => x.RunSetupWizardAsync(It.IsAny<IEnumerable<GameInstallation>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException(expectedError));

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<IProfileLauncherFacade>().Object,
            null!,
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IGameProcessManager>().Object,
            new Mock<IShortcutService>().Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            new Mock<INotificationService>().Object,
            setupWizardService.Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            CreateLocalizationService());

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        Assert.False(vm.IsScanning);
        Assert.Equal("Error during scan", vm.StatusMessage);
        Assert.Equal(expectedError, vm.ErrorMessage);
    }

    /// <summary>
    /// Verifies that CopyProfile generates a unique name for the copied profile.
    /// </summary>
    [Fact]
    public void GenerateUniqueProfileName_CreatesUniqueName()
    {
        // Arrange
        var vm = CreateViewModelWithMockDependencies();

        // Add some existing profiles to simulate name conflicts
        var existingProfile1 = new GameProfileItemViewModel("id1", new Mock<IGameProfile>().Object, "icon.png", "cover.jpg")
        {
            Name = $"Test Profile {ProfileConstants.CopyNameSuffix}",
        };
        var existingProfile2 = new GameProfileItemViewModel("id2", new Mock<IGameProfile>().Object, "icon.png", "cover.jpg")
        {
            Name = $"Test Profile {string.Format(ProfileConstants.CopyNameNumberedFormat, 2)}",
        };

        vm.Profiles.Add(existingProfile1);
        vm.Profiles.Add(existingProfile2);

        // Act
        var uniqueName = vm.GenerateUniqueProfileName("Test Profile");

        // Assert
        Assert.Equal($"Test Profile {string.Format(ProfileConstants.CopyNameNumberedFormat, 3)}", uniqueName);
    }

    /// <summary>
    /// Verifies that ScanForGamesCommand creates zero profiles when the wizard is skipped/cancelled.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanForGamesCommand_WhenWizardCancelled_CreatesZeroProfilesAsync()
    {
        var installationService = new Mock<IGameInstallationService>();
        var installation = new GameInstallation(Path.Combine("C:", "Steam", "Games"), GameInstallationType.Steam, new Mock<ILogger<GameInstallation>>().Object);
        installation.PopulateGameClients([
            new GameClient
            {
                Id = "cp-client",
                Name = "Community Patch",
                PublisherType = CommunityOutpostConstants.PublisherType,
                GameType = GameType.ZeroHour,
            },
        ]);
        var installations = new List<GameInstallation> { installation };

        installationService.Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess(installations));

        var shortcutService = new Mock<IShortcutService>();
        var notificationService = new Mock<INotificationService>();
        var publisherOrchestrator = new Mock<IPublisherProfileOrchestrator>();
        var profileManager = new Mock<IGameProfileManager>();
        var editorFacade = new Mock<IProfileEditorFacade>();

        var setupWizardService = new Mock<ISetupWizardService>();
        setupWizardService.Setup(x => x.RunSetupWizardAsync(It.IsAny<IEnumerable<GameInstallation>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SetupWizardResult
            {
                Confirmed = false,
                CommunityPatchAction = GameClientConstants.WizardActionTypes.Install,
            });

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            profileManager.Object,
            null!,
            null!,
            editorFacade.Object,
            null!,
            null!,
            shortcutService.Object,
            publisherOrchestrator.Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            notificationService.Object,
            setupWizardService.Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            CreateLocalizationService());

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        Assert.Equal("Scan complete. Found 1 installations, created 0 profiles", vm.StatusMessage);
        publisherOrchestrator.Verify(
            x => x.CreateProfilesForPublisherClientAsync(It.IsAny<GameInstallation>(), It.IsAny<GameClient>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
        profileManager.Verify(
            x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that when the wizard returns CreateProfile for Generals Online, the publisher orchestrator is invoked with skipAcquisition true.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ScanForGamesCommand_WhenWizardReturnsCreateProfileForGeneralsOnline_CallsOrchestratorWithSkipAcquisitionAsync()
    {
        var installationService = new Mock<IGameInstallationService>();
        var installation = new GameInstallation(Path.Combine("C:", "Steam", "Games"), GameInstallationType.Steam, new Mock<ILogger<GameInstallation>>().Object);
        installation.PopulateGameClients([
            new GameClient
            {
                Id = "base-zh",
                Name = "Zero Hour",
                GameType = GameType.ZeroHour,
                InstallationId = installation.Id,
            },
        ]);
        var installations = new List<GameInstallation> { installation };

        installationService.Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess(installations));

        var shortcutService = new Mock<IShortcutService>();
        var notificationService = new Mock<INotificationService>();
        var publisherOrchestrator = new Mock<IPublisherProfileOrchestrator>();
        publisherOrchestrator
            .Setup(x => x.CreateProfilesForPublisherClientAsync(
                It.IsAny<GameInstallation>(),
                It.IsAny<GameClient>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<int>.CreateSuccess(1));

        var profileManager = new Mock<IGameProfileManager>();
        var editorFacade = new Mock<IProfileEditorFacade>();

        var setupWizardService = new Mock<ISetupWizardService>();
        setupWizardService.Setup(x => x.RunSetupWizardAsync(It.IsAny<IEnumerable<GameInstallation>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SetupWizardResult
            {
                Confirmed = true,
                GeneralsOnlineAction = GameClientConstants.WizardActionTypes.CreateProfile,
            });

        var vm = new GameProfileLauncherViewModel(
            installationService.Object,
            profileManager.Object,
            null!,
            null!,
            editorFacade.Object,
            null!,
            null!,
            shortcutService.Object,
            publisherOrchestrator.Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            notificationService.Object,
            setupWizardService.Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            CreateLocalizationService());

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        Assert.Equal("Scan complete. Found 1 installations, created 1 profiles", vm.StatusMessage);
        publisherOrchestrator.Verify(
            x => x.CreateProfilesForPublisherClientAsync(
                It.Is<GameInstallation>(inst => inst == installation),
                It.Is<GameClient>(c => c.PublisherType == PublisherTypeConstants.GeneralsOnline),
                false,
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that SetupWizardItemViewModel strips leading 'v' or 'V' prefix.
    /// </summary>
    /// <param name="rawVersion">The input version string.</param>
    /// <param name="expectedVersion">The expected sanitized version string.</param>
    [Theory]
    [InlineData("v081326_QFE3", "081326_QFE3")]
    [InlineData("vweekly-2026-08-14", "weekly-2026-08-14")]
    [InlineData("v02-08-2026", "02-08-2026")]
    [InlineData("V1.04", "1.04")]
    [InlineData("1.08", "1.08")]
    [InlineData("  v1.04  ", "1.04")]
    [InlineData("  1.08  ", "1.08")]
    public void SetupWizardItemViewModel_Version_StripsLeadingVPrefix(string rawVersion, string expectedVersion)
    {
        var item = new SetupWizardItemViewModel
        {
            Version = rawVersion,
        };

        Assert.Equal(expectedVersion, item.Version);
    }

    /// <summary>
    /// Verifies that receiving ProfileLaunchedMessage updates the matching profile's IsProcessRunning and ProcessId properties.
    /// </summary>
    [AvaloniaFact]
    public void Receive_ProfileLaunchedMessage_UpdatesIsProcessRunningAndProcessId()
    {
        var vm = CreateViewModelWithMockDependencies();
        var profile = new GameProfile
        {
            Id = "test-profile-123",
            Name = "Test Profile",
        };
        var item = new GameProfileItemViewModel("test-profile-123", profile, string.Empty, string.Empty);
        vm.Profiles.Add(item);

        vm.Receive(new ProfileLaunchedMessage("test-profile-123", 45678));

        Assert.True(item.IsProcessRunning);
        Assert.Equal(45678, item.ProcessId);
    }

    /// <summary>
    /// Verifies that receiving ProfileStoppedMessage clears IsProcessRunning and resets ProcessId to 0.
    /// </summary>
    [AvaloniaFact]
    public void Receive_ProfileStoppedMessage_ClearsIsProcessRunningAndProcessId()
    {
        var vm = CreateViewModelWithMockDependencies();
        var profile = new GameProfile
        {
            Id = "test-profile-123",
            Name = "Test Profile",
        };
        var item = new GameProfileItemViewModel("test-profile-123", profile, string.Empty, string.Empty)
        {
            IsProcessRunning = true,
            ProcessId = 45678,
        };
        vm.Profiles.Add(item);

        vm.Receive(new ProfileStoppedMessage("test-profile-123", 45678));

        Assert.False(item.IsProcessRunning);
        Assert.Equal(0, item.ProcessId);
    }

    /// <summary>
    /// Verifies that a successful launch carrying receipt drift shows an informational
    /// notice naming the drift, while the success presentation stays unchanged.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <param name="warning">The diagnostic or localized-warning key.</param>
    /// <param name="expected">The expected notification body text.</param>
    [Theory]
    [InlineData("Executable size changed from 1 to 2 bytes: generalszh", "Executable size changed from 1 to 2 bytes")]
    [InlineData(LaunchReceiptConstants.VariantsAddedWarningKey, "The game client now supports platform variants.")]
    [InlineData(LaunchReceiptConstants.VariantsRemovedWarningKey, "The game client no longer declares platform variants.")]
    [InlineData("GameProfiles.Notification.LaunchChanged.Title", "Launch Configuration Changed")]
    [InlineData(LaunchReceiptConstants.RevalidationWarningKey, "The previous launch receipt could not be checked.")]
    public async Task LaunchProfileCommand_WithReceiptDrift_ShowsInformationalNoticeAsync(string warning, string expected)
    {
        var notificationService = new Mock<INotificationService>();
        var launcherFacade = new Mock<IProfileLauncherFacade>();
        var launchInfo = new GameLaunchInfo
        {
            LaunchId = "launch-1",
            ProfileId = "profile-1",
            WorkspaceId = "profile-1",
            ProcessInfo = new GameProcessInfo { ProcessId = 123 },
            ReceiptDriftWarnings = [warning],
        };
        launcherFacade.Setup(x => x.LaunchProfileAsync("profile-1", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo));

        var vm = CreateLauncherViewModel(launcherFacade, notificationService);
        var profileItem = CreateProfileItem("profile-1", "Test Profile");

        await vm.LaunchProfileCommand.ExecuteAsync(profileItem);

        Assert.Equal("Test Profile launched successfully (Process ID: 123)", vm.StatusMessage);
        notificationService.Verify(
            x => x.ShowSuccess("Game Launched", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        notificationService.Verify(
            x => x.ShowInfo(
                "Launch Configuration Changed",
                It.Is<string>(m =>
                    m.Contains("This launch differs from the last recorded launch") &&
                    m.Contains(expected)),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Once);
        notificationService.Verify(
            x => x.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>Long drift reports show five details and an accurate remainder.</summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task LaunchProfileCommand_WithManyReceiptChanges_CapsNoticeAsync()
    {
        var notifications = new Mock<INotificationService>();
        var facade = new Mock<IProfileLauncherFacade>();
        var launchInfo = new GameLaunchInfo
        {
            LaunchId = "launch-1",
            ProfileId = "profile-1",
            WorkspaceId = "profile-1",
            ProcessInfo = new GameProcessInfo { ProcessId = 123 },
            ReceiptDriftWarnings = Enumerable.Range(1, 8).Select(i => $"Change {i}").ToList(),
        };
        facade.Setup(x => x.LaunchProfileAsync("profile-1", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo));
        var vm = CreateLauncherViewModel(facade, notifications);

        await vm.LaunchProfileCommand.ExecuteAsync(CreateProfileItem("profile-1", "Test Profile"));

        notifications.Verify(
            x => x.ShowInfo(
                "Launch Configuration Changed",
                It.Is<string>(m => Enumerable.Range(1, 5).All(i => m.Contains($"Change {i}"))
                    && !m.Contains("Change 6") && !m.Contains("Change 7") && !m.Contains("Change 8")
                    && m.Contains("...and 3 more; see the logs for full detail.")),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a successful launch without receipt drift shows no notice.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task LaunchProfileCommand_WithoutReceiptDrift_ShowsNoNoticeAsync()
    {
        var notificationService = new Mock<INotificationService>();
        var launcherFacade = new Mock<IProfileLauncherFacade>();
        var launchInfo = new GameLaunchInfo
        {
            LaunchId = "launch-1",
            ProfileId = "profile-1",
            WorkspaceId = "profile-1",
            ProcessInfo = new GameProcessInfo { ProcessId = 123 },
        };
        launcherFacade.Setup(x => x.LaunchProfileAsync("profile-1", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo));

        var vm = CreateLauncherViewModel(launcherFacade, notificationService);
        var profileItem = CreateProfileItem("profile-1", "Test Profile");

        await vm.LaunchProfileCommand.ExecuteAsync(profileItem);

        Assert.Equal("Test Profile launched successfully (Process ID: 123)", vm.StatusMessage);
        notificationService.Verify(
            x => x.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that a successful launch emits ProfileLaunched with game client and timing telemetry.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task LaunchProfileCommand_OnSuccess_TracksProfileLaunchedWithGameClientAndTimingAsync()
    {
        var notificationService = new Mock<INotificationService>();
        var launcherFacade = new Mock<IProfileLauncherFacade>();
        var telemetryService = new Mock<ITelemetryService>();
        var launchInfo = new GameLaunchInfo
        {
            LaunchId = "launch-1",
            ProfileId = "profile-1",
            WorkspaceId = "profile-1",
            ProcessInfo = new GameProcessInfo { ProcessId = 123 },
        };
        launcherFacade.Setup(x => x.LaunchProfileAsync("profile-1", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo));

        var vm = CreateLauncherViewModel(launcherFacade, notificationService, telemetryService);
        var client = new GameClient { Id = "thesuperhackers.zh", Name = "TheSuperHackers Zero Hour", Version = "1.06", GameType = GameType.ZeroHour };
        var profileItem = CreateProfileItemWithClient("profile-1", "Test Profile", client);

        await vm.LaunchProfileCommand.ExecuteAsync(profileItem);

        telemetryService.Verify(
            x => x.TrackEvent(
                TelemetryConstants.Events.ProfileLaunched,
                It.Is<IReadOnlyDictionary<string, object?>>(props =>
                    (string?)props[TelemetryConstants.Properties.ProfileId] == "profile-1" &&
                    (string?)props[TelemetryConstants.Properties.GameClientId] == "thesuperhackers.zh" &&
                    (string?)props[TelemetryConstants.Properties.GameClientName] == "TheSuperHackers Zero Hour" &&
                    props.ContainsKey(TelemetryConstants.Properties.TimeToLaunchMs)),
                TelemetryLevel.AnonymousMetrics),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a failed launch emits ProfileLaunchFailed with game client and error telemetry.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task LaunchProfileCommand_OnFailure_TracksProfileLaunchFailedWithGameClientAndErrorsAsync()
    {
        var notificationService = new Mock<INotificationService>();
        var launcherFacade = new Mock<IProfileLauncherFacade>();
        var telemetryService = new Mock<ITelemetryService>();

        launcherFacade.Setup(x => x.LaunchProfileAsync("profile-1", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateFailure("Process start failed"));

        var vm = CreateLauncherViewModel(launcherFacade, notificationService, telemetryService);
        var client = new GameClient { Id = "thesuperhackers.zh", Name = "TheSuperHackers Zero Hour", Version = "1.06", GameType = GameType.ZeroHour };
        var profileItem = CreateProfileItemWithClient("profile-1", "Test Profile", client);

        await vm.LaunchProfileCommand.ExecuteAsync(profileItem);

        telemetryService.Verify(
            x => x.TrackEvent(
                TelemetryConstants.Events.ProfileLaunchFailed,
                It.Is<IReadOnlyDictionary<string, object?>>(props =>
                    (string?)props[TelemetryConstants.Properties.ProfileId] == "profile-1" &&
                    (string?)props[TelemetryConstants.Properties.GameClientId] == "thesuperhackers.zh" &&
                    (string?)props[TelemetryConstants.Properties.ErrorCategory] == TelemetryConstants.ErrorCategories.LaunchFailed &&
                    props.ContainsKey(TelemetryConstants.Properties.TimeToLaunchMs)),
                TelemetryLevel.AnonymousMetrics),
            Times.Once);
    }

    /// <summary>
    /// Verifies that receiving ProfileStoppedMessage with mismatched PID is ignored as stale.
    /// </summary>
    [AvaloniaFact]
    public void Receive_ProfileStoppedMessage_WithMismatchedProcessId_IgnoresStaleStop()
    {
        var vm = CreateViewModelWithMockDependencies();
        var profile = new GameProfile
        {
            Id = "test-profile-123",
            Name = "Test Profile",
        };
        var item = new GameProfileItemViewModel("test-profile-123", profile, string.Empty, string.Empty)
        {
            IsProcessRunning = true,
            ProcessId = 45678,
        };
        vm.Profiles.Add(item);

        // A message arrives with a different, stale PID (e.g. from an earlier instance that exited late)
        vm.Receive(new ProfileStoppedMessage("test-profile-123", 11111));

        Assert.True(item.IsProcessRunning);
        Assert.Equal(45678, item.ProcessId);
    }

    /// <summary>
    /// Verifies that receiving ProfileStoppedMessage with reused PID is ignored as stale.
    /// </summary>
    [AvaloniaFact]
    public void Receive_ProfileStoppedMessage_WithMismatchedIdentity_IgnoresStaleStop()
    {
        var vm = CreateViewModelWithMockDependencies();
        var profile = new GameProfile
        {
            Id = "test-profile-123",
            Name = "Test Profile",
        };
        var item = new GameProfileItemViewModel("test-profile-123", profile, string.Empty, string.Empty)
        {
            IsProcessRunning = true,
            ProcessId = 45678,
            ProcessInstanceId = Guid.NewGuid(),
        };
        vm.Profiles.Add(item);

        // The PID was reused, but this message carries the stale identity of an earlier instance.
        vm.Receive(new ProfileStoppedMessage("test-profile-123", 45678) { ProcessInstanceId = Guid.NewGuid() });

        Assert.True(item.IsProcessRunning);
        Assert.Equal(45678, item.ProcessId);
    }

    /// <summary>
    /// Verifies that when LaunchProfileAsync launches a new profile ID (e.g. from reconciler clone),
    /// the launcher ViewModel switches SelectedProfile to the new profile item.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [AvaloniaFact]
    public async Task LaunchProfileCommand_WhenNewProfileLaunched_SwitchesSelectionToNewProfileAsync()
    {
        var launcherFacade = new Mock<IProfileLauncherFacade>();
        var launchInfo = new GameLaunchInfo
        {
            LaunchId = "launch-1",
            ProfileId = "new-profile-id",
            WorkspaceId = "ws-1",
            ProcessInfo = new GameProcessInfo { ProcessId = 1234 },
        };
        launcherFacade.Setup(x => x.LaunchProfileAsync("orig-profile-id", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo));

        var vm = CreateViewModelWithLauncherFacade(launcherFacade.Object);

        var origProfile = new GameProfile { Id = "orig-profile-id", Name = "Original Profile" };
        var newProfile = new GameProfile { Id = "new-profile-id", Name = "New Profile" };

        var origItem = new GameProfileItemViewModel("orig-profile-id", origProfile, string.Empty, string.Empty);
        var newItem = new GameProfileItemViewModel("new-profile-id", newProfile, string.Empty, string.Empty);

        vm.Profiles.Add(origItem);
        vm.Profiles.Add(newItem);
        vm.SelectedProfile = origItem;

        await vm.LaunchProfileCommand.ExecuteAsync(origItem);

        Assert.Same(newItem, vm.SelectedProfile);
        Assert.True(newItem.IsProcessRunning);
        Assert.Equal(1234, newItem.ProcessId);
    }

    /// <summary>
    /// Verifies that when LaunchProfileAsync launches the original profile,
    /// SelectedProfile remains on that profile.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [AvaloniaFact]
    public async Task LaunchProfileCommand_WhenOriginalProfileLaunched_MaintainsSelectionAsync()
    {
        var launcherFacade = new Mock<IProfileLauncherFacade>();
        var launchInfo = new GameLaunchInfo
        {
            LaunchId = "launch-1",
            ProfileId = "orig-profile-id",
            WorkspaceId = "ws-1",
            ProcessInfo = new GameProcessInfo { ProcessId = 5678 },
        };
        launcherFacade.Setup(x => x.LaunchProfileAsync("orig-profile-id", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(launchInfo));

        var vm = CreateViewModelWithLauncherFacade(launcherFacade.Object);

        var origProfile = new GameProfile { Id = "orig-profile-id", Name = "Original Profile" };
        var origItem = new GameProfileItemViewModel("orig-profile-id", origProfile, string.Empty, string.Empty);

        vm.Profiles.Add(origItem);
        vm.SelectedProfile = origItem;

        await vm.LaunchProfileCommand.ExecuteAsync(origItem);

        Assert.Same(origItem, vm.SelectedProfile);
        Assert.True(origItem.IsProcessRunning);
        Assert.Equal(5678, origItem.ProcessId);
    }

    /// <summary>
    /// Verifies that TryExtractRemoteImportHost recognizes both import and view prefixes with url query.
    /// </summary>
    /// <param name="uriOrPath">The sharing URI or file path to evaluate.</param>
    /// <param name="expectedHost">The expected remote host extracted, or null.</param>
    [Theory]
    [InlineData("genhub://profile/import?url=https://example.com/profile.ghprofile", "example.com")]
    [InlineData("genhub://profile/view?url=https://example.com/profile.ghprofile", "example.com")]
    [InlineData("GENHUB://PROFILE/VIEW?url=https://outpost.org/mod.ghprofile&foo=bar", "outpost.org")]
    [InlineData("genhub://profile/import?data=eyJhbGciOi...", null)]
    [InlineData("genhub://profile/view?data=eyJhbGciOi...", null)]
    [InlineData("https://example.com/profile.ghprofile", null)]
    [InlineData("/path/to/profile.ghprofile", null)]
    public void TryExtractRemoteImportHost_RecognizesBothImportAndViewUrls(string uriOrPath, string? expectedHost)
    {
        var result = GameProfileLauncherViewModel.TryExtractRemoteImportHost(uriOrPath);
        Assert.Equal(expectedHost, result);
    }

    /// <summary>
    /// A clean exit is the user quitting: the running state clears and nothing is
    /// reported as an error.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [AvaloniaFact]
    public async Task ProcessExitedCleanly_DoesNotReportAFailureAsync()
    {
        var gameProcessManager = new Mock<IGameProcessManager>();
        var notificationService = new Mock<INotificationService>();
        var vm = CreateViewModelWithMockDependencies(gameProcessManager, notificationService);

        // InitializeAsync is where the view model subscribes to ProcessExited.
        await vm.InitializeAsync();

        var profile = CreateProfileItem("Quitting Profile");
        profile.ProcessId = 4243;
        profile.IsProcessRunning = true;
        vm.Profiles.Add(profile);

        gameProcessManager.Raise(m => m.ProcessExited += null, new GameProcessExitedEventArgs
        {
            ProcessId = 4243,
            ExitCode = 0,
        });

        Assert.False(profile.IsProcessRunning);
        Assert.Equal(0, profile.ProcessId);
        Assert.Equal(string.Empty, vm.ErrorMessage);
        notificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// A stop the user asked for kills the process with a non-zero exit code; that must
    /// not raise the "exited unexpectedly" alarm — the stop path's own status stands.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [AvaloniaFact]
    public async Task ProcessExitedFromARequestedStop_DoesNotRaiseTheFailureAlarmAsync()
    {
        var gameProcessManager = new Mock<IGameProcessManager>();
        var notificationService = new Mock<INotificationService>();
        var vm = CreateViewModelWithMockDependencies(gameProcessManager, notificationService);

        // InitializeAsync is where the view model subscribes to ProcessExited.
        await vm.InitializeAsync();

        var profile = CreateProfileItem("Stopped Profile");
        profile.ProcessId = 4244;
        profile.IsProcessRunning = true;
        vm.Profiles.Add(profile);

        // The status a completed stop leaves behind; the exit event must not replace it.
        vm.StatusMessage = "Stopped Profile stopped successfully";

        gameProcessManager.Raise(m => m.ProcessExited += null, new GameProcessExitedEventArgs
        {
            ProcessId = 4244,
            ExitCode = 137,
            TerminationRequested = true,
        });

        Assert.False(profile.IsProcessRunning);
        Assert.Equal(0, profile.ProcessId);
        Assert.Equal("Stopped Profile stopped successfully", vm.StatusMessage);
        Assert.Equal(string.Empty, vm.ErrorMessage);
        notificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// A process that dies after the launch was announced as running must not vanish
    /// silently: the late failure surfaces as a notification and in the status line, naming the archive when known.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <param name="includeProfile">Whether the profile row is still present when the process exits.</param>
    /// <param name="isTool">Whether the profile launches a tool instead of a game.</param>
    /// <param name="announced">Whether successful launch was announced before the exit.</param>
    [AvaloniaTheory]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    public async Task ProcessExitedWithFailure_SurfacesTheFailureToTheUserAsync(bool includeProfile, bool isTool, bool announced)
    {
        var gameProcessManager = new Mock<IGameProcessManager>();
        var notificationService = new Mock<INotificationService>();
        var vm = CreateViewModelWithMockDependencies(gameProcessManager, notificationService);

        // InitializeAsync is where the view model subscribes to ProcessExited.
        await vm.InitializeAsync();

        var gameProfile = new GameProfile
        {
            Name = "Failing Profile",
            ToolContentId = isTool ? "test-tool" : null,
        };
        var profile = new GameProfileItemViewModel("profile-1", gameProfile, string.Empty, string.Empty);

        profile.PropertyChanged += (_, _) => Assert.True(Dispatcher.UIThread.CheckAccess());
        profile.ProcessId = 4242;
        profile.IsProcessRunning = true;
        if (includeProfile)
        {
            vm.Profiles.Add(profile);
        }

        if (announced)
        {
            vm.Receive(new ProfileLaunchedMessage("profile-1", 4242) { IsToolProfile = isTool });
        }

        var statusBeforeExit = vm.StatusMessage;
        var errorBeforeExit = vm.ErrorMessage;
        await Task.Run(() => gameProcessManager.Raise(m => m.ProcessExited += null, new GameProcessExitedEventArgs
        {
            ProcessId = 4242,
            ExitCode = 1,
            StandardErrorTail = "init abort",
            UnmountableArchives = ["TexturesZH.big"],
        }));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        var notified = !isTool && (announced || includeProfile);
        Assert.Equal(!includeProfile, profile.IsProcessRunning);
        Assert.Equal(includeProfile ? 0 : 4242, profile.ProcessId);
        if (notified)
        {
            Assert.Contains("TexturesZH.big", vm.StatusMessage);
        }
        else
        {
            Assert.Equal(statusBeforeExit, vm.StatusMessage);
        }

        Assert.Equal(errorBeforeExit, vm.ErrorMessage);
        notificationService.Verify(
            n => n.ShowError(
                "Game Exited Unexpectedly",
                It.Is<string>(s => s.Contains("TexturesZH.big") && (!includeProfile || s.Contains("Failing Profile"))),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            notified ? Times.Once() : Times.Never());
    }

    /// <summary>
    /// The stop message can clear the profile's PID before the exit event arrives, and the
    /// exit event can also arrive first. Either way the failure names the profile and the
    /// status line stops reporting the launch as successful.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <param name="stopFirst">Whether the stop message arrives before the exit event.</param>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProcessExitedWithFailure_NamesTheProfileRegardlessOfStopOrderAsync(bool stopFirst)
    {
        var gameProcessManager = new Mock<IGameProcessManager>();
        var notificationService = new Mock<INotificationService>();
        var vm = CreateViewModelWithMockDependencies(gameProcessManager, notificationService);
        await vm.InitializeAsync();

        var profile = CreateProfileItem("Crashing Profile");
        vm.Profiles.Add(profile);
        vm.Receive(new ProfileLaunchedMessage("profile-1", 4250));
        vm.StatusMessage = "Crashing Profile launched successfully (Process ID: 4250)";

        if (stopFirst)
        {
            vm.Receive(new ProfileStoppedMessage("profile-1", 4250));
        }

        gameProcessManager.Raise(m => m.ProcessExited += null, new GameProcessExitedEventArgs
        {
            ProcessId = 4250,
            ExitCode = -1,
        });

        if (!stopFirst)
        {
            vm.Receive(new ProfileStoppedMessage("profile-1", 4250));
        }

        Assert.False(profile.IsProcessRunning);
        Assert.Equal(0, profile.ProcessId);
        Assert.StartsWith("Crashing Profile: ", vm.StatusMessage);
        Assert.Contains("-1", vm.StatusMessage);
        notificationService.Verify(
            n => n.ShowError(
                "Game Exited Unexpectedly",
                It.Is<string>(s => s.StartsWith("Crashing Profile: ")),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Once());
    }

    /// <summary>
    /// A client that dies with STATUS_DLL_NOT_FOUND is reported with the raw code and an
    /// explanation that a DLL is missing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <param name="includeArchives">Whether archive diagnostics accompany the exit code.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessExitedWithDllNotFound_ExplainsTheMissingDllAsync(bool includeArchives)
    {
        var gameProcessManager = new Mock<IGameProcessManager>();
        var notificationService = new Mock<INotificationService>();
        var vm = CreateViewModelWithMockDependencies(gameProcessManager, notificationService);
        await vm.InitializeAsync();

        var profile = CreateProfileItem("Missing DLL Profile");
        vm.Profiles.Add(profile);
        vm.Receive(new ProfileLaunchedMessage("profile-1", 4251));

        gameProcessManager.Raise(m => m.ProcessExited += null, new GameProcessExitedEventArgs
        {
            ProcessId = 4251,
            ExitCode = StartupExitCodeConstants.StatusDllNotFound,
            UnmountableArchives = includeArchives ? ["TexturesZH.big"] : [],
        });

        if (includeArchives)
        {
            Assert.Contains("TexturesZH.big", vm.StatusMessage);
        }

        Assert.Contains("-1073741515", vm.StatusMessage);
        Assert.Contains("DLL", vm.StatusMessage);
        notificationService.Verify(
            n => n.ShowError(
                "Game Exited Unexpectedly",
                It.Is<string>(s => s.Contains("-1073741515") && s.Contains("A required DLL could not be found")),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Once());
    }

    /// <summary>
    /// A clean exit or a requested stop that arrives after the stop message is not a
    /// failure: no error is shown and the status line is left alone.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="terminationRequested">Whether the user asked for the stop.</param>
    [AvaloniaTheory]
    [InlineData(0, false)]
    [InlineData(137, true)]
    public async Task ProcessExitedAfterStopMessage_WithoutFailure_LeavesStatusAloneAsync(int exitCode, bool terminationRequested)
    {
        var gameProcessManager = new Mock<IGameProcessManager>();
        var notificationService = new Mock<INotificationService>();
        var vm = CreateViewModelWithMockDependencies(gameProcessManager, notificationService);
        await vm.InitializeAsync();

        vm.Profiles.Add(CreateProfileItem("Quiet Profile"));
        vm.Receive(new ProfileLaunchedMessage("profile-1", 4251));
        vm.Receive(new ProfileStoppedMessage("profile-1", 4251));
        vm.StatusMessage = "Quiet Profile stopped successfully";

        gameProcessManager.Raise(m => m.ProcessExited += null, new GameProcessExitedEventArgs
        {
            ProcessId = 4251,
            ExitCode = exitCode,
            TerminationRequested = terminationRequested,
        });

        Assert.Equal("Quiet Profile stopped successfully", vm.StatusMessage);
        notificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// A late failure of the previous process must not clobber a relaunch of the same
    /// profile that is already running.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [AvaloniaFact]
    public async Task ProcessExitedWithFailure_ForPreviousProcessAfterRelaunch_KeepsTheRunningStateAsync()
    {
        var gameProcessManager = new Mock<IGameProcessManager>();
        var notificationService = new Mock<INotificationService>();
        var vm = CreateViewModelWithMockDependencies(gameProcessManager, notificationService);
        await vm.InitializeAsync();

        var profile = CreateProfileItem("Relaunched Profile");
        vm.Profiles.Add(profile);
        vm.Receive(new ProfileLaunchedMessage("profile-1", 4252));
        vm.Receive(new ProfileStoppedMessage("profile-1", 4252));
        vm.Receive(new ProfileLaunchedMessage("profile-1", 4253));
        vm.StatusMessage = "Relaunched Profile launched successfully (Process ID: 4253)";

        gameProcessManager.Raise(m => m.ProcessExited += null, new GameProcessExitedEventArgs
        {
            ProcessId = 4252,
            ExitCode = -1,
        });

        Assert.True(profile.IsProcessRunning);
        Assert.Equal(4253, profile.ProcessId);
        Assert.Equal("Relaunched Profile launched successfully (Process ID: 4253)", vm.StatusMessage);
    }

    /// <summary>
    /// A delayed exit with the old process identity must not stop a relaunch that reused its PID.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task ProcessExitedWithFailure_ForReusedPidWithOldIdentity_KeepsRelaunchAsync()
    {
        var gameProcessManager = new Mock<IGameProcessManager>();
        var notificationService = new Mock<INotificationService>();
        var vm = CreateViewModelWithMockDependencies(gameProcessManager, notificationService);
        await vm.InitializeAsync();

        var profile = CreateProfileItem("Relaunched Profile");
        var oldIdentity = Guid.NewGuid();
        var newIdentity = Guid.NewGuid();
        vm.Profiles.Add(profile);
        vm.Receive(new ProfileLaunchedMessage("profile-1", 4252) { ProcessInstanceId = oldIdentity });
        vm.Receive(new ProfileStoppedMessage("profile-1", 4252) { ProcessInstanceId = oldIdentity });
        vm.Receive(new ProfileLaunchedMessage("profile-1", 4252) { ProcessInstanceId = newIdentity });
        vm.StatusMessage = "Relaunched Profile is running";

        gameProcessManager.Raise(m => m.ProcessExited += null, new GameProcessExitedEventArgs
        {
            ProcessId = 4252,
            ProcessInstanceId = oldIdentity,
            ExitCode = -1,
        });

        Assert.True(profile.IsProcessRunning);
        Assert.Equal(4252, profile.ProcessId);
        Assert.Equal(newIdentity, profile.ProcessInstanceId);
        Assert.Equal("Relaunched Profile is running", vm.StatusMessage);
        notificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);

        gameProcessManager.Raise(m => m.ProcessExited += null, new GameProcessExitedEventArgs
        {
            ProcessId = 4252,
            ProcessInstanceId = newIdentity,
            ExitCode = -1,
        });

        Assert.False(profile.IsProcessRunning);
        Assert.Equal(0, profile.ProcessId);
        Assert.StartsWith("Relaunched Profile: ", vm.StatusMessage);
        notificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that HasNoProfiles correctly tracks presence of regular profiles versus empty or add-profile button.
    /// </summary>
    [Fact]
    public void HasNoProfiles_TracksProfileCollectionChanges()
    {
        var vm = CreateViewModelWithMockDependencies();
        Assert.True(vm.HasNoProfiles);

        var profileItem = CreateProfileItem("Test Profile");
        vm.Profiles.Add(profileItem);
        Assert.False(vm.HasNoProfiles);

        vm.Profiles.Remove(profileItem);
        Assert.True(vm.HasNoProfiles);

        var toolProfile = new GameProfile
        {
            Name = "WorldBuilder",
            ToolContentId = "tool-manifest-1",
        };
        var toolItem = new GameProfileItemViewModel("tool-1", toolProfile, string.Empty, string.Empty);
        vm.Profiles.Add(toolItem);
        Assert.True(vm.HasNoProfiles);
    }

    /// <summary>
    /// Verifies that storefront commands can execute and open their respective storefront URLs.
    /// </summary>
    [Fact]
    public void StorefrontCommands_Execute_OpensStoreUrls()
    {
        var vm = CreateViewModelWithMockDependencies();
        var openedUrls = new List<string>();
        vm.UrlOpener = url => openedUrls.Add(url);

        Assert.True(vm.OpenSteamStoreCommand.CanExecute(null));
        Assert.True(vm.OpenEaStoreCommand.CanExecute(null));

        vm.OpenSteamStoreCommand.Execute(null);
        vm.OpenEaStoreCommand.Execute(null);

        Assert.Equal(2, openedUrls.Count);
        Assert.Equal(PublisherInfoConstants.Steam.StoreUrl, openedUrls[0]);
        Assert.Equal(PublisherInfoConstants.EaApp.StoreUrl, openedUrls[1]);
    }

    /// <summary>
    /// Verifies that ShouldShowStorefrontBanner displays when no profiles exist or when no installations were detected.
    /// </summary>
    [Fact]
    public void ShouldShowStorefrontBanner_ShowsWhenNoProfilesOrNoDetectedInstallations()
    {
        var vm = CreateViewModelWithMockDependencies();
        vm.HasLoadedProfilesSuccessfully = true;

        // When HasNoProfiles is true (no profiles loaded)
        Assert.True(vm.HasNoProfiles);
        Assert.True(vm.ShouldShowStorefrontBanner);

        // When a profile exists
        var profileItem = CreateProfileItem("Test Profile");
        vm.Profiles.Add(profileItem);
        Assert.False(vm.HasNoProfiles);
        Assert.False(vm.ShouldShowStorefrontBanner);

        // When scan detected no installations, banner shows even if profiles exist
        vm.HasNoDetectedInstallations = true;
        Assert.True(vm.ShouldShowStorefrontBanner);

        // When installations detected, banner hides
        vm.HasNoDetectedInstallations = false;
        Assert.False(vm.ShouldShowStorefrontBanner);
    }

    /// <summary>
    /// Verifies that ScanForGamesAsync shows notification toasts and sets HasNoDetectedInstallations
    /// when auto-detection finds 0 installations and manual prompt returns null.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScanForGamesAsync_WhenNoInstallations_PromptsNotificationAndShowsWarning()
    {
        var vm = CreateViewModelWithScanMocks(out var installMock, out var notifMock);
        vm.HasLoadedProfilesSuccessfully = true;

        installMock
            .Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([]));

        vm.ManualDirectoryPrompter = () => Task.FromResult<GameInstallation?>(null);

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        // Verifies manual selection info notification was shown
        notifMock.Verify(
            n => n.ShowInfo(
                "Select Game Directory",
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Once);

        // Verifies no installations warning notification was shown
        notifMock.Verify(
            n => n.ShowWarning(
                "No Game Installations Found",
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Once);

        Assert.True(vm.HasNoDetectedInstallations);
        Assert.True(vm.ShouldShowStorefrontBanner);
    }

    /// <summary>
    /// Verifies that ScanForGamesAsync resets HasNoDetectedInstallations to false when a scan starts.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScanForGamesAsync_WhenRescanning_ResetsHasNoDetectedInstallationsBeforeScan()
    {
        var vm = CreateViewModelWithScanMocks(out var installMock, out _);
        vm.HasNoDetectedInstallations = true;

        var wasResetDuringScan = false;
        installMock
            .Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                wasResetDuringScan = !vm.HasNoDetectedInstallations;
                return Task.FromResult(OperationResult<IReadOnlyList<GameInstallation>>.CreateFailure("Scan error"));
            });

        await vm.ScanForGamesCommand.ExecuteAsync(null);

        Assert.True(wasResetDuringScan);
        Assert.False(vm.HasNoDetectedInstallations);
    }

    /// <summary>
    /// Alphabetical sorting orders profiles by name and keeps the add card at the end.
    /// </summary>
    [Fact]
    public void ApplySorting_Alphabetical_SortsProfilesByNameAndKeepsAddCardAtEnd()
    {
        var vm = CreateViewModel();
        var itemB = CreateProfileItem("Bravo");
        var itemA = CreateProfileItem("Alpha");
        var itemC = CreateProfileItem("Charlie");

        vm.Profiles.Add(itemB);
        vm.Profiles.Add(itemA);
        vm.Profiles.Add(itemC);
        vm.Profiles.Add(new AddProfileItemViewModel());

        vm.SelectedSortMode = ProfileSortMode.Alphabetical;
        vm.ApplySorting();

        Assert.Equal("Alpha", vm.Profiles[0].Name);
        Assert.Equal("Bravo", vm.Profiles[1].Name);
        Assert.Equal("Charlie", vm.Profiles[2].Name);
        Assert.IsType<AddProfileItemViewModel>(vm.Profiles[3]);
    }

    /// <summary>
    /// Descending alphabetical sorting orders profiles from Z to A.
    /// </summary>
    [Fact]
    public void ApplySorting_AlphabeticalDesc_SortsProfilesDescending()
    {
        var vm = CreateViewModel();
        var itemB = CreateProfileItem("Bravo");
        var itemA = CreateProfileItem("Alpha");
        var itemC = CreateProfileItem("Charlie");

        vm.Profiles.Add(itemB);
        vm.Profiles.Add(itemA);
        vm.Profiles.Add(itemC);
        vm.Profiles.Add(new AddProfileItemViewModel());

        vm.SelectedSortMode = ProfileSortMode.AlphabeticalDesc;
        vm.ApplySorting();

        Assert.Equal("Charlie", vm.Profiles[0].Name);
        Assert.Equal("Bravo", vm.Profiles[1].Name);
        Assert.Equal("Alpha", vm.Profiles[2].Name);
        Assert.IsType<AddProfileItemViewModel>(vm.Profiles[3]);
    }

    /// <summary>
    /// Free mode enables manual reorder controls with correct edge flags.
    /// </summary>
    [Fact]
    public void ApplySorting_FreeMode_EnablesReorderFlags()
    {
        var vm = CreateViewModel();
        var itemA = CreateProfileItem("Alpha");
        var itemB = CreateProfileItem("Bravo");

        vm.Profiles.Add(itemA);
        vm.Profiles.Add(itemB);
        vm.Profiles.Add(new AddProfileItemViewModel());

        vm.SelectedSortMode = ProfileSortMode.Free;
        vm.ApplySorting();

        Assert.True(itemA.IsFreeReorderMode);
        Assert.True(itemB.IsFreeReorderMode);
        Assert.False(itemA.CanMoveLeft);
        Assert.True(itemA.CanMoveRight);
        Assert.True(itemB.CanMoveLeft);
        Assert.False(itemB.CanMoveRight);
    }

    /// <summary>
    /// Sorting reorders the live collection in place so card visuals are preserved
    /// instead of being torn down and rebuilt (which flashes a black frame).
    /// </summary>
    [Fact]
    public void ApplySorting_ReordersInPlace_WithoutResettingCollection()
    {
        var vm = CreateViewModel();
        var itemB = CreateProfileItem("Bravo");
        var itemA = CreateProfileItem("Alpha");
        var itemC = CreateProfileItem("Charlie");
        var addCard = new AddProfileItemViewModel();

        vm.Profiles.Add(itemB);
        vm.Profiles.Add(itemA);
        vm.Profiles.Add(itemC);
        vm.Profiles.Add(addCard);

        var actions = new List<NotifyCollectionChangedAction>();
        vm.Profiles.CollectionChanged += (s, e) => actions.Add(e.Action);

        vm.SelectedSortMode = ProfileSortMode.Alphabetical;

        Assert.Equal("Alpha", vm.Profiles[0].Name);
        Assert.Equal("Bravo", vm.Profiles[1].Name);
        Assert.Equal("Charlie", vm.Profiles[2].Name);
        Assert.Same(addCard, vm.Profiles[3]);
        Assert.Same(itemA, vm.Profiles[0]);
        Assert.Same(itemB, vm.Profiles[1]);
        Assert.Same(itemC, vm.Profiles[2]);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    /// <summary>
    /// Last-played sorting puts the most recently played profile first and unplayed profiles last.
    /// </summary>
    [Fact]
    public void ApplySorting_LastPlayed_PutsRecentlyPlayedFirst()
    {
        var vm = CreateViewModel();
        var oldItem = CreateProfileItem("Old");
        oldItem.LastPlayedAt = DateTime.UtcNow.AddDays(-7);
        var newItem = CreateProfileItem("New");
        newItem.LastPlayedAt = DateTime.UtcNow;
        var neverItem = CreateProfileItem("Never");

        vm.Profiles.Add(oldItem);
        vm.Profiles.Add(neverItem);
        vm.Profiles.Add(newItem);
        vm.Profiles.Add(new AddProfileItemViewModel());

        vm.SelectedSortMode = ProfileSortMode.LastPlayed;
        vm.ApplySorting();

        Assert.Equal("New", vm.Profiles[0].Name);
        Assert.Equal("Old", vm.Profiles[1].Name);
        Assert.Equal("Never", vm.Profiles[2].Name);
        Assert.IsType<AddProfileItemViewModel>(vm.Profiles[3]);
    }

    /// <summary>
    /// Last-played sorting breaks timestamp ties by creation date and keeps unplayed profiles last.
    /// </summary>
    [Fact]
    public void ApplySorting_LastPlayed_BreaksTiesByCreationDate()
    {
        var vm = CreateViewModel();
        var stamp = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var olderTie = CreateProfileItem("OlderTie");
        olderTie.LastPlayedAt = stamp;
        olderTie.CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newerTie = CreateProfileItem("NewerTie");
        newerTie.LastPlayedAt = stamp;
        newerTie.CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newerNever = CreateProfileItem("NewerNever");
        newerNever.CreatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var olderNever = CreateProfileItem("OlderNever");
        olderNever.CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        vm.Profiles.Add(olderNever);
        vm.Profiles.Add(olderTie);
        vm.Profiles.Add(newerNever);
        vm.Profiles.Add(newerTie);
        vm.Profiles.Add(new AddProfileItemViewModel());

        vm.SelectedSortMode = ProfileSortMode.LastPlayed;
        vm.ApplySorting();

        Assert.Equal("NewerTie", vm.Profiles[0].Name);
        Assert.Equal("OlderTie", vm.Profiles[1].Name);
        Assert.Equal("NewerNever", vm.Profiles[2].Name);
        Assert.Equal("OlderNever", vm.Profiles[3].Name);
        Assert.IsType<AddProfileItemViewModel>(vm.Profiles[4]);
    }

    /// <summary>
    /// Rapid sort mode changes persist the final selection once earlier requests go stale.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SelectedSortMode_RapidChanges_PersistsFinalSelectionAsync()
    {
        var savedModes = new List<ProfileSortMode>();
        var settingsService = new Mock<IUserSettingsService>();
        settingsService.Setup(x => x.Get()).Returns(new UserSettings { ProfileSortMode = ProfileSortMode.LastPlayed });
        settingsService
            .Setup(x => x.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()))
            .Callback<Func<UserSettings, bool>>(apply =>
            {
                var settings = new UserSettings();
                if (apply(settings))
                {
                    lock (savedModes)
                    {
                        savedModes.Add(settings.ProfileSortMode);
                    }
                }
            })
            .ReturnsAsync(true);
        var vm = new GameProfileLauncherViewModel(
            new Mock<IGameInstallationService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<IProfileLauncherFacade>().Object,
            null!,
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IGameProcessManager>().Object,
            new Mock<IShortcutService>().Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            CreateLocalizationService(),
            userSettingsService: settingsService.Object);

        vm.SelectedSortMode = ProfileSortMode.Alphabetical;
        vm.SelectedSortMode = ProfileSortMode.DateCreated;

        for (var attempt = 0; attempt < 200; attempt++)
        {
            lock (savedModes)
            {
                if (savedModes.Contains(ProfileSortMode.DateCreated))
                {
                    break;
                }
            }

            await Task.Delay(20);
        }

        await Task.Delay(200);

        lock (savedModes)
        {
            Assert.NotEmpty(savedModes);
            Assert.Equal(ProfileSortMode.DateCreated, savedModes[^1]);
        }
    }

    /// <summary>
    /// The sort dropdown starts closed so the header collapse guard is inactive initially.
    /// </summary>
    [Fact]
    public void IsSortDropdownOpen_DefaultsToFalse()
    {
        var vm = CreateViewModel();

        Assert.False(vm.IsSortDropdownOpen);
    }

    /// <summary>
    /// A successful move persists the new order for every profile.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task MoveProfileRelative_PersistSuccess_SavesNewOrderAsync()
    {
        var gameProfileManager = new Mock<IGameProfileManager>();
        var persistedOrders = new List<(string ProfileId, int DisplayOrder)>();
        gameProfileManager
            .Setup(m => m.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, UpdateProfileRequest, CancellationToken>((profileId, request, _) => persistedOrders.Add((profileId, request.DisplayOrder ?? -1)))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile()));
        var vm = CreateViewModelWithProfileManager(gameProfileManager, new Mock<IGameProcessManager>(), new Mock<INotificationService>());

        var itemA = CreateProfileItem("alpha-id", "Alpha");
        var itemB = CreateProfileItem("bravo-id", "Bravo");
        itemA.DisplayOrder = 0;
        itemB.DisplayOrder = 1;
        vm.Profiles.Add(itemA);
        vm.Profiles.Add(itemB);
        vm.Profiles.Add(new AddProfileItemViewModel());
        vm.SelectedSortMode = ProfileSortMode.Free;
        vm.ApplySorting();

        await itemA.MoveRightAction!(itemA);

        Assert.Same(itemB, vm.Profiles[0]);
        Assert.Same(itemA, vm.Profiles[1]);
        Assert.Equal(0, itemB.DisplayOrder);
        Assert.Equal(1, itemA.DisplayOrder);
        Assert.Equal(new List<(string ProfileId, int DisplayOrder)> { ("bravo-id", 0), ("alpha-id", 1) }, persistedOrders);
    }

    /// <summary>
    /// A failed move restores the previous order and warns the user.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task MoveProfileRelative_PersistFailure_RestoresOriginalOrderAndWarnsAsync()
    {
        var gameProfileManager = new Mock<IGameProfileManager>();
        gameProfileManager
            .Setup(m => m.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("disk error"));
        var notificationService = new Mock<INotificationService>();
        var vm = CreateViewModelWithProfileManager(gameProfileManager, new Mock<IGameProcessManager>(), notificationService);

        var itemA = CreateProfileItem("alpha-id", "Alpha");
        var itemB = CreateProfileItem("bravo-id", "Bravo");
        itemA.DisplayOrder = 0;
        itemB.DisplayOrder = 1;
        vm.Profiles.Add(itemA);
        vm.Profiles.Add(itemB);
        vm.Profiles.Add(new AddProfileItemViewModel());
        vm.SelectedSortMode = ProfileSortMode.Free;
        vm.ApplySorting();

        await itemA.MoveRightAction!(itemA);

        Assert.Same(itemA, vm.Profiles[0]);
        Assert.Same(itemB, vm.Profiles[1]);
        Assert.Equal(0, itemA.DisplayOrder);
        Assert.Equal(1, itemB.DisplayOrder);
        notificationService.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// A partially failed move writes saved orders back so disk matches the restored in-memory order.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task MoveProfileRelative_PartialFailure_WritesBackSavedOrdersAsync()
    {
        var gameProfileManager = new Mock<IGameProfileManager>();
        var persistedOrders = new List<(string ProfileId, int DisplayOrder)>();
        var results = new Queue<ProfileOperationResult<GameProfile>>(
        [
            ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile()),
            ProfileOperationResult<GameProfile>.CreateFailure("disk error"),
            ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile()),
        ]);
        gameProfileManager
            .Setup(m => m.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, UpdateProfileRequest, CancellationToken>((profileId, request, _) => persistedOrders.Add((profileId, request.DisplayOrder ?? -1)))
            .ReturnsAsync(() => results.Dequeue());
        var vm = CreateViewModelWithProfileManager(gameProfileManager, new Mock<IGameProcessManager>(), new Mock<INotificationService>());

        var itemA = CreateProfileItem("alpha-id", "Alpha");
        var itemB = CreateProfileItem("bravo-id", "Bravo");
        itemA.DisplayOrder = 0;
        itemB.DisplayOrder = 1;
        vm.Profiles.Add(itemA);
        vm.Profiles.Add(itemB);
        vm.Profiles.Add(new AddProfileItemViewModel());
        vm.SelectedSortMode = ProfileSortMode.Free;
        vm.ApplySorting();

        await itemA.MoveRightAction!(itemA);

        Assert.Same(itemA, vm.Profiles[0]);
        Assert.Same(itemB, vm.Profiles[1]);
        Assert.Equal(new List<(string ProfileId, int DisplayOrder)> { ("bravo-id", 0), ("alpha-id", 1), ("bravo-id", 1) }, persistedOrders);
    }

    private static ProfileResourceService CreateProfileResourceService()
    {
        return new ProfileResourceService(NullLogger<ProfileResourceService>.Instance, CreateLocalizationService());
    }

    private static SuperHackersProvider CreateSuperHackersProvider()
    {
        var discovererMock = new Mock<IContentDiscoverer>();
        discovererMock.Setup(x => x.SourceName).Returns(GenHub.Core.Constants.PublisherTypeConstants.TheSuperHackers);

        var resolverMock = new Mock<IContentResolver>();
        resolverMock.Setup(x => x.ResolverId).Returns(GenHub.Core.Constants.SuperHackersConstants.ResolverId);

        var delivererMock = new Mock<IContentDeliverer>();
        delivererMock.Setup(x => x.SourceName).Returns(GenHub.Core.Constants.ContentSourceNames.GitHubDeliverer);

        var loaderMock = new Mock<IProviderDefinitionLoader>();

        return new SuperHackersProvider(
            loaderMock.Object,
            [discovererMock.Object],
            [resolverMock.Object],
            [delivererMock.Object],
            new Mock<GenHub.Core.Interfaces.Content.IContentValidator>().Object,
            NullLogger<SuperHackersProvider>.Instance,
            new Mock<IInstallationInstructionsService>().Object);
    }

    /// <summary>
    /// Creates a profile item bound to a mocked profile.
    /// </summary>
    /// <param name="profileId">The profile identifier.</param>
    /// <param name="name">The profile name.</param>
    /// <returns>The profile item.</returns>
    private static GameProfileItemViewModel CreateProfileItem(string profileId, string name)
    {
        var profile = new Mock<IGameProfile>();
        profile.Setup(x => x.Name).Returns(name);
        return new GameProfileItemViewModel(profileId, profile.Object, "icon.png", "cover.jpg");
    }

    private static GameProfileItemViewModel CreateProfileItemWithClient(string profileId, string name, GameClient gameClient)
    {
        var profile = new Mock<IGameProfile>();
        profile.Setup(x => x.Name).Returns(name);
        profile.Setup(x => x.GameClient).Returns(gameClient);
        return new GameProfileItemViewModel(profileId, profile.Object, "icon.png", "cover.jpg");
    }

    /// <summary>
    /// Creates a GameProfileLauncherViewModel wired to the given launcher facade and
    /// notification service, with everything else mocked.
    /// </summary>
    /// <param name="launcherFacade">The launcher facade mock.</param>
    /// <param name="notificationService">The notification service mock.</param>
    /// <param name="telemetryService">The optional telemetry service mock.</param>
    /// <returns>The view model.</returns>
    private static GameProfileLauncherViewModel CreateLauncherViewModel(
        Mock<IProfileLauncherFacade> launcherFacade,
        Mock<INotificationService> notificationService,
        Mock<ITelemetryService>? telemetryService = null)
    {
        return new GameProfileLauncherViewModel(
            new Mock<IGameInstallationService>().Object,
            new Mock<IGameProfileManager>().Object,
            launcherFacade.Object,
            null!,
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IGameProcessManager>().Object,
            new Mock<IShortcutService>().Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            notificationService.Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            CreateLocalizationService(),
            telemetryService: telemetryService?.Object);
    }

    /// <summary>
    /// Creates a GameProfileLauncherViewModel with mocked dependencies for testing.
    /// </summary>
    /// <returns>A GameProfileLauncherViewModel instance for testing.</returns>
    private static GameProfileLauncherViewModel CreateViewModelWithMockDependencies()
    {
        return CreateViewModelWithMockDependencies(
            new Mock<IGameProcessManager>(),
            new Mock<INotificationService>());
    }

    /// <summary>
    /// Creates a GameProfileLauncherViewModel wired to the given installation service and
    /// notification mocks for testing scan scenarios.
    /// </summary>
    /// <param name="installationServiceMock">The game installation service mock.</param>
    /// <param name="notificationServiceMock">The notification service mock to observe.</param>
    /// <returns>A GameProfileLauncherViewModel instance for testing.</returns>
    private static GameProfileLauncherViewModel CreateViewModelWithScanMocks(
        out Mock<IGameInstallationService> installationServiceMock,
        out Mock<INotificationService> notificationServiceMock)
    {
        installationServiceMock = new Mock<IGameInstallationService>();
        notificationServiceMock = new Mock<INotificationService>();
        var gameProfileManager = new Mock<IGameProfileManager>();
        gameProfileManager
            .Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        return new GameProfileLauncherViewModel(
            installationServiceMock.Object,
            gameProfileManager.Object,
            new Mock<IProfileLauncherFacade>().Object,
            null!,
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IGameProcessManager>().Object,
            new Mock<IShortcutService>().Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            notificationServiceMock.Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            CreateLocalizationService());
    }

    /// <summary>
    /// Creates a GameProfileLauncherViewModel wired to the given process manager and
    /// notification mocks, so tests can raise process events and observe notifications.
    /// </summary>
    /// <param name="gameProcessManager">The process manager mock the view model subscribes to.</param>
    /// <param name="notificationService">The notification service mock to observe.</param>
    /// <returns>A GameProfileLauncherViewModel instance for testing.</returns>
    private static GameProfileLauncherViewModel CreateViewModelWithMockDependencies(
        Mock<IGameProcessManager> gameProcessManager,
        Mock<INotificationService> notificationService)
    {
        return CreateViewModelWithProfileManager(new Mock<IGameProfileManager>(), gameProcessManager, notificationService);
    }

    /// <summary>
    /// Creates a GameProfileLauncherViewModel wired to the given profile manager,
    /// process manager, and notification mocks.
    /// </summary>
    /// <param name="gameProfileManager">The game profile manager mock.</param>
    /// <param name="gameProcessManager">The process manager mock the view model subscribes to.</param>
    /// <param name="notificationService">The notification service mock to observe.</param>
    /// <returns>A GameProfileLauncherViewModel instance for testing.</returns>
    private static GameProfileLauncherViewModel CreateViewModelWithProfileManager(
        Mock<IGameProfileManager> gameProfileManager,
        Mock<IGameProcessManager> gameProcessManager,
        Mock<INotificationService> notificationService)
    {
        // InitializeAsync must complete cleanly: it is what subscribes the view model to
        // ProcessExited, and a failed profile load would pollute the error state these
        // tests assert on.
        gameProfileManager
            .Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        return new GameProfileLauncherViewModel(
            new Mock<IGameInstallationService>().Object,
            gameProfileManager.Object,
            new Mock<IProfileLauncherFacade>().Object,
            new GameProfileSettingsViewModel(
                new Mock<IGameProfileManager>().Object,
                new Mock<IGameSettingsService>().Object,
                new Mock<IConfigurationProviderService>().Object,
                new Mock<IProfileContentLoader>().Object,
                CreateProfileResourceService(),
                new Mock<INotificationService>().Object,
                null,
                new Mock<IContentStorageService>().Object,
                null, // ILocalContentService
                null, // IGenLauncherNormalizationService
                null, // IDialogService
                NullLogger<GameProfileSettingsViewModel>.Instance,
                NullLogger<GameSettingsViewModel>.Instance),
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            gameProcessManager.Object,
            new Mock<IShortcutService>().Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            notificationService.Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            CreateLocalizationService());
    }

    private static ILocalizationService CreateLocalizationService()
    {
        var resourceManager = new ResourceManager(LocalizationConstants.StringResourceBaseName, typeof(GenHub.Common.Services.LocalizationService).Assembly);
        var mock = new Mock<ILocalizationService>();
        mock.Setup(m => m.CurrentCulture).Returns(System.Globalization.CultureInfo.InvariantCulture);
        mock.Setup(m => m.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns<string, object?[]>((key, args) =>
            {
                var val = resourceManager.GetString(key, System.Globalization.CultureInfo.InvariantCulture) ?? key;
                return args != null && args.Length > 0 ? string.Format(System.Globalization.CultureInfo.InvariantCulture, val, args) : val;
            });
        mock.Setup(m => m[It.IsAny<string>()])
            .Returns<string>(key => resourceManager.GetString(key, System.Globalization.CultureInfo.InvariantCulture) ?? key);
        mock.Setup(m => m.TryGetString(It.IsAny<string>(), out It.Ref<string?>.IsAny, It.IsAny<object?[]>()))
            .Returns(new TryGetLocalizedString((string key, out string? value, object?[] arguments) =>
            {
                value = resourceManager.GetString(key, System.Globalization.CultureInfo.InvariantCulture);
                return value != null;
            }));
        return mock.Object;
    }

    private static GameProfileLauncherViewModel CreateViewModelWithLauncherFacade(IProfileLauncherFacade launcherFacade)
    {
        var gameProfileManager = new Mock<IGameProfileManager>();

        return new GameProfileLauncherViewModel(
            new Mock<IGameInstallationService>().Object,
            gameProfileManager.Object,
            launcherFacade,
            new GameProfileSettingsViewModel(
                new Mock<IGameProfileManager>().Object,
                new Mock<IGameSettingsService>().Object,
                new Mock<IConfigurationProviderService>().Object,
                new Mock<IProfileContentLoader>().Object,
                CreateProfileResourceService(),
                new Mock<INotificationService>().Object,
                null,
                new Mock<IContentStorageService>().Object,
                null, // ILocalContentService
                null, // IGenLauncherNormalizationService
                null, // IDialogService
                NullLogger<GameProfileSettingsViewModel>.Instance,
                NullLogger<GameSettingsViewModel>.Instance),
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IGameProcessManager>().Object,
            new Mock<IShortcutService>().Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            CreateProfileResourceService(),
            new Mock<IGameClientDetector>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            CreateLocalizationService());
    }

    /// <summary>
    /// Creates a profile item view model backed by a mocked profile.
    /// </summary>
    /// <param name="name">The profile name.</param>
    /// <returns>A profile item for the launcher's collection.</returns>
    private static GameProfileItemViewModel CreateProfileItem(string name)
    {
        var profile = new Mock<IGameProfile>();
        profile.SetupGet(p => p.Name).Returns(name);
        profile.SetupGet(p => p.Version).Returns("1.0");
        profile.SetupGet(p => p.ExecutablePath).Returns(string.Empty);

        return new GameProfileItemViewModel("profile-1", profile.Object, string.Empty, string.Empty);
    }

    /// <summary>
    /// Creates a launcher view model with mocked dependencies for sorting tests.
    /// </summary>
    /// <returns>A launcher view model for testing.</returns>
    private static GameProfileLauncherViewModel CreateViewModel()
    {
        return CreateViewModelWithMockDependencies();
    }
}
