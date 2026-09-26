using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.Catalog;
using System.Collections.Generic;
using System.Text.Json;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Tests for bundle-component descriptors used by ContentBundle cards.
/// </summary>
public sealed class CatalogBundleComponentBuilderTests
{
    /// <summary>
    /// A bundle that depends on lemon-controlbar must expose one resolution dropdown with five options.
    /// </summary>
    [Fact]
    public void Build_LemonControlBarDependency_ExposesResolutionVariants()
    {
        var catalog = CreateCatalogWithLemonBundle();
        var bundle = catalog.Content.Single(item => item.Id == "bundle-stack");
        var release = bundle.Releases[0];

        var components = CatalogBundleComponentBuilder.Build(catalog, bundle, release);

        var lemon = Assert.Single(components, c => c.ContentId == "lemon-controlbar");
        Assert.Equal(5, lemon.Variants.Count);
        Assert.Contains(lemon.Variants, v => v.Label.Equals("720p", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lemon.Variants, v => v.Label.Equals("1080p", StringComparison.OrdinalIgnoreCase) && v.IsDefault);
        Assert.All(lemon.Variants, v => Assert.Equal("resolution", v.Axis, StringComparer.OrdinalIgnoreCase));
        Assert.All(lemon.Variants, v => Assert.False(string.IsNullOrWhiteSpace(v.CatalogId)));
        Assert.All(lemon.Variants, v => Assert.False(string.IsNullOrWhiteSpace(v.ReleaseJson)));

        var baseGame = Assert.Single(components, c => c.IsBaseGame);
        Assert.Equal("zerohour", baseGame.ContentId, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(baseGame.Variants);
    }

    /// <summary>
    /// Variant release JSON must contain only the selected artifact so the resolver downloads that ZIP.
    /// </summary>
    [Fact]
    public void Build_LemonVariantReleaseJson_ContainsSingleMatchingArtifact()
    {
        var catalog = CreateCatalogWithLemonBundle();
        var bundle = catalog.Content.Single(item => item.Id == "bundle-stack");
        var lemon = CatalogBundleComponentBuilder.Build(catalog, bundle, bundle.Releases[0])
            .Single(c => c.ContentId == "lemon-controlbar");
        var variant720 = lemon.Variants.Single(v => v.Label.Equals("720p", StringComparison.OrdinalIgnoreCase));

        var release = JsonSerializer.Deserialize<ContentRelease>(variant720.ReleaseJson);
        Assert.NotNull(release);
        var artifact = Assert.Single(release!.Artifacts);
        Assert.Contains("1280x720", artifact.Filename, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("720p", artifact.Variant, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that when a bundle declares BundledItems at the item level and release.Dependencies is empty,
    /// components are properly resolved from parent.BundledItems.
    /// </summary>
    [Fact]
    public void Build_UsesBundledItemsWhenDependenciesEmpty_ResolvesComponents()
    {
        var catalog = CreateCatalogWithLemonBundle();
        var bundle = catalog.Content.Single(item => item.Id == "bundle-stack");
        bundle.BundledItems =
        [
            new CatalogDependency { ContentId = "lemon-controlbar", DefaultVariant = "1440p" },
        ];
        bundle.Releases[0].Dependencies.Clear();

        var components = CatalogBundleComponentBuilder.Build(catalog, bundle, bundle.Releases[0]);

        var lemon = Assert.Single(components, c => c.ContentId == "lemon-controlbar");
        Assert.Equal(5, lemon.Variants.Count);
        var defaultVariant = Assert.Single(lemon.Variants, v => v.IsDefault);
        Assert.Equal("1440p", defaultVariant.Label);
    }

    /// <summary>
    /// Verifies that dynamic upstream siblings with zero static releases are resolved as available
    /// and their variants populated from UpstreamSync.AssetRules.
    /// </summary>
    [Fact]
    public void Build_UpstreamSyncWithoutReleases_ResolvesVariantsFromAssetRules()
    {
        var upstreamItem = new CatalogContentItem
        {
            Id = "superhackers-client",
            Name = "SuperHackers Game Client",
            ContentType = ContentType.GameClient,
            PublisherType = "thesuperhackers",
            TargetGame = GameType.ZeroHour,
            Releases = [],
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = "TheSuperHackers",
                Repository = "TheSuperHackers/GeneralsGameCode",
                VariantAxis = "channel",
                AssetRules =
                [
                    new CatalogUpstreamAssetRule { Pattern = ".*stable.*", Variant = "Stable", IsDefault = true },
                    new CatalogUpstreamAssetRule { Pattern = ".*nightly.*", Variant = "Nightly", IsDefault = false },
                ],
            },
        };

        var bundle = new CatalogContentItem
        {
            Id = "bundle-competitive",
            Name = "Competitive Bundle",
            ContentType = ContentType.ContentBundle,
            TargetGame = GameType.ZeroHour,
            BundledItems =
            [
                new CatalogDependency { ContentId = "superhackers-client", DefaultVariant = "Stable" },
            ],
            Releases =
            [
                new ContentRelease { Version = "1.0.0", IsLatest = true },
            ],
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content = [upstreamItem, bundle],
        };

        var components = CatalogBundleComponentBuilder.Build(catalog, bundle, bundle.Releases[0]);

        var upstreamComp = Assert.Single(components, c => c.ContentId == "superhackers-client");
        Assert.True(upstreamComp.IsAvailable);
        Assert.Equal(2, upstreamComp.Variants.Count);
        var def = Assert.Single(upstreamComp.Variants, v => v.IsDefault);
        Assert.Equal("Stable", def.Label);
    }

    /// <summary>
    /// A bundle dependency requiring a specific version constraint that cannot be satisfied by an asset-rules item
    /// must be marked unavailable with an appropriate reason, rather than fabricating a latest release.
    /// </summary>
    [Fact]
    public void Build_AssetRulesItemWithUnsatisfiedSpecificConstraint_MarksComponentUnavailable()
    {
        var sibling = new CatalogContentItem
        {
            Id = "upstream-client",
            Name = "Upstream Client",
            ContentType = ContentType.GameClient,
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = "GitHubReleases",
                Repository = "Test/TestRepo",
                VariantAxis = "variant",
                AssetRules =
                [
                    new CatalogUpstreamAssetRule { Pattern = @".*\.zip", Variant = "Default", IsDefault = true },
                ],
            },
        };

        var bundle = new CatalogContentItem
        {
            Id = "test-bundle",
            Name = "Test Bundle",
            ContentType = ContentType.ContentBundle,
            Releases =
            [
                new ContentRelease
                {
                    Version = "1.0.0",
                    Dependencies =
                    [
                        new CatalogDependency
                        {
                            ContentId = "upstream-client",
                            VersionConstraint = "2.0.0",
                        },
                    ],
                },
            ],
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test", Name = "Test" },
            Content = [sibling, bundle],
        };

        var components = CatalogBundleComponentBuilder.Build(catalog, bundle, bundle.Releases[0]);
        var clientComponent = Assert.Single(components, c => c.ContentId == "upstream-client");

        Assert.False(clientComponent.IsAvailable);
        Assert.Contains("matches constraint", clientComponent.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that HydrateSyntheticBundleReleases handles items with null BundledItems or null Releases safely.
    /// </summary>
    [Fact]
    public void HydrateSyntheticBundleReleases_WithNullBundledItems_DoesNotThrowAndAddsRelease()
    {
        var item = new CatalogContentItem
        {
            Id = "bundle-null-items",
            Name = "Bundle with Null Items",
            ContentType = ContentType.ContentBundle,
            BundledItems = null!,
            Releases = null!,
        };

        CatalogBundleComponentBuilder.HydrateSyntheticBundleReleases([item]);

        Assert.NotNull(item.Releases);
        var release = Assert.Single(item.Releases);
        Assert.Equal("1.0.0", release.Version);
        Assert.True(release.IsLatest);
        Assert.NotNull(release.Dependencies);
        Assert.Empty(release.Dependencies);
    }

    private static PublisherCatalog CreateCatalogWithLemonBundle()
    {
        var lemon = new CatalogContentItem
        {
            Id = "lemon-controlbar",
            Name = "Control Bar Pro Lemon Edition ZH",
            ContentType = ContentType.Addon,
            PublisherType = "github",
            TargetGame = GameType.ZeroHour,
            Releases =
            [
                new ContentRelease
                {
                    Version = "1.3",
                    IsLatest = true,
                    Artifacts =
                    [
                        CreateArtifact("1280x720", "720p", isDefault: false),
                        CreateArtifact("1600x900", "900p", isDefault: false),
                        CreateArtifact("1920x1080", "1080p", isDefault: true),
                        CreateArtifact("2560x1440", "1440p", isDefault: false),
                        CreateArtifact("3840x2160", "4K", isDefault: false),
                    ],
                    Dependencies =
                    [
                        new CatalogDependency
                        {
                            PublisherId = "ea",
                            ContentId = "zerohour",
                            VersionConstraint = "1.04",
                        },
                    ],
                },
            ],
        };

        var bundle = new CatalogContentItem
        {
            Id = "bundle-stack",
            Name = "Ultimate Stack",
            ContentType = ContentType.ContentBundle,
            TargetGame = GameType.ZeroHour,
            Releases =
            [
                new ContentRelease
                {
                    Version = "2026.07.31",
                    IsLatest = true,
                    Artifacts = [],
                    Dependencies =
                    [
                        new CatalogDependency
                        {
                            PublisherId = "ea",
                            ContentId = "zerohour",
                            VersionConstraint = "1.04",
                        },
                        new CatalogDependency
                        {
                            PublisherId = "github",
                            ContentId = "lemon-controlbar",
                            VersionConstraint = ">=1.3",
                            ContentType = "Addon",
                        },
                    ],
                },
            ],
        };

        return new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "genhub-test-publishers", Name = "GenHub Test Publishers" },
            Content = [lemon, bundle],
        };
    }

    private static ReleaseArtifact CreateArtifact(string resolution, string variant, bool isDefault) =>
        new()
        {
            Filename = $"ControlBarProLemonEditionZH_v1.3_{resolution}.zip",
            DownloadUrl = $"https://example.com/{resolution}.zip",
            Size = 1_000_000,
            ContentType = "application/zip",
            IsPrimary = isDefault,
            VariantAxis = "resolution",
            Variant = variant,
            IsDefaultVariant = isDefault,
        };
}
