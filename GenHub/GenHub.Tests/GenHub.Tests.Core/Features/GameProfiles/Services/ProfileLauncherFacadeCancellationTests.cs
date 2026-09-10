using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Tests that <see cref="ProfileLauncherFacade"/> preserves cancellation rather than reporting it
/// as a launch failure.
/// </summary>
public class ProfileLauncherFacadeCancellationTests
{
    private readonly Mock<IGameProfileManager> _profileManagerMock = new();

    /// <summary>
    /// Verifies that cancelling the launch token propagates instead of being logged and returned
    /// as a generic "Failed to launch profile" result.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WhenCancelled_PropagatesCancellationAsync()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _profileManagerMock
            .Setup(manager => manager.GetProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var facade = CreateFacade();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => facade.LaunchProfileAsync("profile1", cancellationToken: cts.Token));
    }

    /// <summary>
    /// Verifies that a timeout raised on some other token is still reported as a launch failure,
    /// since HttpClient timeouts also surface as <see cref="TaskCanceledException"/>.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task LaunchProfileAsync_WhenDependencyTimesOut_ReturnsFailureAsync()
    {
        _profileManagerMock
            .Setup(manager => manager.GetProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var facade = CreateFacade();

        var result = await facade.LaunchProfileAsync("profile1");

        Assert.True(result.Failed);
    }

    private ProfileLauncherFacade CreateFacade() => new(
        _profileManagerMock.Object,
        Mock.Of<IGameLauncher>(),
        Mock.Of<IWorkspaceManager>(),
        Mock.Of<ILaunchRegistry>(),
        Mock.Of<IContentManifestPool>(),
        Mock.Of<IGameInstallationService>(),
        Mock.Of<IDependencyResolver>(),
        Mock.Of<ICasService>(),
        Mock.Of<IGameSettingsService>(),
        Mock.Of<IStorageLocationService>(),
        Mock.Of<INotificationService>(),
        Mock.Of<IPublisherReconcilerRegistry>(),
        Mock.Of<IConfigurationProviderService>(),
        Mock.Of<IGameProcessManager>(),
        Mock.Of<ISymlinkCapabilityProvider>(),
        Mock.Of<ILogger<ProfileLauncherFacade>>());
    /// <summary>
    /// Verifies that LaunchProfileAsync initializes the dynamic installation CAS pool path when an installation is resolved.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task LaunchProfileAsync_InitializesDynamicCasPoolPathAsync()
    {
        var installation = new GameInstallation("/games/ZeroHour", GameInstallationType.Retail)
        {
            Id = "install-1",
        };
        var profile = new GameProfile
        {
            Id = "profile-1",
            Name = "Profile 1",
            GameInstallationId = "install-1",
            Client = new GameClient { Id = "client-1", WorkingDirectory = "/games/ZeroHour", ExecutablePath = "/games/ZeroHour/generals.exe" },
            WorkspaceStrategy = WorkspaceStrategy.DynamicIsolated,
        };

        _profileManagerMock
            .Setup(manager => manager.GetProfileAsync("profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        var installationServiceMock = new Mock<IGameInstallationService>();
        installationServiceMock
            .Setup(s => s.GetInstallationAsync("install-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(installation));

        var casPoolServiceMock = new Mock<IInstallationCasPoolService>();
        casPoolServiceMock
            .Setup(c => c.EnsurePoolPathAsync(It.IsAny<IReadOnlyList<GameInstallation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var facade = new ProfileLauncherFacade(
            _profileManagerMock.Object,
            Mock.Of<IGameLauncher>(),
            Mock.Of<IWorkspaceManager>(),
            Mock.Of<ILaunchRegistry>(),
            Mock.Of<IContentManifestPool>(),
            installationServiceMock.Object,
            Mock.Of<IDependencyResolver>(),
            Mock.Of<ICasService>(),
            Mock.Of<IGameSettingsService>(),
            Mock.Of<IStorageLocationService>(),
            Mock.Of<INotificationService>(),
            Mock.Of<IPublisherReconcilerRegistry>(),
            Mock.Of<IConfigurationProviderService>(),
            Mock.Of<IGameProcessManager>(),
            Mock.Of<ISymlinkCapabilityProvider>(),
            Mock.Of<ILogger<ProfileLauncherFacade>>(),
            casPoolServiceMock.Object);

        await facade.LaunchProfileAsync("profile-1");

        casPoolServiceMock.Verify(
            c => c.EnsurePoolPathAsync(It.Is<IReadOnlyList<GameInstallation>>(list => list.Contains(installation)), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }
}
