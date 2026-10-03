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
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Tests for <see cref="CommunityOutpostProfileReconciler"/>.
/// </summary>
public class CommunityOutpostProfileReconcilerTests
{
    private readonly Mock<ICommunityOutpostUpdateService> _updateServiceMock;
    private readonly Mock<IContentManifestPool> _manifestPoolMock;
    private readonly Mock<IContentOrchestrator> _contentOrchestratorMock;
    private readonly Mock<IContentReconciliationService> _reconciliationServiceMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IDialogService> _dialogServiceMock;
    private readonly Mock<IUserSettingsService> _userSettingsServiceMock;
    private readonly Mock<IGameProfileManager> _profileManagerMock;

    private readonly CommunityOutpostProfileReconciler _reconciler;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommunityOutpostProfileReconcilerTests"/> class.
    /// </summary>
    public CommunityOutpostProfileReconcilerTests()
    {
        _updateServiceMock = new Mock<ICommunityOutpostUpdateService>();
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

        _reconciler = new CommunityOutpostProfileReconciler(
            NullLogger<CommunityOutpostProfileReconciler>.Instance,
            _updateServiceMock.Object,
            _manifestPoolMock.Object,
            _contentOrchestratorMock.Object,
            _reconciliationServiceMock.Object,
            _notificationServiceMock.Object,
            _dialogServiceMock.Object,
            _userSettingsServiceMock.Object,
            _profileManagerMock.Object);
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
        settings.SkipVersion(CommunityOutpostConstants.PublisherType, latestVersion);
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
            .Setup(x => x.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(new UpdateDialogResult { Action = "Skip" });

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
        settings.SetAutoUpdatePreference(CommunityOutpostConstants.PublisherType, true);
        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        _manifestPoolMock
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        _contentOrchestratorMock
            .Setup(x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new ContentSearchResult { Name = "Community Patch", Version = latestVersion },
            ]));

        _contentOrchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateFailure("server unavailable"));

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1");

        Assert.False(result.Success);
        Assert.Contains("server unavailable", result.FirstError, StringComparison.OrdinalIgnoreCase);
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
        settings.SetAutoUpdatePreference(CommunityOutpostConstants.PublisherType, true);
        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        _manifestPoolMock
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        _contentOrchestratorMock
            .Setup(x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new ContentSearchResult { Name = "Community Patch", Version = latestVersion },
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
    /// Verifies that the reconciler forwards the persisted DeleteOldVersions preference to the update dialog.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_WhenPromptingUser_ForwardsPersistedDeleteOldVersionsPreferenceAsync()
    {
        _updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable("2.0.0", "1.0.0"));

        var settings = new UserSettings();
        var subscription = settings.GetOrCreateSubscription(CommunityOutpostConstants.PublisherType);
        subscription.DeleteOldVersions = false;
        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        _dialogServiceMock
            .Setup(x => x.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), false))
            .ReturnsAsync(new UpdateDialogResult { Action = "Skip" });

        _userSettingsServiceMock
            .Setup(x => x.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()))
            .ReturnsAsync(true);

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1");

        Assert.True(result.Success);
        _dialogServiceMock.Verify(
            x => x.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), false),
            Times.Once);
    }

    /// <summary>
    /// Returns failure without acquiring content when the manifest pool cannot be read.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_ManifestPoolFails_ReturnsFailureAsync()
    {
        const string latestVersion = "2.0.0";

        _updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "1.0.0"));

        var settings = new UserSettings();
        settings.SetAutoUpdatePreference(CommunityOutpostConstants.PublisherType, true);
        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        _manifestPoolMock
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateFailure("pool unavailable"));

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1");

        Assert.False(result.Success);
        Assert.Contains("pool unavailable", result.FirstError, StringComparison.OrdinalIgnoreCase);
        _contentOrchestratorMock.Verify(
            x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _reconciliationServiceMock.Verify(
            x => x.ScheduleGarbageCollectionAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that a partial failure during reconciliation displays a warning toast instead of success.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_WhenBulkUpdateFails_ShowsWarningToastAsync()
    {
        const string latestVersion = "2.0.0";
        var oldManifest = new ContentManifest
        {
            Id = new ManifestId("community.outpost.GameClient.community-patch.1.0"),
            Name = "Community Patch 1.0",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Version = "1.0.0",
            Publisher = new PublisherInfo { PublisherType = CommunityOutpostConstants.PublisherType },
            Metadata = new ContentMetadata { Tags = ["contentcode:community-patch"] },
        };
        var newManifest = new ContentManifest
        {
            Id = new ManifestId("community.outpost.GameClient.community-patch.2.0"),
            Name = "Community Patch 2.0",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Version = latestVersion,
            Publisher = new PublisherInfo { PublisherType = CommunityOutpostConstants.PublisherType },
            Metadata = new ContentMetadata { Tags = ["contentcode:community-patch"] },
        };

        _updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "1.0.0"));

        var settings = new UserSettings();
        settings.SetAutoUpdatePreference(CommunityOutpostConstants.PublisherType, true);
        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        _manifestPoolMock
            .SetupSequence(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldManifest]))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldManifest, newManifest]));

        _contentOrchestratorMock
            .Setup(x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new ContentSearchResult { Name = "Community Patch", Version = latestVersion },
            ]));

        _contentOrchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(newManifest));

        _reconciliationServiceMock
            .Setup(x => x.OrchestrateBulkUpdateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(1, 0, 1)));

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1");

        Assert.True(result.Success);
        _notificationServiceMock.Verify(
            x => x.ShowWarning(
                It.Is<string>(title => title.Contains("Partial", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.AtLeastOnce);
        _notificationServiceMock.Verify(
            x => x.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that FindReplacementManifest correctly matches retail to retail and non-retail to non-retail.
    /// </summary>
    [Fact]
    public void FindReplacementManifest_MatchesRetailToRetail_AndNonRetailToNonRetail()
    {
        var testReconciler = new TestableCommunityOutpostProfileReconciler(
            NullLogger<CommunityOutpostProfileReconciler>.Instance,
            _updateServiceMock.Object,
            _manifestPoolMock.Object,
            _contentOrchestratorMock.Object,
            _reconciliationServiceMock.Object,
            _notificationServiceMock.Object,
            _dialogServiceMock.Object,
            _userSettingsServiceMock.Object,
            _profileManagerMock.Object);

        var oldRetail = new ContentManifest
        {
            Id = new ManifestId("community.outpost.GameClient.community-patch.1.0"),
            Name = "Community Patch 1.0 (Retail)",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Version = "1.0.0",
            Metadata = new ContentMetadata { Tags = ["contentcode:community-patch"] },
        };

        var oldNonRetail = new ContentManifest
        {
            Id = new ManifestId("community.outpost.GameClient.community-patch-non-retail.1.0"),
            Name = "Community Patch 1.0 (Non-Retail)",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Version = "1.0.0",
            Metadata = new ContentMetadata { Tags = ["contentcode:community-patch-non-retail"] },
        };

        var newRetail = new ContentManifest
        {
            Id = new ManifestId("community.outpost.GameClient.community-patch.1.1"),
            Name = "Community Patch 1.1 (Retail)",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Version = "1.1.0",
            Metadata = new ContentMetadata { Tags = ["contentcode:community-patch"] },
        };

        var newNonRetail = new ContentManifest
        {
            Id = new ManifestId("community.outpost.GameClient.community-patch-non-retail.1.1"),
            Name = "Community Patch 1.1 (Non-Retail)",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Version = "1.1.0",
            Metadata = new ContentMetadata { Tags = ["contentcode:community-patch-non-retail"] },
        };

        var candidatePool = new List<ContentManifest> { newNonRetail, newRetail };

        var matchedRetail = testReconciler.InvokeFindReplacementManifest(oldRetail, candidatePool);
        var matchedNonRetail = testReconciler.InvokeFindReplacementManifest(oldNonRetail, candidatePool);

        Assert.NotNull(matchedRetail);
        Assert.Equal(newRetail.Id, matchedRetail.Id);

        Assert.NotNull(matchedNonRetail);
        Assert.Equal(newNonRetail.Id, matchedNonRetail.Id);
    }

    /// <summary>
    /// Verifies that FindReplacementManifest does not match candidates with different content types, target games, or content codes.
    /// </summary>
    [Fact]
    public void FindReplacementManifest_RequiresMatchingContentType_TargetGame_AndContentCode()
    {
        var testReconciler = new TestableCommunityOutpostProfileReconciler(
            NullLogger<CommunityOutpostProfileReconciler>.Instance,
            _updateServiceMock.Object,
            _manifestPoolMock.Object,
            _contentOrchestratorMock.Object,
            _reconciliationServiceMock.Object,
            _notificationServiceMock.Object,
            _dialogServiceMock.Object,
            _userSettingsServiceMock.Object,
            _profileManagerMock.Object);

        var oldManifest = new ContentManifest
        {
            Id = new ManifestId("community.outpost.GameClient.community-patch.1.0"),
            Name = "Community Patch 1.0",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Version = "1.0.0",
            Metadata = new ContentMetadata { Tags = ["contentcode:community-patch"] },
        };

        var differentContentType = new ContentManifest
        {
            Id = new ManifestId("community.outpost.Map.community-patch.2.0"),
            Name = "Community Map 2.0",
            ContentType = ContentType.Map,
            TargetGame = GameType.ZeroHour,
            Version = "2.0.0",
            Metadata = new ContentMetadata { Tags = ["contentcode:community-patch"] },
        };

        var differentTargetGame = new ContentManifest
        {
            Id = new ManifestId("community.outpost.GameClient.generals.2.0"),
            Name = "Community Patch Generals 2.0",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.Generals,
            Version = "2.0.0",
            Metadata = new ContentMetadata { Tags = ["contentcode:community-patch"] },
        };

        var differentContentCode = new ContentManifest
        {
            Id = new ManifestId("community.outpost.GameClient.other-mod.2.0"),
            Name = "Other Mod 2.0",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Version = "2.0.0",
            Metadata = new ContentMetadata { Tags = ["contentcode:other-mod"] },
        };

        var validReplacement = new ContentManifest
        {
            Id = new ManifestId("community.outpost.GameClient.community-patch.1.1"),
            Name = "Community Patch 1.1",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Version = "1.1.0",
            Metadata = new ContentMetadata { Tags = ["contentcode:community-patch"] },
        };

        var candidatePool = new List<ContentManifest>
        {
            differentContentType,
            differentTargetGame,
            differentContentCode,
            validReplacement,
        };

        var matched = testReconciler.InvokeFindReplacementManifest(oldManifest, candidatePool);

        Assert.NotNull(matched);
        Assert.Equal(validReplacement.Id, matched.Id);
    }

    private sealed class TestableCommunityOutpostProfileReconciler : CommunityOutpostProfileReconciler
    {
        public TestableCommunityOutpostProfileReconciler(
            Microsoft.Extensions.Logging.ILogger<CommunityOutpostProfileReconciler> logger,
            ICommunityOutpostUpdateService updateService,
            IContentManifestPool manifestPool,
            IContentOrchestrator contentOrchestrator,
            IContentReconciliationService reconciliationService,
            INotificationService notificationService,
            IDialogService dialogService,
            IUserSettingsService userSettingsService,
            IGameProfileManager profileManager)
            : base(
                logger,
                updateService,
                manifestPool,
                contentOrchestrator,
                reconciliationService,
                notificationService,
                dialogService,
                userSettingsService,
                profileManager)
        {
        }

        public ContentManifest? InvokeFindReplacementManifest(
            ContentManifest oldManifest,
            IReadOnlyList<ContentManifest> candidatePool)
        {
            return FindReplacementManifest(oldManifest, candidatePool);
        }
    }
}
