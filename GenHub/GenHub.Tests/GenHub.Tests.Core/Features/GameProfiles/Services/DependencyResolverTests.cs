using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Unit tests for <see cref="DependencyResolver"/>.
/// </summary>
public class DependencyResolverTests
{
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<ILogger<DependencyResolver>> _loggerMock = new();
    private readonly DependencyResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyResolverTests"/> class.
    /// </summary>
    public DependencyResolverTests()
    {
        _resolver = new DependencyResolver(_manifestPoolMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies exact match catalog identity check returns true.
    /// </summary>
    [Fact]
    public void HasCompatibleCatalogIdentity_ExactMatch_ReturnsTrue()
    {
        var id = "1.104.communityoutpost.gameclient.zerohour";
        Assert.True(DependencyResolver.HasCompatibleCatalogIdentity(id, id));
    }

    /// <summary>
    /// Verifies version difference catalog identity check returns true.
    /// </summary>
    [Fact]
    public void HasCompatibleCatalogIdentity_VersionDiffers_ReturnsTrue()
    {
        var declaredId = "1.104.communityoutpost.gameclient.zerohour";
        var acquiredId = "1.105.communityoutpost.gameclient.zerohour";
        Assert.True(DependencyResolver.HasCompatibleCatalogIdentity(declaredId, acquiredId));
    }

    /// <summary>
    /// Verifies GeneralsOnline gamedata patch catalog identity check returns true.
    /// </summary>
    [Fact]
    public void HasCompatibleCatalogIdentity_GeneralsOnlineGameDataPatch_ReturnsTrue()
    {
        var declaredId = "1.0828261.generalsonline.gamedata.zerohour";
        var acquiredId = "1.82826.generalsonline.patch.gamedata";
        Assert.True(DependencyResolver.HasCompatibleCatalogIdentity(declaredId, acquiredId));
    }

    /// <summary>
    /// Verifies GeneralsOnline 60Hz game client variant catalog identity check returns false against standard Zero Hour.
    /// </summary>
    [Fact]
    public void HasCompatibleCatalogIdentity_GeneralsOnlineGameClient60HzVariant_ReturnsFalse()
    {
        var declaredId = "1.0828261.generalsonline.gameclient.zerohour";
        var acquiredId = "1.82826.generalsonline.gameclient.60hz";
        Assert.False(DependencyResolver.HasCompatibleCatalogIdentity(declaredId, acquiredId));
    }

    /// <summary>
    /// Verifies different publishers returns false.
    /// </summary>
    [Fact]
    public void HasCompatibleCatalogIdentity_DifferentPublishers_ReturnsFalse()
    {
        var declaredId = "1.104.communityoutpost.gameclient.zerohour";
        var acquiredId = "1.104.thesuperhackers.gameclient.zerohour";
        Assert.False(DependencyResolver.HasCompatibleCatalogIdentity(declaredId, acquiredId));
    }

    /// <summary>
    /// Verifies incompatible content types returns false.
    /// </summary>
    [Fact]
    public void HasCompatibleCatalogIdentity_DifferentIncompatibleContentTypes_ReturnsFalse()
    {
        var declaredId = "1.104.communityoutpost.gameclient.zerohour";
        var acquiredId = "1.104.communityoutpost.mappack.zerohour";
        Assert.False(DependencyResolver.HasCompatibleCatalogIdentity(declaredId, acquiredId));
    }

    /// <summary>
    /// Verifies exact manifest resolution from pool.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task ResolveDependenciesAsync_ExactManifestInPool_ResolvesSuccessfullyAsync()
    {
        var manifestId = "1.104.communityoutpost.gameclient.zerohour";
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create(manifestId),
            Name = "Community Outpost Zero Hour",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };

        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(ManifestId.Create(manifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifest));

        var result = await _resolver.ResolveDependenciesAsync([manifestId]);

        Assert.Single(result);
        Assert.Contains(manifestId, result);
    }

    /// <summary>
    /// Verifies that resolving dependencies for non-existent manifest throws InvalidOperationException.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task ResolveDependenciesAsync_ManifestNotInPool_ThrowsInvalidOperationExceptionAsync()
    {
        var manifestId = "1.104.communityoutpost.gameclient.zerohour";

        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(ManifestId.Create(manifestId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateFailure("Not found"));
        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _resolver.ResolveDependenciesAsync([manifestId]));
    }

    /// <summary>
    /// Verifies that resolving dependencies follows dependency chain.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task ResolveDependenciesAsync_WithDependencies_ResolvesFullChainAsync()
    {
        var rootId = "1.104.communityoutpost.gameclient.zerohour";
        var depId = "1.104.communityoutpost.patch.balance";

        var rootManifest = new ContentManifest
        {
            Id = ManifestId.Create(rootId),
            Name = "Root",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(depId),
                    Name = "Dep",
                    DependencyType = ContentType.Patch,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    StrictPublisher = true,
                },
            ],
        };

        var depManifest = new ContentManifest
        {
            Id = ManifestId.Create(depId),
            Name = "Dep",
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
        };

        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(ManifestId.Create(rootId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(rootManifest));
        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(ManifestId.Create(depId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(depManifest));

        var result = await _resolver.ResolveDependenciesAsync([rootId]);

        Assert.Equal(2, result.Count);
        Assert.Contains(rootId, result);
        Assert.Contains(depId, result);
    }

    /// <summary>
    /// Verifies that resolving dependencies handles diamond dependency without duplicates.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task ResolveDependenciesAsync_DiamondDependency_ResolvesWithoutDuplicatesAsync()
    {
        var rootId = "1.100.mod.root.base";
        var leftId = "1.100.mod.left.base";
        var rightId = "1.100.mod.right.base";
        var commonId = "1.100.mod.common.base";

        var rootManifest = new ContentManifest
        {
            Id = ManifestId.Create(rootId),
            Name = "Root",
            ContentType = ContentType.Mod,
            Dependencies =
            [
                new ContentDependency { Id = ManifestId.Create(leftId), Name = "Left", DependencyType = ContentType.Mod, InstallBehavior = DependencyInstallBehavior.RequireExisting, StrictPublisher = true },
                new ContentDependency { Id = ManifestId.Create(rightId), Name = "Right", DependencyType = ContentType.Mod, InstallBehavior = DependencyInstallBehavior.RequireExisting, StrictPublisher = true },
            ],
        };

        var leftManifest = new ContentManifest
        {
            Id = ManifestId.Create(leftId),
            Name = "Left",
            ContentType = ContentType.Mod,
            Dependencies =
            [
                new ContentDependency { Id = ManifestId.Create(commonId), Name = "Common", DependencyType = ContentType.Mod, InstallBehavior = DependencyInstallBehavior.RequireExisting, StrictPublisher = true },
            ],
        };

        var rightManifest = new ContentManifest
        {
            Id = ManifestId.Create(rightId),
            Name = "Right",
            ContentType = ContentType.Mod,
            Dependencies =
            [
                new ContentDependency { Id = ManifestId.Create(commonId), Name = "Common", DependencyType = ContentType.Mod, InstallBehavior = DependencyInstallBehavior.RequireExisting, StrictPublisher = true },
            ],
        };

        var commonManifest = new ContentManifest
        {
            Id = ManifestId.Create(commonId),
            Name = "Common",
            ContentType = ContentType.Mod,
        };

        _manifestPoolMock.Setup(p => p.GetManifestAsync(ManifestId.Create(rootId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(rootManifest));
        _manifestPoolMock.Setup(p => p.GetManifestAsync(ManifestId.Create(leftId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(leftManifest));
        _manifestPoolMock.Setup(p => p.GetManifestAsync(ManifestId.Create(rightId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(rightManifest));
        _manifestPoolMock.Setup(p => p.GetManifestAsync(ManifestId.Create(commonId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(commonManifest));

        var result = await _resolver.ResolveDependenciesAsync([rootId]);

        Assert.Equal(4, result.Count);
        Assert.Contains(rootId, result);
        Assert.Contains(leftId, result);
        Assert.Contains(rightId, result);
        Assert.Contains(commonId, result);
    }

    /// <summary>
    /// Verifies ResolveDependenciesWithManifestsAsync returns manifest objects.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task ResolveDependenciesWithManifestsAsync_ReturnsManifestObjectsAsync()
    {
        var rootId = "1.104.communityoutpost.gameclient.zerohour";
        var depId = "1.104.communityoutpost.patch.balance";

        var rootManifest = new ContentManifest
        {
            Id = ManifestId.Create(rootId),
            Name = "Root",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(depId),
                    Name = "Dep",
                    DependencyType = ContentType.Patch,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    StrictPublisher = true,
                },
            ],
        };

        var depManifest = new ContentManifest
        {
            Id = ManifestId.Create(depId),
            Name = "Dep",
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
        };

        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(ManifestId.Create(rootId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(rootManifest));
        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(ManifestId.Create(depId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(depManifest));

        var result = await _resolver.ResolveDependenciesWithManifestsAsync([rootId]);

        Assert.True(result.Success);
        Assert.Equal(2, result.ResolvedContentIds.Count);
        Assert.Equal(2, result.ResolvedManifests.Count);
        Assert.Contains(result.ResolvedManifests, m => m.Id.Value == rootId);
        Assert.Contains(result.ResolvedManifests, m => m.Id.Value == depId);
    }

    /// <summary>
    /// Verifies GeneralsOnline variant discrepancy resolves pooled manifest.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task ResolveDependenciesWithManifestsAsync_GeneralsOnlineDiscrepancy_ResolvesPooledManifestAsync()
    {
        var requestedClient = "1.0828261.generalsonline.gameclient.60hz";
        var requestedGameData = "1.0828261.generalsonline.gamedata.zerohour";

        var actualClientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.82826.generalsonline.gameclient.60hz"),
            Name = "GeneralsOnline 60Hz",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "generalsonline" },
        };

        var actualGameDataManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.82826.generalsonline.patch.gamedata"),
            Name = "GeneralsOnline Game Data",
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "generalsonline" },
        };

        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateFailure("Not found"));

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([actualClientManifest, actualGameDataManifest]));

        var result = await _resolver.ResolveDependenciesWithManifestsAsync([requestedClient, requestedGameData]);

        Assert.True(result.Success);
        Assert.Equal(2, result.ResolvedManifests.Count);
        Assert.Contains(result.ResolvedManifests, m => m.Id.Value == "1.82826.generalsonline.gameclient.60hz");
        Assert.Contains(result.ResolvedManifests, m => m.Id.Value == "1.82826.generalsonline.patch.gamedata");
    }

    /// <summary>
    /// Verifies that circular dependencies are detected without infinite loops and reported as warnings.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveDependenciesWithManifestsAsync_WhenCircularDependency_ResolvesWithWarningAsync()
    {
        var idA = ManifestId.Create("1.100.mod.patch.packa");
        var idB = ManifestId.Create("1.100.mod.patch.packb");

        var manifestA = new ContentManifest
        {
            Id = idA,
            Name = "Pack A",
            ContentType = ContentType.Patch,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = idB,
                    Name = "Pack B",
                    DependencyType = ContentType.Patch,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    StrictPublisher = true,
                },
            ],
        };

        var manifestB = new ContentManifest
        {
            Id = idB,
            Name = "Pack B",
            ContentType = ContentType.Patch,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = idA,
                    Name = "Pack A",
                    DependencyType = ContentType.Patch,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    StrictPublisher = true,
                },
            ],
        };

        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(idA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifestA));

        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(idB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifestB));

        var result = await _resolver.ResolveDependenciesWithManifestsAsync([idA.Value]);

        Assert.True(result.Success);
        Assert.Equal(2, result.ResolvedManifests.Count);
        Assert.NotNull(result.Warnings);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains("Circular dependency detected", result.Warnings[0]);
    }

    /// <summary>
    /// Verifies that HasCompatibleCatalogIdentity correctly handles various segment matching scenarios.
    /// </summary>
    /// <param name="declaredId">The declared catalog ID.</param>
    /// <param name="acquiredId">The acquired manifest ID.</param>
    /// <param name="expected">The expected match result.</param>
    [Theory]
    [InlineData("1.104.steam.gameclient.zerohour", "1.104.steam.gameclient.zerohour", true)]
    [InlineData("1.828261.generalsonline.gameclient.zerohour", "1.82826.generalsonline.gameclient.60hz", false)]
    [InlineData("1.82826.generalsonline.gameclient.60hz", "1.828261.generalsonline.gameclient.zerohour", false)]
    [InlineData("1.0.custom.gameclient.myclient", "1.0.custom.gameclient.myclientplus", false)]
    [InlineData("1.0.custom.gameclient.myclient", "1.0.custom.gameclient.myclient-hd", true)]
    [InlineData("1.104.any.gameinstallation.zerohour", "1.104.steam.gameinstallation.zerohour", true)]
    [InlineData("1.104.steam.gameclient.zerohour", "1.104.ea.gameclient.zerohour", false)]
    [InlineData("1.104.steam.gameclient.zerohour", "1.104.steam.patch.zerohour", false)]
    [InlineData("1.104.retail.gameinstallation.generals", "1.104.retail.gameinstallation.zerohour104zh", false)]
    [InlineData("1.104.retail.gameinstallation.zerohour", "1.104.retail.gameinstallation.generals108en", false)]
    [InlineData("1.104.generalsonline.gameclient.generalsonlinezh", "1.104.generalsonline.gameclient.generalsonlinezh-60", false)]
    [InlineData("1.104.retail.gameinstallation.zerohour", "1.104.retail.gameinstallation.zerohour104zh", true)]
    [InlineData("1.108.retail.gameinstallation.generals", "1.108.retail.gameinstallation.generals108en", true)]
    public void HasCompatibleCatalogIdentity_MatchesCorrectly(string declaredId, string acquiredId, bool expected)
    {
        var result = DependencyResolver.HasCompatibleCatalogIdentity(declaredId, acquiredId);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that publisher fallback does not resolve a non-retail Community Patch manifest for a retail Community Patch request.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task ResolveDependenciesWithManifestsAsync_RetailCommunityPatch_DoesNotFallbackToNonRetailManifestAsync()
    {
        var requestedRetailClient = "1.20260723.communityoutpost.gameclient.community-patch";

        var nonRetClientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260911.communityoutpost.gameclient.community-patch-nonret"),
            Name = "Community Patch (TheSuperHackers Non-Retail Stream Build)",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
        };

        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateFailure("Not found"));

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([nonRetClientManifest]));

        var result = await _resolver.ResolveDependenciesWithManifestsAsync([requestedRetailClient]);

        Assert.False(result.Success);
        Assert.Empty(result.ResolvedManifests);
    }
}
