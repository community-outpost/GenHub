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
            _reconciliationServiceMock.Object);

        _userSettingsServiceMock.Setup(s => s.Get()).Returns(_userSettings);

        _reconciler = new GenericCatalogProfileReconciler(
            NullLogger<GenericCatalogProfileReconciler>.Instance,
            _profileManagerMock.Object,
            _subscriptionStoreMock.Object,
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
            .ReturnsAsync(OperationResult<ContentSearchResultPage>.CreateSuccess(new ContentSearchResultPage()));

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.True(result.Success);
        Assert.Equal(PublisherReconciliationResult.None, result.Data);
    }

    /// <summary>
    /// Verifies that when an item is not in UpdateAvailable state, it is skipped.
    /// </summary>
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
        var page = new ContentSearchResultPage { Items = [item] };
        _discovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentSearchResultPage>.CreateSuccess(page));

        _contentStateServiceMock
            .Setup(s => s.GetStateAsync(item, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.Installed);

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("prof1");

        Assert.True(result.Success);
        Assert.Equal(PublisherReconciliationResult.None, result.Data);
        _dialogServiceMock.Verify(d => d.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Verifies that when the item version has been marked as skipped by the user, the prompt is skipped.
    /// </summary>
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
        var page = new ContentSearchResultPage { Items = [item] };
        _discovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentSearchResultPage>.CreateSuccess(page));

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
        var page = new ContentSearchResultPage { Items = [item] };
        _discovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentSearchResultPage>.CreateSuccess(page));

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
        _downloadCoordinatorMock.Verify(d => d.DownloadContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
