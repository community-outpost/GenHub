using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.SuperHackers;
using GenHub.Tests.Core.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Content.Services.SuperHackers;

using ContentType = GenHub.Core.Models.Enums.ContentType;

/// <summary>
/// Tests for <see cref="SuperHackersProfileReconciler"/>.
/// </summary>
public class SuperHackersProfileReconcilerTests
{
    private readonly Mock<ISuperHackersUpdateService> _updateServiceMock;
    private readonly Mock<IContentManifestPool> _manifestPoolMock;
    private readonly Mock<IContentOrchestrator> _contentOrchestratorMock;
    private readonly Mock<IContentReconciliationService> _reconciliationServiceMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IDialogService> _dialogServiceMock;
    private readonly Mock<IUserSettingsService> _userSettingsServiceMock;
    private readonly Mock<IGameProfileManager> _profileManagerMock;

    private readonly SuperHackersProfileReconciler _reconciler;

    /// <summary>
    /// Initializes a new instance of the <see cref="SuperHackersProfileReconcilerTests"/> class.
    /// </summary>
    public SuperHackersProfileReconcilerTests()
    {
        _updateServiceMock = new Mock<ISuperHackersUpdateService>();
        _manifestPoolMock = new Mock<IContentManifestPool>();
        _contentOrchestratorMock = new Mock<IContentOrchestrator>();
        _reconciliationServiceMock = new Mock<IContentReconciliationService>();
        _notificationServiceMock = new Mock<INotificationService>();
        _dialogServiceMock = new Mock<IDialogService>();
        _userSettingsServiceMock = new Mock<IUserSettingsService>();
        _profileManagerMock = new Mock<IGameProfileManager>();

        _reconciliationServiceMock
            .Setup(x => x.OrchestrateBulkUpdateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(0, 0)));

        _reconciliationServiceMock
            .Setup(x => x.ScheduleGarbageCollectionAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());

        _profileManagerMock
            .Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([])));

        _reconciler = new SuperHackersProfileReconciler(
            NullLogger<SuperHackersProfileReconciler>.Instance,
            _updateServiceMock.Object,
            _manifestPoolMock.Object,
            _contentOrchestratorMock.Object,
            _reconciliationServiceMock.Object,
            _notificationServiceMock.Object,
            _dialogServiceMock.Object,
            _userSettingsServiceMock.Object,
            _profileManagerMock.Object,
            TestVersionComparer.CreateDefault());
    }

    /// <summary>
    /// Returns false (no update performed) when no update is available.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_NoUpdateAvailable_ReturnsFalseAsync()
    {
        _updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateNoUpdateAvailable("1.0.0"));

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1");

        Assert.True(result.Success);
        Assert.False(result.Data);
    }

    /// <summary>
    /// Returns failure when the update check itself fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_UpdateCheckFails_ReturnsFailureAsync()
    {
        _updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("network error"));

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1");

        Assert.False(result.Success);
    }

    /// <summary>
    /// Returns false without running reconciliation when the user has skipped the update version.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_VersionSkipped_ReturnsFalseAsync()
    {
        const string latestVersion = "2.0.0";

        _updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "1.0.0"));

        var settings = new UserSettings();
        settings.SkipVersion(PublisherTypeConstants.TheSuperHackers, latestVersion);

        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1");

        Assert.True(result.Success);
        Assert.False(result.Data);
        _contentOrchestratorMock.Verify(
            x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Returns false (no update performed) when the user dismisses the update dialog without accepting.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_UserSkipsDialog_ReturnsFalseAsync()
    {
        _updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable("2.0.0", "1.0.0"));

        var settings = new UserSettings();
        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        _dialogServiceMock
            .Setup(x => x.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new UpdateDialogResult { Action = "Skip" });

        _userSettingsServiceMock
            .Setup(x => x.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()))
            .ReturnsAsync(true);

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1");

        Assert.True(result.Success);
        Assert.False(result.Data);
        _contentOrchestratorMock.Verify(
            x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Returns failure when content acquisition fails after the user accepts the update.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_AcquireFails_ReturnsFailureAsync()
    {
        const string latestVersion = "2.0.0";

        _updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "1.0.0"));

        var settings = new UserSettings();
        settings.SetAutoUpdatePreference(PublisherTypeConstants.TheSuperHackers, true);
        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        _manifestPoolMock
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        _contentOrchestratorMock
            .Setup(x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new ContentSearchResult { Name = "SuperHackers", Version = latestVersion },
            ]));

        _contentOrchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateFailure("download timed out"));

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1");

        Assert.False(result.Success);
        Assert.Contains("download timed out", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that CreateNewProfile strategy creates new profiles with updated Zero Hour and Generals GameClients and preserves metadata.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_CreateNewProfile_UpdatesZeroHourAndGeneralsVariantsAsync()
    {
        const string oldVersion = "weekly-2026-07-31";
        const string latestVersion = "weekly-2026-08-07";

        _updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, oldVersion));

        var settings = new UserSettings();
        settings.SetAutoUpdatePreference(PublisherTypeConstants.TheSuperHackers, true);
        var sub = settings.GetSubscription(PublisherTypeConstants.TheSuperHackers);
        if (sub != null)
        {
            sub.PreferredUpdateStrategy = UpdateStrategy.CreateNewProfile;
        }

        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        var oldZhManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260731.thesuperhackers.gameclient.zerohour"),
            Name = "SuperHackers - Zero Hour",
            Version = oldVersion,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.TheSuperHackers },
        };

        var oldGenManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260731.thesuperhackers.gameclient.generals"),
            Name = "SuperHackers - Generals",
            Version = oldVersion,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.Generals,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.TheSuperHackers },
        };

        var newZhManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260807.thesuperhackers.gameclient.zerohour"),
            Name = "SuperHackers - Zero Hour",
            Version = latestVersion,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.TheSuperHackers },
        };

        var newGenManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260807.thesuperhackers.gameclient.generals"),
            Name = "SuperHackers - Generals",
            Version = latestVersion,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.Generals,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.TheSuperHackers },
        };

        var manifestPoolData = new List<ContentManifest> { oldZhManifest, oldGenManifest };

        _manifestPoolMock
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifestPoolData.ToList()));

        _contentOrchestratorMock
            .Setup(x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new ContentSearchResult { Name = "SuperHackers", Version = latestVersion },
            ]));

        _contentOrchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                manifestPoolData.Add(newZhManifest);
                manifestPoolData.Add(newGenManifest);
            })
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(newZhManifest));

        var zhProfile = new GameProfile
        {
            Id = "zh-profile-1",
            Name = "SuperHackers - Zero Hour",
            Description = "Custom ZH description",
            GameInstallationId = "steam-install",
            GameClient = new GameClient
            {
                Id = "1.20260731.thesuperhackers.gameclient.zerohour",
                Name = "SuperHackers - Zero Hour",
                Version = oldVersion,
                GameType = GameType.ZeroHour,
                PublisherType = PublisherTypeConstants.TheSuperHackers,
                InstallationId = "steam-install",
            },
            EnabledContentIds = ["1.104.steam.gameinstallation.zerohour", "1.20260731.thesuperhackers.gameclient.zerohour"],
            ThemeColor = "#8B0000",
            IconPath = "zh-icon.png",
            CoverPath = "zh-cover.png",
            UseSteamLaunch = true,
        };

        var genProfile = new GameProfile
        {
            Id = "gen-profile-1",
            Name = "SuperHackers - Generals",
            Description = "Custom Gen description",
            GameInstallationId = "steam-install",
            GameClient = new GameClient
            {
                Id = "1.20260731.thesuperhackers.gameclient.generals",
                Name = "SuperHackers - Generals",
                Version = oldVersion,
                GameType = GameType.Generals,
                PublisherType = PublisherTypeConstants.TheSuperHackers,
                InstallationId = "steam-install",
            },
            EnabledContentIds = ["1.108.steam.gameinstallation.generals", "1.20260731.thesuperhackers.gameclient.generals"],
            ThemeColor = "#FFA500",
            IconPath = "gen-icon.png",
            CoverPath = "gen-cover.png",
            UseSteamLaunch = false,
        };

        _profileManagerMock
            .Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([zhProfile, genProfile]));

        var createdRequests = new List<CreateProfileRequest>();
        _profileManagerMock
            .Setup(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((req, _) => createdRequests.Add(req))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(zhProfile));

        // Act
        var result = await _reconciler.CheckAndReconcileIfNeededAsync("zh-profile-1");

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Data);
        Assert.Equal(2, createdRequests.Count);

        var zhRequest = createdRequests.FirstOrDefault(r => r.GameClient?.GameType == GameType.ZeroHour);
        Assert.NotNull(zhRequest);
        Assert.Equal("SuperHackers - Zero Hour (weekly-2026-08-07)", zhRequest.Name);
        Assert.Equal("Custom ZH description", zhRequest.Description);
        Assert.Equal("1.20260807.thesuperhackers.gameclient.zerohour", zhRequest.GameClient!.Id);
        Assert.Equal(latestVersion, zhRequest.GameClient.Version);
        Assert.Equal("1.20260807.thesuperhackers.gameclient.zerohour", zhRequest.GameClientId);
        Assert.Contains("1.20260807.thesuperhackers.gameclient.zerohour", zhRequest.EnabledContentIds!);
        Assert.Equal("#8B0000", zhRequest.ThemeColor);
        Assert.Equal("zh-icon.png", zhRequest.IconPath);
        Assert.Equal("zh-cover.png", zhRequest.CoverPath);
        Assert.True(zhRequest.UseSteamLaunch);

        var genRequest = createdRequests.FirstOrDefault(r => r.GameClient?.GameType == GameType.Generals);
        Assert.NotNull(genRequest);
        Assert.Equal("SuperHackers - Generals (weekly-2026-08-07)", genRequest.Name);
        Assert.Equal("Custom Gen description", genRequest.Description);
        Assert.Equal("1.20260807.thesuperhackers.gameclient.generals", genRequest.GameClient!.Id);
        Assert.Equal(latestVersion, genRequest.GameClient.Version);
        Assert.Equal("1.20260807.thesuperhackers.gameclient.generals", genRequest.GameClientId);
        Assert.Contains("1.20260807.thesuperhackers.gameclient.generals", genRequest.EnabledContentIds!);
        Assert.Equal("#FFA500", genRequest.ThemeColor);
        Assert.Equal("gen-icon.png", genRequest.IconPath);
        Assert.Equal("gen-cover.png", genRequest.CoverPath);
        Assert.False(genRequest.UseSteamLaunch);
    }

    /// <summary>
    /// Propagates cancellation from acquisition instead of surfacing it as a generic download failure.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_AcquireCancelled_PropagatesCancellationAsync()
    {
        const string latestVersion = "2.0.0";

        _updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "1.0.0"));

        var settings = new UserSettings();
        settings.SetAutoUpdatePreference(PublisherTypeConstants.TheSuperHackers, true);
        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        _manifestPoolMock
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        _contentOrchestratorMock
            .Setup(x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new ContentSearchResult { Name = "SuperHackers", Version = latestVersion },
            ]));

        _contentOrchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _reconciler.CheckAndReconcileIfNeededAsync("profile1", cts.Token));

        _notificationServiceMock.Verify(
            x => x.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Treats an acquisition failure raised while shutting down as cancellation, covering the
    /// pipeline layers that convert <see cref="OperationCanceledException"/> into a failed result
    /// before it can reach the reconciler as an exception.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_AcquireFailsWhileCancelled_PropagatesCancellationAsync()
    {
        const string latestVersion = "2.0.0";

        _updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "1.0.0"));

        var settings = new UserSettings();
        settings.SetAutoUpdatePreference(PublisherTypeConstants.TheSuperHackers, true);
        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        _manifestPoolMock
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        _contentOrchestratorMock
            .Setup(x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new ContentSearchResult { Name = "SuperHackers", Version = latestVersion },
            ]));

        using var cts = new CancellationTokenSource();

        _contentOrchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ReturnsAsync(OperationResult<ContentManifest>.CreateFailure(
                "Content acquisition failed: The operation was canceled."));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _reconciler.CheckAndReconcileIfNeededAsync("profile1", cts.Token));

        _notificationServiceMock.Verify(
            x => x.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }
}
