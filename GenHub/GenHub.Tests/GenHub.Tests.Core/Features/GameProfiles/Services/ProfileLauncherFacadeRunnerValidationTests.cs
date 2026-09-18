using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Unit tests for the compatibility runner check in <see cref="ProfileLauncherFacade"/> launch validation.
/// </summary>
public sealed class ProfileLauncherFacadeRunnerValidationTests
{
    private const string ProfileId = "profile-runner-test";
    private const string InstallationManifestId = "1.104.steam.gameinstallation.zerohour";
    private const string ClientManifestId = "1.104.steam.gameclient.zerohour";
    private const string InstallationId = "install-id";

    private readonly Mock<IGameProfileManager> _profileManagerMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<IGameInstallationService> _installationServiceMock = new();
    private readonly Mock<ICasService> _casServiceMock = new();
    private readonly Mock<IGameLaunchRunner> _launchRunnerMock = new();
    private readonly Mock<ILocalizationService> _localizationServiceMock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileLauncherFacadeRunnerValidationTests"/> class.
    /// </summary>
    public ProfileLauncherFacadeRunnerValidationTests()
    {
        _casServiceMock
            .Setup(c => c.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CasStats { ObjectCount = 10, TotalSize = 1024, SpaceSaved = 1024 });
        _launchRunnerMock
            .Setup(r => r.CanLaunchWindowsExecutables())
            .Returns(true);
    }

    /// <summary>
    /// Verifies validation passes when a compatibility runner is available.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateLaunchAsync_WhenRunnerAvailable_PassesValidationAsync()
    {
        // Arrange
        ArrangePassingProfile(useSteamLaunch: false);
        var facade = CreateFacade(_localizationServiceMock.Object);

        // Act
        var result = await facade.ValidateLaunchAsync(ProfileId);

        // Assert
        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Verifies validation fails with the localized message when no runner is available.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateLaunchAsync_WhenRunnerMissing_FailsWithLocalizedMessageAsync()
    {
        // Arrange
        ArrangePassingProfile(useSteamLaunch: false);
        _launchRunnerMock
            .Setup(r => r.CanLaunchWindowsExecutables())
            .Returns(false);
        var localized = "runner-missing-marker";
        _localizationServiceMock
            .Setup(x => x.TryGetString(It.IsAny<string>(), out localized))
            .Returns(true);
        var facade = CreateFacade(_localizationServiceMock.Object);

        // Act
        var result = await facade.ValidateLaunchAsync(ProfileId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(localized, result.FirstError);
        _localizationServiceMock.Verify(
            x => x.TryGetString(ProfileValidationConstants.MissingCompatibilityRunnerKey, out localized),
            Times.Once);
    }

    /// <summary>
    /// Verifies Steam launches skip the runner check because the Steam client provides Proton.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateLaunchAsync_WhenRunnerMissingAndSteamLaunch_SkipsRunnerCheckAsync()
    {
        // Arrange
        ArrangePassingProfile(useSteamLaunch: true);
        _launchRunnerMock
            .Setup(r => r.CanLaunchWindowsExecutables())
            .Returns(false);
        _installationServiceMock
            .Setup(s => s.GetInstallationAsync(InstallationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(
                new GameInstallation(Path.GetTempPath(), GameInstallationType.Steam)));
        var facade = CreateFacade(_localizationServiceMock.Object);

        // Act
        var result = await facade.ValidateLaunchAsync(ProfileId);

        // Assert
        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Verifies the Steam exemption requires a Steam installation, not just the flag.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateLaunchAsync_WhenSteamFlagWithNonSteamInstallation_FailsValidationAsync()
    {
        // Arrange
        ArrangePassingProfile(useSteamLaunch: true);
        _launchRunnerMock
            .Setup(r => r.CanLaunchWindowsExecutables())
            .Returns(false);
        _installationServiceMock
            .Setup(s => s.GetInstallationAsync(InstallationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(
                new GameInstallation(Path.GetTempPath(), GameInstallationType.Custom)));
        var localized = "runner-missing-marker";
        _localizationServiceMock
            .Setup(x => x.TryGetString(It.IsAny<string>(), out localized))
            .Returns(true);
        var facade = CreateFacade(_localizationServiceMock.Object);

        // Act
        var result = await facade.ValidateLaunchAsync(ProfileId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(localized, result.FirstError);
    }

    /// <summary>
    /// Verifies validation falls back to English when localization is unavailable.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateLaunchAsync_WhenRunnerMissingAndLocalizationMissing_FallsBackToEnglishAsync()
    {
        // Arrange
        ArrangePassingProfile(useSteamLaunch: false);
        _launchRunnerMock
            .Setup(r => r.CanLaunchWindowsExecutables())
            .Returns(false);
        var facade = CreateFacade(localizationService: null);

        // Act
        var result = await facade.ValidateLaunchAsync(ProfileId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(ProfileValidationConstants.MissingCompatibilityRunner, result.FirstError);
    }

    private ProfileLauncherFacade CreateFacade(ILocalizationService? localizationService)
    {
        return new ProfileLauncherFacade(
            _profileManagerMock.Object,
            Mock.Of<IGameLauncher>(),
            Mock.Of<IWorkspaceManager>(),
            Mock.Of<ILaunchRegistry>(),
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            Mock.Of<IDependencyResolver>(),
            _casServiceMock.Object,
            Mock.Of<IGameSettingsService>(),
            Mock.Of<IStorageLocationService>(),
            Mock.Of<INotificationService>(),
            Mock.Of<IPublisherReconcilerRegistry>(),
            Mock.Of<IConfigurationProviderService>(),
            Mock.Of<IGameProcessManager>(),
            Mock.Of<ISymlinkCapabilityProvider>(),
            NullLogger<ProfileLauncherFacade>.Instance,
            _launchRunnerMock.Object,
            localizationService: localizationService);
    }

    private void ArrangePassingProfile(bool useSteamLaunch)
    {
        var installationManifest = new ContentManifest
        {
            Id = ManifestId.Create(InstallationManifestId),
            Name = "Steam Zero Hour",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.ZeroHour,
        };

        var clientManifest = new ContentManifest
        {
            Id = ManifestId.Create(ClientManifestId),
            Name = "Steam Zero Hour Client",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };

        var profile = new GameProfile
        {
            Id = ProfileId,
            Name = "Runner Test Profile",
            GameInstallationId = InstallationId,
            UseSteamLaunch = useSteamLaunch,
            GameClient = new GameClient
            {
                Id = ClientManifestId,
                Name = clientManifest.Name,
                GameType = GameType.ZeroHour,
                InstallationId = InstallationId,
            },
            EnabledContentIds =
            [
                InstallationManifestId,
            ],
        };

        _profileManagerMock
            .Setup(p => p.GetProfileAsync(ProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(InstallationManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(installationManifest));
        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(ClientManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(clientManifest));
    }
}
