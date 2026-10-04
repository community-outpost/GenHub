using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Parsers;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Messages;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using CatalogEntry = GenHub.Core.Models.Providers.CatalogEntry;
using ContentState = GenHub.Core.Models.Enums.ContentState;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using GameType = GenHub.Core.Models.Enums.GameType;
using PublisherSubscription = GenHub.Core.Models.Providers.PublisherSubscription;
using TelemetryLevel = GenHub.Core.Models.Enums.TelemetryLevel;

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
    /// Verifies that the ModDB browse experience exposes search and filters.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SelectPublisher_ModDB_ExposesSearchAndFiltersAsync()
    {
        // Arrange
        using var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        // Act
        viewModel.SelectedPublisher = viewModel.Publishers.Single(
            p => p.PublisherId == ModDBConstants.PublisherType);

        // Assert
        Assert.True(viewModel.CanSearch);
        Assert.True(viewModel.CanShowFilters);
    }

    /// <summary>
    /// Verifies that searching a direct ModDB URL auto-switches to the ModDB publisher.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SearchAsync_DirectModDBUrl_SwitchesToModDBPublisherAsync()
    {
        // Arrange
        using var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        viewModel.SelectedPublisher = viewModel.Publishers.Single(
            p => p.PublisherId == GitHubTopicsConstants.PublisherType);

        viewModel.SearchTerm = "https://www.moddb.com/mods/rise-of-the-reds";

        // Act
        await viewModel.SearchCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(ModDBConstants.PublisherType, viewModel.SelectedPublisher?.PublisherId);
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
    /// Verifies that removing a subscription drops its publisher from the sidebar without a restart.
    /// </summary>
    /// <param name="selected">Whether the removed publisher is selected.</param>
    /// <param name="refreshFails">Whether the overlapping refresh fails.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PublisherSubscriptionRemovedMessage_RemovesUnsubscribedPublisherFromSidebarAsync(bool selected, bool refreshFails)
    {
        // Arrange
        var sub = new PublisherSubscription
        {
            PublisherId = "sub-removed",
            PublisherName = "Removed Sub",
            CatalogUrl = "https://example.com/removed/catalog.json",
        };

        var pendingRefresh = new TaskCompletionSource<OperationResult<IReadOnlyList<PublisherSubscription>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .SetupSequence(store => store.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([sub]))
            .Returns(pendingRefresh.Task);

        var mockDiscoverer = new Mock<GenericCatalogDiscoverer>(
            new Mock<ILogger<GenericCatalogDiscoverer>>().Object,
            new Mock<IHttpClientFactory>().Object,
            new Mock<IPublisherCatalogParser>().Object,
            new Mock<IVersionSelector>().Object,
            new Mock<IGitHubApiClient>().Object);

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
        Assert.Contains(viewModel.Publishers, p => p.PublisherId == "sub-removed");

        using var browseCts = new CancellationTokenSource();
        viewModel.SetInFlightOperationForTesting(
            sub.PublisherId,
            new DownloadsBrowserViewModel.PublisherInFlightOperation(sub.PublisherId, new ContentSearchQuery(), browseCts));
        if (selected)
        {
            viewModel.SelectedPublisher = viewModel.Publishers.First(p => p.PublisherId == sub.PublisherId);
        }

        // Start a refresh whose stale snapshot arrives after removal.
        var refresh = viewModel.InitializeAsync();

        // Act
        WeakReferenceMessenger.Default.Send(new PublisherSubscriptionRemovedMessage("sub-removed"));

        Assert.DoesNotContain(viewModel.Publishers, p => p.PublisherId == "sub-removed");
        Assert.True(browseCts.IsCancellationRequested);
        Assert.NotEqual(sub.PublisherId, viewModel.SelectedPublisher?.PublisherId);
        pendingRefresh.SetResult(refreshFails
            ? OperationResult<IReadOnlyList<PublisherSubscription>>.CreateFailure("unavailable")
            : OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([sub]));
        await refresh;

        // Assert
        Assert.DoesNotContain(viewModel.Publishers, p => p.PublisherId == "sub-removed");
    }

    /// <summary>
    /// Verifies Publisher Studio updates still refresh an existing publisher after unsubscribe integration.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PublisherSubscriptionsChangedMessage_UpdatesExistingPublisherMetadataAsync()
    {
        var sub = new PublisherSubscription
        {
            PublisherId = "updated-publisher",
            PublisherName = "Original",
            CatalogUrl = "https://example.com/catalog.json",
        };
        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore.Setup(store => store.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([sub]));

        var mockDiscoverer = new Mock<GenericCatalogDiscoverer>(
            new Mock<ILogger<GenericCatalogDiscoverer>>().Object,
            new Mock<IHttpClientFactory>().Object,
            new Mock<IPublisherCatalogParser>().Object,
            new Mock<IVersionSelector>().Object,
            new Mock<IGitHubApiClient>().Object);

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
        var publisher = viewModel.Publishers.Single(p => p.PublisherId == sub.PublisherId);
        sub.PublisherName = "Updated";
        sub.AvatarUrl = "https://example.com/avatar.png";
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PublisherItemViewModel.LogoSource))
            {
                applied.TrySetResult();
            }
        };

        WeakReferenceMessenger.Default.Send(new PublisherSubscriptionsChangedMessage(sub.PublisherId));
        await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(sub.PublisherName, publisher.DisplayName);
        Assert.Equal(sub.AvatarUrl, publisher.LogoSource);
        Assert.Same(publisher, viewModel.Publishers.Single(p => p.PublisherId == sub.PublisherId));
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

        static async Task WaitForExpectedItemAsync(DownloadsBrowserViewModel vm, string expectedId)
        {
            var timeout = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < timeout)
            {
                if (vm.ContentItems.Count == 1 &&
                    vm.ContentItems[0].SearchResult.Id == expectedId &&
                    !vm.IsLoading)
                {
                    return;
                }

                await Task.Delay(25);
            }
        }

        // Load A into cache
        viewModel.SelectedPublisher = publisherA;
        await WaitForExpectedItemAsync(viewModel, "mod-a1");
        Assert.Single(viewModel.ContentItems);
        Assert.Equal("mod-a1", viewModel.ContentItems[0].SearchResult.Id);

        // Load B into cache
        viewModel.SelectedPublisher = publisherB;
        await WaitForExpectedItemAsync(viewModel, "mod-b1");
        Assert.Single(viewModel.ContentItems);
        Assert.Equal("mod-b1", viewModel.ContentItems[0].SearchResult.Id);

        // Switch back to A (cache hit)
        viewModel.SelectedPublisher = publisherA;
        await WaitForExpectedItemAsync(viewModel, "mod-a1");
        Assert.Single(viewModel.ContentItems);
        Assert.Equal("mod-a1", viewModel.ContentItems[0].SearchResult.Id);

        // Switch back to B (cache hit)
        viewModel.SelectedPublisher = publisherB;
        await WaitForExpectedItemAsync(viewModel, "mod-b1");
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
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
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
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
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
    /// Verifies that DisposeUnretainedViewModels does not dispose view models retained in ContentItems or cache.
    /// </summary>
    [Fact]
    public void DisposeUnretainedViewModels_RetainsItemsInContentItemsAndCache()
    {
        // Arrange
        using var viewModel = CreateViewModel();

        var contentItem = new ContentGridItemViewModel(
            new ContentSearchResult { Id = "content1", Name = "Content Item 1" },
            new Mock<IContentStateService>().Object,
            new Mock<ILogger<ContentGridItemViewModel>>().Object);

        var orphanItem = new ContentGridItemViewModel(
            new ContentSearchResult { Id = "orphan1", Name = "Orphan Item" },
            new Mock<IContentStateService>().Object,
            new Mock<ILogger<ContentGridItemViewModel>>().Object);

        viewModel.ContentItems.Add(contentItem);

        // Act
        viewModel.DisposeUnretainedViewModels([contentItem, orphanItem]);

        // Assert
        Assert.False(contentItem.IsDisposed, "Items present in ContentItems must NOT be disposed.");
        Assert.True(orphanItem.IsDisposed, "Orphan items must be disposed.");
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
    /// Verifies that when an in-flight operation for an older publisher completes after switching
    /// publishers, the captured in-flight catalog ID is preserved in the cache instead of being
    /// overwritten by the active publisher's catalog ID.
    /// </summary>
    [Fact]
    public void CommitBrowseResultsToCache_WhenInFlightOperationCompletesForOldPublisher_PreservesInFlightCatalogIdInCache()
    {
        // Arrange
        using var viewModel = CreateViewModel();

        var publisherA = new PublisherItemViewModel("pub-a", "Publisher A");
        var publisherB = new PublisherItemViewModel("pub-b", "Publisher B");
        viewModel.Publishers.Add(publisherA);
        viewModel.Publishers.Add(publisherB);

        var itemA = new ContentGridItemViewModel(
            new ContentSearchResult { Id = "mod-a", Name = "Mod A" },
            new Mock<IContentStateService>().Object,
            new Mock<ILogger<ContentGridItemViewModel>>().Object);

        using var cts = new CancellationTokenSource();
        var inFlightOpA = new DownloadsBrowserViewModel.PublisherInFlightOperation(
            "pub-a",
            new ContentSearchQuery(),
            cts,
            catalogId: "catalog-a")
        {
            ActiveRequestId = 1,
            IsCompleted = false,
        };

        viewModel.SetInFlightOperationForTesting("pub-a", inFlightOpA);

        // Simulate user currently on Publisher B with Catalog B selected
        viewModel.SelectedPublisher = publisherB;
        viewModel.SelectedCatalog = new CatalogEntry { Id = "catalog-b", Name = "Catalog B" };

        // Act: in-flight request for Publisher A finishes and commits to cache
        viewModel.CommitBrowseResultsToCacheForTesting(
            "pub-a",
            new ContentSearchQuery(),
            hasMoreItems: false,
            isCustomQuery: false,
            append: false,
            inFlightOp: inFlightOpA,
            newVms: [itemA]);

        // Assert: Publisher A's cache must store "catalog-a", not the active "catalog-b"
        var cachedCatalogId = viewModel.GetCachedCatalogIdForTesting("pub-a");
        Assert.Equal("catalog-a", cachedCatalogId);
    }

    /// <summary>
    /// Verifies that CommitBrowseResultsToCache returns false and disposes uncommitted view models
    /// when the in-flight operation cancellation token is cancelled, and CleanupInFlight cleans up in-flight entries.
    /// </summary>
    [Fact]
    public void CommitBrowseResultsToCache_WhenInFlightOperationIsCancelled_ReturnsFalseAndDisposesViewModels()
    {
        // Arrange
        using var viewModel = CreateViewModel();

        var publisherA = new PublisherItemViewModel("pub-a", "Publisher A");
        viewModel.Publishers.Add(publisherA);

        var itemA = new ContentGridItemViewModel(
            new ContentSearchResult { Id = "mod-a", Name = "Mod A" },
            new Mock<IContentStateService>().Object,
            new Mock<ILogger<ContentGridItemViewModel>>().Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var inFlightOpA = new DownloadsBrowserViewModel.PublisherInFlightOperation(
            "pub-a",
            new ContentSearchQuery(),
            cts,
            catalogId: "catalog-a")
        {
            ActiveRequestId = 1,
            IsCompleted = false,
        };

        viewModel.SetInFlightOperationForTesting("pub-a", inFlightOpA);

        // Act
        var committed = viewModel.CommitBrowseResultsToCacheForTesting(
            "pub-a",
            new ContentSearchQuery(),
            hasMoreItems: false,
            isCustomQuery: false,
            append: false,
            inFlightOp: inFlightOpA,
            newVms: [itemA]);

        if (!committed)
        {
            viewModel.CleanupInFlight("pub-a", inFlightOpA);
        }

        // Assert
        Assert.False(committed);
        Assert.True(itemA.IsDisposed);
        Assert.False(viewModel.HasInFlightOperationForTesting("pub-a"));
    }

    /// <summary>
    /// Verifies that SaveOutgoingPublisherState preserves the publisher's own catalog ID
    /// and does not overwrite it with another publisher's catalog ID.
    /// </summary>
    [Fact]
    public void SaveOutgoingPublisherState_WhenSwitchingPublishers_PreservesOutgoingCatalogId()
    {
        // Arrange
        using var viewModel = CreateViewModel();

        var publisherA = new PublisherItemViewModel("pub-a", "Publisher A");
        var publisherB = new PublisherItemViewModel("pub-b", "Publisher B");
        viewModel.Publishers.Add(publisherA);
        viewModel.Publishers.Add(publisherB);

        var itemA = new ContentGridItemViewModel(
            new ContentSearchResult { Id = "mod-a", Name = "Mod A" },
            new Mock<IContentStateService>().Object,
            new Mock<ILogger<ContentGridItemViewModel>>().Object);

        viewModel.SelectedPublisher = publisherA;
        viewModel.SelectedCatalog = new CatalogEntry { Id = "catalog-a", Name = "Catalog A" };
        viewModel.ContentItems.Add(itemA);

        // Act: save state for pub-a with catalog-a before switching
        viewModel.SaveOutgoingPublisherStateForTesting("pub-a", "catalog-a");

        // Now simulate switching to pub-b with catalog-b
        viewModel.SelectedPublisher = publisherB;
        viewModel.SelectedCatalog = new CatalogEntry { Id = "catalog-b", Name = "Catalog B" };

        // Even if SaveOutgoingPublisherState is called for pub-a without catalogId parameter
        viewModel.SaveOutgoingPublisherStateForTesting("pub-a");

        // Assert: Pub A's cached catalog ID is still catalog-a, not catalog-b
        var cachedCatalogId = viewModel.GetCachedCatalogIdForTesting("pub-a");
        Assert.Equal("catalog-a", cachedCatalogId);
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
    /// Verifies that Generals Online QFE and non-QFE releases are reconciled correctly:
    /// newest uninstalled release (092826) stays NotDownloaded, downloaded older QFE release
    /// (082826_QFE1) receives UpdateAvailable pointing to 092826, and uninstalled older
    /// releases (082826) stay NotDownloaded.
    /// </summary>
    [Fact]
    public void ReconcileReleaseUpdateStates_GeneralsOnlineQfeReleases_ReconcilesChronologically()
    {
        // Arrange
        var stateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();

        var newestSr = new ContentSearchResult
        {
            Id = "generalsonline-092826",
            Name = "Generals Online",
            Version = "092826",
            ProviderName = PublisherTypeConstants.GeneralsOnline,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            LastUpdated = new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc),
        };
        var newestVm = new ContentGridItemViewModel(newestSr, stateServiceMock.Object, loggerMock.Object)
        {
            CurrentState = ContentState.NotDownloaded,
            IsDownloaded = false,
        };
        var newestVariant = new InstallableVariant
        {
            Name = "Generals Online",
            ManifestId = "1.928260.generalsonline.gameclient.60hz",
            CurrentState = ContentState.NotDownloaded,
        };
        newestVm.Variants.Add(newestVariant);
        newestVm.SelectedVariant = newestVariant;

        var olderQfeSr = new ContentSearchResult
        {
            Id = "generalsonline-082826_QFE1",
            Name = "Generals Online",
            Version = "082826_QFE1",
            ProviderName = PublisherTypeConstants.GeneralsOnline,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            LastUpdated = new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc),
        };
        var olderQfeVm = new ContentGridItemViewModel(olderQfeSr, stateServiceMock.Object, loggerMock.Object)
        {
            CurrentState = ContentState.Downloaded,
            IsDownloaded = true,
        };
        var olderQfeVariant = new InstallableVariant
        {
            Name = "Generals Online",
            ManifestId = "1.828261.generalsonline.gameclient.60hz",
            CurrentState = ContentState.Downloaded,
        };
        olderQfeVm.Variants.Add(olderQfeVariant);
        olderQfeVm.SelectedVariant = olderQfeVariant;

        var olderSr = new ContentSearchResult
        {
            Id = "generalsonline-082826",
            Name = "Generals Online",
            Version = "082826",
            ProviderName = PublisherTypeConstants.GeneralsOnline,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            LastUpdated = new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc),
        };
        var olderVm = new ContentGridItemViewModel(olderSr, stateServiceMock.Object, loggerMock.Object)
        {
            CurrentState = ContentState.NotDownloaded,
            IsDownloaded = false,
        };
        var olderVariant = new InstallableVariant
        {
            Name = "Generals Online",
            ManifestId = "1.828260.generalsonline.gameclient.60hz",
            CurrentState = ContentState.NotDownloaded,
        };
        olderVm.Variants.Add(olderVariant);
        olderVm.SelectedVariant = olderVariant;

        // Act
        DownloadsBrowserViewModel.ReconcileReleaseUpdateStates([newestVm, olderQfeVm, olderVm]);

        // Assert: Newest release 092826 must remain NotDownloaded
        Assert.Equal(ContentState.NotDownloaded, newestVm.CurrentState);
        Assert.False(newestVm.IsDownloaded);
        Assert.Null(newestVm.UpdateTargetVm);
        Assert.True(newestVm.ShowDownloadButton);
        Assert.False(newestVm.ShowUpdateButton);

        // Assert: Installed 082826_QFE1 must be UpdateAvailable targeting newestVm (092826)
        Assert.Equal(ContentState.UpdateAvailable, olderQfeVm.CurrentState);
        Assert.True(olderQfeVm.IsDownloaded);
        Assert.Same(newestVm, olderQfeVm.UpdateTargetVm);
        Assert.False(olderQfeVm.ShowDownloadButton);
        Assert.True(olderQfeVm.ShowUpdateButton);
        Assert.True(olderQfeVm.ShowAddToProfileButton);

        // Assert: Older uninstalled 082826 must remain NotDownloaded
        Assert.Equal(ContentState.NotDownloaded, olderVm.CurrentState);
        Assert.False(olderVm.IsDownloaded);
        Assert.Null(olderVm.UpdateTargetVm);
        Assert.True(olderVm.ShowDownloadButton);
        Assert.False(olderVm.ShowUpdateButton);
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
    /// Verifies that on a single card grouping parallel resolution variants, a partially
    /// installed set (for example after deleting unneeded resolutions) does not mark the
    /// remaining installed variant as UpdateAvailable. Sibling resolutions are install
    /// options, not newer versions of each other.
    /// </summary>
    [Fact]
    public void ReconcileReleaseUpdateStates_WhenParallelResolutionVariantsPartiallyInstalled_DoesNotShowUpdate()
    {
        // Arrange
        var stateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();

        var cardSr = new ContentSearchResult
        {
            Id = "1.0.communityoutpost.addon.cbpx-1080p",
            Name = "Control Bar Pro (Xezon) - 1080p (Recommended)",
            Version = "1.0",
            ProviderName = CommunityOutpostConstants.PublisherType,
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            VariantFamilyName = "Control Bar Pro (Xezon)",
            VariantGroupId = "communityoutpost.addon.cbpx.1.0",
        };
        var card = new ContentGridItemViewModel(cardSr, stateServiceMock.Object, loggerMock.Object);

        InstallableVariant AddSibling(string variantId, string name, ContentState state) =>
            AddCardVariant(card, $"1.0.communityoutpost.addon.cbpx-{variantId}", name, "1.0", state);

        AddSibling("720p", "Control Bar Pro (Xezon) - 720p", ContentState.NotDownloaded);
        AddSibling("900p", "Control Bar Pro (Xezon) - 900p", ContentState.NotDownloaded);
        var downloaded = AddSibling("1080p", "Control Bar Pro (Xezon) - 1080p (Recommended)", ContentState.Downloaded);

        // A newer valid version on an uninstalled parallel option still must not
        // produce an update: resolutions are install choices, not releases.
        AddCardVariant(card, "1.0.communityoutpost.addon.cbpx-1440p", "Control Bar Pro (Xezon) - 1440p (2K)", "2.0", ContentState.NotDownloaded);

        card.SelectedVariant = downloaded;
        card.CurrentState = ContentState.Downloaded;
        card.IsDownloaded = true;

        // A second, unrelated card so the reconcile pass runs; the cbpx card must
        // still form a single-item family and keep its installed variant current.
        var unrelated = CreateUnrelatedCard(stateServiceMock.Object, loggerMock.Object);

        // Act
        DownloadsBrowserViewModel.ReconcileReleaseUpdateStates([card, unrelated]);

        // Assert: the installed resolution stays current; no update is offered.
        Assert.Equal(ContentState.Downloaded, downloaded.CurrentState);
        Assert.Equal(ContentState.Downloaded, card.CurrentState);
        Assert.True(card.IsDownloaded);
        Assert.Null(card.UpdateTargetVm);
        Assert.False(card.ShowUpdateButton);
        Assert.True(card.ShowAddToProfileButton);
        Assert.False(card.ShowDownloadButton);
    }

    /// <summary>
    /// Verifies that on a single card grouping parallel language variants, installing only
    /// one language does not mark it as UpdateAvailable. Sibling languages are install
    /// options, not newer versions of each other.
    /// </summary>
    [Fact]
    public void ReconcileReleaseUpdateStates_WhenParallelLanguageVariantsPartiallyInstalled_DoesNotShowUpdate()
    {
        // Arrange
        var stateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();

        var cardSr = new ContentSearchResult
        {
            Id = "1.0.communityoutpost.addon.hlei-zerohour-en",
            Name = "Leikeze's Hotkeys (EN)",
            Version = "1.0",
            ProviderName = CommunityOutpostConstants.PublisherType,
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            VariantFamilyName = "Leikeze's Hotkeys",
            VariantGroupId = "communityoutpost.addon.hlei.1.0",
        };
        var card = new ContentGridItemViewModel(cardSr, stateServiceMock.Object, loggerMock.Object);

        InstallableVariant AddSibling(string variantId, string name, ContentState state) =>
            AddCardVariant(card, $"1.0.communityoutpost.addon.hlei-{variantId}", name, "1.0", state);

        var downloaded = AddSibling("zerohour-en", "Leikeze's Hotkeys (EN)", ContentState.Downloaded);

        // A newer valid version on an uninstalled parallel option still must not
        // produce an update: languages are install choices, not releases.
        AddCardVariant(card, "1.0.communityoutpost.addon.hlei-zerohour-de", "Leikeze's Hotkeys (DE)", "2.0", ContentState.NotDownloaded);
        AddSibling("zerohour-ru", "Leikeze's Hotkeys (RU)", ContentState.NotDownloaded);

        card.SelectedVariant = downloaded;
        card.CurrentState = ContentState.Downloaded;
        card.IsDownloaded = true;

        // A second, unrelated card so the reconcile pass runs; the hlei card must
        // still form a single-item family and keep its installed variant current.
        var unrelated = CreateUnrelatedCard(stateServiceMock.Object, loggerMock.Object);

        // Act
        DownloadsBrowserViewModel.ReconcileReleaseUpdateStates([card, unrelated]);

        // Assert: the installed language stays current; no update is offered.
        Assert.Equal(ContentState.Downloaded, downloaded.CurrentState);
        Assert.Equal(ContentState.Downloaded, card.CurrentState);
        Assert.True(card.IsDownloaded);
        Assert.Null(card.UpdateTargetVm);
        Assert.False(card.ShowUpdateButton);
        Assert.True(card.ShowAddToProfileButton);
    }

    /// <summary>
    /// Verifies that a sibling variant with a genuinely newer release version never
    /// produces a card-level self-update claim. Cross-variant comparisons cannot tell
    /// parallel options and unrelated content apart from releases, and a self-target
    /// would re-download the installed result; genuine updates surface through
    /// per-variant states and cross-release families instead.
    /// </summary>
    [Fact]
    public void ReconcileReleaseUpdateStates_WhenSiblingVariantHasNewerVersion_DoesNotClaimSelfUpdate()
    {
        // Arrange
        var stateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();

        var cardSr = new ContentSearchResult
        {
            Id = "github.TheSuperHackers.GeneralsGameCode.weekly-2026-08-28.zerohour",
            Name = "GeneralsGameCode weekly-2026-08-28 — Zero Hour",
            Version = "2026-08-28",
            ProviderName = "thesuperhackers",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };
        var card = new ContentGridItemViewModel(cardSr, stateServiceMock.Object, loggerMock.Object);

        InstallableVariant AddSibling(string date, ContentState state) =>
            AddCardVariant(
                card,
                $"1.{date.Replace("-", string.Empty)}.thesuperhackers.gameclient.zerohour",
                "Zero Hour",
                date,
                state);

        var downloaded = AddSibling("2026-08-28", ContentState.Downloaded);
        AddSibling("2026-09-05", ContentState.NotDownloaded);

        card.SelectedVariant = downloaded;
        card.CurrentState = ContentState.Downloaded;
        card.IsDownloaded = true;

        // A second, unrelated card so the reconcile pass runs.
        var unrelated = CreateUnrelatedCard(stateServiceMock.Object, loggerMock.Object);

        // Act
        DownloadsBrowserViewModel.ReconcileReleaseUpdateStates([card, unrelated]);

        // Assert: no card-level self-update is claimed for the newer sibling.
        Assert.Equal(ContentState.Downloaded, downloaded.CurrentState);
        Assert.Equal(ContentState.Downloaded, card.CurrentState);
        Assert.True(card.IsDownloaded);
        Assert.Null(card.UpdateTargetVm);
        Assert.False(card.ShowUpdateButton);
        Assert.True(card.ShowAddToProfileButton);
        Assert.False(card.ShowDownloadButton);
    }

    /// <summary>
    /// Verifies that sibling variants carrying unparseable version labels (such as
    /// tag-style "nightly" builds) never mark a downloaded variant as UpdateAvailable.
    /// Without structured version evidence the comparison fails closed.
    /// </summary>
    /// <param name="candidateVersion">The uninstalled sibling's version label.</param>
    /// <param name="downloadedVersion">The downloaded variant's version label.</param>
    [Theory]
    [InlineData("weekly", "nightly")]
    [InlineData("nightly", "weekly")]
    public void ReconcileReleaseUpdateStates_WhenSiblingVariantVersionsAreUnparseable_DoesNotShowUpdate(
        string candidateVersion,
        string downloadedVersion)
    {
        // Arrange
        var stateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();

        var cardSr = new ContentSearchResult
        {
            Id = "1.0.github.gameclient.zerohourb",
            Name = "Zero Hour",
            Version = downloadedVersion,
            ProviderName = "github",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };
        var card = new ContentGridItemViewModel(cardSr, stateServiceMock.Object, loggerMock.Object);

        var downloaded = AddCardVariant(card, "1.0.github.gameclient.zerohourb", "Zero Hour", downloadedVersion, ContentState.Downloaded);
        AddCardVariant(card, "1.0.github.gameclient.zerohoura", "Zero Hour", candidateVersion, ContentState.NotDownloaded);

        card.SelectedVariant = downloaded;
        card.CurrentState = ContentState.Downloaded;
        card.IsDownloaded = true;

        // A second, unrelated card so the reconcile pass runs.
        var unrelated = CreateUnrelatedCard(stateServiceMock.Object, loggerMock.Object);

        // Act
        DownloadsBrowserViewModel.ReconcileReleaseUpdateStates([card, unrelated]);

        // Assert: unparseable labels are not version evidence; no update is offered.
        Assert.Equal(ContentState.Downloaded, downloaded.CurrentState);
        Assert.Equal(ContentState.Downloaded, card.CurrentState);
        Assert.True(card.IsDownloaded);
        Assert.Null(card.UpdateTargetVm);
        Assert.False(card.ShowUpdateButton);
        Assert.True(card.ShowAddToProfileButton);
    }

    /// <summary>
    /// Verifies that on a GenLauncher card grouping a whole mod (releases plus addons
    /// as sibling variants), downloading a single addon does not mark it as
    /// UpdateAvailable, even when other siblings carry valid newer versions.
    /// Siblings are different content items, not newer releases of each other.
    /// </summary>
    [Fact]
    public void ReconcileReleaseUpdateStates_WhenGenLauncherCardHasOneAddonDownloaded_DoesNotShowUpdate()
    {
        // Arrange
        var stateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();

        var cardSr = new ContentSearchResult
        {
            Id = "genlauncher-zerohour-shockwave-cursor-pack-hd",
            Name = "ShockWave Cursor Pack HD",
            Version = "1.3",
            ProviderName = "genlauncher",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            VariantFamilyName = "C&C ShockWave",
            VariantGroupId = "zerohour-c-c-shockwave",
        };
        var card = new ContentGridItemViewModel(cardSr, stateServiceMock.Object, loggerMock.Object);

        // Sibling content types are simplified: reconciliation only inspects variant
        // states, identities, and versions, never the content type.
        AddCardVariant(card, "genlauncher-zerohour-c-c-shockwave-public-beta", "C&C Shockwave Public Beta", "1.25 Beta 8", ContentState.NotDownloaded);
        AddCardVariant(card, "genlauncher-zerohour-c-c-shockwave-1-201-genlauncher-fix-1", "C&C Shockwave 1.201 GenLauncher Fix 1", "1.201 GenLauncher Fix 1", ContentState.NotDownloaded);
        AddCardVariant(card, "genlauncher-zerohour-shockwave-single-player-experience", "Shockwave Single Player Experience", "1.0", ContentState.NotDownloaded);
        var downloaded = AddCardVariant(card, "genlauncher-zerohour-shockwave-cursor-pack-hd", "ShockWave Cursor Pack HD", "1.3", ContentState.Downloaded);
        AddCardVariant(card, "genlauncher-zerohour-shockwave-russifier", "ShockWave Russifier", "2.0", ContentState.NotDownloaded);
        AddCardVariant(card, "genlauncher-zerohour-lemon-shockwave-hotkeys", "Lemon Shockwave Hotkeys", "1.1", ContentState.NotDownloaded);
        AddCardVariant(card, "genlauncher-zerohour-lemon-shockwave-beta-8-hotkeys", "Lemon Shockwave Beta 8 Hotkeys", "1.1", ContentState.NotDownloaded);
        AddCardVariant(card, "genlauncher-zerohour-control-bar-pro-shockwave", "Control Bar Pro (Shockwave)", "1.0", ContentState.NotDownloaded);

        card.SelectedVariant = downloaded;
        card.CurrentState = ContentState.Downloaded;
        card.IsDownloaded = true;

        // A second, unrelated card so the reconcile pass runs.
        var unrelated = CreateUnrelatedCard(stateServiceMock.Object, loggerMock.Object);

        // Act
        DownloadsBrowserViewModel.ReconcileReleaseUpdateStates([card, unrelated]);

        // Assert: the installed addon stays current; sibling content is not an update.
        Assert.Equal(ContentState.Downloaded, downloaded.CurrentState);
        Assert.Equal(ContentState.Downloaded, card.CurrentState);
        Assert.True(card.IsDownloaded);
        Assert.Null(card.UpdateTargetVm);
        Assert.False(card.ShowUpdateButton);
        Assert.True(card.ShowAddToProfileButton);
        Assert.False(card.ShowDownloadButton);
    }

    /// <summary>
    /// Verifies that same-release variant siblings with different display names do not mark
    /// a downloaded variant as UpdateAvailable. Variant labels are not versions and must
    /// never drive the newer-version comparison.
    /// </summary>
    [Fact]
    public void ReconcileItemVariants_WhenSameReleaseSiblingsHaveDifferentDisplayNames_DownloadedVariantStaysDownloaded()
    {
        // Arrange: three variants of one release (equal manifest-ID version segments).
        var stateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();
        var searchResult = new ContentSearchResult
        {
            Id = "genericcatalog.genhub-test-publishers.addon.eliorataimprovedmenus",
            Name = "ImprovedMenus",
            Version = "v1.3_h1",
            ProviderName = "genhub-test-publishers",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };
        var item = new ContentGridItemViewModel(searchResult, stateServiceMock.Object, loggerMock.Object)
        {
            CurrentState = ContentState.Downloaded,
            IsDownloaded = true,
        };
        var spanish = new InstallableVariant
        {
            Name = "ImprovedMenus (Spanish)",
            ManifestId = "1.103.genericcatalog.addon.eliorataimprovedmenuslanguagespanish",
            CurrentState = ContentState.NotDownloaded,
        };
        var russian = new InstallableVariant
        {
            Name = "ImprovedMenus (Russian)",
            ManifestId = "1.103.genericcatalog.addon.eliorataimprovedmenuslanguagerussian",
            CurrentState = ContentState.NotDownloaded,
        };
        var english = new InstallableVariant
        {
            Name = "ImprovedMenus (English)",
            ManifestId = "1.103.genericcatalog.addon.eliorataimprovedmenuslanguageenglish",
            CurrentState = ContentState.Downloaded,
        };
        item.Variants.Add(spanish);
        item.Variants.Add(russian);
        item.Variants.Add(english);
        item.SelectedVariant = english;

        // Act
        DownloadsBrowserViewModel.ReconcileItemVariants(item);

        // Assert
        Assert.Equal(ContentState.Downloaded, english.CurrentState);
        Assert.Equal(ContentState.Downloaded, item.CurrentState);
        Assert.True(item.IsDownloaded);
        Assert.Null(item.UpdateTargetVm);
    }

    /// <summary>
    /// Verifies that same-version sibling cards from one multi-asset release (e.g. language
    /// variants surfaced as separate cards) never mark each other as updates: the downloaded
    /// sibling stays Downloaded with no update target.
    /// </summary>
    [Fact]
    public void ReconcileReleaseUpdateStates_WhenFamilySharesOneVersion_DownloadedSiblingStaysDownloaded()
    {
        // Arrange: three cards from ElTioRata/ImprovedMenus v1.3, English downloaded.
        var stateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();

        ContentGridItemViewModel CreateSiblingCard(string language, ContentState state)
        {
            var sr = new ContentSearchResult
            {
                Id = $"github.ElTioRata.ImprovedMenus.v1.3.{language}",
                Name = $"ImprovedMenus ({language})",
                Version = "v1.3_h1",
                ProviderName = "github",
                ContentType = ContentType.Addon,
                TargetGame = GameType.ZeroHour,
                LastUpdated = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                ResolverMetadata =
                {
                    [GitHubConstants.OwnerMetadataKey] = "ElTioRata",
                    [GitHubConstants.RepoMetadataKey] = "ImprovedMenus",
                },
            };
            return new ContentGridItemViewModel(sr, stateServiceMock.Object, loggerMock.Object)
            {
                CurrentState = state,
                IsDownloaded = state is ContentState.Downloaded or ContentState.UpdateAvailable,
            };
        }

        var english = CreateSiblingCard("English", ContentState.Downloaded);
        var russian = CreateSiblingCard("Russian", ContentState.NotDownloaded);
        var spanish = CreateSiblingCard("Spanish", ContentState.NotDownloaded);

        // Act
        DownloadsBrowserViewModel.ReconcileReleaseUpdateStates([english, russian, spanish]);

        // Assert: no card offers an update to a same-version sibling.
        Assert.Equal(ContentState.Downloaded, english.CurrentState);
        Assert.True(english.IsDownloaded);
        Assert.Null(english.UpdateTargetVm);
        Assert.False(english.ShowUpdateButton);
        Assert.Equal(ContentState.NotDownloaded, russian.CurrentState);
        Assert.Equal(ContentState.NotDownloaded, spanish.CurrentState);
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
            .ReturnsAsync(OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.Success(GenHub.Core.Models.Enums.UpdateStrategy.ReplaceCurrent, null)));

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
    /// Verifies that UpdateContentCommand falls through to the explicit update flow (download
    /// plus reconcile) when the publisher reconciler reports success-but-noop, so an
    /// acknowledged update is never silently swallowed.
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
        orchestratorMock
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        var reconcilerMock = new Mock<IPublisherReconciler>();
        reconcilerMock
            .Setup(r => r.CheckAndReconcileIfNeededAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.None));

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

        // Assert: Reconciler was invoked but reported noop, so the explicit update flow downloaded the target
        reconcilerMock.Verify(
            r => r.CheckAndReconcileIfNeededAsync(string.Empty, It.IsAny<CancellationToken>()),
            Times.Once);
        orchestratorMock.Verify(
            o => o.AcquireContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that UpdateContentCommand reports failure without a completion notification
    /// when applying the update strategy throws.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task UpdateContentCommand_WhenUpdateStrategyThrows_ReportsFailureWithoutCompletionNotificationAsync()
    {
        // Arrange
        var orchestratorMock = new Mock<IContentOrchestrator>();
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.custom.mod.test"),
            Name = "Custom Mod",
            Version = "2.0.0",
        };
        orchestratorMock
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        var reconcilerRegistryMock = new Mock<IPublisherReconcilerRegistry>();
        reconcilerRegistryMock
            .Setup(r => r.GetReconciler(It.IsAny<string>()))
            .Returns((IPublisherReconciler?)null);

        var contentStateServiceMock = new Mock<IContentStateService>();
        contentStateServiceMock
            .Setup(s => s.GetLocalManifestIdAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("1.0.custom.mod.old");

        var manifestPoolMock = new Mock<IContentManifestPool>();
        manifestPoolMock
            .Setup(m => m.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Manifest pool unavailable"));

        var telemetryMock = new Mock<ITelemetryService>();
        var notificationMock = new Mock<INotificationService>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(ITelemetryService)))
            .Returns(telemetryMock.Object);
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IContentReconciliationService)))
            .Returns(Mock.Of<IContentReconciliationService>());
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IContentManifestPool)))
            .Returns(manifestPoolMock.Object);

        var viewModel = CreateViewModel(
            orchestrator: orchestratorMock.Object,
            reconcilerRegistry: reconcilerRegistryMock.Object,
            serviceProvider: serviceProviderMock.Object,
            contentStateService: contentStateServiceMock.Object,
            notificationService: notificationMock.Object);

        var gridStateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();
        var targetSr = new ContentSearchResult
        {
            Id = "custom.mod.v2",
            ProviderName = "custom",
            Name = "Custom Mod v2",
            Version = "2.0.0",
        };
        var targetVm = new ContentGridItemViewModel(targetSr, gridStateServiceMock.Object, loggerMock.Object);

        var currentSr = new ContentSearchResult
        {
            Id = "custom.mod.v1",
            ProviderName = "custom",
            Name = "Custom Mod v1",
            Version = "1.0.0",
        };
        var currentVm = new ContentGridItemViewModel(currentSr, gridStateServiceMock.Object, loggerMock.Object)
        {
            UpdateTargetVm = targetVm,
        };

        // Act
        await viewModel.UpdateContentCommand.ExecuteAsync(currentVm);

        // Assert: failure telemetry emitted, error status set, no completion notification
        telemetryMock.Verify(
            t => t.TrackEvent(
                TelemetryConstants.Events.ContentUpdateFailed,
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<TelemetryLevel>()),
            Times.Once);
        Assert.StartsWith(ContentConstants.ErrorStatusPrefix, targetVm.DownloadStatus);
        notificationMock.Verify(
            n => n.ShowSuccess(
                "Update Completed",
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that UpdateContentCommand does not emit failure telemetry when download is cancelled.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task UpdateContentCommand_WhenDownloadIsCancelled_DoesNotEmitContentUpdateFailedAsync()
    {
        // Arrange
        var orchestratorMock = new Mock<IContentOrchestrator>();
        using var cts = new CancellationTokenSource();
        orchestratorMock
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var reconcilerRegistryMock = new Mock<IPublisherReconcilerRegistry>();
        var contentStateServiceMock = new Mock<IContentStateService>();
        var telemetryMock = new Mock<ITelemetryService>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(ITelemetryService)))
            .Returns(telemetryMock.Object);

        var viewModel = CreateViewModel(
            orchestrator: orchestratorMock.Object,
            reconcilerRegistry: reconcilerRegistryMock.Object,
            serviceProvider: serviceProviderMock.Object,
            contentStateService: contentStateServiceMock.Object);

        var gridStateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();
        var targetSr = new ContentSearchResult { Id = "test.mod.v2", Name = "Mod v2", ProviderName = "custom" };
        var targetVm = new ContentGridItemViewModel(targetSr, gridStateServiceMock.Object, loggerMock.Object);
        var currentSr = new ContentSearchResult { Id = "test.mod.v1", Name = "Mod v1", ProviderName = "custom" };
        var currentVm = new ContentGridItemViewModel(currentSr, gridStateServiceMock.Object, loggerMock.Object)
        {
            UpdateTargetVm = targetVm,
        };

        // Act
        await viewModel.UpdateContentCommand.ExecuteAsync(currentVm);

        // Assert: no ContentUpdateFailed telemetry emitted on cancellation
        telemetryMock.Verify(
            t => t.TrackEvent(
                TelemetryConstants.Events.ContentUpdateFailed,
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<TelemetryLevel>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that UpdateContentCommand does not emit failure telemetry when update strategy is cancelled.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task UpdateContentCommand_WhenUpdateStrategyIsCancelled_DoesNotEmitContentUpdateFailedAsync()
    {
        // Arrange
        var orchestratorMock = new Mock<IContentOrchestrator>();
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.custom.mod.test"),
            Name = "Custom Mod",
            Version = "2.0.0",
        };
        orchestratorMock
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        var reconcilerRegistryMock = new Mock<IPublisherReconcilerRegistry>();
        var contentStateServiceMock = new Mock<IContentStateService>();
        contentStateServiceMock
            .Setup(s => s.GetLocalManifestIdAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("1.0.custom.mod.old");

        var manifestPoolMock = new Mock<IContentManifestPool>();
        manifestPoolMock
            .Setup(m => m.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var telemetryMock = new Mock<ITelemetryService>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(ITelemetryService)))
            .Returns(telemetryMock.Object);
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IContentReconciliationService)))
            .Returns(Mock.Of<IContentReconciliationService>());
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IContentManifestPool)))
            .Returns(manifestPoolMock.Object);

        var viewModel = CreateViewModel(
            orchestrator: orchestratorMock.Object,
            reconcilerRegistry: reconcilerRegistryMock.Object,
            serviceProvider: serviceProviderMock.Object,
            contentStateService: contentStateServiceMock.Object);

        var gridStateServiceMock = new Mock<IContentStateService>();
        var loggerMock = new Mock<ILogger<ContentGridItemViewModel>>();
        var targetSr = new ContentSearchResult { Id = "test.mod.v2", Name = "Mod v2", ProviderName = "custom" };
        var targetVm = new ContentGridItemViewModel(targetSr, gridStateServiceMock.Object, loggerMock.Object);
        var currentSr = new ContentSearchResult { Id = "test.mod.v1", Name = "Mod v1", ProviderName = "custom" };
        var currentVm = new ContentGridItemViewModel(currentSr, gridStateServiceMock.Object, loggerMock.Object)
        {
            UpdateTargetVm = targetVm,
        };

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => viewModel.UpdateContentCommand.ExecuteAsync(currentVm));

        // Assert: no ContentUpdateFailed telemetry emitted on cancellation and status not marked as error
        telemetryMock.Verify(
            t => t.TrackEvent(
                TelemetryConstants.Events.ContentUpdateFailed,
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<TelemetryLevel>()),
            Times.Never);
        Assert.False(targetVm.DownloadStatus?.StartsWith(ContentConstants.ErrorStatusPrefix) == true);
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

    /// <summary>
    /// Verifies that closing the detail view for content with an available update preserves
    /// the grid item's UpdateAvailable state and ShowUpdateButton visibility.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CloseDetailCommand_WhenContentHasUpdateAvailable_PreservesUpdateAvailableStateAsync()
    {
        // Arrange
        const string prospectiveCatalogId = "GeneralsOnline_082826_QFE1";
        const string oldLocalManifestId = "1.329260.generalsonline.gameclient.60hz";

        var stateServiceMock = new Mock<IContentStateService>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ILogger<ContentDetailViewModel>)))
            .Returns(new Mock<ILogger<ContentDetailViewModel>>().Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ITabProviderRegistry)))
            .Returns(new Mock<ITabProviderRegistry>().Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IContentDownloadCoordinator)))
            .Returns(new Mock<IContentDownloadCoordinator>().Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IContentManifestPool)))
            .Returns(new Mock<IContentManifestPool>().Object);

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
            Id = prospectiveCatalogId,
            Name = "Generals Online",
            ProviderName = PublisherTypeConstants.GeneralsOnline,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };

        // Match by catalog id: the detail view receives a snapshot of the card's search
        // result (so variant swaps and id rewrites cannot corrupt the card), and state
        // resolution is value-based, so reference-identity matching would miss it.
        stateServiceMock
            .Setup(s => s.GetStateAsync(It.Is<ContentSearchResult>(r => r.Id == prospectiveCatalogId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.UpdateAvailable);
        stateServiceMock
            .Setup(s => s.GetLocalManifestIdAsync(It.Is<ContentSearchResult>(r => r.Id == prospectiveCatalogId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldLocalManifestId);

        var item = new ContentGridItemViewModel(sr, stateServiceMock.Object, new Mock<ILogger<ContentGridItemViewModel>>().Object)
        {
            CurrentState = ContentState.UpdateAvailable,
        };
        viewModel.ContentItems.Add(item);

        Assert.True(item.ShowUpdateButton);

        // Act 1: open detail
        viewModel.ViewContentCommand.Execute(item);
        Assert.NotNull(viewModel.SelectedContent);
        await viewModel.SelectedContent.WaitForInitializationAsync();

        // SearchResult.Id must remain the prospective catalog ID, not overwritten with oldLocalManifestId
        Assert.Equal(prospectiveCatalogId, sr.Id);
        Assert.True(viewModel.SelectedContent.IsUpdateAvailable);

        // Act 2: close detail
        viewModel.CloseDetailCommand.Execute(null);

        // Allow background refresh task to complete
        await Task.Delay(100);

        // Assert: detail is closed, grid item remains UpdateAvailable with ShowUpdateButton true
        Assert.Null(viewModel.SelectedContent);
        Assert.Equal(ContentState.UpdateAvailable, item.CurrentState);
        Assert.True(item.ShowUpdateButton);
    }

    /// <summary>
    /// Verifies that receiving ContentLibraryClearedMessage clears visible items and resets pagination
    /// when currently viewing the Downloaded tab.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task OnContentLibraryCleared_WhenViewingDownloadedPublisher_ClearsItemsAndEvictsCacheAsync()
    {
        // Arrange
        var manifestPoolMock = new Mock<IContentManifestPool>();
        manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IContentManifestPool))).Returns(manifestPoolMock.Object);

        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([]));

        using var viewModel = new DownloadsBrowserViewModel(
            serviceProviderMock.Object,
            new Mock<ILogger<DownloadsBrowserViewModel>>().Object,
            [],
            new Mock<IContentStateService>().Object,
            new Mock<IContentOrchestrator>().Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ILoggerFactory>().Object,
            subscriptionStore.Object);

        await viewModel.InitializeAsync();

        var downloadedPublisher = viewModel.Publishers.First(p => p.PublisherId == PublisherTypeConstants.Downloaded);
        viewModel.SelectedPublisher = downloadedPublisher;

        var sr = new ContentSearchResult
        {
            Id = "1.0.test.mod.testmod",
            Name = "Test Mod",
            ContentType = ContentType.Mod,
        };
        var item = new ContentGridItemViewModel(
            sr,
            new Mock<IContentStateService>().Object,
            new Mock<ILogger<ContentGridItemViewModel>>().Object)
        {
            IsDownloaded = true,
            CurrentState = ContentState.Downloaded,
        };
        viewModel.ContentItems.Add(item);
        Assert.Single(viewModel.ContentItems);

        // Act
        WeakReferenceMessenger.Default.Send(new ContentLibraryClearedMessage());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Assert: ContentItems is cleared because manifests were wiped
        Assert.Empty(viewModel.ContentItems);
        Assert.Equal(1, viewModel.CurrentPage);
        Assert.False(viewModel.CanLoadMore);
    }

    /// <summary>
    /// Verifies that receiving ContentLibraryClearedMessage while viewing another publisher
    /// reconciles release states across visible cards and updates downloaded count.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task OnContentLibraryCleared_WhenViewingOtherPublisher_ReconcilesCardsAsync()
    {
        // Arrange
        var manifestPoolMock = new Mock<IContentManifestPool>();
        manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IContentManifestPool))).Returns(manifestPoolMock.Object);

        var sub = new PublisherSubscription { PublisherId = "test-pub", PublisherName = "Test Pub", CatalogUrl = "https://example.com/pub.json" };
        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([sub]));

        using var viewModel = new DownloadsBrowserViewModel(
            serviceProviderMock.Object,
            new Mock<ILogger<DownloadsBrowserViewModel>>().Object,
            [],
            new Mock<IContentStateService>().Object,
            new Mock<IContentOrchestrator>().Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ILoggerFactory>().Object,
            subscriptionStore.Object);

        await viewModel.InitializeAsync();

        var otherPublisher = viewModel.Publishers.First(p => p.PublisherId == "test-pub");
        viewModel.SelectedPublisher = otherPublisher;

        // Card that was previously marked as Downloaded
        var sr = new ContentSearchResult
        {
            Id = "1.0.test.mod.testmod",
            Name = "Test Mod",
            ContentType = ContentType.Mod,
        };
        var card = new ContentGridItemViewModel(
            sr,
            new Mock<IContentStateService>().Object,
            new Mock<ILogger<ContentGridItemViewModel>>().Object)
        {
            IsDownloaded = true,
            CurrentState = ContentState.Downloaded,
        };
        card.Initialize();
        viewModel.ContentItems.Add(card);

        // Act
        WeakReferenceMessenger.Default.Send(new ContentLibraryClearedMessage());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Assert: Card is reset via ContentLibraryClearedMessage and reconciliation
        Assert.False(card.IsDownloaded);
        Assert.Equal(ContentState.NotDownloaded, card.CurrentState);
        Assert.True(card.ShowDownloadButton);
        Assert.False(card.ShowAddToProfileButton);
    }

    /// <summary>
    /// Verifies that CanShowCatalogSwitcher returns true when a publisher has multiple catalogs.
    /// </summary>
    [Fact]
    public void CanShowCatalogSwitcher_WhenMultipleCatalogs_ReturnsTrue()
    {
        // Arrange
        using var viewModel = CreateViewModel();
        viewModel.AvailableCatalogs.Add(new CatalogEntry { Id = "c1", Name = "Catalog 1", Url = "https://example.com/1.json" });
        viewModel.AvailableCatalogs.Add(new CatalogEntry { Id = "c2", Name = "Catalog 2", Url = "https://example.com/2.json" });

        // Act & Assert
        Assert.True(viewModel.HasMultipleCatalogs);
        Assert.True(viewModel.CanShowCatalogSwitcher);
    }

    /// <summary>
    /// Verifies that CanShowCatalogSwitcher returns true when catalogs are loading for a subscribed publisher.
    /// </summary>
    [Fact]
    public void CanShowCatalogSwitcher_WhenSubscribedPublisherAndLoadingCatalogs_ReturnsTrue()
    {
        // Arrange
        using var viewModel = CreateViewModel();
        var subPub = new PublisherItemViewModel("sub-1", "Subscribed Publisher", null, CatalogConstants.SubscribedPublisherCategory);
        viewModel.Publishers.Add(subPub);
        viewModel.SelectedPublisher = subPub;

        // Act
        viewModel.IsLoadingCatalogs = true;

        // Assert
        Assert.True(viewModel.IsSubscribedPublisher);
        Assert.True(viewModel.CanShowCatalogSwitcher);
    }

    /// <summary>
    /// Verifies that CanShowCatalogSwitcher returns false for a non-subscribed publisher with a single catalog.
    /// </summary>
    [Fact]
    public void CanShowCatalogSwitcher_WhenNonSubscribedSingleCatalog_ReturnsFalse()
    {
        // Arrange
        using var viewModel = CreateViewModel();
        var pub = new PublisherItemViewModel("std-1", "Standard Publisher", null, "Standard");
        viewModel.Publishers.Add(pub);
        viewModel.SelectedPublisher = pub;
        viewModel.AvailableCatalogs.Add(new CatalogEntry { Id = "c1", Name = "Catalog 1", Url = "https://example.com/1.json" });
        viewModel.IsLoadingCatalogs = false;

        // Act & Assert
        Assert.False(viewModel.HasMultipleCatalogs);
        Assert.False(viewModel.IsSubscribedPublisher);
        Assert.False(viewModel.CanShowCatalogSwitcher);
    }

    /// <summary>
    /// Verifies that CanShowCatalogSwitcher returns true for a subscribed publisher even with a single catalog.
    /// </summary>
    [Fact]
    public void CanShowCatalogSwitcher_WhenSubscribedPublisherSingleCatalog_ReturnsTrue()
    {
        // Arrange
        using var viewModel = CreateViewModel();
        var subPub = new PublisherItemViewModel("sub-1", "Subscribed Publisher", null, CatalogConstants.SubscribedPublisherCategory);
        viewModel.Publishers.Add(subPub);
        viewModel.SelectedPublisher = subPub;
        viewModel.AvailableCatalogs.Add(new CatalogEntry { Id = "c1", Name = "Catalog 1", Url = "https://example.com/1.json" });
        viewModel.IsLoadingCatalogs = false;

        // Act & Assert
        Assert.False(viewModel.HasMultipleCatalogs);
        Assert.True(viewModel.IsSubscribedPublisher);
        Assert.True(viewModel.CanShowCatalogSwitcher);
    }

    /// <summary>
    /// Verifies that CatalogLoadingStatusText reflects updating state when catalogs exist,
    /// and loading state when no catalogs exist, using localized resources.
    /// </summary>
    [Fact]
    public void CatalogLoadingStatusText_ReflectsUpdateVsInitialLoadState()
    {
        // Arrange
        var localizationMock = new Mock<ILocalizationService>();
        localizationMock.Setup(l => l.GetString("Downloads.Browser.LoadingCatalogs")).Returns("Localized Loading Catalogs");
        localizationMock.Setup(l => l.GetString("Downloads.Browser.UpdatingCatalog")).Returns("Localized Updating Catalog");

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ILocalizationService))).Returns(localizationMock.Object);

        using var viewModel = CreateViewModel(serviceProvider: serviceProviderMock.Object);

        // Empty catalog list -> initial loading text
        Assert.Equal("Localized Loading Catalogs", viewModel.CatalogLoadingStatusText);

        // Populated catalog list -> updating text
        viewModel.AvailableCatalogs.Add(new CatalogEntry { Id = "c1", Name = "Catalog 1", Url = "https://example.com/1.json" });
        Assert.Equal("Localized Updating Catalog", viewModel.CatalogLoadingStatusText);
    }

    /// <summary>
    /// Verifies that CanReloadCatalog tracks busy and loading states appropriately.
    /// </summary>
    [Fact]
    public void CanReloadCatalog_TracksLoadingStates()
    {
        // Arrange
        using var viewModel = CreateViewModel();
        Assert.False(viewModel.CanReloadCatalog);

        var publisher = new PublisherItemViewModel("pub-1", "Pub 1", null, CatalogConstants.SubscribedPublisherCategory);
        viewModel.Publishers.Add(publisher);
        viewModel.SelectedPublisher = publisher;
        Assert.True(viewModel.CanReloadCatalog);

        viewModel.IsLoadingCatalogs = true;
        Assert.False(viewModel.CanReloadCatalog);

        viewModel.IsLoadingCatalogs = false;
        Assert.True(viewModel.CanReloadCatalog);

        viewModel.IsLoading = true;
        Assert.False(viewModel.CanReloadCatalog);
    }

    /// <summary>
    /// Verifies that ResolveDefaultVariant selects the Generals Online 60Hz game client
    /// over game data patches, map packs, and test clients.
    /// </summary>
    [Fact]
    public void ResolveDefaultVariant_GeneralsOnlineReleaseGroup_Selects60HzGameClientOverGameDataPatch()
    {
        // Arrange
        var patch = new ContentSearchResult
        {
            Id = "1.928260.generalsonline.patch.gamedata",
            Name = "Generals Online Game Data",
            ContentType = ContentType.Patch,
            ProviderName = PublisherTypeConstants.GeneralsOnline,
        };
        var mapPack = new ContentSearchResult
        {
            Id = "1.928260.generalsonline.mappack.quickmatchmaps",
            Name = "Generals Online QuickMatch MapPack",
            ContentType = ContentType.MapPack,
            ProviderName = PublisherTypeConstants.GeneralsOnline,
        };
        var testClient = new ContentSearchResult
        {
            Id = "1.928260.generalsonline.gameclient.test",
            Name = "Generals Online Test GameClient",
            ContentType = ContentType.GameClient,
            ProviderName = PublisherTypeConstants.GeneralsOnline,
        };
        var client60Hz = new ContentSearchResult
        {
            Id = "1.928260.generalsonline.gameclient.60hz",
            Name = "GeneralsOnline 60Hz",
            ContentType = ContentType.GameClient,
            ProviderName = PublisherTypeConstants.GeneralsOnline,
        };

        var group = new List<ContentSearchResult> { patch, mapPack, testClient, client60Hz };

        // Act
        var selected = DownloadsBrowserViewModel.ResolveDefaultVariant(group, group[0]);

        // Assert: 60Hz Game Client must be selected, NOT the patch or map pack
        Assert.Same(client60Hz, selected);
    }

    /// <summary>
    /// Verifies that SelectDefaultVariant selects the 60Hz variant on the card view model.
    /// </summary>
    [Fact]
    public void SelectDefaultVariant_GeneralsOnlineGroup_Selects60HzVariant()
    {
        // Arrange
        var patch = new ContentSearchResult
        {
            Id = "1.928260.generalsonline.patch.gamedata",
            Name = "Generals Online Game Data",
            ContentType = ContentType.Patch,
            ProviderName = PublisherTypeConstants.GeneralsOnline,
        };
        var client60Hz = new ContentSearchResult
        {
            Id = "1.928260.generalsonline.gameclient.60hz",
            Name = "GeneralsOnline 60Hz",
            ContentType = ContentType.GameClient,
            ProviderName = PublisherTypeConstants.GeneralsOnline,
        };

        var group = new List<ContentSearchResult> { patch, client60Hz };
        var card = new ContentGridItemViewModel(client60Hz, Mock.Of<IContentStateService>(), Mock.Of<ILogger<ContentGridItemViewModel>>());

        var patchVariant = new InstallableVariant { Name = "Generals Online Game Data", ManifestId = patch.Id };
        var clientVariant = new InstallableVariant { Name = "GeneralsOnline 60Hz", ManifestId = client60Hz.Id };

        card.AddVariant(patchVariant, patch);
        card.AddVariant(clientVariant, client60Hz);

        // Act
        DownloadsBrowserViewModel.SelectDefaultVariant(card, group, client60Hz, client60Hz);

        // Assert
        Assert.Same(clientVariant, card.SelectedVariant);
    }

    /// <summary>
    /// Verifies that ResolveDefaultVariant respects catalog-declared default variant over generic GameClient for non-Generals-Online content.
    /// </summary>
    [Fact]
    public void ResolveDefaultVariant_GenericGroupWithCatalogDefaultAndGameClient_PrefersCatalogDefault()
    {
        // Arrange
        var gameClientItem = new ContentSearchResult
        {
            Id = "generic.gameclient",
            Name = "Generic GameClient",
            ContentType = ContentType.GameClient,
            ProviderName = "ModDB",
        };
        var defaultModItem = new ContentSearchResult
        {
            Id = "generic.mod",
            Name = "Generic Default Mod",
            ContentType = ContentType.Mod,
            ProviderName = "ModDB",
            Variants =
            [
                new ContentVariantInfo { Id = "generic.mod", Name = "Default Mod Variant", ManifestId = "generic.mod", IsDefault = true },
            ],
        };

        var group = new List<ContentSearchResult> { gameClientItem, defaultModItem };

        // Act
        var resolved = DownloadsBrowserViewModel.ResolveDefaultVariant(group, gameClientItem);

        // Assert: Catalog-declared default item should be authoritative over arbitrary GameClient
        Assert.Same(defaultModItem, resolved);
    }

    /// <summary>
    /// Verifies that SelectDefaultVariant does not select the 60Hz variant fallback for non-Generals-Online content.
    /// </summary>
    [Fact]
    public void SelectDefaultVariant_NonGeneralsOnlineGroup_DoesNotSelect60HzFallback()
    {
        // Arrange
        var generic1 = new ContentSearchResult
        {
            Id = "generic.mod.item1",
            Name = "Generic Mod",
            ContentType = ContentType.Mod,
            ProviderName = "ModDB",
        };
        var generic2 = new ContentSearchResult
        {
            Id = "generic.mod.item2",
            Name = "Generic Mod 60Hz Edition",
            ContentType = ContentType.Mod,
            ProviderName = "ModDB",
        };

        var group = new List<ContentSearchResult> { generic1, generic2 };
        var card = new ContentGridItemViewModel(generic1, Mock.Of<IContentStateService>(), Mock.Of<ILogger<ContentGridItemViewModel>>());

        var defaultVariant = new InstallableVariant { Name = "Generic Mod Standard", ManifestId = "non-matching-id" };
        var variant60Hz = new InstallableVariant { Name = "Generic Mod 60Hz Edition", ManifestId = generic2.Id };
        var lastVariant = new InstallableVariant { Name = "Generic Mod Last", ManifestId = "last-id" };

        card.AddVariant(defaultVariant, generic1);
        card.AddVariant(variant60Hz, generic2);
        card.AddVariant(lastVariant, generic1);

        // Act: defaultVariant.Id does not match card variant ManifestIds, so it hits the fallbacks.
        // Since it is NOT Generals Online, the 60Hz fallback must be bypassed, falling back to last variant.
        DownloadsBrowserViewModel.SelectDefaultVariant(card, group, generic1, generic1);

        // Assert: Must NOT select variant60Hz; should fall back to the last variant
        Assert.Same(lastVariant, card.SelectedVariant);
    }

    /// <summary>
    /// Verifies that MatchesNotificationTarget matches either primary card ID or child variant ManifestId.
    /// </summary>
    [Fact]
    public void MatchesNotificationTarget_MatchesByPrimaryIdOrVariantManifestId()
    {
        // Arrange
        var primaryResult = new ContentSearchResult
        {
            Id = "primary.client.id",
            Name = "Primary Client",
            ContentType = ContentType.GameClient,
        };
        var card = new ContentGridItemViewModel(primaryResult, Mock.Of<IContentStateService>(), Mock.Of<ILogger<ContentGridItemViewModel>>());
        var patchResult = new ContentSearchResult
        {
            Id = "sibling.patch.id",
            Name = "Patch",
            ContentType = ContentType.Patch,
        };
        card.AddVariant(new InstallableVariant { Name = "Patch", ManifestId = patchResult.Id }, patchResult);

        var unrelatedItem = new ContentSearchResult { Id = "other.id", Name = "Other" };

        // Act & Assert
        Assert.True(DownloadsBrowserViewModel.MatchesNotificationTarget(card, primaryResult));
        Assert.True(DownloadsBrowserViewModel.MatchesNotificationTarget(card, patchResult));
        Assert.False(DownloadsBrowserViewModel.MatchesNotificationTarget(card, unrelatedItem));
    }

    private static InstallableVariant AddCardVariant(
        ContentGridItemViewModel card,
        string manifestId,
        string name,
        string version,
        ContentState state)
    {
        var sibling = new ContentSearchResult
        {
            Id = manifestId,
            Name = name,
            Version = version,
            ProviderName = card.SearchResult.ProviderName,
            ContentType = card.SearchResult.ContentType,
            TargetGame = card.SearchResult.TargetGame,
        };
        var installable = new InstallableVariant
        {
            Name = name,
            ManifestId = manifestId,
            CurrentState = state,
        };
        card.AddVariant(installable, sibling);
        return installable;
    }

    private static ContentGridItemViewModel CreateUnrelatedCard(
        IContentStateService stateService,
        ILogger<ContentGridItemViewModel> logger)
    {
        var sr = new ContentSearchResult
        {
            Id = "1.0.github.mod.unrelatedfixture",
            Name = "Unrelated Fixture",
            Version = "1.0",
            ProviderName = "github",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };
        return new ContentGridItemViewModel(sr, stateService, logger)
        {
            CurrentState = ContentState.NotDownloaded,
            IsDownloaded = false,
        };
    }

    private static DownloadsBrowserViewModel CreateViewModel(
        IContentOrchestrator? orchestrator = null,
        IPublisherReconcilerRegistry? reconcilerRegistry = null,
        IServiceProvider? serviceProvider = null,
        IContentStateService? contentStateService = null,
        INotificationService? notificationService = null)
    {
        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([]));

        return new DownloadsBrowserViewModel(
            serviceProvider ?? new Mock<IServiceProvider>().Object,
            new Mock<ILogger<DownloadsBrowserViewModel>>().Object,
            [],
            contentStateService ?? new Mock<IContentStateService>().Object,
            orchestrator ?? new Mock<IContentOrchestrator>().Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            notificationService ?? new Mock<INotificationService>().Object,
            new Mock<ILoggerFactory>().Object,
            subscriptionStore.Object,
            reconcilerRegistry: reconcilerRegistry);
    }
}
