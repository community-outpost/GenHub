using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging.Abstractions;
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
/// Tests for <see cref="ContentDependencyValidator"/>.
/// </summary>
public sealed class ContentDependencyValidatorTests
{
    private const string ControlBarProId = "1.10.communityoutpost.addon.cbpr";
    private const string ControlBarCoreId = "1.10.communityoutpost.addon.cbpc";
    private const string ControlBarLanguageId = "1.10.communityoutpost.addon.cben";
    private const string GenToolId = "1.10.communityoutpost.addon.gent";
    private const string DeclaredGenToolId = "1.1.communityoutpost.addon.gent";
    private const string InstallationId = "1.104.steam.gameinstallation.zerohour";

    private readonly ContentDependencyValidator _validator = new(
        Mock.Of<IContentManifestPool>(),
        NullLogger<ContentDependencyValidator>.Instance);

    /// <summary>
    /// Control Bar Pro declares GenTool as RequireExisting with StrictPublisher false. The resolver
    /// skips that kind of dependency, so the validator must report it by name.
    /// </summary>
    [Fact]
    public void FindMissingDependencies_ControlBarProWithoutGenTool_ReportsGenToolByName()
    {
        var gaps = _validator.FindMissingDependencies(
            [CreateControlBarPro(), CreateAddon(ControlBarCoreId, "cbpc"), CreateAddon(ControlBarLanguageId, "cben"), CreateInstallation()]);

        var gap = Assert.Single(gaps);
        Assert.Equal("GenTool", gap.DisplayName);
        Assert.Equal(DeclaredGenToolId, gap.DependencyId);
        Assert.Equal(ContentType.Addon, gap.DependencyType);
        Assert.True(gap.CanDownload);
        Assert.Equal("Control Bar Pro (ExiLe)", gap.RequiredByName);
        Assert.Equal(ControlBarProId, gap.RequiredById);
    }

    /// <summary>
    /// A GenTool manifest whose release version differs from the declared one satisfies the dependency.
    /// </summary>
    [Fact]
    public void FindMissingDependencies_VersionedGenToolPresent_ReportsNothing()
    {
        var gaps = _validator.FindMissingDependencies(
            [CreateControlBarPro(), CreateAddon(ControlBarCoreId, "cbpc"), CreateAddon(ControlBarLanguageId, "cben"), CreateAddon(GenToolId, "gent"), CreateInstallation()]);

        Assert.Empty(gaps);
    }

    /// <summary>
    /// Optional and suggested dependencies never block a profile.
    /// </summary>
    /// <param name="installBehavior">The install behavior of the dependency.</param>
    /// <param name="isOptional">Whether the dependency is optional.</param>
    [Theory]
    [InlineData(DependencyInstallBehavior.RequireExisting, true)]
    [InlineData(DependencyInstallBehavior.Suggest, false)]
    [InlineData(DependencyInstallBehavior.Optional, false)]
    public void FindMissingDependencies_OptionalDependencyMissing_ReportsNothing(
        DependencyInstallBehavior installBehavior,
        bool isOptional)
    {
        var addon = new ContentManifest
        {
            Id = ManifestId.Create("1.0.communityoutpost.addon.cbhd"),
            Name = "Control Bar HD",
            ContentType = ContentType.Addon,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(DeclaredGenToolId),
                    Name = "GenTool (Recommended)",
                    DependencyType = ContentType.Addon,
                    InstallBehavior = installBehavior,
                    IsOptional = isOptional,
                },
            ],
        };

        Assert.Empty(_validator.FindMissingDependencies([addon]));
    }

    /// <summary>
    /// Every missing item is reported once, and items GenHub cannot download are marked.
    /// </summary>
    [Fact]
    public void FindMissingDependencies_SeveralItemsMissing_ReportsEachOnce()
    {
        var hotkeys = new ContentManifest
        {
            Id = ManifestId.Create("1.10.communityoutpost.addon.hleg"),
            Name = "Legionnaire's Hotkeys",
            ContentType = ContentType.Addon,
            Dependencies =
            [
                GenPatcherDependencyBuilder.CreateGenToolDependency("GenTool (required for Legionnaire's Hotkeys)", DependencyInstallBehavior.AutoInstall),
            ],
        };

        var gaps = _validator.FindMissingDependencies([CreateControlBarPro(), hotkeys]);

        Assert.Equal(4, gaps.Count);
        Assert.Single(gaps, gap => gap.DisplayName == "GenTool");
        Assert.Contains(gaps, gap => gap.DependencyId == "1.1.communityoutpost.addon.cbpc");
        var installation = Assert.Single(gaps, gap => gap.DependencyType == ContentType.GameInstallation);
        Assert.False(installation.CanDownload);
        Assert.All(gaps.Where(gap => gap.DependencyType == ContentType.Addon), gap => Assert.True(gap.CanDownload));
    }

    /// <summary>
    /// A publisher-agnostic installation dependency is satisfied by any installation of a compatible game.
    /// </summary>
    [Fact]
    public void FindMissingDependencies_AnyPublisherInstallation_SatisfiedByCompatibleGame()
    {
        var addon = new ContentManifest
        {
            Id = ManifestId.Create("1.0.communityoutpost.addon.cbpc"),
            Name = "Control Bar Pro (Core)",
            ContentType = ContentType.Addon,
            Dependencies = [GenPatcherDependencyBuilder.CreateZeroHour104Dependency()],
        };
        var eaInstallation = new ContentManifest
        {
            Id = ManifestId.Create("1.104.eaapp.gameinstallation.custompath"),
            Name = "Zero Hour",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.ZeroHour,
        };

        Assert.Empty(_validator.FindMissingDependencies([addon, eaInstallation]));
    }

    /// <summary>
    /// A placeholder dependency is not satisfied by content for another game.
    /// </summary>
    [Fact]
    public void FindMissingDependencies_PlaceholderDependencyWrongGame_ReportsGap()
    {
        var addon = new ContentManifest
        {
            Id = ManifestId.Create("1.0.examplepublisher.addon.zhonly"),
            Name = "Zero Hour Addon",
            ContentType = ContentType.Addon,
            Dependencies =
            [
                new ContentDependency
                {
                    Name = "Zero Hour Installation",
                    DependencyType = ContentType.GameInstallation,
                    CompatibleGameTypes = [GameType.ZeroHour],
                },
            ],
        };
        var generals = new ContentManifest
        {
            Id = ManifestId.Create("1.108.steam.gameinstallation.generals"),
            Name = "Generals",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.Generals,
        };

        var gap = Assert.Single(_validator.FindMissingDependencies([addon, generals]));
        Assert.Equal(ContentType.GameInstallation, gap.DependencyType);
    }

    /// <summary>
    /// A matching item outside the declared version range does not satisfy the dependency.
    /// An item without a recorded version is not rejected for its version.
    /// </summary>
    /// <param name="version">The enabled item's version.</param>
    /// <param name="expectGap">Whether a gap is expected.</param>
    [Theory]
    [InlineData("1.0", true)]
    [InlineData("2.5", false)]
    [InlineData("", false)]
    public void FindMissingDependencies_VersionConstraint_IsApplied(string version, bool expectGap)
    {
        var addon = new ContentManifest
        {
            Id = ManifestId.Create("1.0.examplepublisher.mod.needsbase"),
            Name = "Needs Base",
            ContentType = ContentType.Mod,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create("1.0.examplepublisher.addon.basepack"),
                    Name = "Base Pack",
                    DependencyType = ContentType.Addon,
                    StrictPublisher = true,
                    MinVersion = "2.0",
                },
            ],
        };
        var basePack = new ContentManifest
        {
            Id = ManifestId.Create("1.0.examplepublisher.addon.basepack"),
            Name = "Base Pack",
            ContentType = ContentType.Addon,
            Version = version,
        };

        var gaps = _validator.FindMissingDependencies([addon, basePack]);

        Assert.Equal(expectGap, gaps.Count == 1);
    }

    /// <summary>
    /// The async overload loads the enabled manifests from the pool and skips IDs it cannot load.
    /// </summary>
    /// <returns>A task that completes when validation finishes.</returns>
    [Fact]
    public async Task FindMissingDependenciesAsync_LoadsEnabledManifestsFromPoolAsync()
    {
        var manifests = new Dictionary<string, ContentManifest>(StringComparer.OrdinalIgnoreCase)
        {
            [ControlBarProId] = CreateControlBarPro(),
            [ControlBarCoreId] = CreateAddon(ControlBarCoreId, "cbpc"),
            [ControlBarLanguageId] = CreateAddon(ControlBarLanguageId, "cben"),
            [InstallationId] = CreateInstallation(),
        };
        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId id, CancellationToken _) =>
                OperationResult<ContentManifest?>.CreateSuccess(manifests.GetValueOrDefault(id.Value)));
        var validator = new ContentDependencyValidator(pool.Object, NullLogger<ContentDependencyValidator>.Instance);

        var gaps = await validator.FindMissingDependenciesAsync([.. manifests.Keys, "1.0.unknown.addon.missing"]);

        var gap = Assert.Single(gaps);
        Assert.Equal("GenTool", gap.DisplayName);
    }

    private static ContentManifest CreateControlBarPro() => new()
    {
        Id = ManifestId.Create(ControlBarProId),
        Name = "Control Bar Pro (ExiLe)",
        ContentType = ContentType.Addon,
        TargetGame = GameType.ZeroHour,
        Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
        Metadata = new ContentMetadata { Tags = ["contentCode:cbpr"] },
        Dependencies = GenPatcherDependencyBuilder.GetDependencies("cbpr", GenPatcherContentRegistry.GetMetadata("cbpr")),
    };

    private static ContentManifest CreateAddon(string id, string contentCode) => new()
    {
        Id = ManifestId.Create(id),
        Name = GenPatcherContentRegistry.GetMetadata(contentCode).DisplayName,
        ContentType = ContentType.Addon,
        TargetGame = GameType.ZeroHour,
        Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
        Metadata = new ContentMetadata { Tags = [$"contentCode:{contentCode}"] },
    };

    private static ContentManifest CreateInstallation() => new()
    {
        Id = ManifestId.Create(InstallationId),
        Name = "Zero Hour Installation",
        ContentType = ContentType.GameInstallation,
        TargetGame = GameType.ZeroHour,
    };
}
