using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Content.ViewModels.Catalog;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Regression coverage for local Publisher Studio catalogs used through the subscription flow.
/// </summary>
public sealed class CatalogLocalFileIntegrationTests
{
    private const string CatalogJson = "{\"$schemaVersion\":1,\"publisher\":{\"id\":\"local-publisher\",\"name\":\"Local Publisher\"},\"content\":[]}";

    /// <summary>
    /// Verifies local catalog locations never fall through to the HTTP stack.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ReadAsync_LocalFileUriAndAbsolutePath_ReadsTheSameCatalogAsync()
    {
        var catalogPath = await CreateTemporaryCatalogAsync();
        try
        {
            using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
            var fileUri = new Uri(catalogPath).AbsoluteUri;

            var fromFileUri = await CatalogDocumentReader.ReadAsync(httpClient, fileUri);
            var fromAbsolutePath = await CatalogDocumentReader.ReadAsync(httpClient, catalogPath);

            Assert.Equal(CatalogJson, fromFileUri);
            Assert.Equal(CatalogJson, fromAbsolutePath);
        }
        finally
        {
            File.Delete(catalogPath);
        }
    }

    /// <summary>
    /// Verifies the confirmation dialog accepts a file URI before the subscription is persisted.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task InitializeAsync_FileCatalogUri_EnablesSubscriptionConfirmationAsync()
    {
        var catalogPath = await CreateTemporaryCatalogAsync();
        try
        {
            var parser = new Mock<IPublisherCatalogParser>();
            parser
                .Setup(value => value.ParseCatalogAsync(CatalogJson, It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(CreateCatalog()));

            using var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
            var viewModel = new SubscriptionConfirmationViewModel(
                new Uri(catalogPath).AbsoluteUri,
                new Mock<IPublisherSubscriptionStore>().Object,
                parser.Object,
                httpClient,
                new Mock<ILogger<SubscriptionConfirmationViewModel>>().Object);

            await viewModel.InitializeAsync();

            Assert.True(viewModel.CanConfirm);
            Assert.Null(viewModel.ErrorMessage);
            Assert.Equal("Local Publisher", viewModel.PublisherName);
        }
        finally
        {
            File.Delete(catalogPath);
        }
    }

    /// <summary>
    /// Verifies a selected content item receives catalog-defined tabs from a local subscription.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task GetTabsAsync_LocalCatalogAfterSelection_ReturnsPublisherTabsAsync()
    {
        var catalogPath = await CreateTemporaryCatalogAsync();
        try
        {
            var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
            subscriptionStore
                .Setup(store => store.GetSubscriptionAsync("local-publisher", It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<PublisherSubscription?>.CreateSuccess(new PublisherSubscription
                {
                    PublisherId = "local-publisher",
                    PublisherName = "Local Publisher",
                    CatalogUrl = new Uri(catalogPath).AbsoluteUri,
                }));

            var parser = new Mock<IPublisherCatalogParser>();
            parser
                .Setup(value => value.ParseCatalogAsync(CatalogJson, It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(CreateCatalog()));

            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory
                .Setup(factory => factory.CreateClient(It.IsAny<string>()))
                .Returns(new HttpClient(new ThrowingHttpMessageHandler()));

            var provider = new CatalogTabProvider(
                subscriptionStore.Object,
                parser.Object,
                httpClientFactory.Object,
                new Mock<ILogger<CatalogTabProvider>>().Object);

            var selectedContent = new ContentSearchResult
            {
                Id = "1.0.local-publisher.addon.test",
                ProviderName = "Local Publisher",
            };
            selectedContent.ResolverMetadata["publisherProfileJson"] = "{\"id\":\"local-publisher\",\"name\":\"Local Publisher\"}";

            var tabs = await provider.GetTabsAsync(selectedContent);

            var tab = Assert.Single(tabs);
            Assert.Equal("Release briefing", tab.Header);
            Assert.Single(tab.Cards);
        }
        finally
        {
            File.Delete(catalogPath);
        }
    }

    private static PublisherCatalog CreateCatalog()
    {
        return new PublisherCatalog
        {
            Publisher = new PublisherProfile
            {
                Id = "local-publisher",
                Name = "Local Publisher",
            },
            CustomTabs =
            [
                new CatalogTabDefinition
                {
                    TabId = "release-briefing",
                    Header = "Release briefing",
                    Cards =
                    [
                        new CatalogTabCardDefinition
                        {
                            Title = "Local catalog tab",
                            Description = "Loaded from a file URI.",
                        },
                    ],
                },
            ],
        };
    }

    private static async Task<string> CreateTemporaryCatalogAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"GenHub-{Guid.NewGuid():N}.catalog.json");
        await File.WriteAllTextAsync(path, CatalogJson);
        return path;
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("A local catalog must not be requested over HTTP.");
        }
    }
}
