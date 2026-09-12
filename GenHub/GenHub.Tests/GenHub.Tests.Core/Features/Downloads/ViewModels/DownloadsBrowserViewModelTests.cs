using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Parsers;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ContentState = GenHub.Core.Models.Enums.ContentState;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using GameType = GenHub.Core.Models.Enums.GameType;
using PublisherSubscription = GenHub.Core.Models.Providers.PublisherSubscription;

namespace GenHub.Tests.Core.Features.Downloads.ViewModels;

/// <summary>
/// Regression tests for publisher-specific Downloads browser affordances.
/// </summary>
public class DownloadsBrowserViewModelTests
{
    /// <summary>
    /// Verifies that curated publishers do not expose unused search or filtering controls.
    /// </summary>
    /// <param name="publisherId">The curated publisher to select.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(PublisherTypeConstants.GeneralsOnline)]
    [InlineData(CommunityOutpostConstants.PublisherType)]
    [InlineData(PublisherTypeConstants.TheSuperHackers)]
    public async Task SelectPublisher_CuratedPublisher_HidesSearchAndFiltersAsync(string publisherId)
    {
        // Arrange
        using var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        // Act
        viewModel.SelectedPublisher = viewModel.Publishers.Single(p => p.PublisherId == publisherId);

        // Assert
        Assert.False(viewModel.CanSearch);
        Assert.False(viewModel.CanShowFilters);
        Assert.False(viewModel.IsFilterPanelVisible);
    }

    /// <summary>
    /// Verifies that the GitHub browse experience exposes search and filters.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SelectPublisher_GitHub_ExposesSearchAndFiltersAsync()
    {
        // Arrange
        using var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        // Act
        viewModel.SelectedPublisher = viewModel.Publishers.Single(
            p => p.PublisherId == GitHubTopicsConstants.PublisherType);

        // Assert
        Assert.True(viewModel.CanSearch);
        Assert.True(viewModel.CanShowFilters);
    }

    /// <summary>
    /// Verifies that switching publishers while discovery is in-flight cancels the previous
    /// publisher's load and prevents its items from bleeding into the newly selected publisher's grid.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SelectPublisher_SwitchingPublisherMidDiscovery_PreventsItemBleedFromPreviousPublisherAsync()
    {
        // Arrange
        var sub1 = new PublisherSubscription
        {
            PublisherId = "sub-cnc",
            PublisherName = "CNC Labs Sub",
            CatalogUrl = "https://example.com/cnc/catalog.json",
        };
        var sub2 = new PublisherSubscription
        {
            PublisherId = "sub-github",
            PublisherName = "GitHub Sub",
            CatalogUrl = "https://example.com/github/catalog.json",
        };

        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([sub1, sub2]));

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(GenericCatalogDiscoverer)))
            .Returns(() =>
            {
                var discoverer = new GenericCatalogDiscoverer(
                    new Mock<ILogger<GenericCatalogDiscoverer>>().Object,
                    new Mock<IHttpClientFactory>().Object,
                    new Mock<IPublisherCatalogParser>().Object,
                    new Mock<IVersionSelector>().Object,
                    new Mock<IGitHubApiClient>().Object);
                return discoverer;
            });

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(l => l.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        using var viewModel = new DownloadsBrowserViewModel(
            serviceProviderMock.Object,
            new Mock<ILogger<DownloadsBrowserViewModel>>().Object,
            [],
            new Mock<IContentStateService>().Object,
            new Mock<IContentOrchestrator>().Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            loggerFactoryMock.Object,
            subscriptionStore.Object);

        await viewModel.InitializeAsync();

        var cncPublisher = viewModel.Publishers.First(p => p.PublisherId == "sub-cnc");
        var githubPublisher = viewModel.Publishers.First(p => p.PublisherId == "sub-github");

        // Act
        viewModel.SelectedPublisher = cncPublisher;
        Assert.Equal(cncPublisher, viewModel.SelectedPublisher);
        viewModel.SelectedPublisher = githubPublisher;

        // Assert
        Assert.Equal(githubPublisher, viewModel.SelectedPublisher);
        Assert.True(viewModel.SelectedPublisher.IsSelected);
        Assert.False(cncPublisher.IsSelected);
    }

    /// <summary>
    /// Verifies that spurious null assignments to SelectedPublisher (e.g. from UI detachment on tab change)
    /// are ignored and retain the current publisher selection.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SelectedPublisher_NullAssignmentWhenPopulated_RetainsCurrentPublisherAsync()
    {
        // Arrange
        using var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        var modDbPublisher = viewModel.Publishers.Single(p => p.PublisherId == PublisherTypeConstants.TheSuperHackers);
        viewModel.SelectedPublisher = modDbPublisher;
        Assert.Equal(modDbPublisher, viewModel.SelectedPublisher);

        // Act - simulate visual tree detachment setting SelectedItem to null
        viewModel.SelectedPublisher = null;

        // Assert
        Assert.NotNull(viewModel.SelectedPublisher);
        Assert.Equal(PublisherTypeConstants.TheSuperHackers, viewModel.SelectedPublisher.PublisherId);
    }

    /// <summary>
    /// Verifies that OnTabActivatedAsync does not reset an existing publisher back to GeneralsOnline.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task OnTabActivatedAsync_WithExistingSelectedPublisher_PreservesSelectionAsync()
    {
        // Arrange
        using var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        var modDbPublisher = viewModel.Publishers.Single(p => p.PublisherId == PublisherTypeConstants.TheSuperHackers);
        viewModel.SelectedPublisher = modDbPublisher;

        // Act
        await viewModel.OnTabActivatedAsync();

        // Assert
        Assert.NotNull(viewModel.SelectedPublisher);
        Assert.Equal(PublisherTypeConstants.TheSuperHackers, viewModel.SelectedPublisher.PublisherId);
    }

    /// <summary>
    /// Verifies that item loading in the browser streams items incrementally into the collection.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task StreamingLoading_AppendsItemsDynamicallyAsResolvedAsync()
    {
        // Arrange
        var item1 = new ContentSearchResult { Id = "mod1", Name = "Mod 1", ProviderName = "Generic" };
        var item2 = new ContentSearchResult { Id = "mod2", Name = "Mod 2", ProviderName = "Generic" };
        var item3 = new ContentSearchResult { Id = "mod3", Name = "Mod 3", ProviderName = "Generic" };

        var discoveryResult = new ContentDiscoveryResult
        {
            Items = [item1, item2, item3],
            TotalItems = 3,
            HasMoreItems = false,
        };

        var sub = new PublisherSubscription
        {
            PublisherId = "sub-stream",
            PublisherName = "Streaming Sub",
            CatalogUrl = "https://example.com/stream/catalog.json",
        };

        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([sub]));

        var mockDiscoverer = new Mock<GenericCatalogDiscoverer>(
            new Mock<ILogger<GenericCatalogDiscoverer>>().Object,
            new Mock<IHttpClientFactory>().Object,
            new Mock<IPublisherCatalogParser>().Object,
            new Mock<IVersionSelector>().Object,
            new Mock<IGitHubApiClient>().Object);

        mockDiscoverer
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(discoveryResult));

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(GenericCatalogDiscoverer)))
            .Returns(mockDiscoverer.Object);

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(l => l.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        using var viewModel = new DownloadsBrowserViewModel(
            serviceProviderMock.Object,
            new Mock<ILogger<DownloadsBrowserViewModel>>().Object,
            [],
            new Mock<IContentStateService>().Object,
            new Mock<IContentOrchestrator>().Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            loggerFactoryMock.Object,
            subscriptionStore.Object);

        await viewModel.InitializeAsync();

        var streamPublisher = viewModel.Publishers.First(p => p.PublisherId == "sub-stream");

        // Act
        viewModel.SelectedPublisher = streamPublisher;

        // Allow async streaming tasks to complete
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (viewModel.IsLoading && DateTime.UtcNow < timeout)
        {
            await Task.Delay(25);
        }

        // Assert
        Assert.Equal(3, viewModel.ContentItems.Count);
        Assert.Equal("mod1", viewModel.ContentItems[0].SearchResult.Id);
        Assert.Equal("mod2", viewModel.ContentItems[1].SearchResult.Id);
        Assert.Equal("mod3", viewModel.ContentItems[2].SearchResult.Id);
        Assert.False(viewModel.IsLoading);
    }

    /// <summary>
    /// Verifies that rapidly clicking Publisher A -> Publisher B -> Publisher C leaves
    /// the UI showing strictly Publisher C's items with zero bleed from A or B.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RapidPublisherSwitching_A_to_B_to_C_LeavesOnly_C_InViewWithZeroBleedAsync()
    {
        // Arrange
        var tcsA = new TaskCompletionSource<OperationResult<ContentDiscoveryResult>>();
        var tcsB = new TaskCompletionSource<OperationResult<ContentDiscoveryResult>>();

        var itemsA = new List<ContentSearchResult>
        {
            new() { Id = "mod-a1", Name = "Mod A1", ProviderName = "Generic" },
            new() { Id = "mod-a2", Name = "Mod A2", ProviderName = "Generic" },
        };
        var itemsB = new List<ContentSearchResult>
        {
            new() { Id = "mod-b1", Name = "Mod B1", ProviderName = "Generic" },
        };
        var itemsC = new List<ContentSearchResult>
        {
            new() { Id = "mod-c1", Name = "Mod C1", ProviderName = "Generic" },
            new() { Id = "mod-c2", Name = "Mod C2", ProviderName = "Generic" },
        };

        var subA = new PublisherSubscription { PublisherId = "sub-a", PublisherName = "A", CatalogUrl = "https://example.com/a.json" };
        var subB = new PublisherSubscription { PublisherId = "sub-b", PublisherName = "B", CatalogUrl = "https://example.com/b.json" };
        var subC = new PublisherSubscription { PublisherId = "sub-c", PublisherName = "C", CatalogUrl = "https://example.com/c.json" };

        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([subA, subB, subC]));

        var discA = new Mock<GenericCatalogDiscoverer>(
            new Mock<ILogger<GenericCatalogDiscoverer>>().Object,
            new Mock<IHttpClientFactory>().Object,
            new Mock<IPublisherCatalogParser>().Object,
            new Mock<IVersionSelector>().Object,
            new Mock<IGitHubApiClient>().Object);
        discA.Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .Returns(tcsA.Task);

        var discB = new Mock<GenericCatalogDiscoverer>(
            new Mock<ILogger<GenericCatalogDiscoverer>>().Object,
            new Mock<IHttpClientFactory>().Object,
            new Mock<IPublisherCatalogParser>().Object,
            new Mock<IVersionSelector>().Object,
            new Mock<IGitHubApiClient>().Object);
        discB.Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .Returns(tcsB.Task);

        var discC = new Mock<GenericCatalogDiscoverer>(
            new Mock<ILogger<GenericCatalogDiscoverer>>().Object,
            new Mock<IHttpClientFactory>().Object,
            new Mock<IPublisherCatalogParser>().Object,
            new Mock<IVersionSelector>().Object,
            new Mock<IGitHubApiClient>().Object);
        discC.Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = itemsC, TotalItems = 2 }));

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(GenericCatalogDiscoverer)))
            .Returns<Type>(_ =>
            {
                var d = new Mock<GenericCatalogDiscoverer>(
                    new Mock<ILogger<GenericCatalogDiscoverer>>().Object,
                    new Mock<IHttpClientFactory>().Object,
                    new Mock<IPublisherCatalogParser>().Object,
                    new Mock<IVersionSelector>().Object,
                    new Mock<IGitHubApiClient>().Object);
                return d.Object;
            });

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(l => l.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        using var viewModel = new DownloadsBrowserViewModel(
            serviceProviderMock.Object,
            new Mock<ILogger<DownloadsBrowserViewModel>>().Object,
            [],
            new Mock<IContentStateService>().Object,
            new Mock<IContentOrchestrator>().Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            loggerFactoryMock.Object,
            subscriptionStore.Object);

        // Inject discoverers directly into service provider setup
        var discovererIndex = 0;
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(GenericCatalogDiscoverer)))
            .Returns(() =>
            {
                var selected = discovererIndex switch
                {
                    0 => discA.Object,
                    1 => discB.Object,
                    _ => discC.Object,
                };
                discovererIndex++;
                return selected;
            });

        await viewModel.InitializeAsync();

        var publisherA = viewModel.Publishers.First(p => p.PublisherId == "sub-a");
        var publisherB = viewModel.Publishers.First(p => p.PublisherId == "sub-b");
        var publisherC = viewModel.Publishers.First(p => p.PublisherId == "sub-c");

        // Act: Rapidly switch A -> B -> C
        viewModel.SelectedPublisher = publisherA;
        Assert.Equal(publisherA, viewModel.SelectedPublisher);
        viewModel.SelectedPublisher = publisherB;
        Assert.Equal(publisherB, viewModel.SelectedPublisher);
        viewModel.SelectedPublisher = publisherC;

        // Now resolve delayed tasks for A and B in background
        tcsA.SetResult(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = itemsA, TotalItems = 2 }));
        tcsB.SetResult(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = itemsB, TotalItems = 1 }));

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (viewModel.IsLoading && DateTime.UtcNow < timeout)
        {
            await Task.Delay(25);
        }

        // Assert: ContentItems MUST contain ONLY items from C
        Assert.Equal(2, viewModel.ContentItems.Count);
        Assert.All(viewModel.ContentItems, item => Assert.StartsWith("mod-c", item.SearchResult.Id));
    }

    /// <summary>
    /// Verifies that interrupting a fetch of Publisher A allows the background fetch to complete
    /// fully so navigating back to Publisher A later restores the full dataset from cache.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task BackgroundCacheCompletion_InterruptedFetchOfPublisherA_CommitsFullDatasetToCacheAsync()
    {
        // Arrange
        var tcsA = new TaskCompletionSource<OperationResult<ContentDiscoveryResult>>();

        var itemsA = new List<ContentSearchResult>
        {
            new() { Id = "mod-a1", Name = "Mod A1", ProviderName = "Generic" },
            new() { Id = "mod-a2", Name = "Mod A2", ProviderName = "Generic" },
            new() { Id = "mod-a3", Name = "Mod A3", ProviderName = "Generic" },
        };
        var itemsB = new List<ContentSearchResult>
        {
            new() { Id = "mod-b1", Name = "Mod B1", ProviderName = "Generic" },
        };

        var subA = new PublisherSubscription { PublisherId = "sub-a", PublisherName = "A", CatalogUrl = "https://example.com/a.json" };
        var subB = new PublisherSubscription { PublisherId = "sub-b", PublisherName = "B", CatalogUrl = "https://example.com/b.json" };

        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([subA, subB]));

        var discA = new Mock<GenericCatalogDiscoverer>(
            new Mock<ILogger<GenericCatalogDiscoverer>>().Object,
            new Mock<IHttpClientFactory>().Object,
            new Mock<IPublisherCatalogParser>().Object,
            new Mock<IVersionSelector>().Object,
            new Mock<IGitHubApiClient>().Object);
        discA.Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .Returns(tcsA.Task);

        var discB = new Mock<GenericCatalogDiscoverer>(
            new Mock<ILogger<GenericCatalogDiscoverer>>().Object,
            new Mock<IHttpClientFactory>().Object,
            new Mock<IPublisherCatalogParser>().Object,
            new Mock<IVersionSelector>().Object,
            new Mock<IGitHubApiClient>().Object);
        discB.Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = itemsB, TotalItems = 1 }));

        var discovererIndex = 0;
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(GenericCatalogDiscoverer)))
            .Returns(() =>
            {
                var selected = discovererIndex switch
                {
                    0 => discA.Object,
                    _ => discB.Object,
                };
                discovererIndex++;
                return selected;
            });

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(l => l.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        using var viewModel = new DownloadsBrowserViewModel(
            serviceProviderMock.Object,
            new Mock<ILogger<DownloadsBrowserViewModel>>().Object,
            [],
            new Mock<IContentStateService>().Object,
            new Mock<IContentOrchestrator>().Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            loggerFactoryMock.Object,
            subscriptionStore.Object);

        await viewModel.InitializeAsync();

        var publisherA = viewModel.Publishers.First(p => p.PublisherId == "sub-a");
        var publisherB = viewModel.Publishers.First(p => p.PublisherId == "sub-b");

        // Act 1: Select Publisher A, then switch to B while A is in-flight
        viewModel.SelectedPublisher = publisherA;
        Assert.Equal(publisherA, viewModel.SelectedPublisher);
        viewModel.SelectedPublisher = publisherB;

        // Act 2: Complete Publisher A's background fetch and allow background task to commit to cache
        tcsA.SetResult(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = itemsA, TotalItems = 3 }));
        await Task.Delay(250);

        // Verify currently on B
        Assert.Single(viewModel.ContentItems);
        Assert.Equal("mod-b1", viewModel.ContentItems[0].SearchResult.Id);

        // Act 3: Switch back to Publisher A
        viewModel.SelectedPublisher = publisherA;
        for (var i = 0; i < 20 && viewModel.ContentItems.Count < 3; i++)
        {
            await Task.Delay(50);
        }

        // Assert: All 3 items from Publisher A are restored from cache
        Assert.Equal(3, viewModel.ContentItems.Count);
        Assert.Equal("mod-a1", viewModel.ContentItems[0].SearchResult.Id);
        Assert.Equal("mod-a2", viewModel.ContentItems[1].SearchResult.Id);
        Assert.Equal("mod-a3", viewModel.ContentItems[2].SearchResult.Id);
    }

    /// <summary>
    /// Verifies that reading from cache strictly returns only items associated with the selected publisher.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CacheRetrieval_StrictlyReturnsOnlySelectedPublisherItemsAsync()
    {
        // Arrange
        var itemsA = new List<ContentSearchResult>
        {
            new() { Id = "mod-a1", Name = "Mod A1", ProviderName = "Generic" },
        };
        var itemsB = new List<ContentSearchResult>
        {
            new() { Id = "mod-b1", Name = "Mod B1", ProviderName = "Generic" },
        };

        var subA = new PublisherSubscription { PublisherId = "sub-a", PublisherName = "A", CatalogUrl = "https://example.com/a.json" };
        var subB = new PublisherSubscription { PublisherId = "sub-b", PublisherName = "B", CatalogUrl = "https://example.com/b.json" };

        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([subA, subB]));

        var discA = new Mock<GenericCatalogDiscoverer>(
            new Mock<ILogger<GenericCatalogDiscoverer>>().Object,
            new Mock<IHttpClientFactory>().Object,
            new Mock<IPublisherCatalogParser>().Object,
            new Mock<IVersionSelector>().Object,
            new Mock<IGitHubApiClient>().Object);
        discA.Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = itemsA, TotalItems = 1 }));

        var discB = new Mock<GenericCatalogDiscoverer>(
            new Mock<ILogger<GenericCatalogDiscoverer>>().Object,
            new Mock<IHttpClientFactory>().Object,
            new Mock<IPublisherCatalogParser>().Object,
            new Mock<IVersionSelector>().Object,
            new Mock<IGitHubApiClient>().Object);
        discB.Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = itemsB, TotalItems = 1 }));

        var discovererIndex = 0;
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(GenericCatalogDiscoverer)))
            .Returns(() =>
            {
                var selected = discovererIndex switch
                {
                    0 => discA.Object,
                    _ => discB.Object,
                };
                discovererIndex++;
                return selected;
            });

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(l => l.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        using var viewModel = new DownloadsBrowserViewModel(
            serviceProviderMock.Object,
            new Mock<ILogger<DownloadsBrowserViewModel>>().Object,
            [],
            new Mock<IContentStateService>().Object,
            new Mock<IContentOrchestrator>().Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            loggerFactoryMock.Object,
            subscriptionStore.Object);

        await viewModel.InitializeAsync();

        var publisherA = viewModel.Publishers.First(p => p.PublisherId == "sub-a");
        var publisherB = viewModel.Publishers.First(p => p.PublisherId == "sub-b");

        // Load A into cache
        viewModel.SelectedPublisher = publisherA;
        await Task.Delay(50);
        Assert.Single(viewModel.ContentItems);
        Assert.Equal("mod-a1", viewModel.ContentItems[0].SearchResult.Id);

        // Load B into cache
        viewModel.SelectedPublisher = publisherB;
        await Task.Delay(50);
        Assert.Single(viewModel.ContentItems);
        Assert.Equal("mod-b1", viewModel.ContentItems[0].SearchResult.Id);

        // Switch back to A (cache hit)
        viewModel.SelectedPublisher = publisherA;
        await Task.Delay(50);
        Assert.Single(viewModel.ContentItems);
        Assert.Equal("mod-a1", viewModel.ContentItems[0].SearchResult.Id);

        // Switch back to B (cache hit)
        viewModel.SelectedPublisher = publisherB;
        await Task.Delay(50);
        Assert.Single(viewModel.ContentItems);
        Assert.Equal("mod-b1", viewModel.ContentItems[0].SearchResult.Id);
    }

    /// <summary>
    /// Verifies that DownloadContentCommand propagates cancellation token and updates item download status on cancellation.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Fact]
    public async Task DownloadContentCommand_WhenCancelled_UpdatesDownloadStatusToCancelledAsync()
    {
        // Arrange
        var orchestrator = new Mock<IContentOrchestrator>();
        var tcs = new TaskCompletionSource<OperationResult<ContentManifest>>();
        orchestrator
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .Returns<ContentSearchResult, IProgress<ContentAcquisitionProgress>, CancellationToken>((_, _, token) =>
            {
                token.Register(() => tcs.TrySetCanceled(token));
                return tcs.Task;
            });

        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([]));

        using var viewModel = new DownloadsBrowserViewModel(
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<DownloadsBrowserViewModel>>().Object,
            [],
            new Mock<IContentStateService>().Object,
            orchestrator.Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ILoggerFactory>().Object,
            subscriptionStore.Object);

        var item = new ContentGridItemViewModel(
            new ContentSearchResult { Id = "test", Name = "Test Mod" },
            new Mock<IContentStateService>().Object,
            new Mock<ILogger<ContentGridItemViewModel>>().Object);

        // Act
        var downloadTask = viewModel.DownloadContentCommand.ExecuteAsync(item);
        viewModel.DownloadContentCommand.Cancel();
        await downloadTask;

        // Assert
        Assert.False(item.IsDownloading);
        Assert.Equal("Download cancelled", item.DownloadStatus);
    }

    /// <summary>
    /// Verifies that DownloadContentCommand routes through the content download coordinator when present.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DownloadContentAsync_WhenCoordinatorProvided_RoutesThroughCoordinatorAsync()
    {
        // Arrange
        var coordinator = new Mock<IContentDownloadCoordinator>();
        var orchestrator = new Mock<IContentOrchestrator>();
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.mod.testmod"),
            Name = "Test Mod",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        coordinator
            .Setup(c => c.DownloadContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        var stateService = new Mock<IContentStateService>();
        stateService
            .Setup(s => s.GetStateAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.Downloaded);

        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([]));

        using var viewModel = new DownloadsBrowserViewModel(
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<DownloadsBrowserViewModel>>().Object,
            [],
            stateService.Object,
            orchestrator.Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ILoggerFactory>().Object,
            subscriptionStore.Object,
            coordinator.Object);

        var item = new ContentGridItemViewModel(
            new ContentSearchResult { Id = "test", Name = "Test Mod" },
            stateService.Object,
            new Mock<ILogger<ContentGridItemViewModel>>().Object);

        // Act
        await viewModel.DownloadContentCommand.ExecuteAsync(item);

        // Assert
        coordinator.Verify(
            c => c.DownloadContentAsync(
                item.SearchResult,
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        orchestrator.Verify(
            o => o.AcquireContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.True(item.IsDownloaded);
    }

    /// <summary>
    /// Verifies that CleanupInFlight does not dispose items currently present in ContentItems.
    /// </summary>
    [Fact]
    public void CleanupInFlight_DoesNotDisposeItemsCurrentlyInContentItems()
    {
        // Arrange
        using var viewModel = CreateViewModel();

        var renderedItem = new ContentGridItemViewModel(
            new ContentSearchResult { Id = "test1", Name = "Rendered Mod" },
            new Mock<IContentStateService>().Object,
            new Mock<ILogger<ContentGridItemViewModel>>().Object);

        var unrenderedItem = new ContentGridItemViewModel(
            new ContentSearchResult { Id = "test2", Name = "Unrendered Mod" },
            new Mock<IContentStateService>().Object,
            new Mock<ILogger<ContentGridItemViewModel>>().Object);

        viewModel.ContentItems.Add(renderedItem);

        using var cts = new CancellationTokenSource();
        var inFlightOp = new DownloadsBrowserViewModel.PublisherInFlightOperation(
            "test-publisher",
            new ContentSearchQuery(),
            cts);
        inFlightOp.ResolvedItems.Add(renderedItem);
        inFlightOp.ResolvedItems.Add(unrenderedItem);

        // Act
        viewModel.CleanupInFlight("test-publisher", inFlightOp);

        // Assert
        Assert.False(renderedItem.IsDisposed, "Items visible in ContentItems must NOT be disposed by CleanupInFlight.");
        Assert.True(unrenderedItem.IsDisposed, "Orphan items not in ContentItems or cache MUST be disposed.");
    }

    /// <summary>
    /// Verifies that when a custom query is superseded or cancelled mid-creation,
    /// orphan card VMs created during the fetch are disposed by CleanupInFlight.
    /// </summary>
    [Fact]
    public void CleanupInFlight_DisposesOrphanVmsWhenCustomQueryIsSuperseded()
    {
        // Arrange
        using var viewModel = CreateViewModel();

        var orphanItem1 = new ContentGridItemViewModel(
            new ContentSearchResult { Id = "orphan1", Name = "Orphan Mod 1" },
            new Mock<IContentStateService>().Object,
            new Mock<ILogger<ContentGridItemViewModel>>().Object);

        var orphanItem2 = new ContentGridItemViewModel(
            new ContentSearchResult { Id = "orphan2", Name = "Orphan Mod 2" },
            new Mock<IContentStateService>().Object,
            new Mock<ILogger<ContentGridItemViewModel>>().Object);

        using var cts = new CancellationTokenSource();
        var customQueryOp = new DownloadsBrowserViewModel.PublisherInFlightOperation(
            "custom-search-publisher",
            new ContentSearchQuery { SearchTerm = "test search" },
            cts);
        customQueryOp.ResolvedItems.Add(orphanItem1);
        customQueryOp.ResolvedItems.Add(orphanItem2);

        // Act: search cancelled or superseded, items never made it to ContentItems
        viewModel.CleanupInFlight("custom-search-publisher", customQueryOp);

        // Assert
        Assert.True(orphanItem1.IsDisposed, "Orphan VMs from superseded custom query must be disposed.");
        Assert.True(orphanItem2.IsDisposed, "Orphan VMs from superseded custom query must be disposed.");
    }

    /// <summary>
    /// Verifies that when switching away from an in-flight publisher and switching back,
    /// the active request ID is updated on the in-flight operation so it doesn't get stuck in loading state.
    /// </summary>
    [Fact]
    public void HandleSelectedPublisherChanged_WhenInFlightOperationExists_UpdatesActiveRequestIdAndAttaches()
    {
        // Arrange
        using var viewModel = CreateViewModel();

        var publisherA = new PublisherItemViewModel("pub-a", "Publisher A");

        var itemA = new ContentGridItemViewModel(
            new ContentSearchResult { Id = "mod-a", Name = "Mod A" },
            new Mock<IContentStateService>().Object,
            new Mock<ILogger<ContentGridItemViewModel>>().Object);

        var cts = new CancellationTokenSource();
        var inFlightOp = new DownloadsBrowserViewModel.PublisherInFlightOperation(
            "pub-a",
            new ContentSearchQuery(),
            cts)
        {
            ActiveRequestId = 1,
            IsCompleted = false,
        };
        inFlightOp.ResolvedItems.Add(itemA);

        viewModel.SetInFlightOperationForTesting("pub-a", inFlightOp);

        // Act: select Publisher A (attaching to in-flight operation)
        viewModel.SelectedPublisher = publisherA;

        // Assert
        Assert.Single(viewModel.ContentItems);
        Assert.Equal("mod-a", viewModel.ContentItems[0].Id);
        Assert.True(viewModel.IsLoading);
        Assert.Equal(viewModel.ActiveRequestId, inFlightOp.ActiveRequestId);
    }

    /// <summary>
    /// Verifies that in a multi-release feed, the newest release (not yet downloaded) is marked
    /// NotDownloaded (showing only Download) while older downloaded releases are marked
    /// UpdateAvailable targeting the newest release (showing Update Available and Add to Profile).
    /// </summary>
    [Fact]
    public void ReconcileReleaseUpdateStates_WhenNewestReleaseIsNotDownloadedAndOlderIsDownloaded_ReconcilesCorrectly()
    {
        // Arrange
        var stateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();

        var newestSr = new ContentSearchResult
        {
            Id = "github.TheSuperHackers.GeneralsGameCode.weekly-2026-09-05.zerohour",
            Name = "GeneralsGameCode weekly-2026-09-05 — Zero Hour",
            Version = "2026-09-05",
            ProviderName = "thesuperhackers",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            LastUpdated = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc),
            ResolverMetadata =
            {
                [GitHubConstants.OwnerMetadataKey] = "TheSuperHackers",
                [GitHubConstants.RepoMetadataKey] = "GeneralsGameCode",
            },
        };
        var newestVm = new ContentGridItemViewModel(newestSr, stateServiceMock.Object, loggerMock.Object)
        {
            CurrentState = ContentState.UpdateAvailable,
            IsDownloaded = true,
        };
        var newestVariant = new InstallableVariant
        {
            Name = "Zero Hour",
            ManifestId = "1.20260905.thesuperhackers.gameclient.zerohour",
            CurrentState = ContentState.UpdateAvailable,
        };
        newestVm.Variants.Add(newestVariant);
        newestVm.SelectedVariant = newestVariant;

        var olderSr = new ContentSearchResult
        {
            Id = "github.TheSuperHackers.GeneralsGameCode.weekly-2026-08-28.zerohour",
            Name = "GeneralsGameCode weekly-2026-08-28 — Zero Hour",
            Version = "2026-08-28",
            ProviderName = "thesuperhackers",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            LastUpdated = new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc),
            ResolverMetadata =
            {
                [GitHubConstants.OwnerMetadataKey] = "TheSuperHackers",
                [GitHubConstants.RepoMetadataKey] = "GeneralsGameCode",
            },
        };
        var olderVm = new ContentGridItemViewModel(olderSr, stateServiceMock.Object, loggerMock.Object)
        {
            CurrentState = ContentState.Downloaded,
            IsDownloaded = true,
        };
        var olderVariant = new InstallableVariant
        {
            Name = "Zero Hour",
            ManifestId = "1.20260828.thesuperhackers.gameclient.zerohour",
            CurrentState = ContentState.Downloaded,
        };
        olderVm.Variants.Add(olderVariant);
        olderVm.SelectedVariant = olderVariant;

        var oldestSr = new ContentSearchResult
        {
            Id = "github.TheSuperHackers.GeneralsGameCode.weekly-2026-08-21.zerohour",
            Name = "GeneralsGameCode weekly-2026-08-21 — Zero Hour",
            Version = "2026-08-21",
            ProviderName = "thesuperhackers",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            LastUpdated = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc),
            ResolverMetadata =
            {
                [GitHubConstants.OwnerMetadataKey] = "TheSuperHackers",
                [GitHubConstants.RepoMetadataKey] = "GeneralsGameCode",
            },
        };
        var oldestVm = new ContentGridItemViewModel(oldestSr, stateServiceMock.Object, loggerMock.Object)
        {
            CurrentState = ContentState.NotDownloaded,
            IsDownloaded = false,
        };
        var oldestVariant = new InstallableVariant
        {
            Name = "Zero Hour",
            ManifestId = "1.20260821.thesuperhackers.gameclient.zerohour",
            CurrentState = ContentState.NotDownloaded,
        };
        oldestVm.Variants.Add(oldestVariant);
        oldestVm.SelectedVariant = oldestVariant;

        // Act
        DownloadsBrowserViewModel.ReconcileReleaseUpdateStates([newestVm, olderVm, oldestVm]);

        // Assert: Newest item must be NotDownloaded, show only Download button
        Assert.Equal(ContentState.NotDownloaded, newestVm.CurrentState);
        Assert.Equal(ContentState.NotDownloaded, newestVm.EffectiveCurrentState);
        Assert.False(newestVm.IsDownloaded);
        Assert.False(newestVm.EffectiveIsDownloaded);
        Assert.Null(newestVm.UpdateTargetVm);
        Assert.True(newestVm.ShowDownloadButton);
        Assert.False(newestVm.ShowUpdateButton);
        Assert.False(newestVm.ShowAddToProfileButton);
        Assert.Equal(ContentState.NotDownloaded, newestVariant.CurrentState);

        // Assert: Older downloaded item must be UpdateAvailable targeting newestVm
        Assert.Equal(ContentState.UpdateAvailable, olderVm.CurrentState);
        Assert.Equal(ContentState.UpdateAvailable, olderVm.EffectiveCurrentState);
        Assert.True(olderVm.IsDownloaded);
        Assert.True(olderVm.EffectiveIsDownloaded);
        Assert.Same(newestVm, olderVm.UpdateTargetVm);
        Assert.False(olderVm.ShowDownloadButton);
        Assert.True(olderVm.ShowUpdateButton);
        Assert.True(olderVm.ShowAddToProfileButton);
        Assert.Equal(ContentState.UpdateAvailable, olderVariant.CurrentState);

        // Assert: Oldest un-downloaded item remains NotDownloaded, show only Download
        Assert.Equal(ContentState.NotDownloaded, oldestVm.CurrentState);
        Assert.Equal(ContentState.NotDownloaded, oldestVm.EffectiveCurrentState);
        Assert.False(oldestVm.IsDownloaded);
        Assert.False(oldestVm.EffectiveIsDownloaded);
        Assert.Null(oldestVm.UpdateTargetVm);
        Assert.True(oldestVm.ShowDownloadButton);
        Assert.False(oldestVm.ShowUpdateButton);
        Assert.False(oldestVm.ShowAddToProfileButton);
    }

    /// <summary>
    /// Verifies that when the newest release is already downloaded, older downloaded releases
    /// stay Downloaded and do not show an unnecessary Update Available button.
    /// </summary>
    [Fact]
    public void ReconcileReleaseUpdateStates_WhenNewestReleaseIsAlreadyDownloaded_OlderReleaseDoesNotOfferUpdate()
    {
        // Arrange
        var stateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();

        var newestSr = new ContentSearchResult
        {
            Id = "github.TheSuperHackers.GeneralsGameCode.weekly-2026-09-05.zerohour",
            Name = "GeneralsGameCode weekly-2026-09-05 — Zero Hour",
            Version = "2026-09-05",
            ProviderName = "thesuperhackers",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            LastUpdated = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc),
            ResolverMetadata =
            {
                [GitHubConstants.OwnerMetadataKey] = "TheSuperHackers",
                [GitHubConstants.RepoMetadataKey] = "GeneralsGameCode",
            },
        };
        var newestVm = new ContentGridItemViewModel(newestSr, stateServiceMock.Object, loggerMock.Object)
        {
            CurrentState = ContentState.Downloaded,
            IsDownloaded = true,
        };
        var newestVariant = new InstallableVariant
        {
            Name = "Zero Hour",
            ManifestId = "1.20260905.thesuperhackers.gameclient.zerohour",
            CurrentState = ContentState.Downloaded,
        };
        newestVm.Variants.Add(newestVariant);
        newestVm.SelectedVariant = newestVariant;

        var olderSr = new ContentSearchResult
        {
            Id = "github.TheSuperHackers.GeneralsGameCode.weekly-2026-08-28.zerohour",
            Name = "GeneralsGameCode weekly-2026-08-28 — Zero Hour",
            Version = "2026-08-28",
            ProviderName = "thesuperhackers",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            LastUpdated = new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc),
            ResolverMetadata =
            {
                [GitHubConstants.OwnerMetadataKey] = "TheSuperHackers",
                [GitHubConstants.RepoMetadataKey] = "GeneralsGameCode",
            },
        };
        var olderVm = new ContentGridItemViewModel(olderSr, stateServiceMock.Object, loggerMock.Object)
        {
            CurrentState = ContentState.Downloaded,
            IsDownloaded = true,
        };
        var olderVariant = new InstallableVariant
        {
            Name = "Zero Hour",
            ManifestId = "1.20260828.thesuperhackers.gameclient.zerohour",
            CurrentState = ContentState.Downloaded,
        };
        olderVm.Variants.Add(olderVariant);
        olderVm.SelectedVariant = olderVariant;

        // Act
        DownloadsBrowserViewModel.ReconcileReleaseUpdateStates([newestVm, olderVm]);

        // Assert
        Assert.Equal(ContentState.Downloaded, newestVm.CurrentState);
        Assert.Null(newestVm.UpdateTargetVm);
        Assert.True(newestVm.ShowAddToProfileButton);
        Assert.False(newestVm.ShowUpdateButton);

        Assert.Equal(ContentState.Downloaded, olderVm.CurrentState);
        Assert.Null(olderVm.UpdateTargetVm);
        Assert.True(olderVm.ShowAddToProfileButton);
        Assert.False(olderVm.ShowUpdateButton);
    }

    /// <summary>
    /// Verifies that in a multi-release feed where prospective uninstalled releases arrive with UpdateAvailable
    /// from ContentStateService, only the truly downloaded release is marked UpdateAvailable (targeting the newest release),
    /// while prospective newer releases are reset to NotDownloaded (showing only Download).
    /// </summary>
    [Fact]
    public void ReconcileReleaseUpdateStates_WhenIntermediateReleasesArriveWithUpdateAvailable_OnlyInstalledReleaseShowsUpdate()
    {
        // Arrange
        var stateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();

        ContentGridItemViewModel CreateReleaseVm(string date, ContentState state, bool isDownloaded)
        {
            var sr = new ContentSearchResult
            {
                Id = $"github.TheSuperHackers.GeneralsGameCode.weekly-{date}.zerohour",
                Name = $"GeneralsGameCode weekly-{date} — Zero Hour",
                Version = date,
                ProviderName = "thesuperhackers",
                ContentType = ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
                LastUpdated = DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal),
                ResolverMetadata =
                {
                    [GitHubConstants.OwnerMetadataKey] = "TheSuperHackers",
                    [GitHubConstants.RepoMetadataKey] = "GeneralsGameCode",
                },
            };
            var vm = new ContentGridItemViewModel(sr, stateServiceMock.Object, loggerMock.Object)
            {
                CurrentState = state,
                IsDownloaded = isDownloaded,
            };
            var variant = new InstallableVariant
            {
                Name = "Zero Hour",
                ManifestId = $"1.{date.Replace("-", string.Empty)}.thesuperhackers.gameclient.zerohour",
                CurrentState = state,
            };
            vm.Variants.Add(variant);
            vm.SelectedVariant = variant;
            return vm;
        }

        var v20260905 = CreateReleaseVm("2026-09-05", ContentState.UpdateAvailable, true);
        var v20260828 = CreateReleaseVm("2026-08-28", ContentState.UpdateAvailable, true);
        var v20260821 = CreateReleaseVm("2026-08-21", ContentState.Downloaded, true);
        var v20260814 = CreateReleaseVm("2026-08-14", ContentState.NotDownloaded, false);
        var v20260807 = CreateReleaseVm("2026-08-07", ContentState.NotDownloaded, false);

        var allItems = new[] { v20260905, v20260828, v20260821, v20260814, v20260807 };

        // Act - First pass
        DownloadsBrowserViewModel.ReconcileReleaseUpdateStates(allItems);

        // Assert: Newest release (2026-09-05) is NotDownloaded (Download button only)
        Assert.Equal(ContentState.NotDownloaded, v20260905.CurrentState);
        Assert.False(v20260905.IsDownloaded);
        Assert.Null(v20260905.UpdateTargetVm);
        Assert.True(v20260905.ShowDownloadButton);
        Assert.False(v20260905.ShowUpdateButton);
        Assert.False(v20260905.ShowAddToProfileButton);

        // Assert: Intermediate uninstalled release (2026-08-28) is NotDownloaded (Download button only)
        Assert.Equal(ContentState.NotDownloaded, v20260828.CurrentState);
        Assert.False(v20260828.IsDownloaded);
        Assert.Null(v20260828.UpdateTargetVm);
        Assert.True(v20260828.ShowDownloadButton);
        Assert.False(v20260828.ShowUpdateButton);
        Assert.False(v20260828.ShowAddToProfileButton);

        // Assert: Downloaded release (2026-08-21) is UpdateAvailable targeting newest (2026-09-05)
        Assert.Equal(ContentState.UpdateAvailable, v20260821.CurrentState);
        Assert.True(v20260821.IsDownloaded);
        Assert.Same(v20260905, v20260821.UpdateTargetVm);
        Assert.False(v20260821.ShowDownloadButton);
        Assert.True(v20260821.ShowUpdateButton);
        Assert.True(v20260821.ShowAddToProfileButton);

        // Assert: Older uninstalled releases (2026-08-14, 2026-08-07) remain NotDownloaded
        Assert.Equal(ContentState.NotDownloaded, v20260814.CurrentState);
        Assert.True(v20260814.ShowDownloadButton);
        Assert.False(v20260814.ShowUpdateButton);

        Assert.Equal(ContentState.NotDownloaded, v20260807.CurrentState);
        Assert.True(v20260807.ShowDownloadButton);
        Assert.False(v20260807.ShowUpdateButton);

        // Act - Second pass (idempotency check)
        DownloadsBrowserViewModel.ReconcileReleaseUpdateStates(allItems);

        // Assert: Still identical
        Assert.Equal(ContentState.NotDownloaded, v20260905.CurrentState);
        Assert.Equal(ContentState.NotDownloaded, v20260828.CurrentState);
        Assert.Equal(ContentState.UpdateAvailable, v20260821.CurrentState);
        Assert.Same(v20260905, v20260821.UpdateTargetVm);
        Assert.True(v20260821.ShowUpdateButton);
    }

    /// <summary>
    /// Verifies that UpdateContentCommand invokes the publisher reconciler when one is registered.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task UpdateContentCommand_WhenPublisherReconcilerIsAvailable_InvokesReconcilerAsync()
    {
        // Arrange
        var reconcilerMock = new Mock<IPublisherReconciler>();
        reconcilerMock
            .Setup(r => r.CheckAndReconcileIfNeededAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var reconcilerRegistryMock = new Mock<IPublisherReconcilerRegistry>();
        reconcilerRegistryMock
            .Setup(r => r.GetReconciler("thesuperhackers"))
            .Returns(reconcilerMock.Object);

        var viewModel = CreateViewModel(reconcilerRegistry: reconcilerRegistryMock.Object);

        var stateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();
        var sr = new ContentSearchResult
        {
            Id = "github.TheSuperHackers.GeneralsGameCode.weekly-2026-08-21.zerohour",
            ProviderName = "thesuperhackers",
            Name = "GeneralsGameCode weekly-2026-08-21 — Zero Hour",
            Version = "2026-08-21",
        };
        var vm = new ContentGridItemViewModel(sr, stateServiceMock.Object, loggerMock.Object);

        // Act
        await viewModel.UpdateContentCommand.ExecuteAsync(vm);

        // Assert
        reconcilerMock.Verify(
            r => r.CheckAndReconcileIfNeededAsync(string.Empty, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that UpdateContentCommand falls back to downloading the target item when no publisher reconciler exists.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task UpdateContentCommand_WhenNoPublisherReconciler_FallsBackToDownloadAsync()
    {
        // Arrange
        var orchestratorMock = new Mock<IContentOrchestrator>();
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.custom.mod.test"),
            Name = "Custom Mod",
            Version = "1.0.0",
        };
        string? acquiredId = null;
        orchestratorMock
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken>((sr, _, _) => acquiredId = sr.Id)
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        var reconcilerRegistryMock = new Mock<IPublisherReconcilerRegistry>();
        reconcilerRegistryMock
            .Setup(r => r.GetReconciler(It.IsAny<string>()))
            .Returns((IPublisherReconciler?)null);

        var viewModel = CreateViewModel(
            orchestrator: orchestratorMock.Object,
            reconcilerRegistry: reconcilerRegistryMock.Object);

        var stateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();
        var targetSr = new ContentSearchResult
        {
            Id = "custom.mod.v2",
            ProviderName = "custom",
            Name = "Custom Mod v2",
            Version = "2.0.0",
        };
        var targetVm = new ContentGridItemViewModel(targetSr, stateServiceMock.Object, loggerMock.Object);

        var currentSr = new ContentSearchResult
        {
            Id = "custom.mod.v1",
            ProviderName = "custom",
            Name = "Custom Mod v1",
            Version = "1.0.0",
        };
        var currentVm = new ContentGridItemViewModel(currentSr, stateServiceMock.Object, loggerMock.Object)
        {
            UpdateTargetVm = targetVm,
        };

        // Act
        await viewModel.UpdateContentCommand.ExecuteAsync(currentVm);

        // Assert: Orchestrator downloaded the target VM
        Assert.Equal("custom.mod.v2", acquiredId);
        orchestratorMock.Verify(
            o => o.AcquireContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that UpdateContentCommand falls back to downloading the target item when publisher reconciler reports no reconciliation.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task UpdateContentCommand_WhenPublisherReconcilerReturnsNoReconciliation_FallsBackToDownloadAsync()
    {
        // Arrange
        var orchestratorMock = new Mock<IContentOrchestrator>();
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.custom.mod.test"),
            Name = "Custom Mod",
            Version = "2.0.0",
        };
        string? acquiredId = null;
        orchestratorMock
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>()))
            .Callback<ContentSearchResult, IProgress<ContentAcquisitionProgress>?, CancellationToken>((sr, _, _) => acquiredId = sr.Id)
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        var reconcilerMock = new Mock<IPublisherReconciler>();
        reconcilerMock
            .Setup(r => r.CheckAndReconcileIfNeededAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var reconcilerRegistryMock = new Mock<IPublisherReconcilerRegistry>();
        reconcilerRegistryMock
            .Setup(r => r.GetReconciler("custom"))
            .Returns(reconcilerMock.Object);

        var viewModel = CreateViewModel(
            orchestrator: orchestratorMock.Object,
            reconcilerRegistry: reconcilerRegistryMock.Object);

        var stateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();
        var targetSr = new ContentSearchResult
        {
            Id = "custom.mod.v2",
            ProviderName = "custom",
            Name = "Custom Mod v2",
            Version = "2.0.0",
        };
        var targetVm = new ContentGridItemViewModel(targetSr, stateServiceMock.Object, loggerMock.Object);

        var currentSr = new ContentSearchResult
        {
            Id = "custom.mod.v1",
            ProviderName = "custom",
            Name = "Custom Mod v1",
            Version = "1.0.0",
        };
        var currentVm = new ContentGridItemViewModel(currentSr, stateServiceMock.Object, loggerMock.Object)
        {
            UpdateTargetVm = targetVm,
        };

        // Act
        await viewModel.UpdateContentCommand.ExecuteAsync(currentVm);

        // Assert: Reconciler was invoked but returned false, so fallback downloaded target VM
        reconcilerMock.Verify(
            r => r.CheckAndReconcileIfNeededAsync(string.Empty, It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal("custom.mod.v2", acquiredId);
        orchestratorMock.Verify(
            o => o.AcquireContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that UpdateTargetVm is null when the selected variant is NotDownloaded,
    /// even if a sibling variant was downloaded (Kilo Code bot comment).
    /// </summary>
    [Fact]
    public void ReconcileReleaseUpdateStates_WhenSelectedVariantIsNotDownloaded_UpdateTargetVmIsNull()
    {
        var newestSr = new ContentSearchResult
        {
            Id = "github.TheSuperHackers.GeneralsGameCode.weekly-2026-09-05.zerohour",
            Name = "Release 2026-09-05",
            Version = "2026-09-05",
            ProviderName = "thesuperhackers",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            LastUpdated = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc),
            ResolverMetadata = { [GitHubConstants.OwnerMetadataKey] = "TheSuperHackers" },
        };
        var newestVm = new ContentGridItemViewModel(newestSr, Mock.Of<IContentStateService>(), Mock.Of<ILogger<ContentGridItemViewModel>>());

        var olderSr = new ContentSearchResult
        {
            Id = "github.TheSuperHackers.GeneralsGameCode.weekly-2026-08-28.zerohour",
            Name = "Release 2026-08-28",
            Version = "2026-08-28",
            ProviderName = "thesuperhackers",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            LastUpdated = new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc),
            ResolverMetadata = { [GitHubConstants.OwnerMetadataKey] = "TheSuperHackers" },
        };
        var olderVm = new ContentGridItemViewModel(olderSr, Mock.Of<IContentStateService>(), Mock.Of<ILogger<ContentGridItemViewModel>>());

        var downloadedVariant = new InstallableVariant { Name = "ZH", CurrentState = ContentState.Downloaded };
        var notDownloadedVariant = new InstallableVariant { Name = "Gen", CurrentState = ContentState.NotDownloaded };
        olderVm.Variants.Add(downloadedVariant);
        olderVm.Variants.Add(notDownloadedVariant);
        olderVm.SelectedVariant = notDownloadedVariant;
        olderVm.CurrentState = ContentState.NotDownloaded;

        DownloadsBrowserViewModel.ReconcileReleaseUpdateStates([newestVm, olderVm]);

        Assert.Null(olderVm.UpdateTargetVm);
        Assert.Equal(ContentState.NotDownloaded, olderVm.CurrentState);
    }

    /// <summary>
    /// Verifies that when a variant is selected on a card in the browser view,
    /// ViewContentCommand opens ContentDetailViewModel with that variant preserved and not reset.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ViewContentCommand_WithSelectedVariant_PreservesVariantInDetailViewAsync()
    {
        // Arrange
        var tabRegistryMock = new Mock<ITabProviderRegistry>();
        var coordinatorMock = new Mock<IContentDownloadCoordinator>();
        var manifestPoolMock = new Mock<IContentManifestPool>();
        var stateServiceMock = new Mock<IContentStateService>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        var contentLoggerMock = new Mock<ILogger<ContentDetailViewModel>>();

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ITabProviderRegistry))).Returns(tabRegistryMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IContentDownloadCoordinator))).Returns(coordinatorMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IContentManifestPool))).Returns(manifestPoolMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ILogger<ContentDetailViewModel>))).Returns(contentLoggerMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IContentStateService))).Returns(stateServiceMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IEnumerable<IWebPageParser>))).Returns(Array.Empty<IWebPageParser>());

        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([]));

        using var viewModel = new DownloadsBrowserViewModel(
            serviceProviderMock.Object,
            new Mock<ILogger<DownloadsBrowserViewModel>>().Object,
            [],
            stateServiceMock.Object,
            new Mock<IContentOrchestrator>().Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            loggerFactoryMock.Object,
            subscriptionStore.Object);

        var sr = new ContentSearchResult
        {
            Id = "1.0.communityoutpost.addon.cbpx",
            Name = "Control Bar Pro",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Variants =
            [
                new ContentVariantInfo { Id = "720p", Name = "720p", ManifestId = "1.0.communityoutpost.addon.cbpx-720p" },
                new ContentVariantInfo { Id = "1080p", Name = "1080p", ManifestId = "1.0.communityoutpost.addon.cbpx-1080p" },
            ],
        };

        var variantsMap = new Dictionary<string, ContentSearchResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["1.0.communityoutpost.addon.cbpx-720p"] = new() { Id = "1.0.communityoutpost.addon.cbpx-720p", Name = "720p", TargetGame = GameType.ZeroHour },
            ["1.0.communityoutpost.addon.cbpx-1080p"] = new() { Id = "1.0.communityoutpost.addon.cbpx-1080p", Name = "1080p", TargetGame = GameType.ZeroHour },
        };

        var item = new ContentGridItemViewModel(sr, stateServiceMock.Object, new Mock<ILogger<ContentGridItemViewModel>>().Object);
        var v720 = new InstallableVariant { Name = "720p", ManifestId = "1.0.communityoutpost.addon.cbpx-720p" };
        var v1080 = new InstallableVariant { Name = "1080p", ManifestId = "1.0.communityoutpost.addon.cbpx-1080p" };
        item.AddVariant(v720, variantsMap["1.0.communityoutpost.addon.cbpx-720p"]);
        item.AddVariant(v1080, variantsMap["1.0.communityoutpost.addon.cbpx-1080p"]);

        // Select 1080p on the card
        item.SelectedVariant = v1080;
        viewModel.ContentItems.Add(item);

        // Act: click content card to open detail view
        viewModel.ViewContentCommand.Execute(item);

        // Assert: SelectedContent is populated and retains 1080p
        Assert.NotNull(viewModel.SelectedContent);
        await viewModel.SelectedContent.WaitForInitializationAsync();
        Assert.NotNull(viewModel.SelectedContent.SelectedVariant);
        Assert.Equal("1.0.communityoutpost.addon.cbpx-1080p", viewModel.SelectedContent.SelectedVariant.ManifestId);

        // Act: close detail view
        viewModel.CloseDetailCommand.Execute(null);

        // Assert: Detail is closed and card retained the 1080p variant
        Assert.Null(viewModel.SelectedContent);
        Assert.Equal("1.0.communityoutpost.addon.cbpx-1080p", item.SelectedVariant?.ManifestId);
    }

    /// <summary>
    /// Verifies that when CloseDetailCommand is executed, state and local manifest ID
    /// are synchronized to the underlying ContentGridItemViewModel.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CloseDetailCommand_WhenContentAcquiredInDetailView_SynchronizesGridItemStateAndIdAsync()
    {
        // Arrange
        var tabRegistryMock = new Mock<ITabProviderRegistry>();
        var coordinatorMock = new Mock<IContentDownloadCoordinator>();
        var manifestPoolMock = new Mock<IContentManifestPool>();
        var stateServiceMock = new Mock<IContentStateService>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        var contentLoggerMock = new Mock<ILogger<ContentDetailViewModel>>();

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ITabProviderRegistry))).Returns(tabRegistryMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IContentDownloadCoordinator))).Returns(coordinatorMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IContentManifestPool))).Returns(manifestPoolMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ILogger<ContentDetailViewModel>))).Returns(contentLoggerMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IContentStateService))).Returns(stateServiceMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IEnumerable<IWebPageParser>))).Returns(Array.Empty<IWebPageParser>());

        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([]));

        using var viewModel = new DownloadsBrowserViewModel(
            serviceProviderMock.Object,
            new Mock<ILogger<DownloadsBrowserViewModel>>().Object,
            [],
            stateServiceMock.Object,
            new Mock<IContentOrchestrator>().Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            loggerFactoryMock.Object,
            subscriptionStore.Object);

        var sr = new ContentSearchResult
        {
            Id = "cnclabs.map.3394",
            Name = "Defcon 8",
            ContentType = ContentType.Map,
            TargetGame = GameType.ZeroHour,
            ProviderName = "CNC Labs Maps",
            SourceUrl = "https://www.cnclabs.com/downloads/details/3394/",
        };
        sr.ResolverMetadata[CNCLabsConstants.MapIdMetadataKey] = "3394";

        var item = new ContentGridItemViewModel(sr, stateServiceMock.Object, new Mock<ILogger<ContentGridItemViewModel>>().Object)
        {
            CurrentState = ContentState.NotDownloaded,
            IsDownloaded = false,
        };

        viewModel.ContentItems.Add(item);

        // Act: Open detail
        viewModel.ViewContentCommand.Execute(item);
        Assert.NotNull(viewModel.SelectedContent);
        await viewModel.SelectedContent.WaitForInitializationAsync();

        // Simulate state change to Downloaded
        stateServiceMock.Setup(s => s.GetStateAsync(sr, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.Downloaded);
        stateServiceMock.Setup(s => s.GetLocalManifestIdAsync(sr, It.IsAny<CancellationToken>()))
            .ReturnsAsync("1.0.cnclabs.map.defcon8");

        // Act: Close detail
        viewModel.CloseDetailCommand.Execute(null);

        // Allow UI thread dispatcher actions to execute
        await Task.Delay(100);

        // Assert: Detail is closed and grid item state and ID are synchronized
        Assert.Null(viewModel.SelectedContent);
        Assert.Equal(ContentState.Downloaded, item.CurrentState);
        Assert.True(item.IsDownloaded);
        Assert.True(item.EffectiveIsDownloaded);
        Assert.Equal("1.0.cnclabs.map.defcon8", item.SearchResult.Id);
        Assert.True(item.ShowAddToProfileButton);
        Assert.False(item.ShowDownloadButton);
    }

    private static DownloadsBrowserViewModel CreateViewModel(
        IContentOrchestrator? orchestrator = null,
        IPublisherReconcilerRegistry? reconcilerRegistry = null)
    {
        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([]));

        return new DownloadsBrowserViewModel(
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<DownloadsBrowserViewModel>>().Object,
            [],
            new Mock<IContentStateService>().Object,
            orchestrator ?? new Mock<IContentOrchestrator>().Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ILoggerFactory>().Object,
            subscriptionStore.Object,
            reconcilerRegistry: reconcilerRegistry);
    }
}
