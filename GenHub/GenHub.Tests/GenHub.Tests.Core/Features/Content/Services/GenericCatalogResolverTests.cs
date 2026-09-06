using System.Text.Json;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Content.Services.ContentDeliverers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Tests for generic catalog resolve/deliver paths that previously failed on ContentBundles.
/// </summary>
public sealed class GenericCatalogResolverTests
{
    /// <summary>
    /// Dependency-only ContentBundle releases (empty artifacts) must resolve without throwing.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_DependencyOnlyBundle_SucceedsWithoutRemoteFilesAsync()
    {
        var contentItem = new CatalogContentItem
        {
            Id = "bundle-a",
            Name = "Bundle A",
            ContentType = ContentType.ContentBundle,
            TargetGame = GameType.ZeroHour,
            Description = "Meta package",
            Tags = ["bundle"],
        };

        var release = new ContentRelease
        {
            Version = "1.0.0",
            Artifacts = [],
            Dependencies =
            [
                new CatalogDependency
                {
                    PublisherId = "test-pub",
                    ContentId = "client-a",
                    VersionConstraint = ">=1.0",
                },
            ],
        };

        var publisher = new PublisherProfile { Id = "test-pub", Name = "Test Pub" };

        var searchResult = new ContentSearchResult
        {
            Id = "1.0.testpub.contentbundle.bundlea",
            Name = contentItem.Name,
            ContentType = ContentType.ContentBundle,
            ResolverId = CatalogConstants.GenericCatalogResolverId,
            ResolverMetadata =
            {
                [CatalogConstants.ReleaseJsonMetadataKey] = JsonSerializer.Serialize(release),
                [CatalogConstants.CatalogItemJsonMetadataKey] = JsonSerializer.Serialize(contentItem),
                [CatalogConstants.PublisherProfileJsonMetadataKey] = JsonSerializer.Serialize(publisher),
            },
        };

        var builderMock = new Mock<IContentManifestBuilder>();
        var builtManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.testpub.contentbundle.bundlea"),
            Name = "Bundle A",
            Version = "1.0.0",
            ContentType = ContentType.ContentBundle,
            Files = [],
            Metadata = new ContentMetadata(),
            Publisher = new PublisherInfo { PublisherType = CatalogConstants.GenericCatalogResolverId },
        };

        builderMock.Setup(b => b.WithBasicInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.WithContentType(It.IsAny<ContentType>(), It.IsAny<GameType>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.WithName(It.IsAny<string>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.WithId(It.IsAny<ManifestId>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.WithPublisher(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.WithMetadata(
                It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<string>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.AddDependency(
                It.IsAny<ManifestId>(),
                It.IsAny<string>(),
                It.IsAny<ContentType>(),
                It.IsAny<DependencyInstallBehavior>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<List<ManifestId>?>(),
                It.IsAny<List<GameType>?>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.Build()).Returns(builtManifest);

        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            () => builderMock.Object);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!.Files);
        builderMock.Verify(
            b => b.AddRemoteFileAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ContentSourceType>(), It.IsAny<bool>(), It.IsAny<FilePermissions?>()),
            Times.Never);
    }

    /// <summary>
    /// HttpContentDeliverer must accept dependency-only manifests with no files.
    /// </summary>
    [Fact]
    public void HttpContentDeliverer_CanDeliver_EmptyFiles_ReturnsTrue()
    {
        var deliverer = new HttpContentDeliverer(
            Mock.Of<GenHub.Core.Interfaces.Common.IDownloadService>(),
            Mock.Of<GenHub.Core.Interfaces.Tools.IPlaywrightService>(),
            () => Mock.Of<IContentManifestBuilder>(),
            Mock.Of<GenHub.Core.Interfaces.Common.IFileHashProvider>(),
            NullLogger<HttpContentDeliverer>.Instance);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.testpub.contentbundle.bundlea"),
            Name = "Bundle A",
            ContentType = ContentType.ContentBundle,
            Files = [],
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create("1.0.testpub.mod.clienta"),
                    Name = "client-a",
                    DependencyType = ContentType.Mod,
                },
            ],
        };

        Assert.True(deliverer.CanDeliver(manifest));
    }

    /// <summary>
    /// A GameClient catalog item that declares a dependency on its base game (publisher "ea",
    /// contentId "zerohour") must resolve to the canonical semantic GameInstallation dependency
    /// (1.104.any.gameinstallation.zerohour), not a literal "1.104.ea.mod.zerohour" ID that no
    /// manifest pool can satisfy. This is the regression behind the profile-creation failure.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ResolveAsync_GameClientWithBaseGameDependency_EmitsCanonicalGameInstallationDependencyAsync()
    {
        var contentItem = new CatalogContentItem
        {
            Id = "zerohour",
            Name = "TheSuperHackers Zero Hour Game Code",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Description = "Weekly game client",
            Tags = ["game-client"],
        };

        var release = new ContentRelease
        {
            Version = "weekly-2026-07-31",
            Artifacts =
            [
                new ReleaseArtifact
                {
                    Filename = "generalszh.zip",
                    DownloadUrl = "https://example.com/generalszh.zip",
                    ContentType = "application/zip",
                    IsPrimary = true,
                },
            ],
            Dependencies =
            [
                new CatalogDependency
                {
                    PublisherId = "ea",
                    ContentId = "zerohour",
                    VersionConstraint = "1.04",
                    IsOptional = false,
                },
            ],
        };

        var publisher = new PublisherProfile { Id = "genhub-test-publishers", Name = "GenHub Test Publishers" };

        var searchResult = new ContentSearchResult
        {
            Id = "1.0.genhubtestpublishers.gameclient.thesuperhackerszerohourgamecode",
            Name = contentItem.Name,
            ContentType = ContentType.GameClient,
            ResolverId = CatalogConstants.GenericCatalogResolverId,
            ResolverMetadata =
            {
                [CatalogConstants.ReleaseJsonMetadataKey] = JsonSerializer.Serialize(release),
                [CatalogConstants.CatalogItemJsonMetadataKey] = JsonSerializer.Serialize(contentItem),
                [CatalogConstants.PublisherProfileJsonMetadataKey] = JsonSerializer.Serialize(publisher),
            },
        };

        var builderMock = new Mock<IContentManifestBuilder>();
        var builtManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.genhubtestpublishers.gameclient.thesuperhackerszerohourgamecode"),
            Name = contentItem.Name,
            Version = release.Version,
            ContentType = ContentType.GameClient,
            Files = [],
            Metadata = new ContentMetadata(),
            Publisher = new PublisherInfo { PublisherType = CatalogConstants.GenericCatalogResolverId },
        };

        builderMock.Setup(b => b.WithBasicInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.WithContentType(It.IsAny<ContentType>(), It.IsAny<GameType>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.WithName(It.IsAny<string>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.WithId(It.IsAny<ManifestId>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.WithPublisher(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.WithMetadata(
                It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<string>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.AddRemoteFileAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ContentSourceType>(), It.IsAny<bool>(), It.IsAny<FilePermissions?>()))
            .ReturnsAsync(builderMock.Object);
        builderMock.Setup(b => b.AddDependency(
                It.IsAny<ManifestId>(),
                It.IsAny<string>(),
                It.IsAny<ContentType>(),
                It.IsAny<DependencyInstallBehavior>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<List<ManifestId>?>(),
                It.IsAny<List<GameType>?>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.Build()).Returns(builtManifest);

        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            () => builderMock.Object);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.True(result.Success, result.FirstError);

        builderMock.Verify(
            b => b.AddDependency(
                It.Is<ManifestId>(id => id.Value == ManifestConstants.ZeroHourFoundationDependencyId),
                It.IsAny<string>(),
                It.Is<ContentType>(t => t == ContentType.GameInstallation),
                It.Is<DependencyInstallBehavior>(b => b == DependencyInstallBehavior.RequireExisting),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<List<ManifestId>?>(),
                It.IsAny<List<GameType>?>()),
            Times.Once);

        // The literal "1.104.ea.mod.zerohour" ID that previously caused the failure must never be emitted.
        builderMock.Verify(
            b => b.AddDependency(
                It.Is<ManifestId>(id => id.Value.Contains("ea.mod.zerohour", StringComparison.Ordinal)),
                It.IsAny<string>(),
                It.IsAny<ContentType>(),
                It.IsAny<DependencyInstallBehavior>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<List<ManifestId>?>(),
                It.IsAny<List<GameType>?>()),
            Times.Never);
    }

    /// <summary>
    /// A ContentBundle must emit the canonical Zero Hour foundation ID and keep sibling GameClient
    /// dependencies as GameClient (not Mod). This is the identity mismatch behind
    /// <c>1.104.ea.mod.zerohour</c> profile-creation failures.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_ContentBundle_EmitsFoundationAndSiblingGameClientIdsAsync()
    {
        var contentItem = new CatalogContentItem
        {
            Id = "bundle-ultimate-zh-community-stack",
            Name = "Ultimate ZH Community Stack",
            ContentType = ContentType.ContentBundle,
            TargetGame = GameType.ZeroHour,
            Description = "Stack",
            Tags = ["bundle"],
        };

        var expectedClientId = CatalogManifestIdentity.CreateContentId(
            "genhub-test-publishers",
            ContentType.GameClient,
            "zerohour",
            ">=weekly-2026-07-31");

        var release = new ContentRelease
        {
            Version = "2026.07.31",
            Artifacts = [],
            Dependencies =
            [
                new CatalogDependency
                {
                    PublisherId = "ea",
                    ContentId = "zerohour",
                    VersionConstraint = "1.04",
                    ContentType = "GameInstallation",
                },
                new CatalogDependency
                {
                    PublisherId = "genhub-test-publishers",
                    ContentId = "zerohour",
                    VersionConstraint = ">=weekly-2026-07-31",
                    ContentType = "GameClient",
                },
            ],
        };

        var publisher = new PublisherProfile { Id = "genhub-test-publishers", Name = "GenHub Test Publishers" };
        var searchResultId = CatalogManifestIdentity.CreateContentId(
            publisher.Id,
            ContentType.ContentBundle,
            contentItem.Id,
            release.Version);

        var searchResult = new ContentSearchResult
        {
            Id = searchResultId,
            Name = contentItem.Name,
            ContentType = ContentType.ContentBundle,
            ResolverId = CatalogConstants.GenericCatalogResolverId,
            ResolverMetadata =
            {
                [CatalogConstants.ReleaseJsonMetadataKey] = JsonSerializer.Serialize(release),
                [CatalogConstants.CatalogItemJsonMetadataKey] = JsonSerializer.Serialize(contentItem),
                [CatalogConstants.PublisherProfileJsonMetadataKey] = JsonSerializer.Serialize(publisher),
            },
        };

        var builtManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.wrongpublisher.contentbundle.wrongname"),
            Name = "wrong",
            Version = release.Version,
            ContentType = ContentType.ContentBundle,
            Files = [],
            Metadata = new ContentMetadata(),
            Publisher = new PublisherInfo { PublisherType = CatalogConstants.GenericCatalogResolverId },
        };

        var builderMock = CreateBuilderMock(builtManifest);
        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            () => builderMock.Object);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(searchResultId, result.Data!.Id.Value);
        Assert.Equal(contentItem.Name, result.Data.Name);

        builderMock.Verify(
            b => b.AddDependency(
                It.Is<ManifestId>(id => id.Value == ManifestConstants.ZeroHourFoundationDependencyId),
                It.IsAny<string>(),
                It.Is<ContentType>(t => t == ContentType.GameInstallation),
                It.Is<DependencyInstallBehavior>(behavior => behavior == DependencyInstallBehavior.RequireExisting),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<List<ManifestId>?>(),
                It.IsAny<List<GameType>?>()),
            Times.Once);

        builderMock.Verify(
            b => b.AddDependency(
                It.Is<ManifestId>(id => id.Value == expectedClientId),
                It.IsAny<string>(),
                It.Is<ContentType>(t => t == ContentType.GameClient),
                It.Is<DependencyInstallBehavior>(behavior => behavior == DependencyInstallBehavior.AutoInstall),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<List<ManifestId>?>(),
                It.IsAny<List<GameType>?>()),
            Times.Once);

        builderMock.Verify(
            b => b.AddDependency(
                It.Is<ManifestId>(id => id.Value.Contains(".mod.", StringComparison.Ordinal)),
                It.IsAny<string>(),
                It.IsAny<ContentType>(),
                It.IsAny<DependencyInstallBehavior>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<List<ManifestId>?>(),
                It.IsAny<List<GameType>?>()),
            Times.Never);
    }

    /// <summary>
    /// Consecutive calls to ResolveAsync must invoke manifestBuilderFactory to obtain a fresh builder,
    /// preventing state from leaking across resolution calls.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_UsesFreshBuilderPerInvocation_DoesNotAccumulateStateAsync()
    {
        var contentItem = new CatalogContentItem
        {
            Id = "item-1",
            Name = "Item One",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Description = "Test item",
            Tags = ["mod"],
        };

        var release = new ContentRelease
        {
            Version = "1.0.0",
            Artifacts = [],
            Dependencies = [],
        };

        var publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" };
        var searchResult = new ContentSearchResult
        {
            Id = "1.0.testpub.mod.item1",
            Name = contentItem.Name,
            ContentType = ContentType.Mod,
            ResolverId = CatalogConstants.GenericCatalogResolverId,
            ResolverMetadata =
            {
                [CatalogConstants.ReleaseJsonMetadataKey] = JsonSerializer.Serialize(release),
                [CatalogConstants.CatalogItemJsonMetadataKey] = JsonSerializer.Serialize(contentItem),
                [CatalogConstants.PublisherProfileJsonMetadataKey] = JsonSerializer.Serialize(publisher),
            },
        };

        var factoryCallCount = 0;
        IContentManifestBuilder Factory()
        {
            factoryCallCount++;
            return CreateBuilderMock(new ContentManifest
            {
                Id = ManifestId.Create("1.0.testpub.mod.item1"),
                Name = contentItem.Name,
                Version = release.Version,
                ContentType = ContentType.Mod,
                Files = [],
                Metadata = new ContentMetadata(),
                Publisher = new PublisherInfo { PublisherType = CatalogConstants.GenericCatalogResolverId },
            }).Object;
        }

        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            Factory);

        var result1 = await resolver.ResolveAsync(searchResult);
        var result2 = await resolver.ResolveAsync(searchResult);

        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(2, factoryCallCount);
    }

    /// <summary>
    /// Verifies that resolving a variant catalog item preserves the variant-specific ManifestId, Name, and tags.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_VariantArtifact_PreservesVariantIdAndNameAsync()
    {
        var contentItem = new CatalogContentItem
        {
            Id = "lemon-controlbar",
            Name = "Control Bar Pro Lemon Edition ZH",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            PublisherType = "github",
            Tags = ["addon", "controlbar"],
        };

        var release = new ContentRelease
        {
            Version = "1.3",
            Artifacts =
            [
                new ReleaseArtifact
                {
                    Filename = "LemonControlBar1080p.zip",
                    DownloadUrl = "https://example.com/cb1080.zip",
                    Variant = "1080p",
                    VariantAxis = "resolution",
                    IsPrimary = true,
                },
            ],
        };

        var publisher = new PublisherProfile { Id = "github", Name = "GitHub" };

        var searchResult = new ContentSearchResult
        {
            Id = "1.103.github.addon.lemoncontrolbar1080p",
            Name = "Control Bar Pro Lemon Edition ZH (1080p)",
            ContentType = ContentType.Addon,
            ResolverId = CatalogConstants.GenericCatalogResolverId,
            ResolverMetadata =
            {
                [CatalogConstants.ReleaseJsonMetadataKey] = JsonSerializer.Serialize(release),
                [CatalogConstants.CatalogItemJsonMetadataKey] = JsonSerializer.Serialize(contentItem),
                [CatalogConstants.PublisherProfileJsonMetadataKey] = JsonSerializer.Serialize(publisher),
            },
        };

        var builtManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.103.github.addon.lemoncontrolbar1080p"),
            Name = "Control Bar Pro Lemon Edition ZH (1080p)",
            Version = "1.3",
            ContentType = ContentType.Addon,
            Files = [new ManifestFile { RelativePath = "LemonControlBar1080p.zip" }],
            Metadata = new ContentMetadata { Tags = ["addon", "controlbar"] },
            Publisher = new PublisherInfo { PublisherType = "github" },
        };

        var builderMock = CreateBuilderMock(builtManifest);
        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            () => builderMock.Object);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.True(result.Success);
        var manifest = result.Data!;
        Assert.Equal("1.103.github.addon.lemoncontrolbar1080p", manifest.Id.Value);
        Assert.Equal("Control Bar Pro Lemon Edition ZH (1080p)", manifest.Name);
        Assert.Contains("variant:1080p", manifest.Metadata.Tags);
    }

    private static Mock<IContentManifestBuilder> CreateBuilderMock(ContentManifest builtManifest)
    {
        var builderMock = new Mock<IContentManifestBuilder>();
        builderMock.Setup(b => b.WithBasicInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.WithContentType(It.IsAny<ContentType>(), It.IsAny<GameType>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.WithName(It.IsAny<string>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.WithId(It.IsAny<ManifestId>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.WithPublisher(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.WithMetadata(
                It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<string>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.AddRemoteFileAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ContentSourceType>(), It.IsAny<bool>(), It.IsAny<FilePermissions?>()))
            .ReturnsAsync(builderMock.Object);
        builderMock.Setup(b => b.AddDependency(
                It.IsAny<ManifestId>(),
                It.IsAny<string>(),
                It.IsAny<ContentType>(),
                It.IsAny<DependencyInstallBehavior>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<List<ManifestId>?>(),
                It.IsAny<List<GameType>?>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.Build()).Returns(builtManifest);
        return builderMock;
    }
}
