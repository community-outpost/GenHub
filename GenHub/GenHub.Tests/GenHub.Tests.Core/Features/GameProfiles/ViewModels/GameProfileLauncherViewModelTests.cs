using Avalonia.Headless.XUnit;
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
using GenHub.Core.Models.Enums;
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
using System.Resources;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Contains unit tests for <see cref="GameProfileLauncherViewModel"/>.
/// </summary>
public class GameProfileLauncherViewModelTests
{
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
    }

    private static ProfileResourceService CreateProfileResourceService()
    {
        return new ProfileResourceService(NullLogger<ProfileResourceService>.Instance);
    }

    private static SuperHackersProvider CreateSuperHackersProvider()
    {
        var discovererMock = new Mock<IContentDiscoverer>();
        discovererMock.Setup(x => x.SourceName).Returns("GitHubReleasesDiscoverer");

        var resolverMock = new Mock<IContentResolver>();
        resolverMock.Setup(x => x.ResolverId).Returns(GenHub.Core.Constants.SuperHackersConstants.ResolverId);

        var delivererMock = new Mock<IContentDeliverer>();
        delivererMock.Setup(x => x.SourceName).Returns(GenHub.Core.Constants.ContentSourceNames.GitHubDeliverer);

        var gitHubApiClientMock = new Mock<IGitHubApiClient>();

        var loaderMock = new Mock<IProviderDefinitionLoader>();

        return new SuperHackersProvider(
            loaderMock.Object,
            gitHubApiClientMock.Object,
            [resolverMock.Object],
            [delivererMock.Object],
            new Mock<GenHub.Core.Interfaces.Content.IContentValidator>().Object,
            NullLogger<SuperHackersProvider>.Instance,
            new Mock<IInstallationInstructionsService>().Object);
    }

    /// <summary>
    /// Creates a GameProfileLauncherViewModel with mocked dependencies for testing.
    /// </summary>
    /// <returns>A GameProfileLauncherViewModel instance for testing.</returns>
    private static GameProfileLauncherViewModel CreateViewModelWithMockDependencies()
    {
        var gameProfileManager = new Mock<IGameProfileManager>();

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

    private static ILocalizationService CreateLocalizationService()
    {
        var resourceManager = new ResourceManager(LocalizationConstants.StringResourceBaseName, typeof(GenHub.Common.Services.LocalizationService).Assembly);
        var mock = new Mock<ILocalizationService>();
        mock.Setup(m => m.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns<string, object?[]>((key, args) =>
            {
                var val = resourceManager.GetString(key, System.Globalization.CultureInfo.InvariantCulture) ?? key;
                return args != null && args.Length > 0 ? string.Format(System.Globalization.CultureInfo.InvariantCulture, val, args) : val;
            });
        mock.Setup(m => m[It.IsAny<string>()])
            .Returns<string>(key => resourceManager.GetString(key, System.Globalization.CultureInfo.InvariantCulture) ?? key);
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
}
