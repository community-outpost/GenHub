using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.Publishers;

/// <summary>
/// Unit tests for <see cref="SuperHackersProvider"/>.
/// </summary>
public class SuperHackersProviderTests
{
    private readonly Mock<IContentDiscoverer> _discovererMock = new();
    private readonly Mock<IContentResolver> _resolverMock = new();
    private readonly Mock<IContentDeliverer> _delivererMock = new();
    private readonly Mock<IContentValidator> _validatorMock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SuperHackersProviderTests"/> class.
    /// </summary>
    public SuperHackersProviderTests()
    {
        _discovererMock.Setup(d => d.SourceName).Returns(PublisherTypeConstants.TheSuperHackers);
        _resolverMock.Setup(r => r.ResolverId).Returns(SuperHackersConstants.ResolverId);
        _delivererMock.Setup(d => d.SourceName).Returns(ContentSourceNames.GitHubDeliverer);
    }

    /// <summary>
    /// Verifies that the constructor resolves the SuperHackers discoverer, resolver, and deliverer.
    /// </summary>
    [Fact]
    public void Constructor_ResolvesSuperHackersComponents()
    {
        // Act
        var provider = CreateProvider();

        // Assert
        Assert.Equal(PublisherTypeConstants.TheSuperHackers, provider.SourceName);
        Assert.True(provider.IsEnabled);
    }

    /// <summary>
    /// Verifies that the constructor throws when the discoverer is missing.
    /// </summary>
    [Fact]
    public void Constructor_WhenDiscovererMissing_ThrowsInvalidOperationException()
    {
        // Act
        Action act = () => CreateProvider(discoverers: []);

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }

    /// <summary>
    /// Verifies that GetValidatedContentAsync resolves and validates the manifest.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetValidatedContentAsync_ResolvesAndValidatesManifestAsync()
    {
        // Arrange
        var manifest = new ContentManifest
        {
            Id = "1.0.thesuperhackers.gameclient.zerohour",
            Name = "SuperHackers ZH",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };

        _resolverMock.Setup(r => r.ResolveAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));
        _validatorMock.Setup(v => v.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", []));

        var provider = CreateProvider();

        // Act
        var result = await provider.GetValidatedContentAsync("content-id");

        // Assert
        Assert.True(result.Success);
        Assert.Same(manifest, result.Data);
    }

    /// <summary>
    /// Verifies that SearchAsync delegates discovery to the SuperHackers discoverer.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_DelegatesToDiscovererAsync()
    {
        // Arrange
        var card = new ContentSearchResult
        {
            Id = "card-1",
            Name = "Card",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            ProviderName = PublisherTypeConstants.TheSuperHackers,
        };

        _discovererMock.Setup(d => d.DiscoverAsync(
                It.IsAny<ProviderDefinition>(),
                It.IsAny<ContentSearchQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = [card],
                TotalItems = 1,
            }));

        var provider = CreateProvider();

        // Act
        var result = await provider.SearchAsync(new ContentSearchQuery());

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Data!);
        _discovererMock.Verify(
            d => d.DiscoverAsync(
                It.IsAny<ProviderDefinition>(),
                It.IsAny<ContentSearchQuery>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private SuperHackersProvider CreateProvider(List<IContentDiscoverer>? discoverers = null)
    {
        return new SuperHackersProvider(
            Mock.Of<IProviderDefinitionLoader>(),
            discoverers ?? [_discovererMock.Object],
            [_resolverMock.Object],
            [_delivererMock.Object],
            _validatorMock.Object,
            NullLogger<SuperHackersProvider>.Instance,
            Mock.Of<IInstallationInstructionsService>());
    }
}
