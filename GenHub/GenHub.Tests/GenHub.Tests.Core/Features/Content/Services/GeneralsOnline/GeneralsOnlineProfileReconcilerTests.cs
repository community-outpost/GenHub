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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

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
            _profileManagerMock.Object);
    }

    /// <summary>
    /// Should ignore local manifests during reconciliation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckAndReconcile_ShouldIgnore_LocalManifests()
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
}
