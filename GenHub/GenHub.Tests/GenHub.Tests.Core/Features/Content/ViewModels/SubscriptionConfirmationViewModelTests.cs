using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.ViewModels.Catalog;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using CoreContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.ViewModels;

/// <summary>
/// Unit tests for <see cref="SubscriptionConfirmationViewModel"/>.
/// </summary>
public sealed class SubscriptionConfirmationViewModelTests
{
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
    /// Verifies that confirming an already subscribed publisher calls UpdateSubscriptionAsync.
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

        _subscriptionStore
            .Setup(s => s.GetSubscriptionAsync("existing-pub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherSubscription?>.CreateSuccess(new PublisherSubscription
            {
                PublisherId = "existing-pub",
                PublisherName = "Existing Publisher",
                CatalogUrl = "https://example.com/old-catalog.json",
                TrustLevel = TrustLevel.Trusted,
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
        _subscriptionStore.Verify(s => s.UpdateSubscriptionAsync(It.Is<PublisherSubscription>(sub => sub.PublisherId == "existing-pub" && sub.TrustLevel == TrustLevel.Trusted), It.IsAny<CancellationToken>()), Times.Once);
        _subscriptionStore.Verify(s => s.AddSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()), Times.Never);
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
}
