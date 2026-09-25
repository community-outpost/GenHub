using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Interfaces.Tools.Checksum;
using GenHub.Core.Messages;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using GenHub.Features.Online.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.ObjectModel;

namespace GenHub.Tests.Core.Features.Online;

/// <summary>
/// Unit tests for <see cref="OnlineViewModel"/>.
/// </summary>
public class OnlineViewModelTests
{
    /// <summary>
    /// Tests that refreshing populates the directory.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RefreshNetworksAsync_OnSuccess_ShouldPopulateAsync()
    {
        // Arrange
        var network = new Mock<IOnlineNetworkService>();
        network.Setup(n => n.GetNetworksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<OnlineNetworkSummary>>.CreateSuccess(
            [
                new OnlineNetworkSummary { Id = "net-1", Name = "Lobby" },
            ]));
        var vm = CreateViewModel(network.Object);

        // Act
        await vm.RefreshNetworksAsync();

        // Assert
        Assert.Single(vm.Networks);
        Assert.False(vm.DirectoryFailed);
        Assert.False(vm.DirectoryEmpty);
    }

    /// <summary>
    /// Tests that concurrent refreshes on one view model both complete safely through the refresh lock.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RefreshNetworksAsync_TwiceInARow_ShouldBothCompleteAsync()
    {
        // Arrange: the gate holds the first refresh inside the lock so the
        // second one genuinely contends on it instead of running after.
        var gate = new TaskCompletionSource<OperationResult<IReadOnlyList<OnlineNetworkSummary>>>();
        var network = new Mock<IOnlineNetworkService>();
        network.Setup(n => n.GetNetworksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(gate.Task);
        var vm = CreateViewModel(network.Object);

        // Act
        var first = vm.RefreshNetworksAsync();
        var second = vm.RefreshNetworksAsync();
        gate.SetResult(OperationResult<IReadOnlyList<OnlineNetworkSummary>>.CreateSuccess(
            [
                new OnlineNetworkSummary { Id = "net-1", Name = "Lobby" },
            ]));
        await Task.WhenAll(first, second);

        // Assert
        Assert.Single(vm.Networks);
        Assert.False(vm.DirectoryFailed);
    }

    /// <summary>
    /// Tests that a failed refresh flags the directory and toasts.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RefreshNetworksAsync_OnFailure_ShouldFlagAndToastAsync()
    {
        // Arrange
        var network = new Mock<IOnlineNetworkService>();
        network.Setup(n => n.GetNetworksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<OnlineNetworkSummary>>.CreateFailure(
                OnlineConstants.ErrorServiceUnavailable));
        var notifications = new Mock<INotificationService>();
        var vm = CreateViewModel(network.Object, notifications.Object);

        // Act
        await vm.RefreshNetworksAsync();

        // Assert
        Assert.True(vm.DirectoryFailed);
        notifications.Verify(n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// Tests that creating a public network without a password calls the service.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateNetworkAsync_PublicWithoutPassword_ShouldCallServiceAsync()
    {
        // Arrange
        var join = new OnlineJoinResult
        {
            NetworkId = "net-1",
            Grant = "grant-token",
            OverlayIp = "10.42.0.7",
        };
        var network = new Mock<IOnlineNetworkService>();
        network.Setup(n => n.CreateNetworkAsync(It.IsAny<OnlineCreateNetworkRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<OnlineJoinResult>.CreateSuccess(join));
        network.SetupGet(n => n.AdapterState).Returns(OnlineAdapterState.Up);
        var profile = new GameProfile { Id = "profile-1", Name = "Zero Hour" };
        var profiles = new Mock<IGameProfileManager>();
        profiles.Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));
        var vm = CreateViewModel(network.Object, profiles: profiles.Object);
        vm.CreateName = "Lobby";
        vm.SelectedCreateProfile = profile;
        vm.CreateIsPublic = true;

        // Act
        await vm.CreateNetworkAsync();

        // Assert
        Assert.True(vm.IsJoined);
        Assert.Equal("Lobby", vm.CurrentNetworkName);
        network.Verify(
            n => n.CreateNetworkAsync(It.Is<OnlineCreateNetworkRequest>(r => r.Password == string.Empty), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that creating without a game profile toasts and never calls the service.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateNetworkAsync_WithoutProfile_ShouldToastProfileRequiredAsync()
    {
        // Arrange
        var network = new Mock<IOnlineNetworkService>(MockBehavior.Strict);
        var notifications = new Mock<INotificationService>();
        var vm = CreateViewModel(network.Object, notifications.Object);
        vm.CreateName = "Lobby";

        // Act
        await vm.CreateNetworkAsync();

        // Assert
        Assert.False(vm.IsJoined);
        network.Verify(
            n => n.CreateNetworkAsync(It.IsAny<OnlineCreateNetworkRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), "Online.Error.ProfileRequired", It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that saving host settings without a game profile toasts and never calls the service.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveNetworkAsync_WithoutProfile_ShouldToastProfileRequiredAsync()
    {
        // Arrange
        var network = new Mock<IOnlineNetworkService>(MockBehavior.Strict);
        var notifications = new Mock<INotificationService>();
        var vm = CreateViewModel(network.Object, notifications.Object);
        vm.IsCurrentUserHost = true;

        // Act
        await vm.SaveNetworkAsync();

        // Assert
        network.Verify(
            n => n.UpdateNetworkAsync(It.IsAny<string>(), It.IsAny<OnlineExpectedProfile>(), It.IsAny<CancellationToken>()),
            Times.Never);
        notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), "Online.Error.ProfileRequired", It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that a successful play marks the game running and stop clears it.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_OnSuccess_ShouldSetGameRunningAsync()
    {
        // Arrange
        var launch = new Mock<IOnlineLaunchService>();
        launch.Setup(l => l.PlayAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<OnlinePlayResult>.CreateSuccess(new OnlinePlayResult("profile-1", "Zero Hour", "Lobby")));
        launch.Setup(l => l.StopAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        var network = new Mock<IOnlineNetworkService>();
        network.SetupGet(n => n.AdapterState).Returns(OnlineAdapterState.Up);
        var notifications = new Mock<INotificationService>();
        var vm = CreateViewModel(network.Object, notifications.Object, launch.Object);
        vm.IsJoined = true;
        vm.SelectedPlayProfile = new GameProfile { Id = "profile-1", Name = "Zero Hour" };

        // Act
        await vm.PlayAsync();

        // Assert
        Assert.True(vm.IsGameRunning);
        Assert.False(vm.PlayCommand.CanExecute(null));
        Assert.True(vm.StopCommand.CanExecute(null));

        // Act: stopping clears the running state.
        await vm.StopAsync();

        // Assert
        Assert.False(vm.IsGameRunning);
        launch.Verify(l => l.StopAsync("profile-1", It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.AtLeast(2));
    }

    /// <summary>
    /// Tests that playing while the adapter is down toasts the tunnel outage and never launches.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_WhenAdapterDown_ShouldToastAndNotLaunchAsync()
    {
        // Arrange: strict launch service throws on any call, proving no launch happens.
        var launch = new Mock<IOnlineLaunchService>(MockBehavior.Strict);
        var network = new Mock<IOnlineNetworkService>();
        network.SetupGet(n => n.AdapterState).Returns(OnlineAdapterState.Down);
        network.SetupGet(n => n.AdapterError).Returns(OnlineConstants.AdapterElevationRequired);
        var notifications = new Mock<INotificationService>();
        var vm = CreateViewModel(network.Object, notifications.Object, launch.Object);
        vm.IsJoined = true;
        vm.SelectedPlayProfile = new GameProfile { Id = "profile-1", Name = "Zero Hour" };

        // Act
        await vm.PlayAsync();

        // Assert
        Assert.False(vm.IsGameRunning);
        notifications.Verify(
            n => n.ShowError("Online.Play.NoTunnelTitle", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that joining with an elevation adapter error shows elevation guidance.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_WhenAdapterErrorIsElevation_ShouldShowElevationToastAsync()
    {
        // Arrange
        var join = new OnlineJoinResult
        {
            NetworkId = "net-1",
            Grant = "grant-token",
            OverlayIp = "10.42.0.7",
        };
        var network = new Mock<IOnlineNetworkService>();
        network.Setup(n => n.JoinNetworkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<OnlineJoinResult>.CreateSuccess(join));
        network.SetupGet(n => n.AdapterState).Returns(OnlineAdapterState.Down);
        network.SetupGet(n => n.AdapterError).Returns(OnlineConstants.AdapterElevationRequired);
        var notifications = new Mock<INotificationService>();
        var vm = CreateViewModel(network.Object, notifications.Object);
        vm.Networks = new ObservableCollection<OnlineNetworkSummary>(
        [
            new OnlineNetworkSummary { Id = "net-1", Name = "Lobby" },
        ]);
        vm.SelectedNetwork = vm.Networks[0];

        // Act
        await vm.JoinNetworkAsync();

        // Assert
        Assert.True(vm.IsJoined);
        Assert.True(vm.IsLobbyOnly);
        notifications.Verify(
            n => n.ShowWarning("Online.Adapter.ElevationTitle", "Online.Adapter.ElevationMessage", It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that selecting another network while joined never reloads detail.
    /// </summary>
    [Fact]
    public void SelectNetwork_WhenJoined_ShouldNotLoadDetail()
    {
        // Arrange: strict service throws on any detail fetch.
        var network = new Mock<IOnlineNetworkService>(MockBehavior.Strict);
        var vm = CreateViewModel(network.Object);
        vm.IsJoined = true;

        // Act
        vm.SelectedNetwork = new OnlineNetworkSummary { Id = "net-2", Name = "Other" };

        // Assert
        Assert.Null(vm.SelectedDetail);
        Assert.False(vm.IsDetailVisible);
    }

    /// <summary>
    /// Tests that joins prefer relay mode to mask public IP addresses by default.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_ShouldPreferRelayMaskingAsync()
    {
        // Arrange
        var join = new OnlineJoinResult
        {
            NetworkId = "net-1",
            Grant = "grant-token",
            OverlayIp = "10.42.0.7",
        };
        var network = new Mock<IOnlineNetworkService>();
        network.Setup(n => n.JoinNetworkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<OnlineJoinResult>.CreateSuccess(join));
        network.SetupGet(n => n.AdapterState).Returns(OnlineAdapterState.Up);
        var vm = CreateViewModel(network.Object);
        vm.Networks = new ObservableCollection<OnlineNetworkSummary>(
        [
            new OnlineNetworkSummary { Id = "net-1", Name = "Lobby" },
        ]);
        vm.SelectedNetwork = vm.Networks[0];

        // Act
        await vm.JoinNetworkAsync();

        // Assert
        network.Verify(
            n => n.JoinNetworkAsync(
                "net-1",
                It.IsAny<string>(),
                true,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that joining a network missing from the directory still shows its name.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_WhenDirectoryLacksNetwork_ShouldUseSelectedNameAsync()
    {
        // Arrange
        var join = new OnlineJoinResult
        {
            NetworkId = "net-1",
            Grant = "grant-token",
            OverlayIp = "10.42.0.7",
        };
        var network = new Mock<IOnlineNetworkService>();
        network.Setup(n => n.JoinNetworkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<OnlineJoinResult>.CreateSuccess(join));
        network.SetupGet(n => n.AdapterState).Returns(OnlineAdapterState.Up);
        var vm = CreateViewModel(network.Object);
        vm.Networks = [];
        vm.SelectedNetwork = new OnlineNetworkSummary { Id = "net-1", Name = "Lobby" };

        // Act
        await vm.JoinNetworkAsync();

        // Assert
        Assert.True(vm.IsJoined);
        Assert.Equal("Lobby", vm.CurrentNetworkName);
    }

    /// <summary>
    /// Tests that a successful join with the adapter down warns about tunneling.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_WithAdapterDown_ShouldWarnAboutTunnelingAsync()
    {
        // Arrange
        var join = new OnlineJoinResult
        {
            NetworkId = "net-1",
            Grant = "grant-token",
            OverlayIp = "10.42.0.7",
        };
        var network = new Mock<IOnlineNetworkService>();
        network.Setup(n => n.JoinNetworkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<OnlineJoinResult>.CreateSuccess(join));
        network.SetupGet(n => n.AdapterState).Returns(OnlineAdapterState.Down);
        var notifications = new Mock<INotificationService>();
        var vm = CreateViewModel(network.Object, notifications.Object);
        vm.Networks = new ObservableCollection<OnlineNetworkSummary>(
        [
            new OnlineNetworkSummary { Id = "net-1", Name = "Lobby" },
        ]);
        vm.SelectedNetwork = vm.Networks[0];

        // Act
        await vm.JoinNetworkAsync();

        // Assert
        Assert.True(vm.IsJoined);
        notifications.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        notifications.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that a join while the overlay selection is pending shows info, not a warning.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_WithPendingOverlay_ShouldShowInfoAsync()
    {
        // Arrange
        var pendingConfig = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("""{"v":0,"overlay":"pending-selection"}"""));
        var join = new OnlineJoinResult
        {
            NetworkId = "net-1",
            Grant = "grant-token",
            OverlayIp = "10.42.0.7",
            AdapterConfig = pendingConfig,
        };
        var network = new Mock<IOnlineNetworkService>();
        network.Setup(n => n.JoinNetworkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<OnlineJoinResult>.CreateSuccess(join));
        network.SetupGet(n => n.AdapterState).Returns(OnlineAdapterState.Down);
        network.SetupGet(n => n.CurrentJoin).Returns(join);
        var notifications = new Mock<INotificationService>();
        var vm = CreateViewModel(network.Object, notifications.Object);
        vm.Networks = new ObservableCollection<OnlineNetworkSummary>(
        [
            new OnlineNetworkSummary { Id = "net-1", Name = "Lobby" },
        ]);
        vm.SelectedNetwork = vm.Networks[0];

        // Act
        await vm.JoinNetworkAsync();

        // Assert
        Assert.True(vm.IsJoined);
        notifications.Verify(
            n => n.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        notifications.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Tests that a second join while one is in flight issues no extra request.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_WhenJoinInFlight_ShouldCallServiceOnceAsync()
    {
        // Arrange
        var join = new OnlineJoinResult
        {
            NetworkId = "net-1",
            Grant = "grant-token",
            OverlayIp = "10.42.0.7",
        };
        var gate = new TaskCompletionSource<OperationResult<OnlineJoinResult>>();
        var network = new Mock<IOnlineNetworkService>();
        network.Setup(n => n.JoinNetworkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(gate.Task);
        network.SetupGet(n => n.AdapterState).Returns(OnlineAdapterState.Up);
        var vm = CreateViewModel(network.Object);
        vm.Networks = new ObservableCollection<OnlineNetworkSummary>(
        [
            new OnlineNetworkSummary { Id = "net-1", Name = "Lobby" },
        ]);
        vm.SelectedNetwork = vm.Networks[0];

        // Act
        var first = vm.JoinNetworkAsync();
        var second = vm.JoinNetworkAsync();
        gate.SetResult(OperationResult<OnlineJoinResult>.CreateSuccess(join));
        await Task.WhenAll(first, second);

        // Assert
        network.Verify(
            n => n.JoinNetworkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.True(vm.IsJoined);
    }

    /// <summary>
    /// Tests that joining while already joined issues no extra request.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_WhenJoined_ShouldNotCallServiceAsync()
    {
        // Arrange
        var network = new Mock<IOnlineNetworkService>(MockBehavior.Strict);
        var vm = CreateViewModel(network.Object);
        vm.Networks = new ObservableCollection<OnlineNetworkSummary>(
        [
            new OnlineNetworkSummary { Id = "net-1", Name = "Lobby" },
        ]);
        vm.SelectedNetwork = vm.Networks[0];
        vm.IsJoined = true;

        // Act
        await vm.JoinNetworkAsync();

        // Assert
        network.Verify(
            n => n.JoinNetworkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Tests that creating while joined toasts and issues no request.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateNetworkAsync_WhenJoined_ShouldToastAndNotCallServiceAsync()
    {
        // Arrange
        var network = new Mock<IOnlineNetworkService>(MockBehavior.Strict);
        var notifications = new Mock<INotificationService>();
        var vm = CreateViewModel(network.Object, notifications.Object);
        vm.IsJoined = true;
        vm.CreateName = "Lobby";

        // Act
        await vm.CreateNetworkAsync();

        // Assert
        network.Verify(
            n => n.CreateNetworkAsync(It.IsAny<OnlineCreateNetworkRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), "Online.Error.AlreadyJoined", It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that play without a matched profile warns and never launches.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_WithoutMatchedProfile_ShouldWarnAsync()
    {
        // Arrange
        var launch = new Mock<IOnlineLaunchService>(MockBehavior.Strict);
        var notifications = new Mock<INotificationService>();
        var vm = CreateViewModel(launchService: launch.Object, notifications: notifications.Object);

        // Act
        await vm.PlayAsync();

        // Assert
        notifications.Verify(n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
        notifications.Verify(n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Tests that saving host settings publishes the picked profile setup.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveNetworkAsync_WithPickedProfile_ShouldPublishExpectedAsync()
    {
        // Arrange
        var profile = new GameProfile { Id = "profile-1", Name = "Zero Hour" };
        var profiles = new Mock<IGameProfileManager>();
        profiles.Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));
        var network = new Mock<IOnlineNetworkService>();
        network.Setup(n => n.UpdateNetworkAsync(
                It.IsAny<string>(),
                It.IsAny<OnlineExpectedProfile>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        var vm = CreateViewModel(network.Object, profiles: profiles.Object);
        vm.IsCurrentUserHost = true;
        vm.SelectedHostProfile = profile;

        // Act
        await vm.SaveNetworkAsync();

        // Assert
        network.Verify(
            n => n.UpdateNetworkAsync(
                It.IsAny<string>(),
                It.Is<OnlineExpectedProfile>(e =>
                    e.ExpectedProfileId == "profile-1" &&
                    e.ExpectedProfileName == "Zero Hour" &&
                    e.ExpectedProfileFingerprint.Length > 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal("profile-1", vm.ExpectedProfileId);
    }

    /// <summary>
    /// Tests that declining the ban confirmation never calls the service.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task BanMemberAsync_WhenDeclined_ShouldNotCallServiceAsync()
    {
        // Arrange
        var network = new Mock<IOnlineNetworkService>(MockBehavior.Strict);
        var dialogs = new Mock<IDialogService>();
        dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);
        var vm = CreateViewModel(network.Object, dialogs: dialogs.Object);
        vm.SelectedMember = new OnlineMember { DisplayName = "Guest", OverlayIp = "10.42.0.3" };

        // Act
        await vm.BanMemberAsync();

        // Assert
        Assert.NotNull(vm.SelectedMember);
    }

    /// <summary>
    /// Tests that an older edge demanding a password maps to the password message, not a connectivity error.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateNetworkAsync_WithPasswordRequired_ShouldToastPasswordMessageAsync()
    {
        // Arrange
        var network = new Mock<IOnlineNetworkService>();
        network.Setup(n => n.CreateNetworkAsync(It.IsAny<OnlineCreateNetworkRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<OnlineJoinResult>.CreateFailure(OnlineConstants.ErrorPasswordRequired));
        var profiles = new Mock<IGameProfileManager>();
        profiles.Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));
        var profile = new GameProfile { Id = "profile-1", Name = "Zero Hour" };
        var notifications = new Mock<INotificationService>();
        var vm = CreateViewModel(network.Object, notifications.Object, profiles: profiles.Object);
        vm.CreateName = "Lobby";
        vm.SelectedCreateProfile = profile;
        vm.CreateIsPublic = true;

        // Act
        await vm.CreateNetworkAsync();

        // Assert
        Assert.False(vm.IsJoined);
        notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), "Online.Error.PasswordRequired", It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that selecting a network with non-matching profile setup marks it as Mismatch.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SelectNetwork_WithDifferentProfileSetup_ShouldShowMismatchDetailAsync()
    {
        // Arrange
        var profile = ProfileWithClient("profile-1", "Zero Hour", GameType.ZeroHour, "zerohour-client", "1.04", "mod-a", "mod-x");
        var vm = CreateViewModelWithDetail(profile, out _);

        // Act
        vm.SelectedNetwork = new OnlineNetworkSummary { Id = "net-1", Name = "Lobby" };
        await WaitForAsync(() => vm.SelectedPlayProfile is not null);

        // Assert
        Assert.Equal(OnlineProfileMatch.Mismatch, vm.ProfileMatchState);
        Assert.Equal("Online.Detail.MatchDetailMismatch", vm.ProfileMatchDetail);
    }

    /// <summary>
    /// Tests that picking another launch profile re-matches and re-advertises it.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SelectedPlayProfile_WhenChangedAfterDetail_ShouldReAdvertiseAsync()
    {
        // Arrange
        var zeroHour = ProfileWithClient("profile-1", "Zero Hour", GameType.ZeroHour, "zerohour-client", "1.04", "mod-a", "mod-x");
        var generals = ProfileWithClient("profile-2", "Generals", GameType.Generals, "generals-client", "1.08", "mod-a");
        var advertised = new List<string>();
        var vm = CreateViewModelWithDetail(zeroHour, out var network, generals);
        network.Setup(n => n.SetLocalProfileAdvertisement(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Callback<string, string, string, bool>((fingerprint, _, _, _) => advertised.Add(fingerprint));
        vm.SelectedNetwork = new OnlineNetworkSummary { Id = "net-1", Name = "Lobby" };
        await WaitForAsync(() => vm.SelectedPlayProfile is not null);
        advertised.Clear();

        // Act: the user overrides the auto-match from the launch-profile picker.
        vm.SelectedPlayProfile = generals;
        await WaitForAsync(() => advertised.Count > 0);

        // Assert
        Assert.Equal(OnlineProfileMatch.Mismatch, vm.ProfileMatchState);
        Assert.Equal("Online.Detail.MatchDetailMismatch", vm.ProfileMatchDetail);
        Assert.StartsWith("opf3|Generals|1.08|generals-client|", advertised[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// Tests that initializing loads the persisted nickname.
    /// </summary>
    [Fact]
    public void Initialize_ShouldLoadPersistedNickname()
    {
        // Arrange
        var settings = new Mock<IUserSettingsService>();
        settings.Setup(s => s.Get()).Returns(new UserSettings { OnlineNickname = "Ace" });
        settings.Setup(s => s.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>())).ReturnsAsync(true);
        var vm = CreateViewModel(userSettings: settings.Object);

        // Act
        vm.Initialize();

        // Assert
        Assert.Equal("Ace", vm.Nickname);
    }

    /// <summary>
    /// Tests that an overlong nickname is clamped to the game's limit and persisted clamped.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Nickname_SetOverlong_ShouldClampToGameLimitAsync()
    {
        // Arrange
        string? saved = null;
        var settings = new Mock<IUserSettingsService>();
        settings.Setup(s => s.Get()).Returns(new UserSettings());
        settings.Setup(s => s.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()))
            .Callback<Func<UserSettings, bool>>(apply =>
            {
                var copy = new UserSettings();
                if (apply(copy))
                {
                    saved = copy.OnlineNickname;
                }
            })
            .ReturnsAsync(true);
        var vm = CreateViewModel(userSettings: settings.Object);
        vm.NicknameDebounceMs = 10;
        vm.Initialize();

        // Act
        vm.Nickname = new string('C', OnlineConstants.MaxNicknameLength + 5);

        // Assert
        Assert.Equal(new string('C', OnlineConstants.MaxNicknameLength), vm.Nickname);
        await WaitForAsync(() => saved is not null);
        Assert.Equal(new string('C', OnlineConstants.MaxNicknameLength), saved);
    }

    /// <summary>
    /// Tests that rapid keystrokes to Nickname are debounced so only the final value is saved.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Nickname_RapidKeystrokes_ShouldDebouncePersistenceAsync()
    {
        // Arrange
        var savedList = new List<string?>();
        var settings = new Mock<IUserSettingsService>();
        settings.Setup(s => s.Get()).Returns(new UserSettings());
        settings.Setup(s => s.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()))
            .Callback<Func<UserSettings, bool>>(apply =>
            {
                var copy = new UserSettings();
                if (apply(copy))
                {
                    savedList.Add(copy.OnlineNickname);
                }
            })
            .ReturnsAsync(true);
        var vm = CreateViewModel(userSettings: settings.Object);
        vm.NicknameDebounceMs = 50;
        vm.Initialize();

        // Act - simulate typing "A", "Ac", "Ace" rapidly
        vm.Nickname = "A";
        vm.Nickname = "Ac";
        vm.Nickname = "Ace";

        // Assert
        await WaitForAsync(() => savedList.Count > 0);
        Assert.Single(savedList);
        Assert.Equal("Ace", savedList[0]);
    }

    /// <summary>
    /// Tests that playing passes the nickname to the launch service.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_WithNickname_ShouldPassNicknameToLaunchServiceAsync()
    {
        // Arrange
        var launch = new Mock<IOnlineLaunchService>();
        launch.Setup(l => l.PlayAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<OnlinePlayResult>.CreateSuccess(new OnlinePlayResult("profile-1", "Zero Hour", "Lobby")));
        var network = new Mock<IOnlineNetworkService>();
        network.SetupGet(n => n.AdapterState).Returns(OnlineAdapterState.Up);
        var vm = CreateViewModel(network.Object, launchService: launch.Object);
        vm.IsJoined = true;
        vm.SelectedPlayProfile = new GameProfile { Id = "profile-1", Name = "Zero Hour" };
        vm.Nickname = "Ace";

        // Act
        await vm.PlayAsync();

        // Assert
        launch.Verify(l => l.PlayAsync("profile-1", It.IsAny<string>(), It.IsAny<string>(), "Ace", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that joining sends the player nickname as the roster display name.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_WithNickname_ShouldSendDisplayNameAsync()
    {
        // Arrange
        var join = new OnlineJoinResult
        {
            NetworkId = "net-1",
            Grant = "grant-token",
            OverlayIp = "10.42.0.7",
        };
        var network = new Mock<IOnlineNetworkService>();
        network.Setup(n => n.JoinNetworkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<OnlineJoinResult>.CreateSuccess(join));
        network.SetupGet(n => n.AdapterState).Returns(OnlineAdapterState.Up);
        var vm = CreateViewModel(network.Object);
        vm.Networks = [new OnlineNetworkSummary { Id = "net-1", Name = "Lobby" }];
        vm.SelectedNetwork = vm.Networks[0];
        vm.Nickname = "Ace";

        // Act
        await vm.JoinNetworkAsync();

        // Assert
        network.Verify(
            n => n.JoinNetworkAsync(
                "net-1",
                It.IsAny<string>(),
                true,
                It.IsAny<string>(),
                It.IsAny<string>(),
                "Ace",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that editing the selected play profile while joined re-matches
    /// and re-advertises it with the player nickname.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Receive_ProfileUpdatedMessage_ForPlayProfile_ShouldReAdvertiseAsync()
    {
        // Arrange
        var profile = ProfileWithClient("profile-1", "Zero Hour", GameType.ZeroHour, "zerohour-client", "1.04", "mod-a", "mod-x");
        var advertised = new List<(string Fingerprint, string Name, string DisplayName)>();
        var vm = CreateViewModelWithDetail(profile, out var network);
        network.Setup(n => n.SetLocalProfileAdvertisement(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Callback<string, string, string, bool>((fingerprint, name, display, _) => advertised.Add((fingerprint, name, display)));
        vm.IsJoined = true;
        vm.Nickname = "Ace";
        vm.SelectedPlayProfile = profile;
        await WaitForAsync(() => advertised.Count > 0);
        advertised.Clear();

        // Act: the user edits the play profile content in Game Profiles (sent via messenger).
        WeakReferenceMessenger.Default.Send(new ProfileUpdatedMessage(profile));
        await WaitForAsync(() => advertised.Count > 0);

        // Assert
        Assert.StartsWith("opf3|ZeroHour|1.04|zerohour-client|", advertised[0].Fingerprint, StringComparison.Ordinal);
        Assert.Equal("Ace", advertised[0].DisplayName);
    }

    /// <summary>
    /// Tests that the advertised fingerprint embeds engine CRCs when the
    /// calculator resolves them.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task AdvertiseSelectedProfile_WithCalculator_ShouldEmbedCrcsAsync()
    {
        // Arrange
        var gameDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gameDir);
        try
        {
            var exePath = Path.Combine(gameDir, "generals.exe");
            await File.WriteAllTextAsync(exePath, "fake-exe");
            var profile = ProfileWithClient("profile-1", "Zero Hour", GameType.ZeroHour, "zerohour-client", "1.04", "mod-a");
            profile.GameClient!.ExecutablePath = exePath;
            IReadOnlyList<ContentManifest> manifests =
            [
                new() { Id = new ManifestId("mod-a"), ContentType = GenHub.Core.Models.Enums.ContentType.Mod, SourcePath = gameDir },
            ];
            var profiles = new Mock<IGameProfileManager>();
            profiles.Setup(p => p.GetAvailableContentAsync(It.IsAny<GameClient>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateSuccess(manifests));
            var calculator = new Mock<IGameCrcCalculatorService>();
            calculator.Setup(c => c.CalculateExeCrcAsync(exePath, gameDir, null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess("0x22222222"));
            calculator.Setup(c => c.CalculateIniCrcAsync(gameDir, GameType.ZeroHour, It.IsAny<IReadOnlyList<string>?>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess("0x11111111"));
            var advertised = new List<string>();
            var network = new Mock<IOnlineNetworkService>();
            network.Setup(n => n.SetLocalProfileAdvertisement(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, string, string, bool>((fingerprint, _, _, _) => advertised.Add(fingerprint));
            var vm = CreateViewModel(network.Object, profiles: profiles.Object, crcCalculator: calculator.Object);
            vm.IsJoined = true;
            vm.SelectedPlayProfile = profile;
            await WaitForAsync(() => advertised.Count > 0);

            // Assert
            Assert.StartsWith("opf4|", advertised[0], StringComparison.Ordinal);
            Assert.EndsWith("|0x11111111|0x22222222", advertised[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(gameDir, true);
        }
    }

    /// <summary>
    /// Tests that a calculator failure falls back to the id-only fingerprint
    /// instead of blocking the advertisement.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task AdvertiseSelectedProfile_WithCalculatorFailure_ShouldFallBackAsync()
    {
        // Arrange
        var gameDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gameDir);
        try
        {
            var exePath = Path.Combine(gameDir, "generals.exe");
            await File.WriteAllTextAsync(exePath, "fake-exe");
            var profile = ProfileWithClient("profile-1", "Zero Hour", GameType.ZeroHour, "zerohour-client", "1.04", "mod-a");
            profile.GameClient!.ExecutablePath = exePath;
            var profiles = new Mock<IGameProfileManager>();
            profiles.Setup(p => p.GetAvailableContentAsync(It.IsAny<GameClient>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateSuccess([]));
            var calculator = new Mock<IGameCrcCalculatorService>(MockBehavior.Strict);
            calculator.Setup(c => c.CalculateExeCrcAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateFailure("no exe"));
            calculator.Setup(c => c.CalculateIniCrcAsync(It.IsAny<string>(), It.IsAny<GameType>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateFailure("no ini"));
            var advertised = new List<string>();
            var network = new Mock<IOnlineNetworkService>();
            network.Setup(n => n.SetLocalProfileAdvertisement(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, string, string, bool>((fingerprint, _, _, _) => advertised.Add(fingerprint));
            var vm = CreateViewModel(network.Object, profiles: profiles.Object, crcCalculator: calculator.Object);
            vm.IsJoined = true;
            vm.SelectedPlayProfile = profile;
            await WaitForAsync(() => advertised.Count > 0);

            // Assert
            Assert.StartsWith("opf3|", advertised[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(gameDir, true);
        }
    }

    /// <summary>
    /// Tests that the view model registers with WeakReferenceMessenger on construction.
    /// </summary>
    [Fact]
    public void OnlineViewModel_ShouldRegisterWithWeakReferenceMessengerOnConstruction()
    {
        using var vm = CreateViewModel();
        Assert.True(WeakReferenceMessenger.Default.IsRegistered<ProfileCreatedMessage>(vm));
        Assert.True(WeakReferenceMessenger.Default.IsRegistered<ProfileUpdatedMessage>(vm));
        Assert.True(WeakReferenceMessenger.Default.IsRegistered<ProfileDeletedMessage>(vm));
        Assert.True(WeakReferenceMessenger.Default.IsRegistered<ProfileListUpdatedMessage>(vm));
    }

    /// <summary>
    /// Tests that the view model unregisters from WeakReferenceMessenger when disposed.
    /// </summary>
    [Fact]
    public void OnlineViewModel_Dispose_ShouldUnregisterFromWeakReferenceMessenger()
    {
        var vm = CreateViewModel();
        Assert.True(WeakReferenceMessenger.Default.IsRegistered<ProfileCreatedMessage>(vm));
        Assert.True(WeakReferenceMessenger.Default.IsRegistered<ProfileUpdatedMessage>(vm));
        Assert.True(WeakReferenceMessenger.Default.IsRegistered<ProfileDeletedMessage>(vm));
        Assert.True(WeakReferenceMessenger.Default.IsRegistered<ProfileListUpdatedMessage>(vm));
        vm.Dispose();
        Assert.False(WeakReferenceMessenger.Default.IsRegistered<ProfileCreatedMessage>(vm));
        Assert.False(WeakReferenceMessenger.Default.IsRegistered<ProfileUpdatedMessage>(vm));
        Assert.False(WeakReferenceMessenger.Default.IsRegistered<ProfileDeletedMessage>(vm));
        Assert.False(WeakReferenceMessenger.Default.IsRegistered<ProfileListUpdatedMessage>(vm));
    }

    /// <summary>
    /// Tests that toggling the create panel open re-fetches available profiles.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ToggleCreatePanel_WhenOpened_ShouldReloadProfilesAsync()
    {
        // Arrange
        var profile1 = ProfileWithClient("p1", "Profile 1", GameType.ZeroHour, "c1", "1.04");
        var profile2 = ProfileWithClient("p2", "Profile 2", GameType.ZeroHour, "c2", "1.04");
        var profiles = new Mock<IGameProfileManager>();
        var currentProfiles = new List<GameProfile> { profile1 };
        profiles.Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess(currentProfiles.ToList()));

        using var vm = CreateViewModel(profiles: profiles.Object);

        // First open loads profile1
        vm.ToggleCreatePanel();
        await WaitForAsync(() => vm.AvailableProfiles.Count == 1);
        Assert.Contains(vm.AvailableProfiles, p => p.Id == "p1");

        // Close panel
        vm.ToggleCreatePanel();
        Assert.False(vm.IsCreatePanelOpen);

        // Profile 2 is created
        currentProfiles.Add(profile2);

        // Act: reopen create panel
        vm.ToggleCreatePanel();
        await WaitForAsync(() => vm.AvailableProfiles.Count == 2);

        // Assert: profile2 is now present
        Assert.Contains(vm.AvailableProfiles, p => p.Id == "p2");
    }

    /// <summary>
    /// Tests that receiving ProfileCreatedMessage adds the new profile to AvailableProfiles.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Receive_ProfileCreatedMessage_ShouldAddToAvailableProfilesAsync()
    {
        // Arrange
        var profile1 = ProfileWithClient("p1", "Alpha Profile", GameType.ZeroHour, "c1", "1.04");
        var profiles = new Mock<IGameProfileManager>();
        profiles.Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile1]));

        using var vm = CreateViewModel(profiles: profiles.Object);
        vm.ToggleCreatePanel();
        await WaitForAsync(() => vm.AvailableProfiles.Count == 1);

        // Act
        var newProfile = ProfileWithClient("p2", "Beta Profile", GameType.ZeroHour, "c2", "1.04");
        WeakReferenceMessenger.Default.Send(new ProfileCreatedMessage(newProfile));

        // Assert
        Assert.Equal(2, vm.AvailableProfiles.Count);
        Assert.Contains(vm.AvailableProfiles, p => p.Id == "p2");
    }

    /// <summary>
    /// Tests that receiving ProfileUpdatedMessage updates the existing profile in AvailableProfiles and SelectedCreateProfile.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Receive_ProfileUpdatedMessage_ShouldUpdateAvailableProfilesAsync()
    {
        // Arrange
        var profile1 = ProfileWithClient("p1", "Old Name", GameType.ZeroHour, "c1", "1.04");
        var profiles = new Mock<IGameProfileManager>();
        profiles.Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile1]));

        using var vm = CreateViewModel(profiles: profiles.Object);
        vm.ToggleCreatePanel();
        await WaitForAsync(() => vm.AvailableProfiles.Count == 1);
        vm.SelectedCreateProfile = vm.AvailableProfiles[0];

        // Act
        var updatedProfile = ProfileWithClient("p1", "New Name", GameType.ZeroHour, "c1", "1.04");
        WeakReferenceMessenger.Default.Send(new ProfileUpdatedMessage(updatedProfile));

        // Assert
        Assert.Single(vm.AvailableProfiles);
        Assert.Equal("New Name", vm.AvailableProfiles[0].Name);
        Assert.Equal("New Name", vm.SelectedCreateProfile?.Name);
    }

    /// <summary>
    /// Tests that receiving ProfileDeletedMessage removes the profile from AvailableProfiles and clears selection.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Receive_ProfileDeletedMessage_ShouldRemoveFromAvailableProfilesAsync()
    {
        // Arrange
        var profile1 = ProfileWithClient("p1", "Profile 1", GameType.ZeroHour, "c1", "1.04");
        var profiles = new Mock<IGameProfileManager>();
        profiles.Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile1]));

        using var vm = CreateViewModel(profiles: profiles.Object);
        vm.ToggleCreatePanel();
        await WaitForAsync(() => vm.AvailableProfiles.Count == 1);
        vm.SelectedCreateProfile = vm.AvailableProfiles[0];

        // Act
        WeakReferenceMessenger.Default.Send(new ProfileDeletedMessage("p1", "Profile 1"));

        // Assert
        Assert.Empty(vm.AvailableProfiles);
        Assert.Null(vm.SelectedCreateProfile);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(condition(), "Timed out waiting for the background match.");
    }

    private static GameProfile ProfileWithClient(
        string id,
        string name,
        GameType gameType,
        string clientId,
        string version,
        params string[] contentIds)
    {
        return new GameProfile
        {
            Id = id,
            Name = name,
            GameClient = new GameClient
            {
                Id = clientId,
                Name = name,
                Version = version,
                GameType = gameType,
            },
            EnabledContentIds = [.. contentIds],
        };
    }

    private static OnlineViewModel CreateViewModelWithDetail(
        GameProfile profile,
        out Mock<IOnlineNetworkService> network,
        params GameProfile[] extraProfiles)
    {
        var all = new List<GameProfile> { profile };
        all.AddRange(extraProfiles);
        var profiles = new Mock<IGameProfileManager>();
        profiles.Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess(all));
        profiles.Setup(p => p.GetAvailableContentAsync(It.IsAny<GameClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateSuccess([]));
        var detail = new OnlineNetworkDetail
        {
            Id = "net-1",
            Name = "Lobby",
            ExpectedProfileFingerprint = "opf3|ZeroHour|1.04|zerohour-client|not-a-real-hash",
            ExpectedProfileName = "Host Setup",
            ExpectedGameClientId = "ZeroHour|1.04|zerohour-client",
            ExpectedContentIds = ["mod-a", "mod-b"],
        };
        var networkMock = new Mock<IOnlineNetworkService>();
        networkMock.Setup(n => n.GetNetworkDetailAsync("net-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<OnlineNetworkDetail>.CreateSuccess(detail));
        network = networkMock;
        return CreateViewModel(networkMock.Object, profiles: profiles.Object);
    }

    private static OnlineViewModel CreateViewModel(
        IOnlineNetworkService? network = null,
        INotificationService? notifications = null,
        IOnlineLaunchService? launchService = null,
        IDialogService? dialogs = null,
        IGameProfileManager? profiles = null,
        IUserSettingsService? userSettings = null,
        IGameCrcCalculatorService? crcCalculator = null)
    {
        return new OnlineViewModel(
            network ?? Mock.Of<IOnlineNetworkService>(),
            launchService ?? Mock.Of<IOnlineLaunchService>(),
            profiles ?? Mock.Of<IGameProfileManager>(),
            notifications ?? Mock.Of<INotificationService>(),
            dialogs ?? Mock.Of<IDialogService>(),
            Mock.Of<ILogger<OnlineViewModel>>(),
            new OnlineViewModelDependencies(null, userSettings, null, crcCalculator));
    }
}
