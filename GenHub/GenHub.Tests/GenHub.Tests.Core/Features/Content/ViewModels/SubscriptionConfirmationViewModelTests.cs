using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Content.ViewModels.Catalog;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using CoreContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.ViewModels;

/// <summary>
/// Unit tests for <see cref="SubscriptionConfirmationViewModel"/>.
/// </summary>
public sealed class SubscriptionConfirmationViewModelTests : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubscriptionConfirmationViewModelTests"/> class.
    /// </summary>
    public SubscriptionConfirmationViewModelTests()
    {
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = false;
    }

    private readonly Mock<IPublisherSubscriptionStore> _subscriptionStore = new();
    private readonly Mock<IPublisherCatalogParser> _catalogParser = new();
    private readonly Mock<ILogger<SubscriptionConfirmationViewModel>> _logger = new();
    private readonly HttpClient _httpClient = new(new FakeHttpMessageHandler());

    /// <summary>
    /// Verifies that initializing with an unsubscribed publisher sets up the view model for new subscription.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitializeAsync_WhenNotSubscribed_SetsIsAlreadySubscribedFalseAsync()
    {
        // Arrange
        var catalog = CreateSampleCatalog("new-pub", "New Publisher");
        _catalogParser
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        _subscriptionStore
            .Setup(s => s.IsSubscribedAsync("new-pub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var vm = new SubscriptionConfirmationViewModel(
            "https://example.com/catalog.json",
            _subscriptionStore.Object,
            _catalogParser.Object,
            _httpClient,
            _logger.Object);

        // Act
        await vm.InitializeAsync();

        // Assert
        Assert.True(vm.IsCatalogLoaded);
        Assert.False(vm.IsAlreadySubscribed);
        Assert.Equal("Subscribe to Library", vm.ConfirmButtonText);
        Assert.Equal("N", vm.PublisherInitial);
        Assert.Equal(3, vm.ContentCount);
        Assert.Equal(3, vm.FilteredContentItems.Count);
        Assert.True(vm.ShowDetails);
        Assert.False(vm.ShowInitialError);
        Assert.False(vm.ShowActionError);
    }

    /// <summary>
    /// Verifies that initializing with an already subscribed publisher sets up the view model for updating.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitializeAsync_WhenAlreadySubscribed_SetsIsAlreadySubscribedTrueAsync()
    {
        // Arrange
        var catalog = CreateSampleCatalog("existing-pub", "Existing Publisher");
        _catalogParser
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        _subscriptionStore
            .Setup(s => s.IsSubscribedAsync("existing-pub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var vm = new SubscriptionConfirmationViewModel(
            "https://example.com/catalog.json",
            _subscriptionStore.Object,
            _catalogParser.Object,
            _httpClient,
            _logger.Object);

        // Act
        await vm.InitializeAsync();

        // Assert
        Assert.True(vm.IsCatalogLoaded);
        Assert.True(vm.IsAlreadySubscribed);
        Assert.Equal("Update Subscription", vm.ConfirmButtonText);
        Assert.Equal("E", vm.PublisherInitial);
    }

    /// <summary>
    /// Verifies that confirming a new publisher calls AddSubscriptionAsync.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ConfirmCommand_WhenNotSubscribed_CallsAddSubscriptionAsync()
    {
        // Arrange
        var catalog = CreateSampleCatalog("new-pub", "New Publisher");
        _catalogParser
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        _subscriptionStore
            .Setup(s => s.IsSubscribedAsync("new-pub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        _subscriptionStore
            .Setup(s => s.GetSubscriptionAsync("new-pub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherSubscription?>.CreateSuccess(null));

        _subscriptionStore
            .Setup(s => s.AddSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var vm = new SubscriptionConfirmationViewModel(
            "https://example.com/catalog.json",
            _subscriptionStore.Object,
            _catalogParser.Object,
            _httpClient,
            _logger.Object);

        bool? closeResult = null;
        vm.RequestClose = res => closeResult = res;

        await vm.InitializeAsync();

        // Act
        await vm.ConfirmCommand.ExecuteAsync(null);

        // Assert
        Assert.True(closeResult);
        _subscriptionStore.Verify(s => s.AddSubscriptionAsync(It.Is<PublisherSubscription>(sub => sub.PublisherId == "new-pub"), It.IsAny<CancellationToken>()), Times.Once);
        _subscriptionStore.Verify(s => s.UpdateSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that confirming an already subscribed publisher calls UpdateSubscriptionAsync and preserves settings.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ConfirmCommand_WhenAlreadySubscribed_CallsUpdateSubscriptionAsync()
    {
        // Arrange
        var catalog = CreateSampleCatalog("existing-pub", "Existing Publisher");
        _catalogParser
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        _subscriptionStore
            .Setup(s => s.IsSubscribedAsync("existing-pub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var existingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _subscriptionStore
            .Setup(s => s.GetSubscriptionAsync("existing-pub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherSubscription?>.CreateSuccess(new PublisherSubscription
            {
                PublisherId = "existing-pub",
                PublisherName = "Existing Publisher",
                CatalogUrl = "https://example.com/old-catalog.json",
                TrustLevel = TrustLevel.Trusted,
                AutoUpdate = true,
                NotifyNewReleases = false,
                CachedCatalogHash = "hash123",
                LastFetched = existingDate,
            }));

        _subscriptionStore
            .Setup(s => s.UpdateSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var vm = new SubscriptionConfirmationViewModel(
            "https://example.com/new-catalog.json",
            _subscriptionStore.Object,
            _catalogParser.Object,
            _httpClient,
            _logger.Object);

        bool? closeResult = null;
        vm.RequestClose = res => closeResult = res;

        await vm.InitializeAsync();

        // Act
        await vm.ConfirmCommand.ExecuteAsync(null);

        // Assert
        Assert.True(closeResult);
        _subscriptionStore.Verify(
            s => s.UpdateSubscriptionAsync(
                It.Is<PublisherSubscription>(sub =>
                    sub.PublisherId == "existing-pub" &&
                    sub.CatalogUrl == "https://example.com/new-catalog.json" &&
                    sub.TrustLevel == TrustLevel.Trusted &&
                    sub.AutoUpdate == true &&
                    sub.NotifyNewReleases == false &&
                    sub.CachedCatalogHash == "hash123" &&
                    sub.LastFetched == existingDate),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _subscriptionStore.Verify(s => s.AddSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that ShowActionError is true only when catalog is loaded and an error occurs.
    /// </summary>
    [Fact]
    public void ShowActionError_EvaluatesCorrectly()
    {
        var vm = new SubscriptionConfirmationViewModel(
            "https://example.com/catalog.json",
            _subscriptionStore.Object,
            _catalogParser.Object,
            _httpClient,
            _logger.Object)
        {
            // Initially loading and not loaded
            ErrorMessage = "Some error",
        };
        Assert.False(vm.ShowActionError);

        // Loaded with error -> action error is true
        vm.IsCatalogLoaded = true;
        Assert.True(vm.ShowActionError);

        // Cleared error -> action error is false
        vm.ErrorMessage = null;
        Assert.False(vm.ShowActionError);
    }

    /// <summary>
    /// Verifies that category selection filters content items and updates the active filter.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SelectCategory_FiltersContentItemsCorrectlyAsync()
    {
        // Arrange
        var catalog = CreateSampleCatalog("pub-1", "Test Publisher");
        _catalogParser
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        _subscriptionStore
            .Setup(s => s.IsSubscribedAsync("pub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var vm = new SubscriptionConfirmationViewModel(
            "https://example.com/catalog.json",
            _subscriptionStore.Object,
            _catalogParser.Object,
            _httpClient,
            _logger.Object);

        await vm.InitializeAsync();

        // Assert initial All
        Assert.Equal(3, vm.FilteredContentItems.Count);

        // Act: Filter to Mod
        vm.SelectCategory("Mod");

        // Assert
        Assert.Single(vm.FilteredContentItems);
        Assert.Equal("mod-1", vm.FilteredContentItems[0].Id);

        // Act: Filter back to All
        vm.SelectCategory("All");

        // Assert
        Assert.Equal(3, vm.FilteredContentItems.Count);
    }

    /// <summary>
    /// Verifies that DismissError clears the error message.
    /// </summary>
    [Fact]
    public void DismissError_ClearsErrorMessage()
    {
        // Arrange
        var vm = new SubscriptionConfirmationViewModel(
            "https://example.com/catalog.json",
            _subscriptionStore.Object,
            _catalogParser.Object,
            _httpClient,
            _logger.Object)
        {
            ErrorMessage = "Test Error",
        };

        // Act
        vm.DismissError();

        // Assert
        Assert.Null(vm.ErrorMessage);
    }

    /// <summary>
    /// Verifies that subscribing via a publisher definition shows all catalogs, switches content
    /// between them, and confirms against the selected catalog while preserving the definition URL.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitializeAsync_DefinitionWithTwoCatalogs_ShowsSelectorAndConfirmsSelectionAsync()
    {
        // Arrange: literal-IP URLs pass SSRF checks without DNS so the test stays hermetic.
        const string definitionUrl = "https://93.184.216.34/publisher.json";
        const string modsUrl = "https://93.184.216.34/mods.json";
        const string mapsUrl = "https://93.184.216.34/maps.json";
        var definitionJson = "{\"$schemaVersion\":1,\"publisher\":{\"id\":\"multi-pub\",\"name\":\"Multi Publisher\"},\"catalogs\":[{\"id\":\"mods\",\"name\":\"Mods\",\"url\":\"" + modsUrl + "\"},{\"id\":\"maps\",\"name\":\"Maps\",\"url\":\"" + mapsUrl + "\"}]}";

        using var httpClient = new HttpClient(new MappedFakeHttpMessageHandler(new Dictionary<string, string>
        {
            [definitionUrl] = definitionJson,
            [modsUrl] = "CATALOG_MODS",
            [mapsUrl] = "CATALOG_MAPS",
        }));

        var modsCatalog = CreateSampleCatalog("multi-pub", "Multi Publisher");
        var mapsCatalog = new PublisherCatalog
        {
            Publisher = new PublisherProfile { Id = "multi-pub", Name = "Multi Publisher" },
            Content =
            [
                new CatalogContentItem
                {
                    Id = "map-1",
                    Name = "Desert Map",
                    ContentType = CoreContentType.Map,
                    TargetGame = GameType.ZeroHour,
                },
            ],
        };
        _catalogParser
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string payload, CancellationToken ct) => OperationResult<PublisherCatalog>.CreateSuccess(
                payload.Contains("CATALOG_MAPS", StringComparison.Ordinal) ? mapsCatalog : modsCatalog));

        _subscriptionStore
            .Setup(s => s.IsSubscribedAsync("multi-pub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        _subscriptionStore
            .Setup(s => s.GetSubscriptionAsync("multi-pub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherSubscription?>.CreateSuccess(null));
        PublisherSubscription? savedSubscription = null;
        _subscriptionStore
            .Setup(s => s.AddSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<PublisherSubscription, CancellationToken>((sub, ct) => savedSubscription = sub)
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var vm = new SubscriptionConfirmationViewModel(
            definitionUrl,
            _subscriptionStore.Object,
            _catalogParser.Object,
            httpClient,
            _logger.Object);
        bool? closeResult = null;
        vm.RequestClose = res => closeResult = res;

        // Act
        await vm.InitializeAsync();

        // Assert: both catalogs offered, first selected
        Assert.True(vm.IsCatalogLoaded);
        Assert.True(vm.IsDefinitionSubscription);
        Assert.Equal(2, vm.DefinitionCatalogOptions.Count);
        Assert.True(vm.ShowCatalogSelector);
        Assert.Equal("+ New Publisher • 2 catalogs", vm.NewSourceBadgeText);
        Assert.Equal(3, vm.ContentCount);

        // Act: switch to the second catalog
        vm.SelectDefinitionCatalog("maps");

        // Assert: content reflects the selected catalog
        Assert.Equal(1, vm.ContentCount);
        Assert.Equal("Desert Map", vm.FilteredContentItems[0].Name);

        // Act: confirm subscribes to the selected catalog
        await vm.ConfirmCommand.ExecuteAsync(null);

        // Assert
        Assert.True(closeResult);
        Assert.NotNull(savedSubscription);
        Assert.Equal("multi-pub", savedSubscription.PublisherId);
        Assert.Equal(mapsUrl, savedSubscription.CatalogUrl);
        Assert.Equal(definitionUrl, savedSubscription.DefinitionUrl);
        _subscriptionStore.Verify(s => s.AddSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that subscribing via a definition with a distinct publisher name/id preserves
    /// the definition's publisher identity rather than stomping it with the catalog's embedded author identity.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitializeAsync_DefinitionWithDifferentPublisherThanCatalog_PreservesDefinitionPublisherIdentityAsync()
    {
        // Arrange
        const string definitionUrl = "https://93.184.216.34/definition.json";
        const string catalogUrl = "https://93.184.216.34/catalog.json";
        var definitionJson = "{"\":1,"publisher":{"id":"my-custom-pub","name":"My Custom Publisher"},"catalogs":[{"id":"dominator","name":"Dominator Mappacks","url":"" + catalogUrl + ""}]}";

        using var httpClient = new HttpClient(new MappedFakeHttpMessageHandler(new Dictionary<string, string>
        {
            [definitionUrl] = definitionJson,
            [catalogUrl] = "DOMINATOR_CATALOG",
        }));

        var dominatorCatalog = CreateSampleCatalog("dominator", "Dominator Mappacks");
        _catalogParser
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(dominatorCatalog));

        _subscriptionStore
            .Setup(s => s.IsSubscribedAsync("my-custom-pub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        _subscriptionStore
            .Setup(s => s.GetSubscriptionAsync("my-custom-pub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherSubscription?>.CreateSuccess(null));

        PublisherSubscription? savedSubscription = null;
        _subscriptionStore
            .Setup(s => s.AddSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<PublisherSubscription, CancellationToken>((sub, ct) => savedSubscription = sub)
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var vm = new SubscriptionConfirmationViewModel(
            definitionUrl,
            _subscriptionStore.Object,
            _catalogParser.Object,
            httpClient,
            _logger.Object);

        // Act
        await vm.InitializeAsync();

        // Assert publisher name comes from definition, not catalog author
        Assert.Equal("My Custom Publisher", vm.PublisherName);
        Assert.Equal("M", vm.PublisherInitial);

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.NotNull(savedSubscription);
        Assert.Equal("my-custom-pub", savedSubscription.PublisherId);
        Assert.Equal("My Custom Publisher", savedSubscription.PublisherName);
    }

    private static PublisherCatalog CreateSampleCatalog(string id, string name)
    {
        return new PublisherCatalog
        {
            Publisher = new PublisherProfile
            {
                Id = id,
                Name = name,
                Website = "https://example.com",
                SupportUrl = "https://example.com/support",
                ContactEmail = "contact@example.com",
            },
            Content =
            [
                new CatalogContentItem
                {
                    Id = "client-1",
                    Name = "Game Client 1",
                    ContentType = CoreContentType.GameClient,
                    TargetGame = GameType.ZeroHour,
                },
                new CatalogContentItem
                {
                    Id = "client-2",
                    Name = "Game Client 2",
                    ContentType = CoreContentType.GameClient,
                    TargetGame = GameType.ZeroHour,
                },
                new CatalogContentItem
                {
                    Id = "mod-1",
                    Name = "ShockWave Mod",
                    ContentType = CoreContentType.Mod,
                    TargetGame = GameType.ZeroHour,
                },
            ],
        };
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            });
        }
    }

    private sealed class MappedFakeHttpMessageHandler(Dictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            responses.TryGetValue(uri, out var body);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body ?? "{}"),
            });
        }
    }
}
