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
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
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
    /// Verifies that the AOD Maps browse experience presents its player-count filters immediately.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SelectPublisher_AodMaps_ExposesAndOpensFiltersByDefaultAsync()
    {
        // Arrange
        using var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        // Act
        viewModel.SelectedPublisher = viewModel.Publishers.Single(
            p => p.PublisherId == AODMapsConstants.PublisherType);

        // Assert
        Assert.True(viewModel.CanSearch);
        Assert.True(viewModel.CanShowFilters);
        Assert.True(viewModel.IsFilterPanelVisible);
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

        var modDbPublisher = viewModel.Publishers.Single(p => p.PublisherId == ModDBConstants.PublisherType);
        viewModel.SelectedPublisher = modDbPublisher;
        Assert.Equal(modDbPublisher, viewModel.SelectedPublisher);

        // Act - simulate visual tree detachment setting SelectedItem to null
        viewModel.SelectedPublisher = null;

        // Assert
        Assert.NotNull(viewModel.SelectedPublisher);
        Assert.Equal(ModDBConstants.PublisherType, viewModel.SelectedPublisher.PublisherId);
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

        var modDbPublisher = viewModel.Publishers.Single(p => p.PublisherId == ModDBConstants.PublisherType);
        viewModel.SelectedPublisher = modDbPublisher;

        // Act
        await viewModel.OnTabActivatedAsync();

        // Assert
        Assert.NotNull(viewModel.SelectedPublisher);
        Assert.Equal(ModDBConstants.PublisherType, viewModel.SelectedPublisher.PublisherId);
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
        await Task.Delay(50);

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

    private static DownloadsBrowserViewModel CreateViewModel()
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
            new Mock<IContentOrchestrator>().Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ILoggerFactory>().Object,
            subscriptionStore.Object);
    }
}
