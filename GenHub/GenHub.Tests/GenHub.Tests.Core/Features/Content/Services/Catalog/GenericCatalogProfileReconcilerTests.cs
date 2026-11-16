using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GitHub;
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
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using PublisherSubscription = GenHub.Core.Models.Providers.PublisherSubscription;

namespace GenHub.Tests.Core.Features.Content.Services.Catalog;

/// <summary>
/// Unit tests for <see cref="GenericCatalogProfileReconciler"/> and <see cref="GenericCatalogContentServices"/>.
/// </summary>
public sealed class GenericCatalogProfileReconcilerTests
{
    private const string OldItemManifestId = "1.0.pub1.mod.itemone";
    private const string NewItemManifestId = "2.0.pub1.mod.itemone";
    private const string OldSecondManifestId = "1.0.pub1.mod.itemtwo";
    private const string NewSecondManifestId = "2.0.pub1.mod.itemtwo";
    private const int ProfilesUpdatedPerItem = 2;

    private readonly Mock<IGameProfileManager> _profileManagerMock = new();
    private readonly Mock<IPublisherSubscriptionStore> _subscriptionStoreMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<IDialogService> _dialogServiceMock = new();
    private readonly Mock<IUserSettingsService> _userSettingsServiceMock = new();

    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<GenericCatalogDiscoverer> _discovererMock;
    private readonly Mock<IContentStateService> _contentStateServiceMock = new();
    private readonly Mock<IContentDownloadCoordinator> _downloadCoordinatorMock = new();
    private readonly Mock<IContentReconciliationService> _reconciliationServiceMock = new();

    private readonly GenericCatalogContentServices _contentServices;
    private readonly GenericCatalogProfileReconciler _reconciler;
    private readonly UserSettings _userSettings = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GenericCatalogProfileReconcilerTests"/> class.
    /// </summary>
    public GenericCatalogProfileReconcilerTests()
    {
        _discovererMock = new Mock<GenericCatalogDiscoverer>(
            NullLogger<GenericCatalogDiscoverer>.Instance,
            new Mock<IHttpClientFactory>().Object,
            new Mock<IPublisherCatalogParser>().Object,
            new Mock<IVersionSelector>().Object,
            new Mock<IGitHubApiClient>().Object);

        _contentServices = new GenericCatalogContentServices(
            _manifestPoolMock.Object,
            _discovererMock.Object,
            _contentStateServiceMock.Object,
            _downloadCoordinatorMock.Object,
            _reconciliationServiceMock.Object,
            _subscriptionStoreMock.Object);

        _userSettingsServiceMock.Setup(s => s.Get()).Returns(_userSettings);

        _reconciler = new GenericCatalogProfileReconciler(
            NullLogger<GenericCatalogProfileReconciler>.Instance,
            _profileManagerMock.Object,
            _contentServices,
            _notificationServiceMock.Object,
            _dialogServiceMock.Object,
            _userSettingsServiceMock.Object);
    }

    /// <summary>
    /// Verifies that GenericCatalogContentServices properly exposes all injected services.
    /// </summary>
    [Fact]
    public void GenericCatalogContentServices_ExposesPropertiesCorrectly()
    {
        Assert.Same(_manifestPoolMock.Object, _contentServices.ManifestPool);
        Assert.Same(_discovererMock.Object, _contentServices.CatalogDiscoverer);
        Assert.Same(_contentStateServiceMock.Object, _contentServices.ContentStateService);
        Assert.Same(_downloadCoordinatorMock.Object, _contentServices.DownloadCoordinator);
        Assert.Same(_reconciliationServiceMock.Object, _contentServices.ReconciliationService);
        Assert.Same(_subscriptionStoreMock.Object, _contentServices.SubscriptionStore);
    }

    /// <summary>
    /// Verifies that PublisherType returns the expected constant.
    /// </summary>
    [Fact]
    public void PublisherType_ReturnsGenericPublisherType()
    {
        Assert.Equal(CatalogConstants.GenericPublisherType, _reconciler.PublisherType);
    }

    /// <summary>
    /// Verifies that when the triggering profile is not found, None is returned.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_ProfileNotFound_ReturnsNone()
    {
        _profileManagerMock
            .Setup(m => m.GetProfileAsync("non-existent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("Profile not found"));

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("non-existent");

        Assert.True(result.Success);
        Assert.Equal(PublisherReconciliationResult.None, result.Data);
    }

    /// <summary>
    /// Verifies that when no subscriptions exist, None is returned.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_NoSubscriptions_ReturnsNone()
    {
        var profile = new GameProfile { Id = "prof1", Name = "Test Profile" };
        _profileManagerMock
            .Setup(m => m.GetProfileAsync("prof1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _subscriptionStoreMock
            .Setup(s => s.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([]));

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.True(result.Success);
        Assert.Equal(PublisherReconciliationResult.None, result.Data);
    }

    /// <summary>
    /// Verifies that when discovery returns no items, None is returned.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_NoDiscoveryItems_ReturnsNone()
    {
        var profile = new GameProfile { Id = "prof1", Name = "Test Profile" };
        _profileManagerMock
            .Setup(m => m.GetProfileAsync("prof1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        var subscription = new PublisherSubscription { PublisherId = "pub1", PublisherName = "Publisher 1" };
        _subscriptionStoreMock
            .Setup(s => s.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([subscription]));

        _discovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult()));

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.True(result.Success);
        Assert.Equal(PublisherReconciliationResult.None, result.Data);
    }

    /// <summary>
    /// Verifies that when an item is not in UpdateAvailable state, it is skipped.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_ItemNotUpdateAvailable_SkipsAndReturnsNone()
    {
        var profile = new GameProfile { Id = "prof1", Name = "Test Profile" };
        _profileManagerMock
            .Setup(m => m.GetProfileAsync("prof1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        var subscription = new PublisherSubscription { PublisherId = "pub1", PublisherName = "Publisher 1" };
        _subscriptionStoreMock
            .Setup(s => s.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([subscription]));

        var item = new ContentSearchResult { Id = "item1", Name = "Test Item", Version = "2.0" };
        var page = new ContentDiscoveryResult { Items = [item] };
        _discovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(page));

        _contentStateServiceMock
            .Setup(s => s.GetStateAsync(item, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.Downloaded);

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.True(result.Success);
        Assert.Equal(PublisherReconciliationResult.None, result.Data);
        _dialogServiceMock.Verify(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Verifies that when the item version has been marked as skipped by the user, the prompt is skipped.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_VersionSkipped_SkipsPromptAndReturnsNone()
    {
        var profile = new GameProfile
        {
            Id = "prof1",
            Name = "Test Profile",
            EnabledContentIds = ["manifest-item-1"],
        };

        _profileManagerMock
            .Setup(m => m.GetProfileAsync("prof1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        var subscription = new PublisherSubscription { PublisherId = "pub1", PublisherName = "Publisher 1" };
        _subscriptionStoreMock
            .Setup(s => s.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([subscription]));

        var item = new ContentSearchResult { Id = "item1", Name = "Test Item", Version = "2.0" };
        var page = new ContentDiscoveryResult { Items = [item] };
        _discovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(page));

        _contentStateServiceMock
            .Setup(s => s.GetStateAsync(item, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.UpdateAvailable);
        _contentStateServiceMock
            .Setup(s => s.GetLocalManifestIdAsync(item, It.IsAny<CancellationToken>()))
            .ReturnsAsync("manifest-item-1");

        _userSettings.SkipVersion("pub1", "2.0");

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.True(result.Success);
        Assert.Equal(PublisherReconciliationResult.None, result.Data);
        _dialogServiceMock.Verify(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Verifies that when the user chooses to skip the update dialog, reconciliation returns None.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_UserSkipsUpdatePrompt_ReturnsNone()
    {
        var profile = new GameProfile
        {
            Id = "prof1",
            Name = "Test Profile",
            EnabledContentIds = ["manifest-item-1"],
        };

        _profileManagerMock
            .Setup(m => m.GetProfileAsync("prof1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        var subscription = new PublisherSubscription { PublisherId = "pub1", PublisherName = "Publisher 1" };
        _subscriptionStoreMock
            .Setup(s => s.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([subscription]));

        var item = new ContentSearchResult { Id = "item1", Name = "Test Item", Version = "2.0" };
        var page = new ContentDiscoveryResult { Items = [item] };
        _discovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(page));

        _contentStateServiceMock
            .Setup(s => s.GetStateAsync(item, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.UpdateAvailable);
        _contentStateServiceMock
            .Setup(s => s.GetLocalManifestIdAsync(item, It.IsAny<CancellationToken>()))
            .ReturnsAsync("manifest-item-1");

        _dialogServiceMock
            .Setup(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), true))
            .ReturnsAsync(new UpdateDialogResult { Action = "Skip" });

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.True(result.Success);
        Assert.Equal(PublisherReconciliationResult.None, result.Data);
        _downloadCoordinatorMock.Verify(d => d.DownloadContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a catalog with automatic updates enabled is updated without showing the dialog,
    /// and that the user is told which content moved to which version.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_AutoUpdateEnabled_AppliesUpdateWithoutDialogAsync()
    {
        var item = SetupProfileWithItems(("item1", "Test Item", OldItemManifestId)).Single();
        SetupSuccessfulUpdate(item, OldItemManifestId, NewItemManifestId);
        _userSettings.SetAutoUpdatePreference("pub1", true);

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.True(result.Success);
        Assert.True(result.Data);
        Assert.Equal(UpdateStrategy.ReplaceCurrent, result.Data!.Strategy);
        _dialogServiceMock.Verify(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        _downloadCoordinatorMock.Verify(d => d.DownloadContentAsync(item, It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
        _reconciliationServiceMock.Verify(
            r => r.OrchestrateBulkUpdateAsync(
                It.Is<IReadOnlyDictionary<string, string>>(m => m.Count == 1 && m[OldItemManifestId] == NewItemManifestId),
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _notificationServiceMock.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.Is<string>(m => m.Contains("Test Item") && m.Contains("2.0") && m.Contains("automatically")), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that the automatic update preference is per catalog: opting in for one catalog
    /// still shows the dialog for content from another catalog.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_AutoUpdateEnabledForOtherCatalog_StillShowsDialogAsync()
    {
        SetupProfileWithItems(("item1", "Test Item", OldItemManifestId));
        _userSettings.SetAutoUpdatePreference("other-publisher", true);
        _dialogServiceMock
            .Setup(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(new UpdateDialogResult { Action = "Skip" });

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.True(result.Success);
        Assert.Equal(PublisherReconciliationResult.None, result.Data);
        _dialogServiceMock.Verify(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        _downloadCoordinatorMock.Verify(d => d.DownloadContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Verifies that choosing Update with "Don't ask again" stores the automatic update preference for
    /// the catalog, so the next launch updates without showing the dialog.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_UpdateWithDoNotAskAgain_PersistsPreferenceAndSkipsNextDialogAsync()
    {
        var item = SetupProfileWithItems(("item1", "Test Item", OldItemManifestId)).Single();
        SetupSuccessfulUpdate(item, OldItemManifestId, NewItemManifestId);
        SetupSettingsSaves();
        _dialogServiceMock
            .Setup(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(new UpdateDialogResult
            {
                Action = "Update",
                Strategy = UpdateStrategy.ReplaceCurrent,
                DeleteOldVersions = false,
                IsDoNotAskAgain = true,
            });

        var first = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");
        var second = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.True(first.Success);
        Assert.True(second.Success);
        var preference = _userSettings.GetSubscription("pub1");
        Assert.NotNull(preference);
        Assert.True(preference.AutoUpdateEnabled);
        Assert.Equal(UpdateStrategy.ReplaceCurrent, preference.PreferredUpdateStrategy);
        Assert.False(preference.DeleteOldVersions);
        _dialogServiceMock.Verify(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        _downloadCoordinatorMock.Verify(d => d.DownloadContentAsync(item, It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Exactly(2));
        _reconciliationServiceMock.Verify(r => r.OrchestrateBulkUpdateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), false, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// Verifies that choosing Update without "Don't ask again" leaves the preference off.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_UpdateWithoutDoNotAskAgain_DoesNotEnableAutoUpdateAsync()
    {
        var item = SetupProfileWithItems(("item1", "Test Item", OldItemManifestId)).Single();
        SetupSuccessfulUpdate(item, OldItemManifestId, NewItemManifestId);
        SetupSettingsSaves();
        _dialogServiceMock
            .Setup(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(new UpdateDialogResult { Action = "Update", Strategy = UpdateStrategy.ReplaceCurrent });

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.True(result.Success);
        Assert.True(result.Data);
        Assert.NotEqual(true, _userSettings.GetSubscription("pub1")?.AutoUpdateEnabled);
        _userSettingsServiceMock.Verify(s => s.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a failed automatic download leaves the profile alone, reports a failure the launcher
    /// treats as non-blocking, and tells the user the installed version is still in use.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_AutoUpdateDownloadFails_ReturnsFailureAndNotifiesAsync()
    {
        var item = SetupProfileWithItems(("item1", "Test Item", OldItemManifestId)).Single();
        _userSettings.SetAutoUpdatePreference("pub1", true);
        _downloadCoordinatorMock
            .Setup(d => d.DownloadContentAsync(item, It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateFailure("network down"));

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.False(result.Success);
        _dialogServiceMock.Verify(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        _reconciliationServiceMock.Verify(r => r.OrchestrateBulkUpdateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _profileManagerMock.Verify(m => m.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _notificationServiceMock.Verify(
            n => n.ShowError(It.IsAny<string>(), It.Is<string>(m => m.Contains("Test Item") && m.Contains("2.0") && m.Contains("network down") && m.Contains("still in use")), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that an exception during an automatic update is reported to the user instead of being swallowed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_AutoUpdateThrows_ReturnsFailureAndNotifiesAsync()
    {
        var item = SetupProfileWithItems(("item1", "Test Item", OldItemManifestId)).Single();
        _userSettings.SetAutoUpdatePreference("pub1", true);
        _downloadCoordinatorMock
            .Setup(d => d.DownloadContentAsync(item, It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ThrowsAsync(new HttpRequestException("connection reset"));

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.False(result.Success);
        _notificationServiceMock.Verify(
            n => n.ShowError(It.IsAny<string>(), It.Is<string>(m => m.Contains("connection reset") && m.Contains("still in use")), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that cancelling a launch during an automatic download propagates cancellation
    /// instead of reporting a failed update.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_CancelledDuringAutoUpdate_PropagatesCancellationAsync()
    {
        var item = SetupProfileWithItems(("item1", "Test Item", OldItemManifestId)).Single();
        _userSettings.SetAutoUpdatePreference("pub1", true);
        using var cts = new CancellationTokenSource();
        _downloadCoordinatorMock
            .Setup(d => d.DownloadContentAsync(item, It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromResult(OperationResult<ContentManifest>.CreateFailure("Operation was cancelled"));
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _reconciler.CheckAndReconcileIfNeededAsync("prof1", cts.Token));

        _notificationServiceMock.Verify(n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);
        _reconciliationServiceMock.Verify(r => r.OrchestrateBulkUpdateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that with automatic updates every outdated item from the catalog in the profile is updated
    /// in one launch, each with a single download.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_AutoUpdateWithTwoItems_UpdatesBothAsync()
    {
        var items = SetupProfileWithItems(
            ("item1", "Client", OldItemManifestId),
            ("item2", "Map Pack", OldSecondManifestId));
        SetupSuccessfulUpdate(items[0], OldItemManifestId, NewItemManifestId);
        SetupSuccessfulUpdate(items[1], OldSecondManifestId, NewSecondManifestId);
        _userSettings.SetAutoUpdatePreference("pub1", true);

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.True(result.Success);
        Assert.True(result.Data);
        Assert.Equal(ProfilesUpdatedPerItem, result.Data!.ProfilesAffectedCount);
        _downloadCoordinatorMock.Verify(d => d.DownloadContentAsync(items[0], It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
        _downloadCoordinatorMock.Verify(d => d.DownloadContentAsync(items[1], It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
        _reconciliationServiceMock.Verify(r => r.OrchestrateBulkUpdateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// Verifies that when the first automatic update fails, the next item is still updated and the
    /// result stays successful so the launcher reloads the profile.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_AutoUpdateFirstItemFails_StillUpdatesSecondAsync()
    {
        var items = SetupProfileWithItems(
            ("item1", "Client", OldItemManifestId),
            ("item2", "Map Pack", OldSecondManifestId));
        _downloadCoordinatorMock
            .Setup(d => d.DownloadContentAsync(items[0], It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateFailure("network down"));
        SetupSuccessfulUpdate(items[1], OldSecondManifestId, NewSecondManifestId);
        _userSettings.SetAutoUpdatePreference("pub1", true);

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.True(result.Success);
        Assert.True(result.Data);
        _reconciliationServiceMock.Verify(
            r => r.OrchestrateBulkUpdateAsync(It.Is<IReadOnlyDictionary<string, string>>(m => m.ContainsKey(OldSecondManifestId)), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _notificationServiceMock.Verify(n => n.ShowError(It.IsAny<string>(), It.Is<string>(m => m.Contains("Client")), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the dialog path keeps applying one prompted update per reconciliation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_DialogUpdateWithTwoItems_StopsAfterFirstAppliedAsync()
    {
        var items = SetupProfileWithItems(
            ("item1", "Client", OldItemManifestId),
            ("item2", "Map Pack", OldSecondManifestId));
        SetupSuccessfulUpdate(items[0], OldItemManifestId, NewItemManifestId);
        SetupSuccessfulUpdate(items[1], OldSecondManifestId, NewSecondManifestId);
        _dialogServiceMock
            .Setup(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(new UpdateDialogResult { Action = "Update", Strategy = UpdateStrategy.ReplaceCurrent });

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.True(result.Success);
        Assert.True(result.Data);
        _dialogServiceMock.Verify(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        _downloadCoordinatorMock.Verify(d => d.DownloadContentAsync(items[1], It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Verifies that reconciliation started without a launching profile (the Downloads update button)
    /// still asks, so opting in never updates content the user did not launch.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_AutoUpdateEnabledWithoutLaunchingProfile_ShowsDialogAsync()
    {
        SetupProfileWithItems(("item1", "Test Item", OldItemManifestId));
        var profile = new GameProfile { Id = "prof1", Name = "Test Profile", EnabledContentIds = [OldItemManifestId] };
        _profileManagerMock
            .Setup(m => m.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));
        _userSettings.SetAutoUpdatePreference("pub1", true);
        _dialogServiceMock
            .Setup(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(new UpdateDialogResult { Action = "Skip" });

        var result = await _reconciler.CheckAndReconcileIfNeededAsync(string.Empty);

        Assert.True(result.Success);
        Assert.Equal(PublisherReconciliationResult.None, result.Data);
        _dialogServiceMock.Verify(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        _downloadCoordinatorMock.Verify(d => d.DownloadContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Verifies that when an automatic update succeeds and a prompted update from another catalog then fails,
    /// the failure is shown to the user and the applied update is still returned so the launcher reloads the profile.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_AutoSuccessThenPromptedFailure_ReturnsAppliedUpdateAsync()
    {
        var autoItem = new ContentSearchResult { Id = "item1", Name = "Client", Version = "2.0" };
        var promptedItem = new ContentSearchResult { Id = "item2", Name = "Map Pack", Version = "2.0" };
        var profile = new GameProfile { Id = "prof1", Name = "Test Profile", EnabledContentIds = [OldItemManifestId, OldSecondManifestId] };
        _profileManagerMock
            .Setup(m => m.GetProfileAsync("prof1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _subscriptionStoreMock
            .Setup(s => s.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess(
            [
                new PublisherSubscription { PublisherId = "pub1", PublisherName = "Publisher 1" },
                new PublisherSubscription { PublisherId = "pub2", PublisherName = "Publisher 2" },
            ]));
        _discovererMock
            .SetupSequence(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [autoItem] }))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [promptedItem] }));
        foreach (var (item, manifestId) in new[] { (autoItem, OldItemManifestId), (promptedItem, OldSecondManifestId) })
        {
            _contentStateServiceMock
                .Setup(s => s.GetStateAsync(item, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ContentState.UpdateAvailable);
            _contentStateServiceMock
                .Setup(s => s.GetLocalManifestIdAsync(item, It.IsAny<CancellationToken>()))
                .ReturnsAsync(manifestId);
        }

        SetupSuccessfulUpdate(autoItem, OldItemManifestId, NewItemManifestId);
        _downloadCoordinatorMock
            .Setup(d => d.DownloadContentAsync(promptedItem, It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateFailure("network down"));
        _userSettings.SetAutoUpdatePreference("pub1", true);
        _dialogServiceMock
            .Setup(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(new UpdateDialogResult { Action = "Update", Strategy = UpdateStrategy.ReplaceCurrent });

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.True(result.Success);
        Assert.True(result.Data);
        _notificationServiceMock.Verify(n => n.ShowError(It.IsAny<string>(), It.Is<string>(m => m.Contains("Map Pack")), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
        _dialogServiceMock.Verify(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        _reconciliationServiceMock.Verify(r => r.OrchestrateBulkUpdateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private List<ContentSearchResult> SetupProfileWithItems(params (string Id, string Name, string LocalManifestId)[] entries)
    {
        var profile = new GameProfile
        {
            Id = "prof1",
            Name = "Test Profile",
            EnabledContentIds = [.. entries.Select(e => e.LocalManifestId)],
        };
        _profileManagerMock
            .Setup(m => m.GetProfileAsync("prof1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        var subscription = new PublisherSubscription { PublisherId = "pub1", PublisherName = "Publisher 1" };
        _subscriptionStoreMock
            .Setup(s => s.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([subscription]));

        var items = new List<ContentSearchResult>();
        foreach (var (id, name, localManifestId) in entries)
        {
            var item = new ContentSearchResult { Id = id, Name = name, Version = "2.0" };
            _contentStateServiceMock
                .Setup(s => s.GetStateAsync(item, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ContentState.UpdateAvailable);
            _contentStateServiceMock
                .Setup(s => s.GetLocalManifestIdAsync(item, It.IsAny<CancellationToken>()))
                .ReturnsAsync(localManifestId);
            items.Add(item);
        }

        _discovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = items }));

        return items;
    }

    private void SetupSuccessfulUpdate(ContentSearchResult item, string oldManifestId, string newManifestId)
    {
        var oldManifest = new ContentManifest { Id = ManifestId.Create(oldManifestId), Name = item.Name, Version = "1.0" };
        var newManifest = new ContentManifest { Id = ManifestId.Create(newManifestId), Name = item.Name, Version = "2.0" };

        _downloadCoordinatorMock
            .Setup(d => d.DownloadContentAsync(item, It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(newManifest));
        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(ManifestId.Create(oldManifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(oldManifest));
        _reconciliationServiceMock
            .Setup(r => r.OrchestrateBulkUpdateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(ProfilesUpdatedPerItem, 0)));
        _reconciliationServiceMock
            .Setup(r => r.ScheduleGarbageCollectionAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());
    }

    private void SetupSettingsSaves()
    {
        _userSettingsServiceMock
            .Setup(s => s.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()))
            .Returns<Func<UserSettings, bool>>(apply => Task.FromResult(apply(_userSettings)));
    }
}
