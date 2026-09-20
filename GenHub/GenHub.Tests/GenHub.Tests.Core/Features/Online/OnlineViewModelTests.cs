using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Online;
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
    /// Tests that consecutive refreshes on one view model both complete.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RefreshNetworksAsync_TwiceInARow_ShouldBothCompleteAsync()
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
        await vm.RefreshNetworksAsync();

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
    /// Tests that creating a public network without a password never calls the service.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateNetworkAsync_PublicWithoutPassword_ShouldNotCallServiceAsync()
    {
        // Arrange
        var network = new Mock<IOnlineNetworkService>(MockBehavior.Strict);
        var vm = CreateViewModel(network.Object);
        vm.CreateName = "Lobby";
        vm.CreatePassword = string.Empty;
        vm.CreateIsPublic = true;

        // Act
        await vm.CreateNetworkAsync();

        // Assert
        Assert.False(vm.IsJoined);
    }

    /// <summary>
    /// Tests that relay mode is on by default to protect public endpoints.
    /// </summary>
    [Fact]
    public void PreferRelay_ShouldDefaultToTrue()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        var preferRelay = vm.PreferRelay;

        // Assert
        Assert.True(preferRelay);
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
        network.Setup(n => n.JoinNetworkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
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
        network.Setup(n => n.JoinNetworkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
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
            n => n.JoinNetworkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
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
            n => n.JoinNetworkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
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
        vm.CreatePassword = "secret-password";

        // Act
        await vm.CreateNetworkAsync();

        // Assert
        network.Verify(
            n => n.CreateNetworkAsync(It.IsAny<OnlineCreateNetworkRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that play with a missing profile shows the download-prompt warning.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PlayAsync_WithMissingProfile_ShouldWarnAsync()
    {
        // Arrange
        var launch = new Mock<IOnlineLaunchService>();
        launch.Setup(l => l.PlayAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<OnlinePlayResult>.CreateFailure(OnlineConstants.ErrorProfileMissing));
        var notifications = new Mock<INotificationService>();
        var vm = CreateViewModel(launchService: launch.Object, notifications: notifications.Object);

        // Act
        await vm.PlayAsync();

        // Assert
        notifications.Verify(n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
        notifications.Verify(n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Tests that reporting without a selection never opens the dialog.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ReportMemberAsync_WithoutSelection_ShouldNotDialogAsync()
    {
        // Arrange
        var dialogs = new Mock<IDialogService>(MockBehavior.Strict);
        var vm = CreateViewModel(dialogs: dialogs.Object);

        // Act
        await vm.ReportMemberAsync();

        // Assert
        Assert.Null(vm.SelectedMember);
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
    /// Tests that a successful connection test toasts success.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task TestConnectionAsync_OnSuccess_ShouldToastAsync()
    {
        // Arrange
        var p2p = new Mock<IP2PConnectionService>();
        p2p.Setup(p => p.ConnectToPeerAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        var notifications = new Mock<INotificationService>();
        var vm = CreateViewModel(notifications: notifications.Object, p2pService: p2p.Object);
        vm.SelectedMember = new OnlineMember { DisplayName = "Guest", OverlayIp = "10.42.0.3", Endpoint = "203.0.113.7:4321" };

        // Act
        await vm.TestConnectionAsync();

        // Assert
        p2p.Verify(p => p.ConnectToPeerAsync("203.0.113.7", 4321, It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// Tests that a member without an endpoint never dials.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task TestConnectionAsync_WithoutEndpoint_ShouldNotDialAsync()
    {
        // Arrange
        var p2p = new Mock<IP2PConnectionService>(MockBehavior.Strict);
        var vm = CreateViewModel(p2pService: p2p.Object);
        vm.SelectedMember = new OnlineMember { DisplayName = "Relay", OverlayIp = "10.42.0.4", Endpoint = string.Empty };

        // Act
        await vm.TestConnectionAsync();

        // Assert
        Assert.NotNull(vm.SelectedMember);
    }

    private static OnlineViewModel CreateViewModel(
        IOnlineNetworkService? network = null,
        INotificationService? notifications = null,
        IOnlineLaunchService? launchService = null,
        IDialogService? dialogs = null,
        IP2PConnectionService? p2pService = null)
    {
        return new OnlineViewModel(
            network ?? Mock.Of<IOnlineNetworkService>(),
            launchService ?? Mock.Of<IOnlineLaunchService>(),
            Mock.Of<IGameProfileManager>(),
            p2pService ?? Mock.Of<IP2PConnectionService>(),
            notifications ?? Mock.Of<INotificationService>(),
            dialogs ?? Mock.Of<IDialogService>(),
            Mock.Of<ILogger<OnlineViewModel>>());
    }
}
