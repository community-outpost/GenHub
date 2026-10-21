using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using TabContentType = GenHub.Core.Models.Content.TabContentType;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Tests mapping publisher catalog tab cards into the Downloads detail tab contract.
/// </summary>
public sealed class CatalogTabProviderTests : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogTabProviderTests"/> class.
    /// </summary>
    public CatalogTabProviderTests()
    {
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = false;
    }

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
                        AppliesTo = ["1.0.example-publisher.mod.example"],
                        Cards =
                        [
                            new CatalogTabCardDefinition
                            {
                                Title = "Stability pass",
                                Description = "Improved multiplayer reliability.",
                                Label = "THIS WEEK",
                                ImageUrl = "avares://GenHub/Assets/Covers/usa-cover.jpg",
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
    /// Ensures a catalog-level tab with no appliesTo entries no longer applies to every item.
    /// </summary>
    /// <param name="useNullAppliesTo">Whether appliesTo is null instead of empty.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetTabsAsync_RootTabWithoutAppliesTo_ReturnsNoTabsAsync(bool useNullAppliesTo)
    {
        var provider = CreateProvider(new PublisherCatalog
        {
            CustomTabs =
            [
                new CatalogTabDefinition
                {
                    TabId = "all-items-tab",
                    Header = "All Items",
                    AppliesTo = useNullAppliesTo ? null! : [],
                },
            ],
        });

        var tabs = await provider.GetTabsAsync(CreateSearchResult("mod-a"));

        Assert.Empty(tabs);
    }

    /// <summary>
    /// Ensures a catalog-level tab applies only to the items its appliesTo list names.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetTabsAsync_RootTabWithAppliesTo_AppliesOnlyToListedItemsAsync()
    {
        var provider = CreateProvider(new PublisherCatalog
        {
            CustomTabs =
            [
                new CatalogTabDefinition { TabId = "shared", Header = "Shared", AppliesTo = ["MOD-A"] },
            ],
        });

        var listed = await provider.GetTabsAsync(CreateSearchResult("mod-a"));
        var unlisted = await provider.GetTabsAsync(CreateSearchResult("mod-b"));

        Assert.Equal("shared", Assert.Single(listed).TabId);
        Assert.Empty(unlisted);
    }

    /// <summary>
    /// Ensures per-item tabs are read from the catalog item in resolver metadata, including the
    /// Markdown body, without needing a publisher subscription.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetTabsAsync_ItemMetadataTabs_ReturnsItemTabsWithoutSubscriptionAsync()
    {
        var provider = CreateProvider(catalog: null);
        var item = new CatalogContentItem
        {
            Id = "mod-a",
            Name = "Mod A",
            CustomTabs =
            [
                new CatalogTabDefinition
                {
                    TabId = "install-guide",
                    Header = "Install guide",
                    Body = "## Steps",
                    ContentType = "richtext",
                    Cards = [new CatalogTabCardDefinition { Title = "Step one" }],
                },
            ],
        };
        var searchResult = CreateSearchResult("mod-a", item);

        var tabs = await provider.GetTabsAsync(searchResult);

        var tab = Assert.Single(tabs);
        Assert.Equal("install-guide", tab.TabId);
        Assert.Equal("## Steps", tab.Body);
        Assert.Equal(TabContentType.RichText, tab.ContentType);
        Assert.Equal("Step one", Assert.Single(tab.Cards).Title);
    }

    /// <summary>
    /// Ensures the selected item receives only its own tabs when the provider falls back to the cached catalog.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetTabsAsync_NoItemMetadata_FallsBackToSelectedCatalogItemTabsAsync()
    {
        var provider = CreateProvider(new PublisherCatalog
        {
            Content =
            [
                new CatalogContentItem
                {
                    Id = "mod-a",
                    CustomTabs = [new CatalogTabDefinition { TabId = "a-notes", Header = "A notes" }],
                },
                new CatalogContentItem
                {
                    Id = "mod-b",
                    CustomTabs = [new CatalogTabDefinition { TabId = "b-notes", Header = "B notes" }],
                },
            ],
        });

        var tabsA = await provider.GetTabsAsync(CreateSearchResult("mod-a"));
        var tabsB = await provider.GetTabsAsync(CreateSearchResult("mod-b"));

        Assert.Equal("a-notes", Assert.Single(tabsA).TabId);
        Assert.Equal("b-notes", Assert.Single(tabsB).TabId);
    }

    /// <summary>
    /// Ensures an item tab wins over a catalog-level tab with the same ID, and distinct catalog-level tabs still apply.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetTabsAsync_ItemAndRootTabShareId_KeepsItemTabAsync()
    {
        var provider = CreateProvider(new PublisherCatalog
        {
            CustomTabs =
            [
                new CatalogTabDefinition { TabId = "notes", Header = "Root notes", AppliesTo = ["mod-a"] },
                new CatalogTabDefinition { TabId = "faq", Header = "FAQ", AppliesTo = ["mod-a"] },
            ],
        });
        var item = new CatalogContentItem
        {
            Id = "mod-a",
            CustomTabs = [new CatalogTabDefinition { TabId = "NOTES", Header = "Item notes" }],
        };

        var tabs = await provider.GetTabsAsync(CreateSearchResult("mod-a", item));

        Assert.Equal(2, tabs.Count);
        Assert.Equal("Item notes", Assert.Single(tabs, t => t.TabId.Equals("notes", StringComparison.OrdinalIgnoreCase)).Header);
        Assert.Contains(tabs, t => t.TabId == "faq");
    }

    /// <summary>
    /// Ensures subsequent calls within cache duration use the cached catalog without re-fetching.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetTabsAsync_SubsequentCall_UsesCachedCatalogAsync()
    {
        // Arrange
        const string publisherId = "cached-publisher";
        const string publisherName = "Cached Publisher";
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
                        TabId = "cached-tab",
                        Header = "Cached Tab",
                        AppliesTo = ["1.0.cached-publisher.mod.example"],
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
            Id = "1.0.cached-publisher.mod.example",
            ProviderName = publisherName,
        };
        searchResult.ResolverMetadata["publisherProfileJson"] = "{\"id\":\"cached-publisher\",\"name\":\"Cached Publisher\"}";

        // Act
        var firstResult = await provider.GetTabsAsync(searchResult);
        var secondResult = await provider.GetTabsAsync(searchResult);

        // Assert
        Assert.Single(firstResult);
        Assert.Single(secondResult);
        subscriptionStore.Verify(store => store.GetSubscriptionAsync(publisherId, It.IsAny<CancellationToken>()), Times.Once);
        catalogParser.Verify(parser => parser.ParseCatalogAsync("{}", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static CatalogTabProvider CreateProvider(PublisherCatalog? catalog)
    {
        const string publisherId = "example-publisher";
        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionAsync(publisherId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog == null
                ? OperationResult<PublisherSubscription?>.CreateFailure("not subscribed")
                : OperationResult<PublisherSubscription?>.CreateSuccess(new PublisherSubscription
                {
                    PublisherId = publisherId,
                    PublisherName = "Example Publisher",
                    CatalogUrl = "https://catalog.example.test/catalog.json",
                }));

        var catalogParser = new Mock<IPublisherCatalogParser>();
        if (catalog != null)
        {
            catalogParser
                .Setup(parser => parser.ParseCatalogAsync("{}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));
        }

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new StaticResponseHandler("{}")));

        return new CatalogTabProvider(
            subscriptionStore.Object,
            catalogParser.Object,
            httpClientFactory.Object,
            new Mock<ILogger<CatalogTabProvider>>().Object);
    }

    private static ContentSearchResult CreateSearchResult(string contentId, CatalogContentItem? item = null)
    {
        var searchResult = new ContentSearchResult
        {
            Id = $"1.0.example-publisher.mod.{contentId}",
            ProviderName = "Example Publisher",
        };
        searchResult.ResolverMetadata[CatalogConstants.PublisherProfileJsonMetadataKey] = "{\"id\":\"example-publisher\",\"name\":\"Example Publisher\"}";
        searchResult.ResolverMetadata[CatalogConstants.CatalogContentIdMetadataKey] = contentId;
        if (item != null)
        {
            searchResult.ResolverMetadata[CatalogConstants.CatalogItemJsonMetadataKey] = JsonSerializer.Serialize(item);
        }

        return searchResult;
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
