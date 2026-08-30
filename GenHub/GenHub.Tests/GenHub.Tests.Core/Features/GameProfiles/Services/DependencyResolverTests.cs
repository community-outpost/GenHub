using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using CoreContentType = GenHub.Core.Models.Enums.ContentType;
using CoreGameType = GenHub.Core.Models.Enums.GameType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Unit tests for <see cref="DependencyResolver"/>.
/// </summary>
public sealed class DependencyResolverTests
{
    private readonly Mock<IContentManifestPool> _mockManifestPool = new();

    /// <summary>
    /// Verifies that exact manifest IDs resolve directly without fallback.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveDependenciesWithManifestsAsync_WhenExactManifestExists_ResolvesSuccessfullyAsync()
    {
        var manifestId = ManifestId.Create("1.104.steam.gameclient.zerohour");
        var manifest = new ContentManifest
        {
            Id = manifestId,
            Name = "Zero Hour Steam Client",
            ContentType = CoreContentType.GameClient,
            TargetGame = CoreGameType.ZeroHour,
            Version = "1.04",
        };

        _mockManifestPool
            .Setup(p => p.GetManifestAsync(manifestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifest));

        var resolver = new DependencyResolver(
            _mockManifestPool.Object,
            NullLogger<DependencyResolver>.Instance);

        var result = await resolver.ResolveDependenciesWithManifestsAsync([manifestId.Value]);

        Assert.True(result.Success);
        Assert.Single(result.ResolvedManifests);
        Assert.Equal(manifestId.Value, result.ResolvedManifests[0].Id.Value);
    }

    /// <summary>
    /// Verifies that when an exact manifest ID is missing, fallback matching resolves to a compatible pooled manifest.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveDependenciesWithManifestsAsync_WhenExactMissing_ResolvesCompatibleFallbackAsync()
    {
        var declaredId = "1.828261.generalsonline.gameclient.zerohour";
        var pooledManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.82826.generalsonline.gameclient.60hz"),
            Name = "GeneralsOnline 60Hz",
            ContentType = CoreContentType.GameClient,
            TargetGame = CoreGameType.ZeroHour,
            Version = "082826_QFE1",
            Publisher = new PublisherInfo { PublisherType = "generalsonline", Name = "GeneralsOnline" },
        };

        _mockManifestPool
            .Setup(p => p.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(null));

        _mockManifestPool
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([pooledManifest]));

        var resolver = new DependencyResolver(
            _mockManifestPool.Object,
            NullLogger<DependencyResolver>.Instance);

        var result = await resolver.ResolveDependenciesWithManifestsAsync([declaredId]);

        Assert.True(result.Success);
        Assert.Single(result.ResolvedManifests);
        Assert.Equal("1.82826.generalsonline.gameclient.60hz", result.ResolvedManifests[0].Id.Value);
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
            ContentType = CoreContentType.Patch,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = idB,
                    Name = "Pack B",
                    DependencyType = CoreContentType.Patch,
                    InstallBehavior = GenHub.Core.Models.Enums.DependencyInstallBehavior.RequireExisting,
                    StrictPublisher = true,
                },
            ],
        };

        var manifestB = new ContentManifest
        {
            Id = idB,
            Name = "Pack B",
            ContentType = CoreContentType.Patch,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = idA,
                    Name = "Pack A",
                    DependencyType = CoreContentType.Patch,
                    InstallBehavior = GenHub.Core.Models.Enums.DependencyInstallBehavior.RequireExisting,
                    StrictPublisher = true,
                },
            ],
        };

        _mockManifestPool
            .Setup(p => p.GetManifestAsync(idA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifestA));

        _mockManifestPool
            .Setup(p => p.GetManifestAsync(idB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifestB));

        var resolver = new DependencyResolver(
            _mockManifestPool.Object,
            NullLogger<DependencyResolver>.Instance);

        var result = await resolver.ResolveDependenciesWithManifestsAsync([idA.Value]);

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
    [InlineData("1.828261.generalsonline.gameclient.zerohour", "1.82826.generalsonline.gameclient.60hz", true)]
    [InlineData("1.104.any.gameinstallation.zerohour", "1.104.steam.gameinstallation.zerohour", true)]
    [InlineData("1.104.steam.gameclient.zerohour", "1.104.ea.gameclient.zerohour", false)]
    [InlineData("1.104.steam.gameclient.zerohour", "1.104.steam.patch.zerohour", false)]
    public void HasCompatibleCatalogIdentity_MatchesCorrectly(string declaredId, string acquiredId, bool expected)
    {
        var result = DependencyResolver.HasCompatibleCatalogIdentity(declaredId, acquiredId);
        Assert.Equal(expected, result);
    }
}
