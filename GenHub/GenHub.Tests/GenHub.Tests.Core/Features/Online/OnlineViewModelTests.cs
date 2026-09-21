using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Online;
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
        var profiles = new Mock<IGameProfileManager>();
        profiles.Setup(p => p.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));
        var vm = CreateViewModel(network.Object, profiles: profiles.Object);
        vm.CreateName = "Lobby";
        vm.CreatePassword = string.Empty;
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
    /// Tests that joins always request relay mode to protect public endpoints.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_ShouldAlwaysPreferRelayAsync()
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
        var notifications = new Mock<INotificationService>();
        var vm = CreateViewModel(network.Object, notifications.Object, profiles: profiles.Object);
        vm.CreateName = "Lobby";
        vm.CreatePassword = string.Empty;
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
    /// Tests that a same-client auto-match shows the shared mod counts.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SelectNetwork_WithSameClientProfile_ShouldShowModCountsAsync()
    {
        // Arrange
        var profile = ProfileWithClient("profile-1", "Zero Hour", GameType.ZeroHour, "zerohour-client", "1.04", "mod-a", "mod-x");
        var vm = CreateViewModelWithDetail(profile, out _);

        // Act
        vm.SelectedNetwork = new OnlineNetworkSummary { Id = "net-1", Name = "Lobby" };
        await WaitForAsync(() => vm.SelectedPlayProfile is not null);

        // Assert
        Assert.Equal(OnlineProfileMatch.SameClient, vm.ProfileMatchState);
        Assert.Equal("Online.Detail.MatchDetailSameClient (1, 2)", vm.ProfileMatchDetail);
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
        network.Setup(n => n.SetLocalProfileAdvertisement(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((fingerprint, _) => advertised.Add(fingerprint));
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
        IGameProfileManager? profiles = null)
    {
        return new OnlineViewModel(
            network ?? Mock.Of<IOnlineNetworkService>(),
            launchService ?? Mock.Of<IOnlineLaunchService>(),
            profiles ?? Mock.Of<IGameProfileManager>(),
            notifications ?? Mock.Of<INotificationService>(),
            dialogs ?? Mock.Of<IDialogService>(),
            Mock.Of<ILogger<OnlineViewModel>>());
    }
}
