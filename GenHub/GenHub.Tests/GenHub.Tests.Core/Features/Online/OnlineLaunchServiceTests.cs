using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameSettings;
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
    /// Tests that a blank profile id fails without touching launch services.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_WithBlankProfileId_ShouldFailAsync()
    {
        // Arrange
        var manager = new Mock<IGameProfileManager>(MockBehavior.Strict);
        var facade = new Mock<IProfileLauncherFacade>(MockBehavior.Strict);
        var settings = new Mock<IGameSettingsService>(MockBehavior.Strict);
        var service = new OnlineLaunchService(manager.Object, facade.Object, settings.Object, Mock.Of<ILogger<OnlineLaunchService>>());

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
            Mock.Of<IGameSettingsService>(),
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
        facade.Setup(f => f.LaunchProfileAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateFailure("No game"));
        var service = new OnlineLaunchService(
            manager.Object,
            facade.Object,
            Mock.Of<IGameSettingsService>(),
            Mock.Of<ILogger<OnlineLaunchService>>());

        // Act
        var result = await service.PlayAsync("p1", "Net");

        // Assert
        Assert.False(result.Success);
        Assert.Contains(OnlineConstants.ErrorLaunchFailed, result.Errors);
    }

    /// <summary>
    /// Tests that a successful launch returns the play outcome and preselects the lobby IP.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_OnSuccess_ShouldPreselectLobbyIpAsync()
    {
        // Arrange
        var manager = new Mock<IGameProfileManager>();
        manager.Setup(m => m.GetProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "p1", Name = "ZH" }));
        var facade = new Mock<IProfileLauncherFacade>();
        facade.Setup(f => f.LaunchProfileAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(
                new GameLaunchInfo { LaunchId = "l1", ProfileId = "p1", WorkspaceId = "w1", ProcessInfo = new GameProcessInfo() }));
        var settings = new Mock<IGameSettingsService>();
        settings.Setup(s => s.LoadOptionsAsync(It.IsAny<GameType>()))
            .ReturnsAsync(OperationResult<IniOptions>.CreateSuccess(new IniOptions()));
        settings.Setup(s => s.SaveOptionsAsync(It.IsAny<GameType>(), It.IsAny<IniOptions>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        var service = new OnlineLaunchService(manager.Object, facade.Object, settings.Object, Mock.Of<ILogger<OnlineLaunchService>>());

        // Act
        var result = await service.PlayAsync("p1", "Net", "10.42.0.7");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("p1", result.Data.ProfileId);
        Assert.Equal("ZH", result.Data.ProfileName);
        facade.Verify(f => f.LaunchProfileAsync("p1", false, It.IsAny<CancellationToken>(), "10.42.0.7"), Times.Once);
        settings.Verify(
            s => s.SaveOptionsAsync(
                GameType.ZeroHour,
                It.Is<IniOptions>(o =>
                    o.Network.GameSpyIPAddress == "10.42.0.7" && o.Network.IPAddress == "10.42.0.7")),
            Times.Once);
    }

    /// <summary>
    /// Tests that playing a profile with a Generals game client saves options for Generals.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_WithGeneralsClient_ShouldSaveOptionsForGeneralsAsync()
    {
        // Arrange
        var profile = new GameProfile
        {
            Id = "p2",
            Name = "Generals Profile",
            GameClient = new GameClient { Id = "c1", GameType = GameType.Generals, Version = "1.08" },
        };
        var manager = new Mock<IGameProfileManager>();
        manager.Setup(m => m.GetProfileAsync("p2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        var facade = new Mock<IProfileLauncherFacade>();
        facade.Setup(f => f.LaunchProfileAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(
                new GameLaunchInfo { LaunchId = "l2", ProfileId = "p2", WorkspaceId = "w1", ProcessInfo = new GameProcessInfo() }));
        var settings = new Mock<IGameSettingsService>();
        settings.Setup(s => s.LoadOptionsAsync(GameType.Generals))
            .ReturnsAsync(OperationResult<IniOptions>.CreateSuccess(new IniOptions()));
        settings.Setup(s => s.SaveOptionsAsync(GameType.Generals, It.IsAny<IniOptions>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var service = new OnlineLaunchService(manager.Object, facade.Object, settings.Object, Mock.Of<ILogger<OnlineLaunchService>>());

        // Act
        var result = await service.PlayAsync("p2", "Net", "10.42.0.8");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("p2", result.Data.ProfileId);
        facade.Verify(f => f.LaunchProfileAsync("p2", false, It.IsAny<CancellationToken>(), "10.42.0.8"), Times.Once);
        settings.Verify(
            s => s.SaveOptionsAsync(
                GameType.Generals,
                It.Is<IniOptions>(o =>
                    o.Network.GameSpyIPAddress == "10.42.0.8" && o.Network.IPAddress == "10.42.0.8")),
            Times.Once);
    }

    /// <summary>
    /// Tests that an invalid overlay IP never touches the game settings.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_WithInvalidOverlayIp_ShouldNotWriteSettingsAsync()
    {
        // Arrange
        var manager = new Mock<IGameProfileManager>();
        manager.Setup(m => m.GetProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "p1", Name = "ZH" }));
        var facade = new Mock<IProfileLauncherFacade>();
        facade.Setup(f => f.LaunchProfileAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(
                new GameLaunchInfo { LaunchId = "l1", ProfileId = "p1", WorkspaceId = "w1", ProcessInfo = new GameProcessInfo() }));
        var settings = new Mock<IGameSettingsService>(MockBehavior.Strict);
        var service = new OnlineLaunchService(manager.Object, facade.Object, settings.Object, Mock.Of<ILogger<OnlineLaunchService>>());

        // Act
        var result = await service.PlayAsync("p1", "Net", "not-an-ip");

        // Assert
        Assert.True(result.Success);
        settings.Verify(s => s.SaveOptionsAsync(It.IsAny<GameType>(), It.IsAny<IniOptions>()), Times.Never);
        facade.Verify(f => f.LaunchProfileAsync("p1", false, It.IsAny<CancellationToken>(), null), Times.Once);
    }

    /// <summary>
    /// Tests that stopping a profile delegates to the launcher facade.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StopAsync_OnSuccess_ShouldSucceedAsync()
    {
        // Arrange
        var facade = new Mock<IProfileLauncherFacade>();
        facade.Setup(f => f.StopProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<bool>.CreateSuccess(true));
        var service = new OnlineLaunchService(
            Mock.Of<IGameProfileManager>(),
            facade.Object,
            Mock.Of<IGameSettingsService>(),
            Mock.Of<ILogger<OnlineLaunchService>>());

        // Act
        var result = await service.StopAsync("p1");

        // Assert
        Assert.True(result.Success);
        facade.Verify(f => f.StopProfileAsync("p1", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that a failed stop maps to the stop-failed error code.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StopAsync_WhenFacadeFails_ShouldFailAsync()
    {
        // Arrange
        var facade = new Mock<IProfileLauncherFacade>();
        facade.Setup(f => f.StopProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<bool>.CreateFailure("No process"));
        var service = new OnlineLaunchService(
            Mock.Of<IGameProfileManager>(),
            facade.Object,
            Mock.Of<IGameSettingsService>(),
            Mock.Of<ILogger<OnlineLaunchService>>());

        // Act
        var result = await service.StopAsync("p1");

        // Assert
        Assert.False(result.Success);
        Assert.Contains(OnlineConstants.ErrorStopFailed, result.Errors);
    }

    /// <summary>
    /// Tests that a blank profile id fails without touching the facade.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StopAsync_WithBlankProfileId_ShouldFailAsync()
    {
        // Arrange
        var facade = new Mock<IProfileLauncherFacade>(MockBehavior.Strict);
        var service = new OnlineLaunchService(
            Mock.Of<IGameProfileManager>(),
            facade.Object,
            Mock.Of<IGameSettingsService>(),
            Mock.Of<ILogger<OnlineLaunchService>>());

        // Act
        var result = await service.StopAsync("  ");

        // Assert
        Assert.False(result.Success);
        Assert.Contains(OnlineConstants.ErrorProfileMissing, result.Errors);
    }

    /// <summary>
    /// Tests that a settings load failure skips preselection but never blocks the launch.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_WhenSettingsLoadFails_ShouldSkipPreselectionAndLaunchAsync()
    {
        // Arrange
        var manager = new Mock<IGameProfileManager>();
        manager.Setup(m => m.GetProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "p1", Name = "ZH" }));
        var facade = new Mock<IProfileLauncherFacade>();
        facade.Setup(f => f.LaunchProfileAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(
                new GameLaunchInfo { LaunchId = "l1", ProfileId = "p1", WorkspaceId = "w1", ProcessInfo = new GameProcessInfo() }));
        var settings = new Mock<IGameSettingsService>();
        settings.Setup(s => s.LoadOptionsAsync(It.IsAny<GameType>()))
            .ReturnsAsync(OperationResult<IniOptions>.CreateFailure("locked"));
        var service = new OnlineLaunchService(manager.Object, facade.Object, settings.Object, Mock.Of<ILogger<OnlineLaunchService>>());

        // Act
        var result = await service.PlayAsync("p1", "Net", "10.42.0.7");

        // Assert
        Assert.True(result.Success);
        facade.Verify(f => f.LaunchProfileAsync("p1", false, It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
        settings.Verify(s => s.SaveOptionsAsync(It.IsAny<GameType>(), It.IsAny<IniOptions>()), Times.Never);
    }

    /// <summary>
    /// Tests that a nickname is synced into the launched game's Network.ini on play.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_WithNickname_ShouldSyncNicknameAsync()
    {
        // Arrange
        var manager = new Mock<IGameProfileManager>();
        manager.Setup(m => m.GetProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "p1", Name = "ZH" }));
        var facade = new Mock<IProfileLauncherFacade>();
        facade.Setup(f => f.LaunchProfileAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(
                new GameLaunchInfo { LaunchId = "l1", ProfileId = "p1", WorkspaceId = "w1", ProcessInfo = new GameProcessInfo() }));
        var nickname = new Mock<ILanNicknameService>();
        nickname.Setup(n => n.SaveNicknameAsync(It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        var service = new OnlineLaunchService(
            manager.Object,
            facade.Object,
            Mock.Of<IGameSettingsService>(),
            Mock.Of<ILogger<OnlineLaunchService>>(),
            nickname.Object);

        // Act
        var result = await service.PlayAsync("p1", "Net", string.Empty, "Commander");

        // Assert
        Assert.True(result.Success);
        nickname.Verify(n => n.SaveNicknameAsync(GameType.ZeroHour, "Commander", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that a blank nickname never touches Network.ini.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_WithBlankNickname_ShouldNotSyncNicknameAsync()
    {
        // Arrange
        var manager = new Mock<IGameProfileManager>();
        manager.Setup(m => m.GetProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "p1", Name = "ZH" }));
        var facade = new Mock<IProfileLauncherFacade>();
        facade.Setup(f => f.LaunchProfileAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(
                new GameLaunchInfo { LaunchId = "l1", ProfileId = "p1", WorkspaceId = "w1", ProcessInfo = new GameProcessInfo() }));
        var nickname = new Mock<ILanNicknameService>(MockBehavior.Strict);
        var service = new OnlineLaunchService(
            manager.Object,
            facade.Object,
            Mock.Of<IGameSettingsService>(),
            Mock.Of<ILogger<OnlineLaunchService>>(),
            nickname.Object);

        // Act
        var result = await service.PlayAsync("p1", "Net", string.Empty, "   ");

        // Assert
        Assert.True(result.Success);
        nickname.Verify(n => n.SaveNicknameAsync(It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Tests that a nickname write failure never blocks the launch.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_WhenNicknameSaveFails_ShouldStillLaunchAsync()
    {
        // Arrange
        var manager = new Mock<IGameProfileManager>();
        manager.Setup(m => m.GetProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "p1", Name = "ZH" }));
        var facade = new Mock<IProfileLauncherFacade>();
        facade.Setup(f => f.LaunchProfileAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(
                new GameLaunchInfo { LaunchId = "l1", ProfileId = "p1", WorkspaceId = "w1", ProcessInfo = new GameProcessInfo() }));
        var nickname = new Mock<ILanNicknameService>();
        nickname.Setup(n => n.SaveNicknameAsync(It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("locked"));
        var service = new OnlineLaunchService(
            manager.Object,
            facade.Object,
            Mock.Of<IGameSettingsService>(),
            Mock.Of<ILogger<OnlineLaunchService>>(),
            nickname.Object);

        // Act
        var result = await service.PlayAsync("p1", "Net", string.Empty, "Commander");

        // Assert
        Assert.True(result.Success);
        facade.Verify(f => f.LaunchProfileAsync("p1", false, It.IsAny<CancellationToken>(), null), Times.Once);
    }

    /// <summary>
    /// Tests that a settings write failure never blocks the launch.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_WhenSettingsWriteFails_ShouldStillLaunchAsync()
    {
        // Arrange
        var manager = new Mock<IGameProfileManager>();
        manager.Setup(m => m.GetProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "p1", Name = "ZH" }));
        var facade = new Mock<IProfileLauncherFacade>();
        facade.Setup(f => f.LaunchProfileAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync(ProfileOperationResult<GameLaunchInfo>.CreateSuccess(
                new GameLaunchInfo { LaunchId = "l1", ProfileId = "p1", WorkspaceId = "w1", ProcessInfo = new GameProcessInfo() }));
        var settings = new Mock<IGameSettingsService>();
        settings.Setup(s => s.LoadOptionsAsync(It.IsAny<GameType>()))
            .ReturnsAsync(OperationResult<IniOptions>.CreateSuccess(new IniOptions()));
        settings.Setup(s => s.SaveOptionsAsync(It.IsAny<GameType>(), It.IsAny<IniOptions>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("locked"));
        var service = new OnlineLaunchService(manager.Object, facade.Object, settings.Object, Mock.Of<ILogger<OnlineLaunchService>>());

        // Act
        var result = await service.PlayAsync("p1", "Net", "10.42.0.7");

        // Assert
        Assert.True(result.Success);
        facade.Verify(f => f.LaunchProfileAsync("p1", false, It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
    }
}
