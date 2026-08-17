using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Tests.Core.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Tests for <see cref="GeneralsOnlineProfileReconciler"/>.
/// </summary>
public class GeneralsOnlineProfileReconcilerTests
{
    private readonly Mock<IGeneralsOnlineUpdateService> _updateServiceMock;
    private readonly Mock<IContentManifestPool> _manifestPoolMock;
    private readonly Mock<IContentOrchestrator> _contentOrchestratorMock;
    private readonly Mock<IContentReconciliationService> _reconciliationServiceMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IDialogService> _dialogServiceMock;
    private readonly Mock<IUserSettingsService> _userSettingsServiceMock;
    private readonly Mock<IGameProfileManager> _profileManagerMock;

    private readonly GeneralsOnlineProfileReconciler _reconciler;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineProfileReconcilerTests"/> class.
    /// </summary>
    public GeneralsOnlineProfileReconcilerTests()
    {
        _manifestPoolMock = new Mock<IContentManifestPool>();

        _updateServiceMock = new Mock<IGeneralsOnlineUpdateService>();

        _contentOrchestratorMock = new Mock<IContentOrchestrator>();
        _reconciliationServiceMock = new Mock<IContentReconciliationService>();
        _notificationServiceMock = new Mock<INotificationService>();
        _dialogServiceMock = new Mock<IDialogService>();
        _userSettingsServiceMock = new Mock<IUserSettingsService>();
        _profileManagerMock = new Mock<IGameProfileManager>();

        _reconciliationServiceMock.Setup(x => x.OrchestrateBulkUpdateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(0, 0)));
        _reconciliationServiceMock.Setup(x => x.OrchestrateBulkRemovalAsync(It.IsAny<IEnumerable<ManifestId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(0, 0)));
        _reconciliationServiceMock.Setup(x => x.ScheduleGarbageCollectionAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());

        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([])));

        _reconciler = new GeneralsOnlineProfileReconciler(
            NullLogger<GeneralsOnlineProfileReconciler>.Instance,
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
    /// Should ignore local manifests during reconciliation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckAndReconcile_ShouldIgnore_LocalManifestsAsync()
    {
        // Arrange
        string latestVersion = "0.0.99";
        _updateServiceMock.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "0.0.1"));

        var settings = new UserSettings();
        settings.SetAutoUpdatePreference(GeneralsOnlineConstants.PublisherType, true);
        settings.GetOrCreateSubscription(GeneralsOnlineConstants.PublisherType).DeleteOldVersions = true;

        _userSettingsServiceMock.Setup(x => x.Get())
            .Returns(settings);

        // Setup mocked local manifest that should be ignored
        var localManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.local.gameclient.gen-online-copy"),
            Name = "My GeneralsOnline Copy",
            Version = "1.0",
            Publisher = new PublisherInfo { PublisherType = "local" },
        };

        var newManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.generalsonline.gameclient.newversion"),
            Version = latestVersion,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        // First call returns only local (excluded by filter), second call returns both
        _manifestPoolMock.SetupSequence(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([localManifest]))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([localManifest, newManifest]))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([localManifest, newManifest]));

        // Setup mock acquisition (simplified for test)
        _contentOrchestratorMock.Setup(
                x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new() { Name = "New GO Version", Version = latestVersion },
            ]));

        _contentOrchestratorMock.Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(newManifest));

        // Act
        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1", CancellationToken.None);

        // Assert
        Assert.True(result.Success, $"Reconciliation failed: {result.FirstError}");

        // Verify that RemoveManifestAsync was NEVER called for the local manifest
        _manifestPoolMock.Verify(
            x => x.RemoveManifestAsync(localManifest.Id, It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Local manifest should not be removed during reconciliation");
    }

    /// <summary>
    /// Propagates cancellation from acquisition instead of surfacing it as a generic download failure.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_AcquireCancelled_PropagatesCancellationAsync()
    {
        // Arrange
        string latestVersion = "0.0.99";
        _updateServiceMock.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "0.0.1"));

        var settings = new UserSettings();
        settings.SetAutoUpdatePreference(GeneralsOnlineConstants.PublisherType, true);
        _userSettingsServiceMock.Setup(x => x.Get())
            .Returns(settings);

        _manifestPoolMock.Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        _contentOrchestratorMock.Setup(
                x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new() { Name = "New GO Version", Version = latestVersion },
            ]));

        _contentOrchestratorMock.Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        using var cts = new CancellationTokenSource();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _reconciler.CheckAndReconcileIfNeededAsync("profile1", cts.Token));

        _notificationServiceMock.Verify(
            x => x.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that old and new GameData patch manifests are recognized by variant and included in reconciliation mapping.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_WithGameDataPatchManifest_MapsAndReconcilesGameDataPatchAsync()
    {
        // Arrange
        const string oldVersion = "101524";
        const string newVersion = "101525";

        _updateServiceMock.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(newVersion, oldVersion));

        var settings = new UserSettings();
        settings.SetAutoUpdatePreference(GeneralsOnlineConstants.PublisherType, true);
        settings.GetOrCreateSubscription(GeneralsOnlineConstants.PublisherType).DeleteOldVersions = true;
        _userSettingsServiceMock.Setup(x => x.Get())
            .Returns(settings);

        var oldClientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.101524.generalsonline.gameclient.60hz"),
            Version = oldVersion,
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        var oldPatchManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.101524.generalsonline.patch.gamedata"),
            Version = oldVersion,
            ContentType = ContentType.Patch,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
            Metadata = new ContentMetadata { Tags = ["gamedata", "patch", "generalsonline"] },
        };

        var newClientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.101525.generalsonline.gameclient.60hz"),
            Version = newVersion,
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        var newPatchManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.101525.generalsonline.patch.gamedata"),
            Version = newVersion,
            ContentType = ContentType.Patch,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
            Metadata = new ContentMetadata { Tags = ["gamedata", "patch", "generalsonline"] },
        };

        _manifestPoolMock.SetupSequence(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldClientManifest, oldPatchManifest]))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldClientManifest, oldPatchManifest, newClientManifest, newPatchManifest]));

        _contentOrchestratorMock.Setup(
                x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new() { Name = "New GO Version", Version = newVersion },
            ]));

        _contentOrchestratorMock.Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(newClientManifest));

        IReadOnlyDictionary<string, string>? capturedMapping = null;
        _reconciliationServiceMock
            .Setup(x => x.OrchestrateBulkUpdateAsync(
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyDictionary<string, string>, bool, CancellationToken>((mapping, createNew, token) => capturedMapping = mapping)
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(1, 0)));

        // Act
        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1", CancellationToken.None);

        // Assert
        Assert.True(result.Success, $"Reconciliation failed: {result.FirstError}");
        Assert.NotNull(capturedMapping);
        Assert.True(capturedMapping.ContainsKey(oldClientManifest.Id.Value));
        Assert.Equal(newClientManifest.Id.Value, capturedMapping[oldClientManifest.Id.Value]);
        Assert.True(capturedMapping.ContainsKey(oldPatchManifest.Id.Value));
        Assert.Equal(newPatchManifest.Id.Value, capturedMapping[oldPatchManifest.Id.Value]);

        _reconciliationServiceMock.Verify(
            x => x.OrchestrateBulkRemovalAsync(
                It.Is<IEnumerable<ManifestId>>(ids => ids.Contains(oldClientManifest.Id) && ids.Contains(oldPatchManifest.Id)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
