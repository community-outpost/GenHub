using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Tests mapping publisher catalog tab cards into the Downloads detail tab contract.
/// </summary>
public sealed class CatalogTabProviderTests
{
    /// <summary>
    /// Ensures optional publisher card data is retained for the detail view renderer.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task GetTabsAsync_CatalogContainsCards_MapsIntroAndCardsAsync()
    {
        // Arrange
        const string publisherId = "example-publisher";
        const string publisherName = "Example Publisher";
        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionAsync(publisherId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherSubscription?>.CreateSuccess(new PublisherSubscription
            {
                PublisherId = publisherId,
                PublisherName = publisherName,
                CatalogUrl = "https://catalog.example.test/catalog.json",
            }));

        var catalogParser = new Mock<IPublisherCatalogParser>();
        catalogParser
            .Setup(parser => parser.ParseCatalogAsync("{}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(new PublisherCatalog
            {
                CustomTabs =
                [
                    new CatalogTabDefinition
                    {
                        TabId = "release-briefing",
                        Header = "Release briefing",
                        Intro = "A concise publisher note.",
                        Cards =
                        [
                            new CatalogTabCardDefinition
                            {
                                Title = "Stability pass",
                                Description = "Improved multiplayer reliability.",
                                Label = "THIS WEEK",
                                ImageUrl = "avares://GenHub/Assets/Covers/usa-cover.png",
                                AccentColor = "#3E78B2",
                            },
                        ],
                    },
                ],
            }));

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new StaticResponseHandler("{}")));

        var provider = new CatalogTabProvider(
            subscriptionStore.Object,
            catalogParser.Object,
            httpClientFactory.Object,
            new Mock<ILogger<CatalogTabProvider>>().Object);

        // Act
        var searchResult = new ContentSearchResult
        {
            Id = "1.0.example-publisher.mod.example",
            ProviderName = publisherName,
        };
        searchResult.ResolverMetadata["publisherProfileJson"] = "{\"id\":\"example-publisher\",\"name\":\"Example Publisher\"}";

        var tabs = await provider.GetTabsAsync(searchResult);

        // Assert
        var tab = Assert.Single(tabs);
        Assert.Equal("A concise publisher note.", tab.Intro);
        var card = Assert.Single(tab.Cards);
        Assert.Equal("Stability pass", card.Title);
        Assert.Equal("THIS WEEK", card.Label);
        Assert.Equal("#3E78B2", card.AccentColor);
    }

    /// <summary>
    /// Ensures tabs with null AppliesTo apply to all content items without throwing.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetTabsAsync_NullAppliesTo_ReturnsTabForAllItemsAsync()
    {
        // Arrange
        const string publisherId = "example-publisher";
        const string publisherName = "Example Publisher";
        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionAsync(publisherId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherSubscription?>.CreateSuccess(new PublisherSubscription
            {
                PublisherId = publisherId,
                PublisherName = publisherName,
                CatalogUrl = "https://catalog.example.test/catalog.json",
            }));

        var catalogParser = new Mock<IPublisherCatalogParser>();
        catalogParser
            .Setup(parser => parser.ParseCatalogAsync("{}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(new PublisherCatalog
            {
                CustomTabs =
                [
                    new CatalogTabDefinition
                    {
                        TabId = "all-items-tab",
                        Header = "All Items",
                        AppliesTo = null!,
                    },
                ],
            }));

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new StaticResponseHandler("{}")));

        var provider = new CatalogTabProvider(
            subscriptionStore.Object,
            catalogParser.Object,
            httpClientFactory.Object,
            new Mock<ILogger<CatalogTabProvider>>().Object);

        var searchResult = new ContentSearchResult
        {
            Id = "1.0.example-publisher.mod.example",
            ProviderName = publisherName,
        };
        searchResult.ResolverMetadata["publisherProfileJson"] = "{\"id\":\"example-publisher\",\"name\":\"Example Publisher\"}";

        var tabs = await provider.GetTabsAsync(searchResult);

        var tab = Assert.Single(tabs);
        Assert.Equal("all-items-tab", tab.TabId);
    }

    private sealed class StaticResponseHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content),
            });
        }
    }
}
