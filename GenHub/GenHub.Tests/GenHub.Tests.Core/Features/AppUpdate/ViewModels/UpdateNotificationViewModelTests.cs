using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Common;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.AppUpdate.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.AppUpdate.ViewModels;

/// <summary>
/// Unit tests for <see cref="UpdateNotificationViewModel"/> with Velopack integration.
/// </summary>
public class UpdateNotificationViewModelTests
{
    /// <summary>
    /// Verifies that when no update is available, status is updated correctly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesCommand_WhenNoUpdateAvailable_UpdatesStatusAsync()
    {
        var mockVelopack = new Mock<IVelopackUpdateManager>();
        mockVelopack.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync((Velopack.UpdateInfo?)null);

        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());

        var vm = new UpdateNotificationViewModel(
            mockVelopack.Object,
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object);

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.CheckForUpdatesCommand).ExecuteAsync(null);

        Assert.False(vm.IsUpdateAvailable);
        Assert.Contains("up to date", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that constructor initializes properly.
    /// </summary>
    [Fact]
    public void Constructor_InitializesSuccessfully()
    {
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());

        var vm = new UpdateNotificationViewModel(
            Mock.Of<IVelopackUpdateManager>(),
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object);

        Assert.NotNull(vm);
        Assert.False(vm.IsUpdateAvailable);
        Assert.False(vm.IsChecking);
        Assert.False(vm.IsInstalling);
    }

    /// <summary>
    /// Verifies that check button state reflects checking status.
    /// </summary>
    [Fact]
    public void IsCheckButtonEnabled_ReflectsCheckingState()
    {
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());

        var vm = new UpdateNotificationViewModel(
            Mock.Of<IVelopackUpdateManager>(),
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object);

        Assert.True(vm.IsCheckButtonEnabled);
    }

    /// <summary>
    /// Verifies that pull request display title formats properly with PR number and title.
    /// </summary>
    [Fact]
    public void PullRequestInfo_DisplayTitle_ShouldIncludePrNumberAndTitle()
    {
        var prInfo = new PullRequestInfo
        {
            Number = 265,
            Title = "feat: UI Downloads",
            BranchName = "feat/ui-downloads",
            Author = "developer",
            State = "open",
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        Assert.Equal("#265 - feat: UI Downloads", prInfo.DisplayTitle);
    }

    /// <summary>
    /// Verifies that subscribing to a PR loads artifacts and auto-selects the latest version.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SubscribeToPr_LoadsArtifactsAndAutoSelectsLatestVersionAsync()
    {
        var mockVelopack = new Mock<IVelopackUpdateManager>();
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());

        var artifacts = new List<ArtifactUpdateInfo>
        {
            new("0.0.1316-pr389", "e1212a5", 389, 1001, "https://github.com/test/run/1", 501, "genhub-velopack-linux-0.0.1316-pr389", DateTime.UtcNow, "https://github.com/test/art/1", 1024),
            new("0.0.1315-pr389", "a1b2c3d", 389, 1000, "https://github.com/test/run/0", 500, "genhub-velopack-linux-0.0.1315-pr389", DateTime.UtcNow.AddMinutes(-10), "https://github.com/test/art/0", 1024),
        };

        var loadTcs = new TaskCompletionSource<IReadOnlyList<ArtifactUpdateInfo>>();
        mockVelopack.Setup(x => x.GetArtifactsForPullRequestAsync(389, It.IsAny<CancellationToken>()))
            .Returns(async (int _, CancellationToken ct) =>
            {
                ct.Register(() => loadTcs.TrySetCanceled(ct));
                return await loadTcs.Task;
            });

        var vm = new UpdateNotificationViewModel(
            mockVelopack.Object,
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object);

        vm.SubscribeToPrCommand.Execute(389);

        Assert.True(vm.IsLoadingVersions);
        loadTcs.SetResult(artifacts);

        // wait briefly for async continuation
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (vm.IsLoadingVersions && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.False(vm.IsLoadingVersions);
        Assert.Equal(2, vm.AvailableVersions.Count);
        Assert.NotNull(vm.SelectedVersion);
        Assert.Equal("0.0.1316-pr389", vm.SelectedVersion.Version);
        Assert.Equal("e1212a5", vm.SelectedVersion.GitHash);
        Assert.True(vm.CanDownloadUpdate);
    }

    /// <summary>
    /// Verifies that subscribing to a branch loads artifacts and auto-selects the latest version.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SubscribeToBranch_LoadsArtifactsAndAutoSelectsLatestVersionAsync()
    {
        var mockVelopack = new Mock<IVelopackUpdateManager>();
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());

        var artifacts = new List<ArtifactUpdateInfo>
        {
            new("0.0.1320-development", "f4e3d2c", null, 2001, "https://github.com/test/run/2", 601, "genhub-velopack-linux-0.0.1320-development", DateTime.UtcNow, "https://github.com/test/art/2", 2048),
        };

        mockVelopack.Setup(x => x.GetArtifactsForBranchAsync("development", It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifacts);

        var vm = new UpdateNotificationViewModel(
            mockVelopack.Object,
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object);

        vm.SubscribeToBranchCommand.Execute("development");

        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (vm.IsLoadingVersions && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.False(vm.IsLoadingVersions);
        Assert.Single(vm.AvailableVersions);
        Assert.NotNull(vm.SelectedVersion);
        Assert.Equal("0.0.1320-development", vm.SelectedVersion.Version);
    }

    /// <summary>
    /// Verifies that when switching PR subscriptions while a previous load is in flight, the old request is cancelled and only the new subscription artifacts are applied.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SubscribeToPr_WhenSwitchedImmediately_CancelsPreviousLoadAndLoadsNewSubscriptionAsync()
    {
        var mockVelopack = new Mock<IVelopackUpdateManager>();
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());

        var pr391Tcs = new TaskCompletionSource<IReadOnlyList<ArtifactUpdateInfo>>();
        var pr389Tcs = new TaskCompletionSource<IReadOnlyList<ArtifactUpdateInfo>>();

        mockVelopack.Setup(x => x.GetArtifactsForPullRequestAsync(391, It.IsAny<CancellationToken>()))
            .Returns(async (int _, CancellationToken ct) =>
            {
                ct.Register(() => pr391Tcs.TrySetCanceled(ct));
                return await pr391Tcs.Task;
            });

        mockVelopack.Setup(x => x.GetArtifactsForPullRequestAsync(389, It.IsAny<CancellationToken>()))
            .Returns(async (int _, CancellationToken ct) =>
            {
                ct.Register(() => pr389Tcs.TrySetCanceled(ct));
                return await pr389Tcs.Task;
            });

        var vm = new UpdateNotificationViewModel(
            mockVelopack.Object,
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object);

        // subscribe to 391 first
        vm.SubscribeToPrCommand.Execute(391);
        Assert.True(vm.IsLoadingVersions);

        // immediately switch to 389 while 391 is loading
        vm.SubscribeToPrCommand.Execute(389);

        // resolve 389 artifacts
        var pr389Artifacts = new List<ArtifactUpdateInfo>
        {
            new("0.0.1316-pr389", "e1212a5", 389, 1001, "https://github.com/test/run/1", 501, "genhub-velopack-linux-0.0.1316-pr389", DateTime.UtcNow, "https://github.com/test/art/1", 1024),
        };
        pr389Tcs.TrySetResult(pr389Artifacts);

        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (vm.IsLoadingVersions && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.True(pr391Tcs.Task.IsCanceled);
        Assert.False(vm.IsLoadingVersions);
        Assert.Single(vm.AvailableVersions);
        Assert.NotNull(vm.SelectedVersion);
        Assert.Equal("0.0.1316-pr389", vm.SelectedVersion.Version);
        Assert.Equal(389, vm.SelectedVersion.PullRequestNumber);
    }

    /// <summary>
    /// Verifies that switching from a branch to another branch cancels the previous load and populates the new branch artifacts.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SubscribeToBranch_WhenSwitchedImmediately_CancelsPreviousLoadAndLoadsNewBranchAsync()
    {
        var mockVelopack = new Mock<IVelopackUpdateManager>();
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());

        var branchOldTcs = new TaskCompletionSource<IReadOnlyList<ArtifactUpdateInfo>>();
        var branchNewTcs = new TaskCompletionSource<IReadOnlyList<ArtifactUpdateInfo>>();

        mockVelopack.Setup(x => x.GetArtifactsForBranchAsync("old-branch", It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken ct) =>
            {
                ct.Register(() => branchOldTcs.TrySetCanceled(ct));
                return await branchOldTcs.Task;
            });

        mockVelopack.Setup(x => x.GetArtifactsForBranchAsync("new-branch", It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken ct) =>
            {
                ct.Register(() => branchNewTcs.TrySetCanceled(ct));
                return await branchNewTcs.Task;
            });

        var vm = new UpdateNotificationViewModel(
            mockVelopack.Object,
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object);

        vm.SubscribeToBranchCommand.Execute("old-branch");
        Assert.True(vm.IsLoadingVersions);

        vm.SubscribeToBranchCommand.Execute("new-branch");

        var newArtifacts = new List<ArtifactUpdateInfo>
        {
            new("0.0.1400-new-branch", "9998887", null, 3001, "https://github.com/test/run/3", 701, "genhub-velopack-linux-0.0.1400-new-branch", DateTime.UtcNow, "https://github.com/test/art/3", 2048),
        };
        branchNewTcs.TrySetResult(newArtifacts);

        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (vm.IsLoadingVersions && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.True(branchOldTcs.Task.IsCanceled);
        Assert.False(vm.IsLoadingVersions);
        Assert.Single(vm.AvailableVersions);
        Assert.NotNull(vm.SelectedVersion);
        Assert.Equal("0.0.1400-new-branch", vm.SelectedVersion.Version);
    }

    /// <summary>
    /// Verifies that unsubscribing cancels in-flight loads and clears available versions and selection.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task Unsubscribe_CancelsInFlightLoadsAndClearsAvailableVersionsAsync()
    {
        var mockVelopack = new Mock<IVelopackUpdateManager>();
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());

        var prTcs = new TaskCompletionSource<IReadOnlyList<ArtifactUpdateInfo>>();
        mockVelopack.Setup(x => x.GetArtifactsForPullRequestAsync(391, It.IsAny<CancellationToken>()))
            .Returns(async (int _, CancellationToken ct) =>
            {
                ct.Register(() => prTcs.TrySetCanceled(ct));
                return await prTcs.Task;
            });

        var vm = new UpdateNotificationViewModel(
            mockVelopack.Object,
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object);

        vm.SubscribeToPrCommand.Execute(391);
        Assert.True(vm.IsLoadingVersions);

        vm.UnsubscribeCommand.Execute(null);

        var timeout = DateTime.UtcNow.AddSeconds(2);
        while ((vm.IsLoadingVersions || vm.AvailableVersions.Count > 0) && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.True(prTcs.Task.IsCanceled);
        Assert.False(vm.IsLoadingVersions);
        Assert.Empty(vm.AvailableVersions);
        Assert.Null(vm.SelectedVersion);
    }

    /// <summary>
    /// Verifies that OpenPullRequestUrlCommand executes without error for valid and invalid PR numbers.
    /// </summary>
    /// <param name="prNumber">The PR number under test.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void OpenPullRequestUrlCommand_ExecutesWithoutException(int prNumber)
    {
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());

        var vm = new UpdateNotificationViewModel(
            Mock.Of<IVelopackUpdateManager>(),
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object);

        // verify command execution does not throw
        vm.OpenPullRequestUrlCommand.Execute(prNumber);
        Assert.NotNull(vm);
    }

    /// <summary>
    /// Verifies that changing the sort option reorders available pull requests accordingly.
    /// </summary>
    [Fact]
    public void SelectedSortOption_ReordersAvailablePullRequests()
    {
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());

        var vm = new UpdateNotificationViewModel(
            Mock.Of<IVelopackUpdateManager>(),
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object);

        var now = DateTimeOffset.UtcNow;
        var pr100 = new PullRequestInfo { Number = 100, Title = "PR 100", BranchName = "b1", Author = "a1", State = "open", UpdatedAt = now.AddDays(-2) };
        var pr200 = new PullRequestInfo { Number = 200, Title = "PR 200", BranchName = "b2", Author = "a2", State = "open", UpdatedAt = now.AddDays(-10) };
        var pr300 = new PullRequestInfo { Number = 300, Title = "PR 300", BranchName = "b3", Author = "a3", State = "open", UpdatedAt = now };

        vm.AvailablePullRequests.Add(pr100);
        vm.AvailablePullRequests.Add(pr200);
        vm.AvailablePullRequests.Add(pr300);

        // sort by PR number descending
        vm.SelectedSortOption = GenHub.Core.Constants.AppUpdateConstants.SortOptionPrNumberDesc;
        Assert.Equal(300, vm.AvailablePullRequests[0].Number);
        Assert.Equal(200, vm.AvailablePullRequests[1].Number);
        Assert.Equal(100, vm.AvailablePullRequests[2].Number);

        // sort by PR number ascending
        vm.SelectedSortOption = GenHub.Core.Constants.AppUpdateConstants.SortOptionPrNumberAsc;
        Assert.Equal(100, vm.AvailablePullRequests[0].Number);
        Assert.Equal(200, vm.AvailablePullRequests[1].Number);
        Assert.Equal(300, vm.AvailablePullRequests[2].Number);

        // sort by last updated (newest first)
        vm.SelectedSortOption = GenHub.Core.Constants.AppUpdateConstants.SortOptionLastUpdated;
        Assert.Equal(300, vm.AvailablePullRequests[0].Number);
        Assert.Equal(100, vm.AvailablePullRequests[1].Number);
        Assert.Equal(200, vm.AvailablePullRequests[2].Number);
    }

    /// <summary>
    /// Verifies that tab commands correctly switch between Update and Browse Builds tabs.
    /// </summary>
    [Fact]
    public void TabCommands_UpdatesSelectedTabIndexAndIsBrowseTabSelected()
    {
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());

        var vm = new UpdateNotificationViewModel(
            Mock.Of<IVelopackUpdateManager>(),
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object);

        Assert.Equal(0, vm.SelectedTabIndex);
        Assert.False(vm.IsBrowseTabSelected);

        vm.ShowBrowseBuildsTabCommand.Execute(null);
        Assert.Equal(1, vm.SelectedTabIndex);
        Assert.True(vm.IsBrowseTabSelected);

        vm.ShowUpdateTabCommand.Execute(null);
        Assert.Equal(0, vm.SelectedTabIndex);
        Assert.False(vm.IsBrowseTabSelected);

        vm.SelectTabCommand.Execute("1");
        Assert.Equal(1, vm.SelectedTabIndex);
        Assert.True(vm.IsBrowseTabSelected);

        vm.SelectTabCommand.Execute(0);
        Assert.Equal(0, vm.SelectedTabIndex);
        Assert.False(vm.IsBrowseTabSelected);

        // Clamping out-of-range inputs
        vm.SelectTabCommand.Execute(-1);
        Assert.Equal(0, vm.SelectedTabIndex);

        vm.SelectTabCommand.Execute(5);
        Assert.Equal(1, vm.SelectedTabIndex);

        vm.SelectTabCommand.Execute("99");
        Assert.Equal(1, vm.SelectedTabIndex);
    }

    /// <summary>
    /// Verifies that DisplayCurrentVersion and InstalledVersionDisplay return a valid non-empty version string.
    /// </summary>
    [Fact]
    public void DisplayCurrentVersion_ReturnsNonEmptyVersion()
    {
        var displayVersion = UpdateNotificationViewModel.DisplayCurrentVersion;
        Assert.False(string.IsNullOrWhiteSpace(displayVersion));
        Assert.StartsWith("v", displayVersion);

        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());

        var vm = new UpdateNotificationViewModel(
            Mock.Of<IVelopackUpdateManager>(),
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object);

        Assert.Equal(displayVersion, vm.InstalledVersionDisplay);
    }

    /// <summary>
    /// Verifies that setting SelectedVersion to a newer artifact updates StatusMessage and sets IsUpdateAvailable to true.
    /// </summary>
    [Fact]
    public void SelectedVersion_WhenNewer_UpdatesStatusMessageAndIsUpdateAvailable()
    {
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());

        var vm = new UpdateNotificationViewModel(
            Mock.Of<IVelopackUpdateManager>(),
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object);

        var newerArtifact = new ArtifactUpdateInfo("0.0.99999-pr389", "abcdef1", 389, 9999, "https://github.com/test/run/9999", 501, "genhub-linux", DateTime.UtcNow, "https://github.com/test/art/1", 1024);
        vm.SelectedVersion = newerArtifact;

        Assert.True(vm.IsUpdateAvailable);
        Assert.Equal("0.0.99999-pr389", vm.LatestVersion);
        Assert.Contains("0.0.99999-pr389", vm.StatusMessage);
    }

    /// <summary>
    /// Verifies that selecting an artifact matching dismissed version clears IsUpdateAvailable, LatestVersion, and ReleaseNotesUrl.
    /// </summary>
    [Fact]
    public void SelectedVersion_WhenDismissed_ClearsUpdateAvailableState()
    {
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings { DismissedUpdateVersion = "0.0.99999-pr389" });

        var vm = new UpdateNotificationViewModel(
            Mock.Of<IVelopackUpdateManager>(),
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object)
        {
            IsUpdateAvailable = true,
            LatestVersion = "0.0.88888",
            ReleaseNotesUrl = "https://example.com/notes",
        };

        var dismissedArtifact = new ArtifactUpdateInfo("0.0.99999-pr389", "abcdef1", 389, 9999, "https://github.com/test/run/9999", 501, "genhub-linux", DateTime.UtcNow, "https://github.com/test/art/1", 1024);
        vm.SelectedVersion = dismissedArtifact;

        Assert.False(vm.IsUpdateAvailable);
        Assert.Empty(vm.LatestVersion);
        Assert.Empty(vm.ReleaseNotesUrl);
        Assert.Contains("dismissed", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that Unsubscribe resets subscription fields, clears update available state, and updates status message.
    /// </summary>
    [Fact]
    public void Unsubscribe_ClearsArtifactUpdateStateAndSwitchesToMain()
    {
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings { SubscribedPrNumber = 389 });

        var mockVelopack = new Mock<IVelopackUpdateManager>();
        mockVelopack.SetupProperty(x => x.SubscribedPrNumber, 389);
        mockVelopack.SetupProperty(x => x.SubscribedBranch, null);

        var vm = new UpdateNotificationViewModel(
            mockVelopack.Object,
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object)
        {
            SubscribedPr = new PullRequestInfo
            {
                Number = 389,
                Title = "Test PR",
                BranchName = "feature/test",
                Author = "testuser",
                State = "open"
            },
            SelectedVersion = new ArtifactUpdateInfo("0.0.99999-pr389", "abcdef1", 389, 9999, "https://github.com/test/run/9999", 501, "genhub-linux", DateTime.UtcNow, "https://github.com/test/art/1", 1024),
            IsUpdateAvailable = true,
            LatestVersion = "0.0.99999-pr389",
            ReleaseNotesUrl = "https://example.com/notes",
        };

        vm.UnsubscribeCommand.Execute(null);

        Assert.Null(vm.SubscribedPr);
        Assert.Null(vm.SubscribedBranch);
        Assert.Null(vm.SelectedVersion);
        Assert.False(vm.IsUpdateAvailable);
        Assert.Empty(vm.LatestVersion);
        Assert.Empty(vm.ReleaseNotesUrl);
        Assert.Contains("MAIN", vm.StatusMessage);
        Assert.Null(mockVelopack.Object.SubscribedPrNumber);
    }
}