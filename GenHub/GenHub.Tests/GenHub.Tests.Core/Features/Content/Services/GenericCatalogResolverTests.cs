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

        var builderMock = CreateBuilderMock(builtManifest);

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

        var builderMock = CreateBuilderMock(builtManifest);

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
                It.IsAny<List<GameType>?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()),
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
                It.IsAny<List<GameType>?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Base-game dependency constraints with ranged tokens should parse min and max bounds and set inclusivity flags.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_BaseGameDependency_WithRangeConstraint_EmitsBoundsAndInclusivityAsync()
    {
        var contentItem = new CatalogContentItem
        {
            Id = "mod-with-zh-bound",
            Name = "Mod With ZH Bound",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Description = "Test Mod",
            Tags = ["mod"],
        };

        var release = new ContentRelease
        {
            Version = "1.0.0",
            Dependencies =
            [
                new CatalogDependency
                {
                    PublisherId = "ea",
                    ContentId = "zerohour",
                    VersionConstraint = ">=1.04 <2.0",
                },
            ],
        };

        var publisher = new PublisherProfile { Id = "test-pub", Name = "Test Pub" };

        var searchResult = new ContentSearchResult
        {
            Id = "1.100.testpub.mod.modwithzhbound",
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

        var builtManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.100.testpub.mod.modwithzhbound"),
            Name = "Mod With ZH Bound",
            Version = "1.0.0",
            ContentType = ContentType.Mod,
            Files = [],
            Metadata = new ContentMetadata(),
            Publisher = new PublisherInfo { PublisherType = "test-pub" },
        };

        var builderMock = CreateBuilderMock(builtManifest);
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
                It.IsAny<List<GameType>?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()))
            .Callback<ManifestId, string, ContentType, DependencyInstallBehavior, string, string, List<string>?, bool, List<ManifestId>?, List<GameType>?, bool, bool>((id, name, type, behavior, min, max, comp, excl, conf, games, minInc, maxInc) =>
                builtManifest.Dependencies.Add(new ContentDependency
                {
                    Id = id,
                    Name = name,
                    DependencyType = type,
                    MinVersion = min,
                    MaxVersion = max,
                    CompatibleVersions = comp ?? [],
                    MinInclusive = minInc,
                    MaxInclusive = maxInc,
                }))
            .Returns(builderMock.Object);

        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            () => builderMock.Object);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.True(result.Success, result.FirstError);
        Assert.Single(builtManifest.Dependencies);
        Assert.Equal("1.04", builtManifest.Dependencies[0].MinVersion);
        Assert.Equal("2.0", builtManifest.Dependencies[0].MaxVersion);
        Assert.True(builtManifest.Dependencies[0].MinInclusive);
        Assert.False(builtManifest.Dependencies[0].MaxInclusive);
    }

    /// <summary>
    /// Verifies that an upper-bound-only constraint preserves the base game foundation min version floor.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_BaseGameDependency_WithMaxOnlyConstraint_PreservesFoundationMinFloorAsync()
    {
        var contentItem = new CatalogContentItem
        {
            Id = "mod-with-zh-max-only",
            Name = "Mod With ZH Max Only",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Description = "Test Mod",
            Tags = ["mod"],
        };

        var release = new ContentRelease
        {
            Version = "1.0.0",
            Dependencies =
            [
                new CatalogDependency
                {
                    PublisherId = "ea",
                    ContentId = "zerohour",
                    VersionConstraint = "<2.0",
                },
            ],
        };

        var publisher = new PublisherProfile { Id = "test-pub", Name = "Test Pub" };

        var searchResult = new ContentSearchResult
        {
            Id = "1.100.testpub.mod.modwithzhmaxonly",
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

        var builtManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.100.testpub.mod.modwithzhmaxonly"),
            Name = "Mod With ZH Max Only",
            Version = "1.0.0",
            ContentType = ContentType.Mod,
            Files = [],
            Metadata = new ContentMetadata(),
            Publisher = new PublisherInfo { PublisherType = "test-pub" },
        };

        var builderMock = CreateBuilderMock(builtManifest);
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
                It.IsAny<List<GameType>?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()))
            .Callback<ManifestId, string, ContentType, DependencyInstallBehavior, string, string, List<string>?, bool, List<ManifestId>?, List<GameType>?, bool, bool>((id, name, type, behavior, min, max, comp, excl, conf, games, minInc, maxInc) =>
                builtManifest.Dependencies.Add(new ContentDependency
                {
                    Id = id,
                    Name = name,
                    DependencyType = type,
                    MinVersion = min,
                    MaxVersion = max,
                    CompatibleVersions = comp ?? [],
                    MinInclusive = minInc,
                    MaxInclusive = maxInc,
                }))
            .Returns(builderMock.Object);

        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            () => builderMock.Object);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.True(result.Success, result.FirstError);
        Assert.Single(builtManifest.Dependencies);
        var dep = builtManifest.Dependencies[0];
        Assert.Equal("1.04", dep.MinVersion);
        Assert.Equal("2.0", dep.MaxVersion);
        Assert.True(dep.MinInclusive);
        Assert.False(dep.MaxInclusive);
    }

    /// <summary>
    /// Verifies that when a base-game constraint max falls below the foundation floor, resolution fails with an unsatisfiable bounds error.
    /// </summary>
    /// <param name="constraint">The version constraint to test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(">=1.02 <1.03")]
    [InlineData("1.03")]
    [InlineData("<1.04")]
    public async Task ResolveAsync_BaseGameDependency_WithMaxBelowOrAtFloorExclusive_FailsResolutionAsync(string constraint)
    {
        var contentItem = new CatalogContentItem
        {
            Id = "mod-with-invalid-zh-range",
            Name = "Mod With Invalid ZH Range",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Description = "Test Mod",
            Tags = ["mod"],
        };

        var release = new ContentRelease
        {
            Version = "1.0.0",
            Dependencies =
            [
                new CatalogDependency
                {
                    PublisherId = "ea",
                    ContentId = "zerohour",
                    VersionConstraint = constraint,
                },
            ],
        };

        var publisher = new PublisherProfile { Id = "test-pub", Name = "Test Pub" };

        var searchResult = new ContentSearchResult
        {
            Id = "1.100.testpub.mod.modwithinvalidzhrange",
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

        var builtManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.100.testpub.mod.modwithinvalidzhrange"),
            Name = "Mod With Invalid ZH Range",
            Version = "1.0.0",
            ContentType = ContentType.Mod,
            Files = [],
            Metadata = new ContentMetadata(),
            Publisher = new PublisherInfo { PublisherType = "test-pub" },
        };

        var builderMock = CreateBuilderMock(builtManifest);
        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            () => builderMock.Object);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.False(result.Success);
        Assert.Contains("unsatisfiable version bounds", result.FirstError);
    }

    /// <summary>
    /// Verifies that base-game dependencies with compatible version lists entirely below the foundation floor fail resolution.
    /// </summary>
    /// <param name="constraint">The version constraint to test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("1.02,1.03")]
    [InlineData("1.02|1.03")]
    public async Task ResolveAsync_BaseGameDependency_WithCompatibleVersionsBelowFloor_FailsResolutionAsync(string constraint)
    {
        var contentItem = new CatalogContentItem
        {
            Id = "mod-with-below-floor-compatible-versions",
            Name = "Mod With Below Floor Compatible Versions",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Description = "Test Mod",
            Tags = ["mod"],
        };

        var release = new ContentRelease
        {
            Version = "1.0.0",
            Dependencies =
            [
                new CatalogDependency
                {
                    PublisherId = "ea",
                    ContentId = "zerohour",
                    VersionConstraint = constraint,
                },
            ],
        };

        var publisher = new PublisherProfile { Id = "test-pub", Name = "Test Pub" };

        var searchResult = new ContentSearchResult
        {
            Id = "1.100.testpub.mod.modwithbelowfloorcompatibleversions",
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

        var builtManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.100.testpub.mod.modwithbelowfloorcompatibleversions"),
            Name = "Mod With Below Floor Compatible Versions",
            Version = "1.0.0",
            ContentType = ContentType.Mod,
            Files = [],
            Metadata = new ContentMetadata(),
            Publisher = new PublisherInfo { PublisherType = "test-pub" },
        };

        var builderMock = CreateBuilderMock(builtManifest);
        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            () => builderMock.Object);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.False(result.Success);
        Assert.Contains("unsatisfiable version bounds after reconciliation: all compatible versions are below the minimum foundation floor '1.04'", result.FirstError);
    }

    /// <summary>
    /// Verifies that mixed compatible version lists have below-floor versions filtered out when reconciled against foundation floor.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_BaseGameDependency_WithMixedCompatibleVersions_FiltersBelowFloorVersionsAsync()
    {
        var contentItem = new CatalogContentItem
        {
            Id = "mod-with-mixed-compatible-versions",
            Name = "Mod With Mixed Compatible Versions",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Description = "Test Mod",
            Tags = ["mod"],
        };

        var release = new ContentRelease
        {
            Version = "1.0.0",
            Dependencies =
            [
                new CatalogDependency
                {
                    PublisherId = "ea",
                    ContentId = "zerohour",
                    VersionConstraint = "1.02,1.04,1.05",
                },
            ],
        };

        var publisher = new PublisherProfile { Id = "test-pub", Name = "Test Pub" };

        var searchResult = new ContentSearchResult
        {
            Id = "1.100.testpub.mod.modwithmixedcompatibleversions",
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

        var builtManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.100.testpub.mod.modwithmixedcompatibleversions"),
            Name = "Mod With Mixed Compatible Versions",
            Version = "1.0.0",
            ContentType = ContentType.Mod,
            Files = [],
            Metadata = new ContentMetadata(),
            Publisher = new PublisherInfo { PublisherType = "test-pub" },
        };

        var builderMock = CreateBuilderMock(builtManifest);
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
                It.IsAny<List<GameType>?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()))
            .Callback<ManifestId, string, ContentType, DependencyInstallBehavior, string, string, List<string>?, bool, List<ManifestId>?, List<GameType>?, bool, bool>((id, name, type, behavior, min, max, comp, excl, conf, games, minInc, maxInc) =>
                builtManifest.Dependencies.Add(new ContentDependency
                {
                    Id = id,
                    Name = name,
                    DependencyType = type,
                    MinVersion = min,
                    MaxVersion = max,
                    CompatibleVersions = comp ?? [],
                    MinInclusive = minInc,
                    MaxInclusive = maxInc,
                }))
            .Returns(builderMock.Object);

        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            () => builderMock.Object);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.True(result.Success, result.FirstError);
        Assert.Single(builtManifest.Dependencies);
        Assert.Equal(["1.04", "1.05"], builtManifest.Dependencies[0].CompatibleVersions);
    }

    /// <summary>
    /// Verifies that base-game dependencies with self-contradictory bounds where the floor was not applied do not include the reconciliation suffix.
    /// </summary>
    /// <param name="constraint">The version constraint to test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(">=1.06 <1.05")]
    public async Task ResolveAsync_BaseGameDependency_WithSelfContradictoryBoundsAboveFloor_ReportsWithoutReconciliationSuffixAsync(string constraint)
    {
        var contentItem = new CatalogContentItem
        {
            Id = "mod-with-invalid-above-floor-range",
            Name = "Mod With Invalid Above Floor Range",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Description = "Test Mod",
            Tags = ["mod"],
        };

        var release = new ContentRelease
        {
            Version = "1.0.0",
            Dependencies =
            [
                new CatalogDependency
                {
                    PublisherId = "ea",
                    ContentId = "zerohour",
                    VersionConstraint = constraint,
                },
            ],
        };

        var publisher = new PublisherProfile { Id = "test-pub", Name = "Test Pub" };

        var searchResult = new ContentSearchResult
        {
            Id = "1.100.testpub.mod.modwithinvalidabovefloorrange",
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

        var builtManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.100.testpub.mod.modwithinvalidabovefloorrange"),
            Name = "Mod With Invalid Above Floor Range",
            Version = "1.0.0",
            ContentType = ContentType.Mod,
            Files = [],
            Metadata = new ContentMetadata(),
            Publisher = new PublisherInfo { PublisherType = "test-pub" },
        };

        var builderMock = CreateBuilderMock(builtManifest);
        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            () => builderMock.Object);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.False(result.Success);
        Assert.Contains("unsatisfiable version bounds: min '1.06' > max '1.05'", result.FirstError);
        Assert.DoesNotContain("after reconciliation", result.FirstError);
    }

    /// <summary>
    /// Verifies that when a non-base-game catalog dependency has unsatisfiable version bounds, resolution fails with an unsatisfiable bounds error.
    /// </summary>
    /// <param name="constraint">The version constraint to test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(">=2.0 <=1.0")]
    [InlineData(">1.5 <1.5")]
    public async Task ResolveAsync_CatalogDependency_WithUnsatisfiableBounds_FailsResolutionAsync(string constraint)
    {
        var contentItem = new CatalogContentItem
        {
            Id = "mod-with-invalid-catalog-dep",
            Name = "Mod With Invalid Catalog Dep",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Description = "Test Mod",
            Tags = ["mod"],
        };

        var release = new ContentRelease
        {
            Version = "1.0.0",
            Dependencies =
            [
                new CatalogDependency
                {
                    PublisherId = "other-pub",
                    ContentId = "other-mod",
                    VersionConstraint = constraint,
                },
            ],
        };

        var publisher = new PublisherProfile { Id = "test-pub", Name = "Test Pub" };

        var searchResult = new ContentSearchResult
        {
            Id = "1.100.testpub.mod.modwithinvalidcatalogdep",
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

        var builtManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.100.testpub.mod.modwithinvalidcatalogdep"),
            Name = "Mod With Invalid Catalog Dep",
            Version = "1.0.0",
            ContentType = ContentType.Mod,
            Files = [],
            Metadata = new ContentMetadata(),
            Publisher = new PublisherInfo { PublisherType = "test-pub" },
        };

        var builderMock = CreateBuilderMock(builtManifest);
        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            () => builderMock.Object);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.False(result.Success);
        Assert.Contains("unsatisfiable version bounds", result.FirstError);
    }

    /// <summary>
    /// Verifies that when a constraint minimum is lower than foundation min version, the stricter foundation floor is kept.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_BaseGameDependency_WithLowerThanFoundationFloor_RetainsFoundationMinFloorAsync()
    {
        var contentItem = new CatalogContentItem
        {
            Id = "mod-with-zh-lower-floor",
            Name = "Mod With ZH Lower Floor",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Description = "Test Mod",
            Tags = ["mod"],
        };

        var release = new ContentRelease
        {
            Version = "1.0.0",
            Dependencies =
            [
                new CatalogDependency
                {
                    PublisherId = "ea",
                    ContentId = "zerohour",
                    VersionConstraint = ">=1.02 <2.0",
                },
            ],
        };

        var publisher = new PublisherProfile { Id = "test-pub", Name = "Test Pub" };

        var searchResult = new ContentSearchResult
        {
            Id = "1.100.testpub.mod.modwithzhlowerfloor",
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

        var builtManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.100.testpub.mod.modwithzhlowerfloor"),
            Name = "Mod With ZH Lower Floor",
            Version = "1.0.0",
            ContentType = ContentType.Mod,
            Files = [],
            Metadata = new ContentMetadata(),
            Publisher = new PublisherInfo { PublisherType = "test-pub" },
        };

        var builderMock = CreateBuilderMock(builtManifest);
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
                It.IsAny<List<GameType>?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()))
            .Callback<ManifestId, string, ContentType, DependencyInstallBehavior, string, string, List<string>?, bool, List<ManifestId>?, List<GameType>?, bool, bool>((id, name, type, behavior, min, max, comp, excl, conf, games, minInc, maxInc) =>
                builtManifest.Dependencies.Add(new ContentDependency
                {
                    Id = id,
                    Name = name,
                    DependencyType = type,
                    MinVersion = min,
                    MaxVersion = max,
                    CompatibleVersions = comp ?? [],
                    MinInclusive = minInc,
                    MaxInclusive = maxInc,
                }))
            .Returns(builderMock.Object);

        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            () => builderMock.Object);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.True(result.Success, result.FirstError);
        Assert.Single(builtManifest.Dependencies);
        var dep = builtManifest.Dependencies[0];
        Assert.Equal("1.04", dep.MinVersion);
        Assert.Equal("2.0", dep.MaxVersion);
        Assert.True(dep.MinInclusive);
        Assert.False(dep.MaxInclusive);
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
                It.IsAny<List<GameType>?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()),
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
                It.IsAny<List<GameType>?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()),
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
                It.IsAny<List<GameType>?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()),
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

    /// <summary>
    /// Verifies that version constraints are parsed into appropriate min, max, and compatible version lists.
    /// </summary>
    /// <param name="constraint">The version constraint expression.</param>
    /// <param name="expectedMin">Expected minimum version.</param>
    /// <param name="expectedMax">Expected maximum version.</param>
    /// <param name="expectedCompatibleCsv">Expected comma-separated compatible versions.</param>
    /// <param name="expectedMinInclusive">Expected min inclusive flag.</param>
    /// <param name="expectedMaxInclusive">Expected max inclusive flag.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(">=1.0.0 <2.0.0", "1.0.0", "2.0.0", null, true, false)]
    [InlineData("^1.2.3", "1.2.3", "2.0.0", null, true, false)]
    [InlineData("~1.2.3", "1.2.3", "1.3.0", null, true, false)]
    [InlineData("1.0.0 | 2.0.0", "", "", "1.0.0,2.0.0", true, true)]
    [InlineData("latest", "", "", null, true, true)]
    [InlineData("1.5.0", "1.5.0", "1.5.0", "1.5.0", true, true)]
    [InlineData("v1.5", "1.5", "1.5", "1.5", true, true)]
    [InlineData("invalid-token", "", "", null, true, true)]
    [InlineData(">1.2", "1.2", "", null, false, true)]
    [InlineData("<2.0", "", "2.0", null, true, false)]
    [InlineData(">= 1.0.0 < 2.0.0", "1.0.0", "2.0.0", null, true, false)]
    [InlineData(">=2.0.0 >=1.0.0", "2.0.0", "", null, true, true)]
    [InlineData("<2.0.0 <3.0.0", "", "2.0.0", null, true, false)]
    [InlineData("vv1.5", "1.5", "1.5", "1.5", true, true)]
    [InlineData("1..0", "", "", null, true, true)]
    public async Task ResolveAsync_ParsesVersionConstraintsCorrectlyAsync(
        string constraint,
        string expectedMin,
        string expectedMax,
        string? expectedCompatibleCsv,
        bool expectedMinInclusive,
        bool expectedMaxInclusive)
    {
        var contentItem = new CatalogContentItem
        {
            Id = "mod-a",
            Name = "Mod A",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Description = "Test Mod",
            Tags = ["mod"],
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
                    ContentId = "dep-a",
                    VersionConstraint = constraint,
                },
            ],
        };

        var publisher = new PublisherProfile { Id = "test-pub", Name = "Test Pub" };

        var searchResult = new ContentSearchResult
        {
            Id = "1.0.testpub.mod.moda",
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

        var builtManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.testpub.mod.moda"),
            Name = contentItem.Name,
            Version = "1.0.0",
            ContentType = ContentType.Mod,
            Publisher = new PublisherInfo { PublisherType = "test-pub" },
        };

        var builderMock = CreateBuilderMock(builtManifest);
        string? capturedMin = null;
        string? capturedMax = null;
        List<string>? capturedCompatible = null;

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
                It.IsAny<List<GameType>?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()))
            .Callback<ManifestId, string, ContentType, DependencyInstallBehavior, string, string, List<string>?, bool, List<ManifestId>?, List<GameType>?, bool, bool>(
                (id, name, type, behavior, min, max, comp, excl, conf, games, minInc, maxInc) =>
                {
                    capturedMin = min;
                    capturedMax = max;
                    capturedCompatible = comp;
                    builtManifest.Dependencies.Add(new ContentDependency
                    {
                        Id = id,
                        Name = name,
                        DependencyType = type,
                        MinVersion = min,
                        MaxVersion = max,
                        CompatibleVersions = comp ?? [],
                        MinInclusive = minInc,
                        MaxInclusive = maxInc,
                    });
                })
            .Returns(builderMock.Object);

        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            () => builderMock.Object);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(expectedMin, capturedMin);
        Assert.Equal(expectedMax, capturedMax);

        if (expectedCompatibleCsv == null)
        {
            Assert.Null(capturedCompatible);
        }
        else
        {
            Assert.NotNull(capturedCompatible);
            Assert.Equal(expectedCompatibleCsv.Split(','), capturedCompatible);
        }

        Assert.Single(builtManifest.Dependencies);
        Assert.Equal(expectedMinInclusive, builtManifest.Dependencies[0].MinInclusive);
        Assert.Equal(expectedMaxInclusive, builtManifest.Dependencies[0].MaxInclusive);
    }

    /// <summary>
    /// Verifies that a catalog dependency without an explicit PublisherId inherits the host content's declared publisher.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_DependencyWithoutPublisherId_InheritsDeclaredHostPublisherAsync()
    {
        var contentItem = new CatalogContentItem
        {
            Id = "mod-with-implicit-dep-pub",
            Name = "Mod With Implicit Dep Pub",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            PublisherType = PublisherTypeConstants.TheSuperHackers,
            Description = "Test Mod",
            Tags = ["mod"],
        };

        var release = new ContentRelease
        {
            Version = "1.0.0",
            Dependencies =
            [
                new CatalogDependency
                {
                    ContentId = "sibling-mod",
                    VersionConstraint = ">=1.0.0",
                },
            ],
        };

        var publisher = new PublisherProfile { Id = "test-pub", Name = "Test Pub" };

        var searchResult = new ContentSearchResult
        {
            Id = "1.100.testpub.mod.modwithimplicitdeppub",
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

        var builtManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.100.testpub.mod.modwithimplicitdeppub"),
            Name = contentItem.Name,
            Version = "1.0.0",
            ContentType = ContentType.Mod,
            Publisher = new PublisherInfo { PublisherType = "test-pub" },
        };

        var builderMock = CreateBuilderMock(builtManifest);
        ManifestId? capturedId = null;
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
                It.IsAny<List<GameType>?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()))
            .Callback<ManifestId, string, ContentType, DependencyInstallBehavior, string, string, List<string>?, bool, List<ManifestId>?, List<GameType>?, bool, bool>(
                (id, name, type, behavior, min, max, comp, excl, conf, games, minInc, maxInc) => capturedId = id)
            .Returns(builderMock.Object);

        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            () => builderMock.Object);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(capturedId);
        Assert.Contains(PublisherTypeConstants.TheSuperHackers, capturedId.Value.Value);
    }

    /// <summary>
    /// Verifies that a catalog dependency without an explicit PublisherId and without a host publisher inherits the generic catalog resolver identity.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_DependencyWithoutPublisherId_AndNoHostPublisher_InheritsGenericCatalogResolverIdAsync()
    {
        var contentItem = new CatalogContentItem
        {
            Id = "mod-with-default-dep",
            Name = "Mod With Default Dep",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            PublisherType = null,
            Description = "Test Mod",
            Tags = ["mod"],
        };

        var release = new ContentRelease
        {
            Version = "1.0.0",
            Dependencies =
            [
                new CatalogDependency
                {
                    PublisherId = string.Empty,
                    ContentId = "sibling-dep",
                    VersionConstraint = "1.0.0",
                },
            ],
        };

        var publisher = new PublisherProfile { Id = string.Empty, Name = string.Empty };

        var searchResult = new ContentSearchResult
        {
            Id = "1.100.generalcatalog.mod.modwithdefaultdep",
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

        var builtManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.100.generalcatalog.mod.modwithdefaultdep"),
            Name = contentItem.Name,
            Version = "1.0.0",
            ContentType = ContentType.Mod,
        };

        ManifestId? capturedId = null;
        var builderMock = CreateBuilderMock(builtManifest);
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
                It.IsAny<List<GameType>?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()))
            .Callback<ManifestId, string, ContentType, DependencyInstallBehavior, string, string, List<string>?, bool, List<ManifestId>?, List<GameType>?, bool, bool>((id, name, type, behavior, min, max, comp, excl, conf, games, minInc, maxInc) => capturedId = id)
            .Returns(builderMock.Object);

        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            () => builderMock.Object);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(capturedId);
        Assert.Contains("genericcatalog", capturedId.Value.Value);
    }

    /// <summary>
    /// Verifies that a release with null Dependencies collection resolves without throwing a NullReferenceException.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_ReleaseWithNullDependencies_ResolvesSuccessfullyAsync()
    {
        var contentItem = new CatalogContentItem
        {
            Id = "mod-null-deps",
            Name = "Mod With Null Deps",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            PublisherType = "test-pub",
            Description = "Test Mod",
            Tags = ["mod"],
        };

        var release = new ContentRelease
        {
            Version = "1.0.0",
            Dependencies = null!,
        };

        var publisher = new PublisherProfile { Id = "test-pub", Name = "Test Pub" };

        var searchResult = new ContentSearchResult
        {
            Id = "1.100.testpub.mod.modnulldeps",
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

        var builtManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.100.testpub.mod.modnulldeps"),
            Name = contentItem.Name,
            Version = "1.0.0",
            ContentType = ContentType.Mod,
        };

        var builderMock = CreateBuilderMock(builtManifest);
        var resolver = new GenericCatalogResolver(
            NullLogger<GenericCatalogResolver>.Instance,
            () => builderMock.Object);

        var result = await resolver.ResolveAsync(searchResult);

        Assert.True(result.Success, result.FirstError);
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
                It.IsAny<List<GameType>?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()))
            .Returns(builderMock.Object);
        builderMock.Setup(b => b.Build()).Returns(builtManifest);
        return builderMock;
    }
}
