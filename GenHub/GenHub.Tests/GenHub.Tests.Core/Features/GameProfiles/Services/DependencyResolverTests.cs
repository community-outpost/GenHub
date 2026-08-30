using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Regression tests for canonical dependency resolution.
/// </summary>
public sealed class DependencyResolverTests
{
    /// <summary>
    /// Verifies that a Community Outpost AutoInstall dependency declared with a stale release
    /// version resolves to the acquired artifact with the same authoritative content code.
    /// </summary>
    /// <returns>A task that completes when the real dependency resolver finishes.</returns>
    [Fact]
    public async Task ResolveDependenciesWithManifestsAsync_LegacyCommunityOutpostDependency_UsesCanonicalAcquiredManifestAsync()
    {
        // Arrange
        const string hotkeysId = "1.10.communityoutpost.addon.hlegenglish";
        const string declaredGenToolId = "1.1.communityoutpost.addon.gent";
        const string acquiredGenToolId = "1.10.communityoutpost.addon.gent";
        var hotkeys = new ContentManifest
        {
            Id = ManifestId.Create(hotkeysId),
            Name = "Legionnaire's Hotkeys",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            Metadata = new ContentMetadata { Tags = ["contentCode:hleg"] },
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(declaredGenToolId),
                    Name = "GenTool",
                    DependencyType = ContentType.Addon,
                    InstallBehavior = DependencyInstallBehavior.AutoInstall,
                    StrictPublisher = false,
                },
            ],
        };
        var genTool = new ContentManifest
        {
            Id = ManifestId.Create(acquiredGenToolId),
            Name = "GenTool",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            Metadata = new ContentMetadata { Tags = ["contentCode:gent"] },
        };
        var manifests = new Dictionary<string, ContentManifest>(StringComparer.OrdinalIgnoreCase)
        {
            [hotkeysId] = hotkeys,
            [acquiredGenToolId] = genTool,
        };
        var manifestPool = CreateManifestPool(manifests);
        var resolver = new DependencyResolver(
            manifestPool.Object,
            NullLogger<DependencyResolver>.Instance);

        // Act
        var result = await resolver.ResolveDependenciesWithManifestsAsync([hotkeysId]);

        // Assert
        Assert.True(result.Success, result.FirstError);
        Assert.Contains(hotkeysId, result.ResolvedContentIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(acquiredGenToolId, result.ResolvedContentIds, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(declaredGenToolId, result.ResolvedContentIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(result.ResolvedManifests, manifest => manifest.Id.Value == acquiredGenToolId);
    }

    /// <summary>
    /// Verifies that semantic <c>*.any.*</c> installation constraints remain type-only and do not
    /// cause a manifest lookup or alias-resolution attempt.
    /// </summary>
    /// <returns>A task that completes when the real dependency resolver finishes.</returns>
    [Fact]
    public async Task ResolveDependenciesWithManifestsAsync_AnyPublisherConstraint_RemainsTypeOnlyAsync()
    {
        // Arrange
        const string contentId = "1.0.test.addon.example";
        var content = new ContentManifest
        {
            Id = ManifestId.Create(contentId),
            Name = "Example Addon",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create("1.104.any.gameinstallation.zerohour"),
                    Name = "Zero Hour installation",
                    DependencyType = ContentType.GameInstallation,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    StrictPublisher = false,
                },
            ],
        };
        var manifestPool = CreateManifestPool(
            new Dictionary<string, ContentManifest>(StringComparer.OrdinalIgnoreCase)
            {
                [contentId] = content,
            });
        var resolver = new DependencyResolver(
            manifestPool.Object,
            NullLogger<DependencyResolver>.Instance);

        // Act
        var result = await resolver.ResolveDependenciesWithManifestsAsync([contentId]);

        // Assert
        Assert.True(result.Success, result.FirstError);
        Assert.Equal([contentId], result.ResolvedContentIds);
        manifestPool.Verify(
            pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that legacy <c>*.genhub.gameinstallation.*</c> RequireExisting constraints are
    /// treated as type-only so already-stored Generals Online / SuperHackers manifests can be
    /// added to profiles without looking up a non-existent genhub installation ID.
    /// </summary>
    /// <returns>A task that completes when the real dependency resolver finishes.</returns>
    [Fact]
    public async Task ResolveDependenciesWithManifestsAsync_LegacyGenHubInstallationConstraint_RemainsTypeOnlyAsync()
    {
        // Arrange
        const string contentId = "1.605261.generalsonline.gameclient.60hz";
        var content = new ContentManifest
        {
            Id = ManifestId.Create(contentId),
            Name = "GeneralsOnline 60Hz",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create("1.104.genhub.gameinstallation.zerohour"),
                    Name = "Zero Hour Installation (Required)",
                    DependencyType = ContentType.GameInstallation,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    StrictPublisher = false,
                    CompatibleGameTypes = [GameType.ZeroHour],
                },
            ],
        };
        var manifestPool = CreateManifestPool(
            new Dictionary<string, ContentManifest>(StringComparer.OrdinalIgnoreCase)
            {
                [contentId] = content,
            });
        var resolver = new DependencyResolver(
            manifestPool.Object,
            NullLogger<DependencyResolver>.Instance);

        // Act
        var result = await resolver.ResolveDependenciesWithManifestsAsync([contentId]);

        // Assert
        Assert.True(result.Success, result.FirstError);
        Assert.Equal([contentId], result.ResolvedContentIds);
        Assert.DoesNotContain(
            "1.104.genhub.gameinstallation.zerohour",
            result.ResolvedContentIds,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that an ordinary publisher dependency retains exact-ID semantics instead of using
    /// a release-version-insensitive fallback.
    /// </summary>
    /// <returns>A task that completes when the real dependency resolver finishes.</returns>
    [Fact]
    public async Task ResolveDependenciesWithManifestsAsync_OrdinaryPublisherVersionMismatch_RemainsMissingAsync()
    {
        // Arrange
        const string contentId = "1.0.test.addon.example";
        const string declaredDependencyId = "1.1.otherpublisher.addon.core";
        var content = new ContentManifest
        {
            Id = ManifestId.Create(contentId),
            Name = "Example Addon",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(declaredDependencyId),
                    Name = "Other Publisher Core",
                    DependencyType = ContentType.Addon,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    StrictPublisher = true,
                },
            ],
        };
        var acquiredDifferentVersion = new ContentManifest
        {
            Id = ManifestId.Create("1.10.otherpublisher.addon.core"),
            Name = "Other Publisher Core",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };
        var manifestPool = CreateManifestPool(
            new Dictionary<string, ContentManifest>(StringComparer.OrdinalIgnoreCase)
            {
                [contentId] = content,
                [acquiredDifferentVersion.Id.Value] = acquiredDifferentVersion,
            });
        var resolver = new DependencyResolver(
            manifestPool.Object,
            NullLogger<DependencyResolver>.Instance);

        // Act
        var result = await resolver.ResolveDependenciesWithManifestsAsync([contentId]);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(declaredDependencyId, result.FirstError, StringComparison.OrdinalIgnoreCase);
        manifestPool.Verify(
            pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Catalog publishers with <see cref="ContentDependency.StrictPublisher"/> false must bind a
    /// version-constraint ID to an acquired sibling whose version (or variant suffix) differs.
    /// </summary>
    /// <returns>A task that completes when the real dependency resolver finishes.</returns>
    [Fact]
    public async Task ResolveDependenciesWithManifestsAsync_CatalogPublisherVersionMismatch_AliasesAcquiredManifestAsync()
    {
        const string bundleId = "1.20260731.genhubtestpublishers.contentbundle.bundlestack";
        const string declaredClientId = "1.1.genhubtestpublishers.gameclient.superhackerszerohourgamecode";
        const string acquiredClientId = "1.99971.genhubtestpublishers.gameclient.superhackerszerohourgamecode";
        var bundle = new ContentManifest
        {
            Id = ManifestId.Create(bundleId),
            Name = "Ultimate Stack",
            ContentType = ContentType.ContentBundle,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(declaredClientId),
                    Name = "zerohour",
                    DependencyType = ContentType.GameClient,
                    InstallBehavior = DependencyInstallBehavior.AutoInstall,
                    StrictPublisher = false,
                },
            ],
        };
        var acquiredClient = new ContentManifest
        {
            Id = ManifestId.Create(acquiredClientId),
            Name = "TheSuperHackers Zero Hour Game Code",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };
        var manifestPool = CreateManifestPool(
            new Dictionary<string, ContentManifest>(StringComparer.OrdinalIgnoreCase)
            {
                [bundleId] = bundle,
                [acquiredClientId] = acquiredClient,
            });
        var resolver = new DependencyResolver(
            manifestPool.Object,
            NullLogger<DependencyResolver>.Instance);

        var result = await resolver.ResolveDependenciesWithManifestsAsync([bundleId]);

        Assert.True(result.Success, result.FirstError);
        Assert.Contains(result.ResolvedManifests, manifest => manifest.Id.Value == acquiredClientId);
        Assert.Contains(acquiredClientId, result.ResolvedContentIds, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(declaredClientId, result.ResolvedContentIds, StringComparer.OrdinalIgnoreCase);
        manifestPool.Verify(
            pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// A catalog constraint on lemon-controlbar must bind to the acquired 720p variant sibling.
    /// </summary>
    /// <returns>A task that completes when the real dependency resolver finishes.</returns>
    [Fact]
    public async Task ResolveDependenciesWithManifestsAsync_CatalogVariantSuffix_AliasesAcquiredManifestAsync()
    {
        const string bundleId = "1.20260731.genhubtestpublishers.contentbundle.bundlestack";
        const string declaredId = "1.13.genhubtestpublishers.addon.lemoncontrolbar";
        const string acquiredId = "1.13.genhubtestpublishers.addon.lemoncontrolbar-720p";
        var bundle = new ContentManifest
        {
            Id = ManifestId.Create(bundleId),
            Name = "Ultimate Stack",
            ContentType = ContentType.ContentBundle,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(declaredId),
                    Name = "lemon-controlbar",
                    DependencyType = ContentType.Addon,
                    InstallBehavior = DependencyInstallBehavior.AutoInstall,
                    StrictPublisher = false,
                },
            ],
        };
        var acquired = new ContentManifest
        {
            Id = ManifestId.Create(acquiredId),
            Name = "Control Bar Pro Lemon Edition ZH (720p)",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };
        var manifestPool = CreateManifestPool(
            new Dictionary<string, ContentManifest>(StringComparer.OrdinalIgnoreCase)
            {
                [bundleId] = bundle,
                [acquiredId] = acquired,
            });
        var resolver = new DependencyResolver(
            manifestPool.Object,
            NullLogger<DependencyResolver>.Instance);

        var result = await resolver.ResolveDependenciesWithManifestsAsync([bundleId]);

        Assert.True(result.Success, result.FirstError);
        Assert.Contains(result.ResolvedManifests, manifest => manifest.Id.Value == acquiredId);
        Assert.Contains(acquiredId, result.ResolvedContentIds, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(declaredId, result.ResolvedContentIds, StringComparer.OrdinalIgnoreCase);
    }

    private static Mock<IContentManifestPool> CreateManifestPool(
        IReadOnlyDictionary<string, ContentManifest> manifests)
    {
        var manifestPool = new Mock<IContentManifestPool>();
        manifestPool
            .Setup(pool => pool.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId manifestId, CancellationToken _) =>
                manifests.TryGetValue(manifestId.Value, out var manifest)
                    ? OperationResult<ContentManifest?>.CreateSuccess(manifest)
                    : OperationResult<ContentManifest?>.CreateSuccess(null));
        manifestPool
            .Setup(pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifests.Values));
        return manifestPool;
    }
}
