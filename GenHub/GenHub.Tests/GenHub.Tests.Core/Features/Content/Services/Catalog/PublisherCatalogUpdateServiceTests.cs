using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services.Catalog;

/// <summary>
/// Contains unit tests for the <see cref="PublisherCatalogUpdateService"/> class.
/// </summary>
public sealed class PublisherCatalogUpdateServiceTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
    private readonly Mock<IServiceScope> _scopeMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<ILocalizationService> _localizationServiceMock = new();
    private readonly Mock<IPublisherSubscriptionStore> _subscriptionStoreMock = new();
    private readonly Mock<IContentStateService> _stateServiceMock = new();
    private readonly Mock<GenericCatalogDiscoverer> _discovererMock;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherCatalogUpdateServiceTests"/> class.
    /// </summary>
    public PublisherCatalogUpdateServiceTests()
    {
        _discovererMock = new Mock<GenericCatalogDiscoverer>(
            NullLogger<GenericCatalogDiscoverer>.Instance,
            new Mock<IHttpClientFactory>().Object,
            new Mock<IPublisherCatalogParser>().Object,
            new Mock<IVersionSelector>().Object,
            new Mock<IGitHubApiClient>().Object);

        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(_scopeMock.Object);
        _scopeMock.Setup(s => s.ServiceProvider).Returns(_serviceProviderMock.Object);

        _serviceProviderMock.Setup(p => p.GetService(typeof(IPublisherSubscriptionStore))).Returns(_subscriptionStoreMock.Object);
        _serviceProviderMock.Setup(p => p.GetService(typeof(IContentStateService))).Returns(_stateServiceMock.Object);
        _serviceProviderMock.Setup(p => p.GetService(typeof(GenericCatalogDiscoverer))).Returns(_discovererMock.Object);

        _localizationServiceMock
            .Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns<string, object?[]>((k, _) => k);
    }

    /// <summary>
    /// Verifies that CheckForUpdatesAsync returns no update when there are no active subscriptions.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_NoSubscriptions_ReturnsNoUpdate()
    {
        _subscriptionStoreMock
            .Setup(s => s.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess(new List<PublisherSubscription>()));

        var service = new PublisherCatalogUpdateService(
            _scopeFactoryMock.Object,
            _notificationServiceMock.Object,
            _localizationServiceMock.Object,
            NullLogger<PublisherCatalogUpdateService>.Instance);

        var result = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.False(result.IsUpdateAvailable);
        _notificationServiceMock.Verify(n => n.Show(It.IsAny<NotificationMessage>()), Times.Never);
    }

    /// <summary>
    /// Verifies that CheckForUpdatesAsync shows a persistent notification when an update is available.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WhenUpdateAvailable_ShowsPersistentNotification()
    {
        var subscription = new PublisherSubscription
        {
            PublisherId = "test-pub",
            PublisherName = "Test Publisher",
            CatalogUrl = "https://example.com/catalog.json",
        };

        _subscriptionStoreMock
            .Setup(s => s.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess(new List<PublisherSubscription> { subscription }));

        var contentItem = new ContentSearchResult
        {
            Id = "map-pack",
            Name = "Super Map Pack",
            Version = "2.0.0",
        };

        _discovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<GenHub.Core.Models.Content.ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = new List<ContentSearchResult> { contentItem },
                TotalItems = 1,
            }));

        _stateServiceMock
            .Setup(s => s.GetStateAsync(contentItem, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.UpdateAvailable);

        NotificationMessage? shownNotification = null;
        _notificationServiceMock
            .Setup(n => n.Show(It.IsAny<NotificationMessage>()))
            .Callback<NotificationMessage>(msg => shownNotification = msg);

        var service = new PublisherCatalogUpdateService(
            _scopeFactoryMock.Object,
            _notificationServiceMock.Object,
            _localizationServiceMock.Object,
            NullLogger<PublisherCatalogUpdateService>.Instance);

        var result = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.True(result.IsUpdateAvailable);
        Assert.NotNull(shownNotification);
        Assert.Null(shownNotification.AutoDismissMilliseconds);
        Assert.True(shownNotification.IsPersistent);
        Assert.Single(shownNotification.Actions);
    }

    /// <summary>
    /// Verifies that CheckForUpdatesAsync does not show a notification again when the update was dismissed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WhenUpdateDismissed_DoesNotShowNotificationAgain()
    {
        var subscription = new PublisherSubscription
        {
            PublisherId = "test-pub",
            PublisherName = "Test Publisher",
            CatalogUrl = "https://example.com/catalog.json",
        };

        _subscriptionStoreMock
            .Setup(s => s.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess(new List<PublisherSubscription> { subscription }));

        var contentItem = new ContentSearchResult
        {
            Id = "map-pack",
            Name = "Super Map Pack",
            Version = "2.0.0",
        };

        _discovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<GenHub.Core.Models.Content.ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = new List<ContentSearchResult> { contentItem },
                TotalItems = 1,
            }));

        _stateServiceMock
            .Setup(s => s.GetStateAsync(contentItem, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.UpdateAvailable);

        var service = new PublisherCatalogUpdateService(
            _scopeFactoryMock.Object,
            _notificationServiceMock.Object,
            _localizationServiceMock.Object,
            NullLogger<PublisherCatalogUpdateService>.Instance);

        // User dismisses update
        service.DismissUpdate("test-pub", "map-pack", "2.0.0");
        Assert.True(service.IsUpdateDismissed("test-pub", "map-pack", "2.0.0"));

        await service.CheckForUpdatesAsync(CancellationToken.None);

        _notificationServiceMock.Verify(n => n.Show(It.IsAny<NotificationMessage>()), Times.Never);
    }
}
