using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging;
using Moq;
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
    /// Verifies GeneralsOnline game client variant catalog identity check returns true.
    /// </summary>
    [Fact]
    public void HasCompatibleCatalogIdentity_GeneralsOnlineGameClientVariant_ReturnsTrue()
    {
        var declaredId = "1.0828261.generalsonline.gameclient.zerohour";
        var acquiredId = "1.82826.generalsonline.gameclient.60hz";
        Assert.True(DependencyResolver.HasCompatibleCatalogIdentity(declaredId, acquiredId));
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

        Assert.Contains(manifestId, result);
    }

    /// <summary>
    /// Verifies fallback to catalog compatible manifest when exact ID not found.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task ResolveDependenciesAsync_FallbackToCatalogCompatibleManifest_ResolvesSuccessfullyAsync()
    {
        var declaredId = "1.0828261.generalsonline.gamedata.zerohour";
        var actualPoolId = "1.82826.generalsonline.patch.gamedata";
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create(actualPoolId),
            Name = "GeneralsOnline Game Data",
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "generalsonline" },
        };

        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(It.Is<ManifestId>(m => m.Value == declaredId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateFailure("Not found"));

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([manifest]));

        var result = await _resolver.ResolveDependenciesAsync([declaredId]);

        Assert.Contains(actualPoolId, result);
    }

    /// <summary>
    /// Verifies missing manifest throws exception with details.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task ResolveDependenciesAsync_MissingManifest_ThrowsInvalidOperationExceptionAsync()
    {
        var missingId = "1.999.unknown.gameclient.nonexistent";

        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateFailure("Not found"));

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _resolver.ResolveDependenciesAsync([missingId]));

        Assert.Contains("Missing or invalid content IDs", ex.Message);
        Assert.Contains(missingId, ex.Message);
    }

    /// <summary>
    /// Verifies transitive dependencies are resolved.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task ResolveDependenciesWithManifestsAsync_TransitiveDependencies_ResolvesAllManifestsAsync()
    {
        var rootId = "1.104.communityoutpost.gameclient.zerohour";
        var depId = "1.104.communityoutpost.mappack.quickmatch";

        var depManifest = new ContentManifest
        {
            Id = ManifestId.Create(depId),
            Name = "QuickMatch Maps",
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
        };

        var rootManifest = new ContentManifest
        {
            Id = ManifestId.Create(rootId),
            Name = "Community Outpost Zero Hour",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(depId),
                    Name = "QuickMatch Maps",
                    DependencyType = ContentType.MapPack,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    StrictPublisher = true,
                },
            ],
        };

        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(ManifestId.Create(rootId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(rootManifest));

        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(ManifestId.Create(depId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(depManifest));

        var result = await _resolver.ResolveDependenciesWithManifestsAsync([rootId]);

        Assert.True(result.Success);
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
        var requestedClient = "1.0828261.generalsonline.gameclient.zerohour";
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
}
