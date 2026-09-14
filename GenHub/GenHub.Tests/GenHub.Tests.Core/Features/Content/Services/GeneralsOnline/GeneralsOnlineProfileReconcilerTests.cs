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
using GenHub.Core.Models.GameClients;
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
        _profileManagerMock.Setup(x => x.GetProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("Profile not found"));

        _reconciliationServiceMock.Setup(x => x.OrchestrateBulkUpdateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(0, 0)));
        _reconciliationServiceMock.Setup(x => x.OrchestrateBulkRemovalAsync(It.IsAny<IEnumerable<ManifestId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(0, 0)));
        _reconciliationServiceMock.Setup(x => x.ScheduleGarbageCollectionAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());

        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([])));
        _profileManagerMock.Setup(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "default-created-id", Name = "Default Created Profile" }));

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

    /// <summary>
    /// Verifies that when the triggering profile is already using the latest version,
    /// the reconciler skips the update prompt and avoids re-downloading.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_WhenTriggeringProfileAlreadyAtLatest_SkipsUpdatePromptAsync()
    {
        // Arrange
        const string latestVersion = "082826_QFE1";
        _updateServiceMock.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, latestVersion));

        var profile = new GameProfile
        {
            Id = "profile-go",
            Name = "Generals Online",
            GameClient = new GenHub.Core.Models.GameClients.GameClient
            {
                Id = "1.82826.generalsonline.gameclient.60hz",
                PublisherType = GeneralsOnlineConstants.PublisherType,
                Version = latestVersion,
            },
        };

        _profileManagerMock.Setup(x => x.GetProfileAsync("profile-go", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        // Act
        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile-go", CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Data);

        _dialogServiceMock.Verify(
            x => x.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
            Times.Never);

        _contentOrchestratorMock.Verify(
            x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that when the user skips the update dialog, the reconciler returns false and does not acquire content.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_WhenUserSkipsDialog_ReturnsSuccessFalseAndDoesNotAcquireAsync()
    {
        // Arrange
        string latestVersion = "0.0.99";
        _updateServiceMock.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "0.0.1"));

        var settings = new UserSettings();
        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        _dialogServiceMock.Setup(x => x.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(new UpdateDialogResult { Action = "Skip", IsDoNotAskAgain = false });

        // Act
        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1", CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Data);

        _contentOrchestratorMock.Verify(
            x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that when the user specifies DeleteOldVersions is false, old manifests are not removed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_WhenUserDisablesDeleteOldVersions_DoesNotDeleteOldManifestsAsync()
    {
        // Arrange
        string latestVersion = "0.0.99";
        _updateServiceMock.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "0.0.1"));

        var settings = new UserSettings();
        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        _dialogServiceMock.Setup(x => x.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(new UpdateDialogResult
            {
                Action = "Update",
                Strategy = UpdateStrategy.ReplaceCurrent,
                DeleteOldVersions = false,
            });

        var oldManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.82826.generalsonline.gameclient.30hz"),
            Name = "Generals Online Client (30Hz)",
            Version = "0.0.1",
        };
        var newManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.82827.generalsonline.gameclient.30hz"),
            Name = "Generals Online Client (30Hz)",
            Version = latestVersion,
        };

        _manifestPoolMock.SetupSequence(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldManifest]))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldManifest, newManifest]));

        _contentOrchestratorMock.Setup(
                x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new() { Name = "New GO Version", Version = latestVersion },
            ]));

        _contentOrchestratorMock.Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(newManifest));

        _reconciliationServiceMock
            .Setup(x => x.OrchestrateBulkUpdateAsync(
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(1, 0)));

        // Act
        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1", CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Data);

        _reconciliationServiceMock.Verify(
            x => x.OrchestrateBulkRemovalAsync(It.IsAny<IEnumerable<ManifestId>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// When zero profiles exist and CreateNewProfile is selected, should create a fresh profile.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckAndReconcile_WhenZeroProfilesExist_AndCreateNewProfileSelected_ShouldCreateFreshProfileAsync()
    {
        // Arrange
        string latestVersion = "1.101525";
        _updateServiceMock.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "1.101524"));

        var settings = new UserSettings();
        settings.SetAutoUpdatePreference(GeneralsOnlineConstants.PublisherType, true);
        var sub = settings.GetOrCreateSubscription(GeneralsOnlineConstants.PublisherType);
        sub.PreferredUpdateStrategy = UpdateStrategy.CreateNewProfile;
        sub.DeleteOldVersions = false;

        _userSettingsServiceMock.Setup(x => x.Get())
            .Returns(settings);

        var newClientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.101525.generalsonline.gameclient.60hz"),
            Name = "GeneralsOnline",
            Version = latestVersion,
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        _manifestPoolMock.SetupSequence(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([newClientManifest]))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([newClientManifest]));

        _contentOrchestratorMock.Setup(
                x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new() { Name = "New GO Version", Version = latestVersion },
            ]));

        _contentOrchestratorMock.Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(newClientManifest));

        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        CreateProfileRequest? capturedRequest = null;
        _profileManagerMock.Setup(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "fresh-1", Name = "GeneralsOnline" }));

        // Act
        var result = await _reconciler.CheckAndReconcileIfNeededAsync(string.Empty, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.Equal($"GeneralsOnline v{latestVersion}", capturedRequest.Name);
        Assert.Equal(GeneralsOnlineConstants.LogoSource, capturedRequest.IconPath);
        Assert.Equal(GeneralsOnlineConstants.CoverSource, capturedRequest.CoverPath);
        Assert.Equal(GeneralsOnlineConstants.ThemeColor, capturedRequest.ThemeColor);
    }

    /// <summary>
    /// When a profile has an existing messy version suffix, formatting for CreateNewProfile strips redundant suffixes cleanly.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckAndReconcile_WhenProfileHasMessyVersionSuffix_ShouldFormatNameCleanlyAsync()
    {
        // Arrange
        string latestVersion = "1.101525";
        _updateServiceMock.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "1.101524"));

        var settings = new UserSettings();
        settings.SetAutoUpdatePreference(GeneralsOnlineConstants.PublisherType, true);
        var sub = settings.GetOrCreateSubscription(GeneralsOnlineConstants.PublisherType);
        sub.PreferredUpdateStrategy = UpdateStrategy.CreateNewProfile;
        sub.DeleteOldVersions = false;

        _userSettingsServiceMock.Setup(x => x.Get())
            .Returns(settings);

        var oldClientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.101524.generalsonline.gameclient.60hz"),
            Version = "1.101524",
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        var newClientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.101525.generalsonline.gameclient.60hz"),
            Name = "GeneralsOnline",
            Version = latestVersion,
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        _manifestPoolMock.SetupSequence(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldClientManifest]))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldClientManifest, newClientManifest]))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldClientManifest, newClientManifest]));

        _contentOrchestratorMock.Setup(
                x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new() { Name = "New GO Version", Version = latestVersion },
            ]));

        _contentOrchestratorMock.Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(newClientManifest));

        var messyProfile = new GameProfile
        {
            Id = "p1",
            Name = "GeneralsOnline v032926 (v082826_QFE1)",
            GameClient = new GameClient { Id = oldClientManifest.Id.Value, PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([messyProfile]));

        CreateProfileRequest? capturedRequest = null;
        _profileManagerMock.Setup(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProfileRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "p2", Name = "GeneralsOnline v1.101525" }));

        // Act
        var result = await _reconciler.CheckAndReconcileIfNeededAsync(string.Empty, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.Equal($"GeneralsOnline v{latestVersion}", capturedRequest.Name);
    }

    /// <summary>
    /// When CreateNewProfile is selected, old preserved profiles should not be mutated or renamed by EnforceMapPackDependency.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckAndReconcile_WhenCreateNewProfileSelected_ShouldNotMutatePreservedOldProfilesAsync()
    {
        // Arrange
        string latestVersion = "1.101525";
        _updateServiceMock.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "1.101524"));

        var settings = new UserSettings();
        settings.SetAutoUpdatePreference(GeneralsOnlineConstants.PublisherType, true);
        var sub = settings.GetOrCreateSubscription(GeneralsOnlineConstants.PublisherType);
        sub.PreferredUpdateStrategy = UpdateStrategy.CreateNewProfile;
        sub.DeleteOldVersions = false;

        _userSettingsServiceMock.Setup(x => x.Get())
            .Returns(settings);

        var oldClientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.101524.generalsonline.gameclient.60hz"),
            Version = "1.101524",
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        var newClientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.101525.generalsonline.gameclient.60hz"),
            Name = "GeneralsOnline",
            Version = latestVersion,
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        var newMapPackManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.101525.generalsonline.mappack.quickmatch-maps"),
            Version = latestVersion,
            ContentType = ContentType.MapPack,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        _manifestPoolMock.SetupSequence(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldClientManifest]))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldClientManifest, newClientManifest, newMapPackManifest]))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldClientManifest, newClientManifest, newMapPackManifest]));

        _contentOrchestratorMock.Setup(
                x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new() { Name = "New GO Version", Version = latestVersion },
            ]));

        _contentOrchestratorMock.Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(newClientManifest));

        var oldProfile = new GameProfile
        {
            Id = "old-profile-id",
            Name = "GeneralsOnline v1.101524",
            GameClient = new GameClient { Id = oldClientManifest.Id.Value, PublisherType = GeneralsOnlineConstants.PublisherType },
            EnabledContentIds = ["1.101524.generalsonline.mappack.quickmatch-maps"],
        };

        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([oldProfile]));

        _profileManagerMock.Setup(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "new-profile-id", Name = $"GeneralsOnline v{latestVersion}" }));

        // Act
        var result = await _reconciler.CheckAndReconcileIfNeededAsync(string.Empty, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        _profileManagerMock.Verify(
            x => x.UpdateProfileAsync("old-profile-id", It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _profileManagerMock.Verify(
            x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that the reconciler forwards the persisted DeleteOldVersions preference to the update dialog.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_WhenPromptingUser_ForwardsPersistedDeleteOldVersionsPreferenceAsync()
    {
        // Arrange
        string latestVersion = "0.0.99";
        _updateServiceMock.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "0.0.1"));

        var settings = new UserSettings();
        var subscription = settings.GetOrCreateSubscription(GeneralsOnlineConstants.PublisherType);
        subscription.DeleteOldVersions = false;
        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        _dialogServiceMock.Setup(x => x.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), false))
            .ReturnsAsync(new UpdateDialogResult { Action = "Skip", IsDoNotAskAgain = false });

        // Act
        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1", CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        _dialogServiceMock.Verify(
            x => x.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), false),
            Times.Once);
    }

    /// <summary>
    /// Verifies that FormatUpdatedProfileName does not append a duplicate suffix when the candidate matches the current name.
    /// </summary>
    [Fact]
    public void FormatUpdatedProfileName_WhenCandidateMatchesOriginalName_ReturnsOriginalNameWithoutCollisionSuffix()
    {
        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Generals Online v101525_QFE2",
            "Generals Online v101525_QFE2 (2)",
        };

        var result = GeneralsOnlineProfileReconciler.FormatUpdatedProfileName(
            "Generals Online v101525_QFE2",
            "101525_QFE2",
            existingNames);

        Assert.Equal("Generals Online v101525_QFE2", result);
    }

    /// <summary>
    /// Verifies that FormatUpdatedProfileName appends a unique suffix when cloning even if the candidate matches the source profile name.
    /// </summary>
    [Fact]
    public void FormatUpdatedProfileName_WhenCloningAndCandidateMatchesOriginalName_AppendsSuffix()
    {
        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Generals Online v101525_QFE2",
        };

        var result = GeneralsOnlineProfileReconciler.FormatUpdatedProfileName(
            "Generals Online v101525_QFE2",
            "101525_QFE2",
            existingNames,
            allowSameAsOriginal: false);

        Assert.Equal("Generals Online v101525_QFE2 (2)", result);
    }

    /// <summary>
    /// Verifies that FormatUpdatedProfileName generates a unique name with suffix when colliding with another existing profile.
    /// </summary>
    [Fact]
    public void FormatUpdatedProfileName_WhenCollidingWithOtherProfile_AppendsSuffix()
    {
        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Generals Online v101525_QFE3",
        };

        var result = GeneralsOnlineProfileReconciler.FormatUpdatedProfileName(
            "Generals Online v101525_QFE2",
            "101525_QFE3",
            existingNames);

        Assert.Equal("Generals Online v101525_QFE3 (2)", result);
    }

    /// <summary>
    /// Verifies that ResolveUpdatedEnabledContent drops stale unmapped GeneralsOnline content and deduplicates.
    /// </summary>
    [Fact]
    public void ResolveUpdatedEnabledContent_DropsStaleUnmappedGeneralsOnlineContentAndDeduplicates()
    {
        var profile = new GameProfile
        {
            EnabledContentIds = new List<string>
            {
                "1.100.steam.gameinstallation.zerohour",
                "1.1015251.generalsonline.gameclient.30hz",
                "1.1015251.generalsonline.mod.stale",
                "non-go-custom-content",
            },
        };

        var mapping = new Dictionary<string, string>
        {
            { "1.1015251.generalsonline.gameclient.30hz", "1.1015252.generalsonline.gameclient.30hz" },
        };

        var newManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.1015252.generalsonline.mappack.quickmatch-maps"),
            Version = "101525_QFE2",
        };

        var result = GeneralsOnlineProfileReconciler.ResolveUpdatedEnabledContent(
            profile,
            mapping,
            [newManifest]);

        Assert.Contains("1.100.steam.gameinstallation.zerohour", result);
        Assert.Contains("non-go-custom-content", result);
        Assert.Contains("1.1015252.generalsonline.gameclient.30hz", result);
        Assert.Contains("1.1015252.generalsonline.mappack.quickmatch-maps", result);
        Assert.DoesNotContain("1.1015251.generalsonline.mod.stale", result);
        Assert.Equal(result.Distinct(StringComparer.OrdinalIgnoreCase).Count(), result.Count);
    }

    /// <summary>
    /// Verifies that when replace/in-place strategy is used, the existing profile is updated via UpdateProfileAsync
    /// with the renamed version and CreateProfileAsync is never called.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckAndReconcile_WhenReplaceStrategySelected_UpdatesExistingProfileAndNeverCreatesAsync()
    {
        // Arrange
        string latestVersion = "1.101525";
        _updateServiceMock.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "1.101524"));

        var settings = new UserSettings();
        settings.SetAutoUpdatePreference(GeneralsOnlineConstants.PublisherType, true);
        var sub = settings.GetOrCreateSubscription(GeneralsOnlineConstants.PublisherType);
        sub.PreferredUpdateStrategy = UpdateStrategy.ReplaceCurrent;
        sub.DeleteOldVersions = true;

        _userSettingsServiceMock.Setup(x => x.Get())
            .Returns(settings);

        var oldClientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.101524.generalsonline.gameclient.60hz"),
            Version = "1.101524",
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        var newClientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.101525.generalsonline.gameclient.60hz"),
            Name = "GeneralsOnline",
            Version = latestVersion,
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        var newMapPackManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.101525.generalsonline.mappack.quickmatch-maps"),
            Version = latestVersion,
            ContentType = ContentType.MapPack,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        _manifestPoolMock.SetupSequence(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldClientManifest]))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldClientManifest, newClientManifest, newMapPackManifest]))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldClientManifest, newClientManifest, newMapPackManifest]));

        _contentOrchestratorMock.Setup(
                x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new() { Name = "New GO Version", Version = latestVersion },
            ]));

        _contentOrchestratorMock.Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(newClientManifest));

        _reconciliationServiceMock.Setup(x => x.OrchestrateBulkUpdateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(1, 0)));

        var existingProfile = new GameProfile
        {
            Id = "existing-profile-id",
            Name = "GeneralsOnline v1.101524",
            GameClient = new GameClient { Id = newClientManifest.Id.Value, PublisherType = GeneralsOnlineConstants.PublisherType },
            EnabledContentIds = ["1.101524.generalsonline.mappack.quickmatch-maps"],
        };

        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([existingProfile]));

        _profileManagerMock.Setup(x => x.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(existingProfile));

        // Act
        var result = await _reconciler.CheckAndReconcileIfNeededAsync(string.Empty, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        _profileManagerMock.Verify(
            x => x.UpdateProfileAsync(
                "existing-profile-id",
                It.Is<UpdateProfileRequest>(req => req.Name == $"GeneralsOnline v{latestVersion}"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _profileManagerMock.Verify(
            x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
