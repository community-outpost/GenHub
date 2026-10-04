using Avalonia.Threading;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using GenHub.Features.GeneralsOnline.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.GeneralsOnline;

/// <summary>
/// Unit tests for <see cref="GeneralsOnlineLobbiesViewModel"/>.
/// </summary>
public class GeneralsOnlineLobbiesViewModelTests
{
    /// <summary>
    /// Tests that a new view model starts signed out with empty lists.
    /// </summary>
    [Fact]
    public void Constructor_ShouldStartSignedOutWithEmptyLists()
    {
        // Arrange & Act
        using var vm = CreateViewModel();

        // Assert
        Assert.Equal(GeneralsOnlineAuthState.Unauthenticated, vm.AuthState);
        Assert.False(vm.IsAuthenticated);
        Assert.Empty(vm.Lobbies);
        Assert.Empty(vm.VisibleLobbies);
        Assert.False(vm.HasLobbies);
        Assert.Equal(GeneralsOnlineCompatibility.Unknown, vm.SelectedLobbyCompatibility);
    }

    /// <summary>
    /// Tests that refresh restores a stored session and loads lobbies.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_WithStoredToken_ShouldRestoreSessionAndLoadLobbiesAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: false);
        fakes.Auth.Setup(a => a.TryLoginWithStoredTokenAsync(It.IsAny<CancellationToken>()))
            .Callback(() => fakes.Auth.SetupGet(a => a.AuthState).Returns(GeneralsOnlineAuthState.Authenticated))
            .ReturnsAsync(OperationResult<LoginResult>.CreateSuccess(new LoginResult
            {
                Result = PendingLoginState.LoginSuccess,
                SessionToken = "session",
                DisplayName = "PlayerName",
            }));
        using var vm = CreateViewModel(fakes);

        // Act
        await vm.RefreshAsync();

        // Assert
        Assert.True(vm.IsAuthenticated);
        Assert.Equal(2, vm.VisibleLobbies.Count);
        Assert.True(vm.HasLobbies);
        Assert.Equal(7, vm.PublicLobbyCount);
        Assert.Equal(140, vm.PublicPlayerCount);
        Assert.Equal(0, vm.PublicPlayingCount);
        fakes.Api.Verify(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()), Times.Once);
        fakes.Api.Verify(a => a.GetPublicCountsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that the playing count sums players in started lobbies.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_WithInProgressLobby_ShouldCountPlayingAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var started = SampleLobby();
        started.State = GeneralsOnlineLobbyState.InGame;
        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
                new GeneralsOnlineLobbiesResult { Lobbies = [started, SampleModLobby()], Latencies = [42] }));
        using var vm = CreateViewModel(fakes);

        // Act
        await vm.RefreshAsync();

        // Assert
        Assert.Equal(2, vm.PublicPlayingCount);
    }

    /// <summary>
    /// Tests that refresh populates community stats alongside the lobby list.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_WhenAuthenticated_ShouldPopulateCommunityStatsAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        using var vm = CreateViewModel(fakes);

        // Act
        await vm.RefreshAsync();

        // Assert
        Assert.Equal(15, vm.MatchesToday);
        Assert.Equal(9, vm.WinsToday);
        Assert.Equal("Days: 2, Hours: 0, Minutes: 59", vm.ServiceUptimeText);
        Assert.Equal("Online.GeneralsOnline.Stats.UptimeSince (2026-09-26 01:24:45)", vm.ServiceStartTimeText);
        Assert.Equal("Welcome, commanders!", vm.MotdText);
        Assert.True(vm.HasMotd);
        var motdRun = Assert.Single(vm.MotdRuns);
        Assert.Equal("Welcome, commanders!", motdRun.Text);
        Assert.Null(motdRun.ColorHex);
        Assert.Equal("Online.GeneralsOnline.Stats.PlayerCard (1450, 20, 11)", vm.PlayerCardText);
        Assert.Equal("Online.GeneralsOnline.Stats.YourRecordDetail (31, 64)", vm.PlayerDetailText);
        Assert.True(vm.HasPlayerStats);
        fakes.Api.Verify(a => a.GetGlobalStatsAsync(It.IsAny<CancellationToken>()), Times.Once);
        fakes.Api.Verify(a => a.GetMotdAsync(It.IsAny<CancellationToken>()), Times.Once);
        fakes.Api.Verify(a => a.GetPlayerStatsAsync(1052, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that stats failures stay silent and leave the lobby list intact.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_WhenStatsFail_ShouldKeepLobbiesWithoutToastAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        fakes.Api.Setup(a => a.GetServiceUptimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineServiceUptime>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable));
        fakes.Api.Setup(a => a.GetGlobalStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineDailyStats>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable));
        fakes.Api.Setup(a => a.GetPlayerStatsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlinePlayerStats>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable));
        fakes.Api.Setup(a => a.GetMotdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable));
        using var vm = CreateViewModel(fakes);

        // Act
        await vm.RefreshAsync();

        // Assert
        Assert.True(vm.HasLobbies);
        Assert.Equal(0, vm.MatchesToday);
        Assert.Equal(0, vm.WinsToday);
        Assert.Null(vm.ServiceUptimeText);
        Assert.Null(vm.ServiceStartTimeText);
        Assert.Null(vm.MotdText);
        Assert.Empty(vm.MotdRuns);
        Assert.Null(vm.PlayerCardText);
        Assert.Null(vm.PlayerDetailText);
        fakes.Notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Tests that sign-out clears community stats with the lobby list.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SignOut_ShouldClearCommunityStatsAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();

        // Act
        await vm.SignOutAsync();

        // Assert
        Assert.Equal(0, vm.MatchesToday);
        Assert.Equal(0, vm.WinsToday);
        Assert.Equal("Online.GeneralsOnline.Stats.UptimeSince (2026-09-26 01:24:45)", vm.ServiceStartTimeText);
        Assert.Null(vm.MotdText);
        Assert.Empty(vm.MotdRuns);
        Assert.Null(vm.PlayerCardText);
        Assert.Null(vm.PlayerDetailText);
        Assert.False(vm.HasMotd);
        Assert.False(vm.HasPlayerStats);
    }

    /// <summary>
    /// Tests that opening a community link delegates to the browser opener.
    /// </summary>
    [Fact]
    public void OpenLink_WithHttpsUrl_ShouldOpenBrowser()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: false);
        using var vm = CreateViewModel(fakes);
        string? opened = null;
        vm.BrowserOpener = url =>
        {
            opened = url;
            return true;
        };

        // Act
        vm.OpenLink("https://www.playgenerals.online");

        // Assert
        Assert.Equal("https://www.playgenerals.online/", opened);
    }

    /// <summary>
    /// Tests that blank or non-HTTPS links are ignored without a toast.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://example.com")]
    [InlineData("not a url")]
    public void OpenLink_WithNonHttpsUrl_ShouldIgnore(string? url)
    {
        // Arrange
        var fakes = CreateFakes(authenticated: false);
        using var vm = CreateViewModel(fakes);
        vm.BrowserOpener = _ => throw new InvalidOperationException("Must not open.");

        // Act
        vm.OpenLink(url);

        // Assert
        fakes.Notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Tests that a failed browser launch surfaces an error toast.
    /// </summary>
    [Fact]
    public void OpenLink_WhenBrowserFails_ShouldShowError()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: false);
        using var vm = CreateViewModel(fakes);
        vm.BrowserOpener = _ => false;

        // Act
        vm.OpenLink("https://www.playgenerals.online");

        // Assert
        fakes.Notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that a browser launch throwing ObjectDisposedException surfaces an error toast.
    /// </summary>
    [Fact]
    public void OpenLink_WhenBrowserThrowsObjectDisposedException_ShouldShowError()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: false);
        using var vm = CreateViewModel(fakes);
        vm.BrowserOpener = _ => throw new ObjectDisposedException("Process");

        // Act
        vm.OpenLink("https://www.playgenerals.online");

        // Assert
        fakes.Notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that a browser launch throwing PlatformNotSupportedException surfaces an error toast.
    /// </summary>
    [Fact]
    public void OpenLink_WhenBrowserThrowsPlatformNotSupportedException_ShouldShowError()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: false);
        using var vm = CreateViewModel(fakes);
        vm.BrowserOpener = _ => throw new PlatformNotSupportedException("No browser");

        // Act
        vm.OpenLink("https://www.playgenerals.online");

        // Assert
        fakes.Notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that the silent login is attempted only once across refreshes.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_TwiceWithoutToken_ShouldAttemptSilentLoginOnceAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: false);
        using var vm = CreateViewModel(fakes);

        // Act
        await vm.RefreshAsync();
        await vm.RefreshAsync();

        // Assert
        fakes.Auth.Verify(a => a.TryLoginWithStoredTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
        fakes.Api.Verify(a => a.GetPublicCountsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal(7, vm.PublicLobbyCount);
        Assert.Equal(140, vm.PublicPlayerCount);
        Assert.False(vm.HasLobbies);
    }

    /// <summary>
    /// Tests that search text filters the visible lobbies.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchText_ShouldFilterVisibleLobbiesAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();

        // Act
        vm.SearchText = "desert";

        // Assert
        var visible = Assert.Single(vm.VisibleLobbies);
        Assert.Equal("Tournament Desert", visible.MapName);
    }

    /// <summary>
    /// Tests that the compatible-only filter hides mismatched lobbies.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CompatibleOnly_ShouldHideMismatchedLobbiesAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();

        // Act
        vm.CompatibleOnly = true;

        // Assert
        var visible = Assert.Single(vm.VisibleLobbies);
        Assert.Equal(9842, visible.LobbyId);
        Assert.Equal(GeneralsOnlineCompatibility.Compatible, vm.SelectedLobbyCompatibility);
    }

    /// <summary>
    /// Tests that launching a compatible lobby plays the best profile.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Launch_CompatibleLobby_ShouldPlayBestProfileAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();
        vm.SelectedLobby = vm.VisibleLobbies.First(l => l.LobbyId == 9842);

        // Act
        await vm.LaunchAsync(null);

        // Assert
        fakes.Launch.Verify(
            l => l.PlayAsync("profile-vanilla", "[EU] Pro 1v1", string.Empty, string.Empty, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that launching a mismatched lobby warns without playing.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Launch_MismatchedLobby_ShouldWarnWithoutPlayingAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();
        vm.SelectedLobby = vm.VisibleLobbies.First(l => l.LobbyId == 777);

        // Act
        await vm.LaunchAsync(null);

        // Assert
        fakes.Launch.Verify(
            l => l.PlayAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fakes.Notifications.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Tests that launching while signed out warns without playing.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Launch_Unauthenticated_ShouldWarnWithoutPlayingAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: false);
        using var vm = CreateViewModel(fakes);

        // Act
        await vm.LaunchAsync(SampleLobby());

        // Assert
        fakes.Launch.Verify(
            l => l.PlayAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fakes.Notifications.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Tests that sign-out clears lobbies and reloads public counts.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SignOut_ShouldClearLobbiesAndReloadPublicCountsAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();
        Assert.True(vm.HasLobbies);

        // Act
        await vm.SignOutAsync();

        // Assert
        fakes.Auth.Verify(a => a.LogoutAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(vm.HasLobbies);
        Assert.Equal(7, vm.PublicLobbyCount);
    }

    /// <summary>
    /// Tests that a sign-out during the lobby load drops the response instead of showing it.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_SignedOutDuringLoad_ShouldDropLobbiesAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        using var vm = CreateViewModel(fakes);
        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => vm.AuthState = GeneralsOnlineAuthState.Unauthenticated)
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
                new GeneralsOnlineLobbiesResult { Lobbies = [SampleLobby()], Latencies = [42] }));

        // Act
        await vm.RefreshAsync();

        // Assert
        Assert.Empty(vm.Lobbies);
        Assert.Empty(vm.VisibleLobbies);
        Assert.False(vm.HasLobbies);
        Assert.Equal(7, vm.PublicLobbyCount);
        Assert.Equal(140, vm.PublicPlayerCount);
    }

    /// <summary>
    /// Tests that a refresh queued behind an in-flight refresh waits instead of skipping.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_WhileRefreshInFlight_WaitsForLockAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        using var vm = CreateViewModel(fakes);
        var gate = new TaskCompletionSource<OperationResult<GeneralsOnlineLobbiesResult>>();
        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>())).Returns(gate.Task);

        // Act: park the first refresh on the gate, queue the second behind it.
        var first = vm.RefreshAsync();
        await Task.Delay(50);
        var second = vm.RefreshAsync();
        await Task.Delay(50);
        gate.SetResult(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
            new GeneralsOnlineLobbiesResult { Lobbies = [SampleLobby()], Latencies = [42] }));
        await Task.WhenAll(first, second);

        // Assert: both refreshes ran instead of the second one bailing out.
        fakes.Api.Verify(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.True(vm.HasLobbies);
    }

    /// <summary>
    /// Tests that a burst queued behind an in-flight refresh collapses into one extra pass.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_BurstBehindInflightRefresh_ShouldCoalesceIntoOneExtraPassAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        using var vm = CreateViewModel(fakes);
        var gate = new TaskCompletionSource<OperationResult<GeneralsOnlineLobbiesResult>>();
        var calls = 0;
        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .Returns(() => ++calls == 1
                ? gate.Task
                : Task.FromResult(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
                    new GeneralsOnlineLobbiesResult { Lobbies = [SampleLobby()], Latencies = [42] })));

        // Act: park the first refresh, queue a burst behind it.
        var first = vm.RefreshAsync();
        await Task.Delay(50);
        var second = vm.RefreshAsync();
        var third = vm.RefreshAsync();
        await Task.Delay(50);
        gate.SetResult(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
            new GeneralsOnlineLobbiesResult { Lobbies = [SampleLobby()], Latencies = [42] }));
        await Task.WhenAll(first, second, third);

        // Assert: the burst collapses into a single extra pass.
        fakes.Api.Verify(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.True(vm.HasLobbies);
    }

    /// <summary>
    /// Tests that the busy state spans a queued refresh instead of flickering off between passes.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_QueuedBehindInflightRefresh_ShouldKeepLoadingUntilBatchFinishesAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        using var vm = CreateViewModel(fakes);
        var firstGate = new TaskCompletionSource<OperationResult<GeneralsOnlineLobbiesResult>>();
        var secondGate = new TaskCompletionSource<OperationResult<GeneralsOnlineLobbiesResult>>();
        var calls = 0;
        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .Returns(() => ++calls == 1 ? firstGate.Task : secondGate.Task);

        // Act
        var first = vm.RefreshAsync();
        await Task.Delay(50);
        var second = vm.RefreshAsync();
        await Task.Delay(50);
        firstGate.SetResult(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
            new GeneralsOnlineLobbiesResult { Lobbies = [SampleLobby()], Latencies = [42] }));
        await first;

        // Assert: the queued refresh still owns the busy state.
        Assert.True(vm.IsLoading);
        secondGate.SetResult(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
            new GeneralsOnlineLobbiesResult { Lobbies = [SampleLobby()], Latencies = [42] }));
        await second;
        Assert.False(vm.IsLoading);
    }

    /// <summary>
    /// Tests that a debounce reset after dispose returns silently.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DebouncedRefresh_AfterDispose_ShouldReturnSilentlyAsync()
    {
        // Arrange: arm the debounce timer, then dispose under it.
        var fakes = CreateFakes(authenticated: true);
        using var vm = CreateViewModel(fakes);
        var method = typeof(GeneralsOnlineLobbiesViewModel).GetMethod(
            "DebouncedRefreshAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var first = (Task)method.Invoke(vm, null)!;
        await Task.Delay(50);
        vm.Dispose();
        await first;

        // Act & Assert: resetting the disposed timer must not throw.
        await (Task)method.Invoke(vm, null)!;
    }

    /// <summary>
    /// Tests that a token read failure during connect does not throw.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ConnectWebSocket_WhenTokenReadFails_ShouldNotThrowAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        fakes.Auth.Setup(a => a.GetSessionTokenAsync()).ThrowsAsync(new IOException("Key unavailable."));
        using var vm = CreateViewModel(fakes);
        var method = typeof(GeneralsOnlineLobbiesViewModel).GetMethod(
            "ConnectWebSocketAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        // Act & Assert
        await (Task)method.Invoke(vm, [CancellationToken.None])!;
    }

    /// <summary>
    /// Tests that a forbidden lobby list shows the inline notice without a toast.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_ForbiddenLobbies_ShouldShowNoticeWithoutToastAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateFailure(GeneralsOnlineConstants.ErrorLobbiesForbidden));
        using var vm = CreateViewModel(fakes);

        // Act
        await vm.RefreshAsync();
        await vm.RefreshAsync();

        // Assert
        Assert.True(vm.HasLobbiesNotice);
        Assert.False(vm.HasLobbies);
        fakes.Notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Tests that repeated identical lobby failures toast only once.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_RepeatedFailure_ShouldToastOnceAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateFailure(GeneralsOnlineConstants.ErrorLobbiesUnavailable));
        using var vm = CreateViewModel(fakes);

        // Act
        await vm.RefreshAsync();
        await vm.RefreshAsync();
        await vm.RefreshAsync();

        // Assert
        Assert.False(vm.HasLobbiesNotice);
        fakes.Notifications.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that a success clears the notice and re-arms failure toasts.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_SuccessAfterForbidden_ShouldClearNoticeAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        fakes.Api.SetupSequence(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateFailure(GeneralsOnlineConstants.ErrorLobbiesForbidden))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
                new GeneralsOnlineLobbiesResult { Lobbies = [SampleLobby()], Latencies = [42] }));
        using var vm = CreateViewModel(fakes);

        // Act
        await vm.RefreshAsync();
        Assert.True(vm.HasLobbiesNotice);
        await vm.RefreshAsync();

        // Assert
        Assert.False(vm.HasLobbiesNotice);
        Assert.True(vm.HasLobbies);
    }

    /// <summary>
    /// Tests that a failed lobbies fetch keeps stale lobbies and selection with a warning.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_WhenGetLobbiesFails_ShouldKeepPreviousLobbiesAndSelectionAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();
        Assert.True(vm.HasLobbies);
        vm.SelectedLobby = vm.VisibleLobbies.First();
        Assert.NotNull(vm.SelectedLobby);
        var selectedId = vm.SelectedLobby.LobbyId;

        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable));

        // Act
        await vm.RefreshAsync();

        // Assert: stale rows stay so a blip never flashes the list empty.
        Assert.True(vm.HasLobbies);
        Assert.NotEmpty(vm.Lobbies);
        Assert.NotEmpty(vm.VisibleLobbies);
        Assert.NotNull(vm.SelectedLobby);
        Assert.Equal(selectedId, vm.SelectedLobby.LobbyId);
        Assert.False(vm.HasLobbiesNotice);
        Assert.NotNull(vm.LobbiesWarningText);
    }

    /// <summary>
    /// Tests that failed public counts and uptime calls reset their respective view model fields.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_WhenPublicCountsAndUptimeFail_ShouldResetCountsAndUptimeAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();
        Assert.Equal(7, vm.PublicLobbyCount);
        Assert.Equal(140, vm.PublicPlayerCount);
        Assert.NotNull(vm.ServiceUptimeText);
        Assert.NotNull(vm.ServiceStartTimeText);

        fakes.Api.Setup(a => a.GetPublicCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlinePublicCounts>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable));
        fakes.Api.Setup(a => a.GetServiceUptimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineServiceUptime>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable));

        // Act
        await vm.RefreshAsync();

        // Assert
        Assert.Equal(0, vm.PublicLobbyCount);
        Assert.Equal(0, vm.PublicPlayerCount);
        Assert.Null(vm.ServiceUptimeText);
        Assert.Null(vm.ServiceStartTimeText);
    }

    /// <summary>
    /// Tests that failed community stats, MOTD, and player stats calls reset their respective view model fields.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_WhenCommunityStatsFail_ShouldResetStatsAndMotdAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();
        Assert.Equal(15, vm.MatchesToday);
        Assert.Equal(9, vm.WinsToday);
        Assert.NotNull(vm.MotdText);
        Assert.NotEmpty(vm.MotdRuns);
        Assert.NotNull(vm.PlayerCardText);
        Assert.NotNull(vm.PlayerDetailText);

        fakes.Api.Setup(a => a.GetGlobalStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineDailyStats>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable));
        fakes.Api.Setup(a => a.GetMotdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable));
        fakes.Api.Setup(a => a.GetPlayerStatsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlinePlayerStats>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable));

        // Act
        await vm.RefreshAsync();

        // Assert
        Assert.Equal(0, vm.MatchesToday);
        Assert.Equal(0, vm.WinsToday);
        Assert.Null(vm.MotdText);
        Assert.Empty(vm.MotdRuns);
        Assert.Null(vm.PlayerCardText);
        Assert.Null(vm.PlayerDetailText);
    }

    /// <summary>
    /// Tests that auth state events update the displayed session.
    /// </summary>
    [Fact]
    public void AuthStateChanged_ShouldUpdateSessionProps()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: false);
        using var vm = CreateViewModel(fakes);

        // Act
        fakes.Auth.SetupGet(a => a.AuthState).Returns(GeneralsOnlineAuthState.Authenticated);
        fakes.Auth.SetupGet(a => a.CurrentDisplayName).Returns("PlayerName");
        fakes.Auth.Raise(a => a.AuthStateChanged += null, this, GeneralsOnlineAuthState.Authenticated);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.True(vm.IsAuthenticated);
        Assert.Equal("PlayerName", vm.DisplayName);
    }

    /// <summary>
    /// Tests that room picking prefers the show-all room for lobby browsing.
    /// </summary>
    [Fact]
    public void SelectShowAllRoom_WithFlaggedRoom_ShouldPreferIt()
    {
        // Arrange
        IReadOnlyList<GeneralsOnlineRoom> rooms =
        [
            new() { Id = 1, Name = "Europe", Flags = 0 },
            new() { Id = 0, Name = "ALL GAMES", Flags = GeneralsOnlineConstants.RoomFlagsShowAllMatches },
        ];

        // Act
        var selected = GeneralsOnlineLobbiesViewModel.SelectShowAllRoom(rooms);

        // Assert
        Assert.NotNull(selected);
        Assert.Equal(0, selected.Id);
    }

    /// <summary>
    /// Tests that room picking falls back to the first room, which the backend
    /// also treats as viewing every room.
    /// </summary>
    [Fact]
    public void SelectShowAllRoom_WithoutFlaggedRoom_ShouldFallBackToFirst()
    {
        // Arrange
        IReadOnlyList<GeneralsOnlineRoom> rooms =
        [
            new() { Id = 4, Name = "First", Flags = 0 },
            new() { Id = 5, Name = "Second", Flags = 0 },
        ];

        // Act
        var selected = GeneralsOnlineLobbiesViewModel.SelectShowAllRoom(rooms);

        // Assert
        Assert.NotNull(selected);
        Assert.Equal(4, selected.Id);
    }

    /// <summary>
    /// Tests that room picking returns null when the backend lists no rooms.
    /// </summary>
    [Fact]
    public void SelectShowAllRoom_WhenEmpty_ShouldReturnNull()
    {
        Assert.Null(GeneralsOnlineLobbiesViewModel.SelectShowAllRoom([]));
    }

    /// <summary>
    /// Tests that disabling started lobbies hides in-progress matches.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ShowInProgress_False_ShouldHideStartedLobbiesAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var started = SampleLobby(100, "[EU] Started");
        started.State = GeneralsOnlineLobbyState.InGame;
        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
                new GeneralsOnlineLobbiesResult { Lobbies = [started, SampleLobby()], Latencies = [10, 20] }));
        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();
        Assert.Equal(2, vm.VisibleLobbies.Count);

        // Act
        vm.ShowInProgress = false;

        // Assert
        var visible = Assert.Single(vm.VisibleLobbies);
        Assert.Equal(9842, visible.LobbyId);
    }

    /// <summary>
    /// Tests that hiding full lobbies removes lobbies without open slots.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task HideFull_True_ShouldHideFullLobbiesAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var full = SampleLobby(101, "[EU] Full");
        full.Members =
        [
            new GeneralsOnlineLobbyMember { UserId = 1, DisplayName = "One", SlotIndex = 0, SlotState = GeneralsOnlineSlotState.SlotPlayer },
            new GeneralsOnlineLobbyMember { UserId = 2, DisplayName = "Two", SlotIndex = 1, SlotState = GeneralsOnlineSlotState.SlotPlayer },
        ];
        var unlimited = SampleLobby(107, "[EU] Unlimited");
        unlimited.Members = [];
        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
                new GeneralsOnlineLobbiesResult { Lobbies = [full, SampleLobby(), unlimited], Latencies = [10, 20, 30] }));
        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();
        Assert.Equal(3, vm.VisibleLobbies.Count);

        // Act
        vm.HideFull = true;

        // Assert
        Assert.Equal(2, vm.VisibleLobbies.Count);
        Assert.Contains(vm.VisibleLobbies, l => l.LobbyId == 9842);
        Assert.Contains(vm.VisibleLobbies, l => l.LobbyId == 107);
    }

    /// <summary>
    /// Tests that hiding locked lobbies removes passworded entries.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task HidePassworded_True_ShouldHideLockedLobbiesAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var locked = SampleLobby(102, "[EU] Locked");
        locked.IsPassworded = true;
        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
                new GeneralsOnlineLobbiesResult { Lobbies = [locked, SampleLobby()], Latencies = [10, 20] }));
        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();
        Assert.Equal(2, vm.VisibleLobbies.Count);

        // Act
        vm.HidePassworded = true;

        // Assert
        var visible = Assert.Single(vm.VisibleLobbies);
        Assert.Equal(9842, visible.LobbyId);
    }

    /// <summary>
    /// Tests that the region filter narrows lobbies to the selected region.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SelectedRegion_ShouldFilterByRegionAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var eu = SampleLobby(103, "[EU] Game");
        eu.Region = "EU";
        var na = SampleLobby(104, "[NA] Game");
        na.Region = "NA";
        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
                new GeneralsOnlineLobbiesResult { Lobbies = [eu, na], Latencies = [10, 20] }));
        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();
        Assert.Equal(2, vm.VisibleLobbies.Count);
        Assert.Contains("EU", vm.AvailableRegions);
        Assert.Contains("NA", vm.AvailableRegions);

        // Act
        vm.SelectedRegion = "NA";

        // Assert
        var visible = Assert.Single(vm.VisibleLobbies);
        Assert.Equal(104, visible.LobbyId);
    }

    /// <summary>
    /// Tests that latency sorting orders lobbies by estimated ping.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SelectedSort_Latency_ShouldOrderByPingAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var slow = SampleLobby(105, "[EU] Slow");
        slow.Members =
        [
            new GeneralsOnlineLobbyMember { UserId = 1, DisplayName = "One", SlotIndex = 0, SlotState = GeneralsOnlineSlotState.SlotPlayer },
            new GeneralsOnlineLobbyMember { UserId = 2, DisplayName = "Two", SlotIndex = 1, SlotState = GeneralsOnlineSlotState.SlotPlayer },
            new GeneralsOnlineLobbyMember { UserId = 3, DisplayName = "Three", SlotIndex = 2, SlotState = GeneralsOnlineSlotState.SlotPlayer },
        ];
        var fast = SampleLobby(106, "[EU] Fast");
        fast.Members =
        [
            new GeneralsOnlineLobbyMember { UserId = 4, DisplayName = "Four", SlotIndex = 0, SlotState = GeneralsOnlineSlotState.SlotPlayer },
        ];
        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
                new GeneralsOnlineLobbiesResult { Lobbies = [slow, fast], Latencies = [300, 25] }));
        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();

        // Act
        vm.SelectedSort = "Latency";

        // Assert
        Assert.Equal(2, vm.VisibleLobbies.Count);
        Assert.Equal(106, vm.VisibleLobbies[0].LobbyId);
        Assert.Equal(105, vm.VisibleLobbies[1].LobbyId);
        Assert.Equal(25, vm.GetLatencyFor(106));
    }

    /// <summary>
    /// Tests that repeated refreshes with identical lobbies keep the same
    /// collection instances so the virtualized list does not flicker.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_TwiceWithSameLobbies_ShouldReuseCollectionsAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();
        var lobbies = vm.Lobbies;
        var visible = vm.VisibleLobbies;
        var firstLobby = vm.Lobbies[0];
        var selectedId = vm.SelectedLobby?.LobbyId;
        Assert.NotNull(selectedId);

        // Act
        await vm.RefreshAsync();

        // Assert
        Assert.Same(lobbies, vm.Lobbies);
        Assert.Same(visible, vm.VisibleLobbies);
        Assert.Same(firstLobby, vm.Lobbies[0]);
        Assert.Equal(selectedId, vm.SelectedLobby?.LobbyId);
        Assert.True(vm.HasLobbies);
    }

    /// <summary>
    /// Tests that refresh populates friends and pending requests.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_ShouldPopulateFriendsAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        fakes.Api.Setup(a => a.GetFriendsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineFriendsResult>.CreateSuccess(
                new GeneralsOnlineFriendsResult
                {
                    Friends =
                    [
                        new GeneralsOnlineFriend { UserId = 7, DisplayName = "Buddy", IsOnline = true, Presence = "In lobby" },
                    ],
                    PendingRequests =
                    [
                        new GeneralsOnlineFriend { UserId = 8, DisplayName = "Stranger" },
                    ],
                }));
        using var vm = CreateViewModel(fakes);

        // Act
        await vm.RefreshAsync();

        // Assert
        Assert.True(vm.HasFriends);
        Assert.Single(vm.Friends);
        Assert.Single(vm.PendingRequests);
        Assert.Equal("Buddy", vm.Friends[0].DisplayName);
    }

    /// <summary>
    /// Tests that sending room chat delegates to the WebSocket listener.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SendRoomChat_WithText_ShouldCallListenerAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var ws = new Mock<IGeneralsOnlineWebSocketListener>();
        ws.Setup(w => w.SendRoomChatAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        using var vm = CreateViewModel(fakes, ws.Object);
        vm.RoomChatInput = "Hello room";

        // Act
        await vm.SendRoomChatCommand.ExecuteAsync(null);

        // Assert
        ws.Verify(w => w.SendRoomChatAsync("Hello room", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(string.Empty, vm.RoomChatInput);
    }

    /// <summary>
    /// Tests that receiving an echo of our own DM marks the message as own and does not trigger unread badges or toast notifications.
    /// </summary>
    [Fact]
    public void OnFriendChatReceived_WhenOwnEchoWithKnownSelfId_ShouldMarkOwnAndNotToast()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var ws = new Mock<IGeneralsOnlineWebSocketListener>();
        using var vm = CreateViewModel(fakes, ws.Object);
        vm.SelfUserId = 1052;
        var friend = new GeneralsOnlineFriend { UserId = 200, DisplayName = "TargetFriend" };
        vm.OpenDmCommand.Execute(friend);

        var echoMessage = new GeneralsOnlineFriendChatMessage
        {
            SourceUserId = 1052,
            TargetUserId = 200,
            Message = "Hey friend!",
        };

        // Act
        ws.Raise(w => w.FriendChatReceived += null, ws.Object, echoMessage);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.True(echoMessage.IsOwn);
        Assert.Contains(echoMessage, vm.DmMessages);
        Assert.Equal(0, vm.DmUnreadCount);
        fakes.Notifications.Verify(n => n.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Tests that receiving an echo when SelfUserId is unknown deduces SelfUserId from the open DM thread and does not show an incoming toast.
    /// </summary>
    [Fact]
    public void OnFriendChatReceived_WhenOwnEchoWithUnknownSelfId_ShouldDeduceSelfIdAndMarkOwn()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var ws = new Mock<IGeneralsOnlineWebSocketListener>();
        using var vm = CreateViewModel(fakes, ws.Object);
        vm.SelfUserId = -1;
        var friend = new GeneralsOnlineFriend { UserId = 200, DisplayName = "TargetFriend" };
        vm.OpenDmCommand.Execute(friend);

        var echoMessage = new GeneralsOnlineFriendChatMessage
        {
            SourceUserId = 1052,
            TargetUserId = 200,
            Message = "Hey friend from unknown self!",
        };

        // Act
        ws.Raise(w => w.FriendChatReceived += null, ws.Object, echoMessage);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.True(echoMessage.IsOwn);
        Assert.Equal(1052, vm.SelfUserId);
        Assert.Contains(echoMessage, vm.DmMessages);
        Assert.Equal(0, vm.DmUnreadCount);
        fakes.Notifications.Verify(n => n.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Tests that incoming direct messages from a peer increment the unread count and trigger a notification.
    /// </summary>
    [Fact]
    public void OnFriendChatReceived_WhenIncomingFromPeer_ShouldTrackUnreadAndToast()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var ws = new Mock<IGeneralsOnlineWebSocketListener>();
        using var vm = CreateViewModel(fakes, ws.Object);
        vm.SelfUserId = 1052;
        var friend = new GeneralsOnlineFriend { UserId = 300, DisplayName = "Alice" };
        vm.Friends.Add(friend);

        var incoming = new GeneralsOnlineFriendChatMessage
        {
            SourceUserId = 300,
            TargetUserId = 1052,
            Message = "Ready for 1v1?",
        };

        // Act
        ws.Raise(w => w.FriendChatReceived += null, ws.Object, incoming);
        Dispatcher.UIThread.RunJobs();

        // Assert
        Assert.False(incoming.IsOwn);
        Assert.Equal(1, vm.DmUnreadCount);
        Assert.True(vm.HasDmUnread);
        fakes.Notifications.Verify(n => n.ShowInfo(It.IsAny<string>(), "Ready for 1v1?", It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// Tests that updating AuthState raises PropertyChanged for IsAuthenticated.
    /// </summary>
    [Fact]
    public void AuthState_WhenChanged_ShouldRaisePropertyChangedForIsAuthenticated()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: false);
        using var vm = CreateViewModel(fakes);
        var isAuthenticatedChangedRaised = false;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.IsAuthenticated))
            {
                isAuthenticatedChangedRaised = true;
            }
        };

        // Act
        vm.AuthState = GeneralsOnlineAuthState.Authenticated;

        // Assert
        Assert.True(isAuthenticatedChangedRaised);
    }

    /// <summary>
    /// Tests that rapid concurrent social actions for the same user are collapsed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AcceptFriendAsync_WhenCalledConcurrently_ShouldExecuteOnlyOnceAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var tcs = new TaskCompletionSource<OperationResult<bool>>();
        fakes.Api
            .Setup(a => a.AcceptFriendRequestAsync(500, It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        using var vm = CreateViewModel(fakes);
        vm.AuthState = GeneralsOnlineAuthState.Authenticated;
        var friend = new GeneralsOnlineFriend { UserId = 500, DisplayName = "Bob" };

        // Act
        var firstCall = vm.AcceptFriendCommand.ExecuteAsync(friend);
        var secondCall = vm.AcceptFriendCommand.ExecuteAsync(friend);

        tcs.SetResult(OperationResult<bool>.CreateSuccess(true));
        await Task.WhenAll(firstCall, secondCall);

        // Assert
        fakes.Api.Verify(a => a.AcceptFriendRequestAsync(500, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that receiving friend chat updates the DM messages when the friend is currently open.
    /// </summary>
    [Fact]
    public void FriendChatReceived_WhenDmFriendOpen_ShouldAppendToDmMessages()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var wsListenerMock = new Mock<IGeneralsOnlineWebSocketListener>();
        using var vm = CreateViewModel(fakes, wsListenerMock.Object);
        var friend = new GeneralsOnlineFriend { UserId = 42, DisplayName = "Alice" };
        vm.OpenDmCommand.Execute(friend);

        // Act
        wsListenerMock.Raise(
            w => w.FriendChatReceived += null,
            wsListenerMock.Object,
            new GeneralsOnlineFriendChatMessage
            {
                SourceUserId = 42,
                TargetUserId = 1052,
                Message = "Hello from Alice",
            });

        // Assert
        Assert.Single(vm.DmMessages);
        Assert.Equal("Hello from Alice", vm.DmMessages[0].Message);
        Assert.False(vm.DmMessages[0].IsOwn);
    }

    /// <summary>
    /// Tests that reassigning the chat room with the same room ID does not clear room chat messages.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ChatRoomChanged_SameRoomId_ShouldNotClearRoomChatMessagesAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var wsListenerMock = new Mock<IGeneralsOnlineWebSocketListener>();
        wsListenerMock.SetupGet(w => w.IsConnected).Returns(true);
        wsListenerMock
            .Setup(w => w.SelectNetworkRoomAsync(It.IsAny<short>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        using var vm = CreateViewModel(fakes, wsListenerMock.Object);

        var room1 = new GeneralsOnlineRoom { Id = 1, Name = "Room 1" };
        vm.ChatRoom = room1;
        await Task.Delay(50);

        vm.RoomChatMessages.Add(new GeneralsOnlineRoomChatMessage { Message = "Message in Room 1" });
        Assert.Single(vm.RoomChatMessages);

        // Act - Reassign same room ID
        var room1Refreshed = new GeneralsOnlineRoom { Id = 1, Name = "Room 1 Updated" };
        vm.ChatRoom = room1Refreshed;
        await Task.Delay(50);

        // Assert
        Assert.Single(vm.RoomChatMessages);
        Assert.Equal("Message in Room 1", vm.RoomChatMessages[0].Message);
    }

    /// <summary>
    /// Tests that switching to a different chat room ID clears room chat messages.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ChatRoomChanged_DifferentRoomId_ShouldClearRoomChatMessagesAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var wsListenerMock = new Mock<IGeneralsOnlineWebSocketListener>();
        wsListenerMock.SetupGet(w => w.IsConnected).Returns(true);
        wsListenerMock
            .Setup(w => w.SelectNetworkRoomAsync(It.IsAny<short>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        using var vm = CreateViewModel(fakes, wsListenerMock.Object);

        var room1 = new GeneralsOnlineRoom { Id = 1, Name = "Room 1" };
        vm.ChatRoom = room1;
        await Task.Delay(50);

        vm.RoomChatMessages.Add(new GeneralsOnlineRoomChatMessage { Message = "Message in Room 1" });
        Assert.Single(vm.RoomChatMessages);

        // Act - Switch to different room ID
        var room2 = new GeneralsOnlineRoom { Id = 2, Name = "Room 2" };
        vm.ChatRoom = room2;
        await Task.Delay(50);

        // Assert
        Assert.Empty(vm.RoomChatMessages);
    }

    /// <summary>
    /// Tests that an in-flight ranking failure for a superseded lobby selection does not clear
    /// the ranked profiles of the newly selected lobby.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SelectedLobby_WhenSupersededRankingFails_ShouldNotClearNewLobbyProfilesAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var tcs1 = new TaskCompletionSource<OperationResult<IReadOnlyList<GeneralsOnlineProfileMatch>>>();
        var lobby1 = SampleLobby(id: 1, name: "Lobby 1");
        var lobby2 = SampleLobby(id: 2, name: "Lobby 2");

        fakes.Compatibility
            .Setup(c => c.RankProfilesAsync(It.Is<GeneralsOnlineLobby>(l => l.LobbyId == 1), It.IsAny<CancellationToken>()))
            .Returns(tcs1.Task);

        fakes.Compatibility
            .Setup(c => c.RankProfilesAsync(It.Is<GeneralsOnlineLobby>(l => l.LobbyId == 2), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GeneralsOnlineProfileMatch>>.CreateSuccess(
                [new GeneralsOnlineProfileMatch("p2", "Profile 2", GeneralsOnlineCompatibility.Compatible, 1, 2)]));

        using var vm = CreateViewModel(fakes);

        // Act - select Lobby 1 (starts ranking 1)
        vm.SelectedLobby = lobby1;
        Assert.Equal(lobby1, vm.SelectedLobby);

        // Switch selection to Lobby 2 before ranking 1 completes
        vm.SelectedLobby = lobby2;
        await Task.Yield();

        // Lobby 2 should now have profiles loaded
        Assert.Single(vm.RankedProfilesForSelected);
        Assert.Equal("p2", vm.RankedProfilesForSelected[0].ProfileId);

        // Now ranking 1 finishes with failure
        tcs1.SetResult(OperationResult<IReadOnlyList<GeneralsOnlineProfileMatch>>.CreateFailure("Rank failed"));
        await Task.Yield();

        // Assert - Lobby 2 profiles should NOT be wiped by Lobby 1 failure
        Assert.Single(vm.RankedProfilesForSelected);
        Assert.Equal("p2", vm.RankedProfilesForSelected[0].ProfileId);
    }

    /// <summary>
    /// Tests that refreshing lobbies resets each lobby's local compatibility to Unknown before re-ranking.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RefreshLobbies_ShouldResetLobbyCompatibilityToUnknownBeforeRankingAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var lobby = SampleLobby(id: 42, name: "Test Lobby");
        lobby.LocalCompatibility = GeneralsOnlineCompatibility.Compatible;

        var lobbiesResult = new GeneralsOnlineLobbiesResult { Lobbies = [lobby] };
        fakes.Api
            .Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(lobbiesResult));

        GeneralsOnlineCompatibility? compatibilityDuringInitialRanking = null;
        fakes.Compatibility
            .Setup(c => c.RankProfilesAsync(It.IsAny<GeneralsOnlineLobby>(), It.IsAny<CancellationToken>()))
            .Callback<GeneralsOnlineLobby, CancellationToken>((l, _) => compatibilityDuringInitialRanking ??= l.LocalCompatibility)
            .ReturnsAsync(OperationResult<IReadOnlyList<GeneralsOnlineProfileMatch>>.CreateSuccess(
                [new GeneralsOnlineProfileMatch("p1", "Profile 1", GeneralsOnlineCompatibility.Compatible, 1, 2)]));

        using var vm = CreateViewModel(fakes);

        // Act
        await vm.RefreshCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(GeneralsOnlineCompatibility.Unknown, compatibilityDuringInitialRanking);
    }

    /// <summary>
    /// Tests that when self user ID is unknown and a DM is open with user A, an incoming message
    /// from friend B is attributed to B and not dropped.
    /// </summary>
    [Fact]
    public void FriendChatReceived_WhenSelfIdUnknownAndDmFriendOpen_IncomingFromOtherFriend_ShouldAttributeToSender()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: false);
        var wsListenerMock = new Mock<IGeneralsOnlineWebSocketListener>();
        using var vm = CreateViewModel(fakes, wsListenerMock.Object);

        var alice = new GeneralsOnlineFriend { UserId = 42, DisplayName = "Alice" };
        var bob = new GeneralsOnlineFriend { UserId = 500, DisplayName = "Bob" };
        vm.Friends.Add(alice);
        vm.Friends.Add(bob);

        // Open DM with Alice while SelfUserId is still unknown (<= 0)
        vm.OpenDmCommand.Execute(alice);
        Assert.Equal(-1, vm.SelfUserId);
        Assert.Equal(42, vm.DmFriend?.UserId);

        // Act - Message arrives from Bob
        wsListenerMock.Raise(
            w => w.FriendChatReceived += null,
            wsListenerMock.Object,
            new GeneralsOnlineFriendChatMessage
            {
                SourceUserId = 500,
                TargetUserId = 0,
                Message = "Hey from Bob",
            });

        // Assert - Alice's DM messages should not receive Bob's message
        Assert.Empty(vm.DmMessages);

        // Bob has an unread message count
        Assert.Equal(1, vm.DmUnreadCount);
        Assert.True(vm.HasDmUnread);

        // Switching to Bob displays the message
        vm.OpenDmCommand.Execute(bob);
        Assert.Single(vm.DmMessages);
        Assert.Equal("Hey from Bob", vm.DmMessages[0].Message);
        Assert.False(vm.DmMessages[0].IsOwn);
    }

    /// <summary>
    /// Tests that when self user ID is unknown, an echo of our own message to a friend recognizes
    /// SelfUserId and is marked as own rather than attributed as an incoming message from ourselves.
    /// </summary>
    [Fact]
    public void FriendChatReceived_WhenSelfIdUnknown_EchoToFriend_ShouldRecognizeOwnMessage()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: false);
        var wsListenerMock = new Mock<IGeneralsOnlineWebSocketListener>();
        using var vm = CreateViewModel(fakes, wsListenerMock.Object);

        var bob = new GeneralsOnlineFriend { UserId = 500, DisplayName = "Bob" };
        vm.Friends.Add(bob);

        // Act - Echo arrives where TargetUserId is Bob and SourceUserId is our user ID (999)
        wsListenerMock.Raise(
            w => w.FriendChatReceived += null,
            wsListenerMock.Object,
            new GeneralsOnlineFriendChatMessage
            {
                SourceUserId = 999,
                TargetUserId = 500,
                Message = "Sent to Bob",
            });

        // Assert - SelfUserId should be discovered as 999
        Assert.Equal(999, vm.SelfUserId);

        // Bob should not have an unread badge because it is our own echo
        Assert.Equal(0, vm.DmUnreadCount);
        Assert.False(vm.HasDmUnread);

        // Opening Bob's DM shows the message with IsOwn == true
        vm.OpenDmCommand.Execute(bob);
        Assert.Single(vm.DmMessages);
        Assert.Equal("Sent to Bob", vm.DmMessages[0].Message);
        Assert.True(vm.DmMessages[0].IsOwn);
    }

    /// <summary>
    /// Tests that when self user ID is unknown, an incoming message from an unknown user not in Friends is dropped.
    /// </summary>
    [Fact]
    public void FriendChatReceived_WhenSelfIdUnknown_UnknownSender_ShouldDropMessage()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: false);
        var wsListenerMock = new Mock<IGeneralsOnlineWebSocketListener>();
        using var vm = CreateViewModel(fakes, wsListenerMock.Object);

        var alice = new GeneralsOnlineFriend { UserId = 42, DisplayName = "Alice" };
        vm.Friends.Add(alice);
        vm.OpenDmCommand.Execute(alice);

        // Act - Message arrives from unknown sender 8888 who is not in Friends
        wsListenerMock.Raise(
            w => w.FriendChatReceived += null,
            wsListenerMock.Object,
            new GeneralsOnlineFriendChatMessage
            {
                SourceUserId = 8888,
                TargetUserId = 0,
                Message = "Spam",
            });

        // Assert - Message should be dropped; Alice has no messages and no unread
        Assert.Empty(vm.DmMessages);
        Assert.Equal(0, vm.DmUnreadCount);
        Assert.False(vm.HasDmUnread);
    }

    /// <summary>
    /// Tests that when self user ID is unknown, a frame where source equals target (degenerate)
    /// or where source is already in friends (relayed friend-to-friend DM) does not poison SelfUserId.
    /// </summary>
    [Fact]
    public void FriendChatReceived_WhenSelfIdUnknown_RelayedOrDegenerateFrame_ShouldNotLearnSelfId()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: false);
        var wsListenerMock = new Mock<IGeneralsOnlineWebSocketListener>();
        using var vm = CreateViewModel(fakes, wsListenerMock.Object);

        var bob = new GeneralsOnlineFriend { UserId = 500, DisplayName = "Bob" };
        var charlie = new GeneralsOnlineFriend { UserId = 600, DisplayName = "Charlie" };
        vm.Friends.Add(bob);
        vm.Friends.Add(charlie);

        // Case 1: Degenerate frame where source == target == 500
        wsListenerMock.Raise(
            w => w.FriendChatReceived += null,
            wsListenerMock.Object,
            new GeneralsOnlineFriendChatMessage
            {
                SourceUserId = 500,
                TargetUserId = 500,
                Message = "Degenerate loopback",
            });

        Assert.Equal(-1, vm.SelfUserId);

        // Case 2: Relayed frame between two friends (Charlie -> Bob)
        wsListenerMock.Raise(
            w => w.FriendChatReceived += null,
            wsListenerMock.Object,
            new GeneralsOnlineFriendChatMessage
            {
                SourceUserId = 600,
                TargetUserId = 500,
                Message = "Charlie talking to Bob",
            });

        Assert.Equal(-1, vm.SelfUserId);
    }

    /// <summary>
    /// Tests that when lobby fields such as Region, Owner, MapPath, or TimeCreated change on an incoming lobby update,
    /// the lobby instance in the collection is updated rather than retaining stale values.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RefreshAsync_WhenLobbyFieldChanges_ShouldUpdateLobbyInCollectionAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var initialLobby = SampleLobby(9842, "[EU] Pro 1v1");
        initialLobby.Region = "EU";
        initialLobby.MapPath = "Maps/td.map";
        initialLobby.TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        initialLobby.Owner = 1052;

        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
                new GeneralsOnlineLobbiesResult { Lobbies = [initialLobby], Latencies = [25] }));

        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();
        Assert.Single(vm.Lobbies);
        Assert.Equal("EU", vm.Lobbies[0].Region);

        // Act - Updated lobby with new Region, Owner, MapPath, TimeCreated
        var updatedLobby = SampleLobby(9842, "[EU] Pro 1v1");
        updatedLobby.Region = "NA";
        updatedLobby.MapPath = "Maps/updated.map";
        updatedLobby.TimeCreated = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        updatedLobby.Owner = 9999;

        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
                new GeneralsOnlineLobbiesResult { Lobbies = [updatedLobby], Latencies = [30] }));

        await vm.RefreshAsync();

        // Assert
        Assert.Single(vm.Lobbies);
        Assert.Equal("NA", vm.Lobbies[0].Region);
        Assert.Equal("Maps/updated.map", vm.Lobbies[0].MapPath);
        Assert.Equal(9999, vm.Lobbies[0].Owner);
        Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), vm.Lobbies[0].TimeCreated);
    }

    /// <summary>
    /// Tests that when only StartingCash changes on an incoming lobby update,
    /// the lobby instance in the collection is updated rather than retaining the stale value.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RefreshAsync_WhenOnlyStartingCashChanges_ShouldUpdateLobbyInCollectionAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var initialLobby = SampleLobby(9842, "[EU] Pro 1v1");
        initialLobby.StartingCash = 10000;

        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
                new GeneralsOnlineLobbiesResult { Lobbies = [initialLobby], Latencies = [25] }));

        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();
        Assert.Single(vm.Lobbies);
        Assert.Equal(10000u, vm.Lobbies[0].StartingCash);

        // Act - Only StartingCash differs
        var updatedLobby = SampleLobby(9842, "[EU] Pro 1v1");
        updatedLobby.StartingCash = 20000u;

        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
                new GeneralsOnlineLobbiesResult { Lobbies = [updatedLobby], Latencies = [25] }));

        await vm.RefreshAsync();

        // Assert
        Assert.Single(vm.Lobbies);
        Assert.Equal(20000u, vm.Lobbies[0].StartingCash);
    }

    private static GeneralsOnlineLobby SampleLobby(long id = 9842, string name = "[EU] Pro 1v1")
    {
        return new GeneralsOnlineLobby
        {
            LobbyId = id,
            Owner = 1052,
            Name = name,
            State = GeneralsOnlineLobbyState.GameSetup,
            MapName = "Tournament Desert",
            IsMapOfficial = true,
            ExeCrc = 2_948_194_012U,
            IniCrc = 1_048_576_021U,
            Members =
            [
                new GeneralsOnlineLobbyMember { UserId = 1052, DisplayName = "GeneralAlex", SlotIndex = 0, SlotState = GeneralsOnlineSlotState.SlotPlayer },
                new GeneralsOnlineLobbyMember { UserId = -1, DisplayName = "Open", SlotIndex = 1, SlotState = GeneralsOnlineSlotState.SlotOpen },
                new GeneralsOnlineLobbyMember { UserId = 1053, DisplayName = "Rookie", SlotIndex = 2, SlotState = GeneralsOnlineSlotState.SlotPlayer },
            ],
        };
    }

    private static GeneralsOnlineLobby SampleModLobby()
    {
        var lobby = SampleLobby(777, "[Contra] 3v3 Mountain King");
        lobby.MapName = "Mountain King";
        lobby.IsMapOfficial = false;
        lobby.IniCrc = 0x3E84F102u;
        return lobby;
    }

    private sealed record Fakes(
        Mock<IGeneralsOnlineApiClient> Api,
        Mock<IGeneralsOnlineAuthService> Auth,
        Mock<IGeneralsOnlineCompatibilityService> Compatibility,
        Mock<INotificationService> Notifications,
        Mock<IOnlineLaunchService> Launch);

    private static Fakes CreateFakes(bool authenticated)
    {
        var state = authenticated ? GeneralsOnlineAuthState.Authenticated : GeneralsOnlineAuthState.Unauthenticated;
        var api = new Mock<IGeneralsOnlineApiClient>();
        api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(
                new GeneralsOnlineLobbiesResult { Lobbies = [SampleLobby(), SampleModLobby()], Latencies = [42] }));
        api.Setup(a => a.GetPublicCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlinePublicCounts>.CreateSuccess(new GeneralsOnlinePublicCounts(7, 140)));
        api.Setup(a => a.GetServiceUptimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineServiceUptime>.CreateSuccess(
                new GeneralsOnlineServiceUptime { StartTime = "2026-09-26 01:24:45", Uptime = "Days: 2, Hours: 0, Minutes: 59" }));
        api.Setup(a => a.GetGlobalStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineDailyStats>.CreateSuccess(
                new GeneralsOnlineDailyStats { Matches = [10, 5], Wins = [7, 2] }));
        api.Setup(a => a.GetPlayerStatsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlinePlayerStats>.CreateSuccess(
                new GeneralsOnlinePlayerStats { UserId = 1052, EloRating = 1450, EloMatches = 31, Wins = [12, 8], Losses = [6, 5], Games = [18, 13] }));
        api.Setup(a => a.GetMotdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess("Welcome, commanders!"));
        api.Setup(a => a.GetFriendsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineFriendsResult>.CreateSuccess(new GeneralsOnlineFriendsResult()));
        api.Setup(a => a.GetRoomsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GeneralsOnlineRoom>>.CreateSuccess([]));
        api.Setup(a => a.GetBlockedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineBlockedResult>.CreateSuccess(new GeneralsOnlineBlockedResult()));
        api.Setup(a => a.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineMe>.CreateSuccess(
                new GeneralsOnlineMe { UserId = authenticated ? 1052 : 0, DisplayName = "PlayerName" }));

        var auth = new Mock<IGeneralsOnlineAuthService>();
        auth.SetupGet(a => a.AuthState).Returns(state);
        auth.SetupGet(a => a.CurrentDisplayName).Returns(authenticated ? "PlayerName" : null);
        auth.SetupGet(a => a.CurrentUserId).Returns(authenticated ? 1052 : null);
        auth.SetupGet(a => a.WebSocketUri).Returns("wss://api.playgenerals.online/ws");
        auth.Setup(a => a.GetSessionTokenAsync()).ReturnsAsync(authenticated ? "session" : null);
        auth.Setup(a => a.TryLoginWithStoredTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorAuthRequired));
        auth.Setup(a => a.LogoutAsync(It.IsAny<CancellationToken>()))
            .Callback(() => auth.SetupGet(a => a.AuthState).Returns(GeneralsOnlineAuthState.Unauthenticated))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var compatibility = new Mock<IGeneralsOnlineCompatibilityService>();
        compatibility.Setup(c => c.RankProfilesAsync(It.IsAny<GeneralsOnlineLobby>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GeneralsOnlineLobby lobby, CancellationToken _) =>
                OperationResult<IReadOnlyList<GeneralsOnlineProfileMatch>>.CreateSuccess(
                    lobby.LobbyId == 9842
                        ? [new GeneralsOnlineProfileMatch("profile-vanilla", "Zero Hour 1.04 Vanilla", GeneralsOnlineCompatibility.Compatible, 2_948_194_012U, 1_048_576_021U)]
                        : [new GeneralsOnlineProfileMatch("profile-contra", "Contra 009", GeneralsOnlineCompatibility.IniMismatch, 2_948_194_012U, 1U)]));

        var notifications = new Mock<INotificationService>();
        var launch = new Mock<IOnlineLaunchService>();
        launch.Setup(l => l.PlayAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<OnlinePlayResult>.CreateSuccess(new OnlinePlayResult("profile-vanilla", "Zero Hour 1.04 Vanilla", "[EU] Pro 1v1")));

        return new Fakes(api, auth, compatibility, notifications, launch);
    }

    /// <summary>
    /// Tests that an expired session on lobbies load recovers via stored-token login and retries.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_WhenLobbiesReturnAuthRequired_ShouldAttemptSessionRecoveryAndRetryAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var ws = new Mock<IGeneralsOnlineWebSocketListener>();
        ws.SetupGet(w => w.IsConnected).Returns(true);
        ws.Setup(w => w.ConnectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var testLobby = new GeneralsOnlineLobby { LobbyId = 9842, MapName = "Tournament Desert" };
        var successResult = new GeneralsOnlineLobbiesResult { Lobbies = [testLobby] };

        fakes.Api.SetupSequence(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateFailure(GeneralsOnlineConstants.ErrorAuthRequired))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(successResult));

        fakes.Auth.Setup(a => a.TryLoginWithStoredTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LoginResult>.CreateSuccess(new LoginResult { Token = "new_token" }));

        using var vm = CreateViewModel(fakes, ws.Object);

        // Act
        await vm.RefreshAsync();

        // Assert
        fakes.Auth.Verify(a => a.TryLoginWithStoredTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
        ws.Verify(w => w.ConnectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        Assert.Single(vm.Lobbies);
        Assert.True(vm.IsAuthenticated);
    }

    /// <summary>
    /// Tests that session recovery failure falls back to unauthenticated and clears lobbies.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Refresh_WhenSessionRecoveryFails_ShouldFallbackToUnauthenticatedAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateFailure(GeneralsOnlineConstants.ErrorAuthRequired));

        fakes.Auth.Setup(a => a.TryLoginWithStoredTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorAuthRequired));

        using var vm = CreateViewModel(fakes);

        // Act
        await vm.RefreshAsync();

        // Assert
        fakes.Auth.Verify(a => a.TryLoginWithStoredTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
        fakes.Auth.Verify(a => a.LogoutAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(vm.IsAuthenticated);
        Assert.Empty(vm.Lobbies);
    }

    /// <summary>
    /// Tests that launch errors or unlocalized English details show generic error detail in toasts.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Launch_WhenLaunchFailsWithOnlineCode_ShouldShowGenericDetailToastAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: true);
        var lobby = new GeneralsOnlineLobby { LobbyId = 9842, MapName = "Tournament Desert" };
        fakes.Api.Setup(a => a.GetLobbiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineLobbiesResult>.CreateSuccess(new GeneralsOnlineLobbiesResult { Lobbies = [lobby] }));

        fakes.Launch.Setup(l => l.PlayAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<OnlinePlayResult>.CreateFailure(OnlineConstants.ErrorLaunchFailed));

        using var vm = CreateViewModel(fakes);
        await vm.RefreshAsync();
        vm.SelectedLobby = vm.Lobbies.FirstOrDefault();

        // Act
        await vm.LaunchCommand.ExecuteAsync(null);

        // Assert
        fakes.Notifications.Verify(
            n => n.ShowError(
                "Online.GeneralsOnline.Launch.FailedTitle",
                "Online.Error.GenericDetail",
                NotificationDurations.Long,
                false),
            Times.Once);
    }

    /// <summary>
    /// Tests that cancelling browser sign-in resets loading and signing in states.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SignIn_WhenCancelled_ShouldResetStateAsync()
    {
        // Arrange
        var fakes = CreateFakes(authenticated: false);
        var tcs = new TaskCompletionSource<OperationResult<LoginResult>>();

        fakes.Auth.Setup(a => a.LoginWithBrowserAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
                return await tcs.Task;
            });

        using var vm = CreateViewModel(fakes);

        // Act
        var signInTask = vm.SignInCommand.ExecuteAsync(null);
        Assert.True(vm.IsSigningIn);
        Assert.True(vm.IsLoading);
        Assert.True(vm.CanCancelSignIn);

        vm.CancelSignInCommand.Execute(null);
        await signInTask;

        // Assert
        Assert.False(vm.IsSigningIn);
        Assert.False(vm.IsLoading);
        Assert.False(vm.IsAuthenticated);
    }

    /// <summary>
    /// Tests chat message local time conversion.
    /// </summary>
    [Fact]
    public void ChatMessages_ReceivedAtLocal_ConvertsUtcToLocal()
    {
        var utc = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        var roomMsg = new GeneralsOnlineRoomChatMessage { ReceivedAtUtc = utc };
        var friendMsg = new GeneralsOnlineFriendChatMessage { ReceivedAtUtc = utc };

        Assert.Equal(utc.ToLocalTime(), roomMsg.ReceivedAtLocal);
        Assert.Equal(utc.ToLocalTime(), friendMsg.ReceivedAtLocal);
    }

    /// <summary>
    /// Tests that GeneralsOnlineConstants.LogoSource points to the existing logo asset.
    /// </summary>
    [Fact]
    public void LogoSource_PointsToGeneralsOnlineLogo()
    {
        Assert.Equal("avares://GenHub/Assets/Logos/generalsonline-logo.png", GeneralsOnlineConstants.LogoSource);
    }

    private static GeneralsOnlineLobbiesViewModel CreateViewModel(
        Fakes? fakes = null,
        IGeneralsOnlineWebSocketListener? wsListener = null)
    {
        fakes ??= CreateFakes(authenticated: false);
        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] args) => args.Length == 0 ? key : $"{key} ({string.Join(", ", args)})");

        return new GeneralsOnlineLobbiesViewModel(
            fakes.Api.Object,
            fakes.Auth.Object,
            fakes.Compatibility.Object,
            fakes.Notifications.Object,
            Mock.Of<ILogger<GeneralsOnlineLobbiesViewModel>>(),
            new GeneralsOnlineLobbiesDependencies(
                wsListener ?? Mock.Of<IGeneralsOnlineWebSocketListener>(),
                fakes.Launch.Object,
                Mock.Of<IGameProfileManager>(),
                localization.Object,
                null));
    }
}
