using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using GenHub.Features.Online.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Online;

/// <summary>
/// Unit tests for <see cref="OnlineLaunchService"/>.
/// </summary>
public class OnlineLaunchServiceTests
{
    /// <summary>
    /// Tests that a blank expected profile id fails without touching launch services.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_WithBlankProfileId_ShouldFailAsync()
    {
        // Arrange
        var manager = new Mock<IGameProfileManager>(MockBehavior.Strict);
        var facade = new Mock<IProfileLauncherFacade>(MockBehavior.Strict);
        var service = new OnlineLaunchService(manager.Object, facade.Object, Mock.Of<ILogger<OnlineLaunchService>>());

        // Act
        var result = await service.PlayAsync("  ", "Net");

        // Assert
        Assert.False(result.Success);
        Assert.Contains(OnlineConstants.ErrorProfileMissing, result.Errors);
    }

    /// <summary>
    /// Tests that a missing profile maps to the profile-missing error code.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_WithMissingProfile_ShouldFailAsync()
    {
        // Arrange
        var manager = new Mock<IGameProfileManager>();
        manager.Setup(m => m.GetProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("Not found"));
        var service = new OnlineLaunchService(
            manager.Object,
            Mock.Of<IProfileLauncherFacade>(),
            Mock.Of<ILogger<OnlineLaunchService>>());

        // Act
        var result = await service.PlayAsync("ghost", "Net");

        // Assert
        Assert.False(result.Success);
        Assert.Contains(OnlineConstants.ErrorProfileMissing, result.Errors);
    }

    /// <summary>
    /// Tests that a failed launch maps to the launch-failed error code.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_WhenLaunchFails_ShouldFailAsync()
    {
        // Arrange
        var manager = new Mock<IGameProfileManager>();
        manager.Setup(m => m.GetProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "p1", Name = "ZH" }));
        var facade = new Mock<IProfileLauncherFacade>();
        facade.Setup(f => f.LaunchProfileAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateFailure("No game"));
        var service = new OnlineLaunchService(manager.Object, facade.Object, Mock.Of<ILogger<OnlineLaunchService>>());

        // Act
        var result = await service.PlayAsync("p1", "Net");

        // Assert
        Assert.False(result.Success);
        Assert.Contains(OnlineConstants.ErrorLaunchFailed, result.Errors);
    }

    /// <summary>
    /// Tests that a successful launch returns the play outcome.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_OnSuccess_ShouldReturnOutcomeAsync()
    {
        // Arrange
        var manager = new Mock<IGameProfileManager>();
        manager.Setup(m => m.GetProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "p1", Name = "ZH" }));
        var facade = new Mock<IProfileLauncherFacade>();
        facade.Setup(f => f.LaunchProfileAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(
                new GameLaunchInfo { LaunchId = "l1", ProfileId = "p1", WorkspaceId = "w1", ProcessInfo = new GameProcessInfo() }));
        var service = new OnlineLaunchService(manager.Object, facade.Object, Mock.Of<ILogger<OnlineLaunchService>>());

        // Act
        var result = await service.PlayAsync("p1", "Net");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("p1", result.Data.ProfileId);
        Assert.Equal("ZH", result.Data.ProfileName);
        facade.Verify(f => f.LaunchProfileAsync("p1", false, It.IsAny<CancellationToken>()), Times.Once);
    }
}
