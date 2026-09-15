using System;
using System.Collections.Generic;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.Publishers;

/// <summary>
/// Unit tests for <see cref="PublisherManifestFactoryResolver"/>.
/// </summary>
public class PublisherManifestFactoryResolverTests
{
    private readonly Mock<IFileHashProvider> _hashProviderMock;
    private readonly Mock<IArchivePayloadProcessor> _archiveProcessorMock;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherManifestFactoryResolverTests"/> class.
    /// </summary>
    public PublisherManifestFactoryResolverTests()
    {
        _hashProviderMock = new Mock<IFileHashProvider>();
        _archiveProcessorMock = new Mock<IArchivePayloadProcessor>();
    }

    /// <summary>
    /// Verifies that ResolveFactory returns the specialized factory when CanHandle matches.
    /// </summary>
    [Fact]
    public void ResolveFactory_ReturnsSpecializedFactory_WhenCanHandleMatches()
    {
        // Arrange
        var superHackersFactory = new SuperHackersManifestFactory(
            NullLogger<SuperHackersManifestFactory>.Instance,
            _hashProviderMock.Object);

        var gitHubFactory = new GitHubManifestFactory(
            NullLogger<GitHubManifestFactory>.Instance,
            _hashProviderMock.Object,
            _archiveProcessorMock.Object);

        var resolver = new PublisherManifestFactoryResolver(
            [superHackersFactory, gitHubFactory],
            NullLogger<PublisherManifestFactoryResolver>.Instance);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.thesuperhackers.gameclient.generals"),
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo
            {
                Name = "TheSuperHackers",
                PublisherType = PublisherTypeConstants.TheSuperHackers,
            },
        };

        // Act
        var result = resolver.ResolveFactory(manifest);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<SuperHackersManifestFactory>(result);
    }

    /// <summary>
    /// Verifies that ResolveFactory falls back to GitHubManifestFactory for non-GameClient publisher content.
    /// </summary>
    [Fact]
    public void ResolveFactory_FallsBackToGitHubFactory_WhenSpecializedFactoryCannotHandle()
    {
        // Arrange
        var superHackersFactory = new SuperHackersManifestFactory(
            NullLogger<SuperHackersManifestFactory>.Instance,
            _hashProviderMock.Object);

        var gitHubFactory = new GitHubManifestFactory(
            NullLogger<GitHubManifestFactory>.Instance,
            _hashProviderMock.Object,
            _archiveProcessorMock.Object);

        var resolver = new PublisherManifestFactoryResolver(
            [superHackersFactory, gitHubFactory],
            NullLogger<PublisherManifestFactoryResolver>.Instance);

        var patchManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.thesuperhackers.patch.generalsgamepatch2"),
            ContentType = ContentType.Patch,
            Publisher = new PublisherInfo
            {
                Name = "TheSuperHackers",
                PublisherType = PublisherTypeConstants.TheSuperHackers,
            },
        };

        // Act
        var result = resolver.ResolveFactory(patchManifest);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<GitHubManifestFactory>(result);
    }

    /// <summary>
    /// Verifies that ResolveFactory returns null when no specialized or fallback factory is available.
    /// </summary>
    [Fact]
    public void ResolveFactory_ReturnsNull_WhenNoFactoryMatchesAndNoFallbackAvailable()
    {
        // Arrange
        var superHackersFactory = new SuperHackersManifestFactory(
            NullLogger<SuperHackersManifestFactory>.Instance,
            _hashProviderMock.Object);

        var resolver = new PublisherManifestFactoryResolver(
            [superHackersFactory],
            NullLogger<PublisherManifestFactoryResolver>.Instance);

        var patchManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.testpublisher.mod.sample"),
            ContentType = ContentType.Mod,
            Publisher = new PublisherInfo
            {
                Name = "Unknown",
                PublisherType = "unknown",
            },
        };

        // Act
        var result = resolver.ResolveFactory(patchManifest);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that ResolveFactory returns null when a GameClient manifest has no specialized factory,
    /// rather than falling back to GitHubManifestFactory.
    /// </summary>
    [Fact]
    public void ResolveFactory_ReturnsNull_WhenGameClientHasNoSpecializedFactory()
    {
        // Arrange
        var gitHubFactory = new GitHubManifestFactory(
            NullLogger<GitHubManifestFactory>.Instance,
            _hashProviderMock.Object,
            _archiveProcessorMock.Object);

        var resolver = new PublisherManifestFactoryResolver(
            [gitHubFactory],
            NullLogger<PublisherManifestFactoryResolver>.Instance);

        var gameClientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.unknownpublisher.gameclient.generals"),
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo
            {
                Name = "UnknownPublisher",
                PublisherType = "unknownpublisher",
            },
        };

        // Act
        var result = resolver.ResolveFactory(gameClientManifest);

        // Assert
        Assert.Null(result);
    }
}
