using System.Net.Http;
using System.Text.Json;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Downloads;

/// <summary>
/// Tests card metadata presentation supplied by publisher discoverers.
/// </summary>
public sealed class ContentGridItemViewModelTests
{
    /// <summary>
    /// Verifies placeholder versions are hidden while player-count metadata is rendered as a clear badge.
    /// </summary>
    [Fact]
    public void Constructor_AodMapWithPlaceholderVersion_ExposesPlayerBadgeAndHidesVersion()
    {
        var searchResult = new ContentSearchResult
        {
            Id = "aod-test-map",
            Name = "AOD Test Map",
            Version = "0",
            ProviderName = AODMapsConstants.DiscovererSourceName,
        };
        ContentCardBadgeHelper.ApplyPlayerCount(searchResult, 4);
        ContentCardBadgeHelper.ApplyCategory(searchResult, AODMapsConstants.CategoryAoa);

        var viewModel = CreateViewModel(searchResult);

        Assert.False(viewModel.HasDisplayVersion);
        Assert.True(viewModel.HasPlayerCountBadge);
        Assert.Equal("4 players", viewModel.PlayerCountBadge);
        Assert.True(viewModel.HasCategoryBadge);
        Assert.Equal(AODMapsConstants.CategoryAoa, viewModel.CategoryBadge);
    }

    /// <summary>
    /// Verifies CNCLabs-style player tags are promoted into the shared card badge surface.
    /// </summary>
    [Fact]
    public void Constructor_CncLabsPlayerTag_ExposesPlayerBadge()
    {
        var searchResult = new ContentSearchResult
        {
            Id = "cnclabs-map",
            Name = "CNC Labs Map",
            Version = "1.0",
            ProviderName = CNCLabsConstants.SourceName,
        };
        searchResult.Tags.Add("6 Players");
        searchResult.Tags.Add("Multiplayer-only");
        ContentCardBadgeHelper.PromoteFromTags(searchResult);

        var viewModel = CreateViewModel(searchResult);

        Assert.True(viewModel.HasPlayerCountBadge);
        Assert.Equal("6 players", viewModel.PlayerCountBadge);
    }

    /// <summary>
    /// Verifies catalog metadata categories surface as card badges.
    /// </summary>
    [Fact]
    public void Constructor_CatalogCategoryMetadata_ExposesCategoryBadge()
    {
        var searchResult = new ContentSearchResult
        {
            Id = "catalog-map",
            Name = "Catalog Map",
            Version = "1.2.3",
            ProviderName = "Sample Catalog",
        };
        ContentCardBadgeHelper.ApplyCategory(searchResult, "Survival");

        var viewModel = CreateViewModel(searchResult);

        Assert.True(viewModel.HasCategoryBadge);
        Assert.Equal("Survival", viewModel.CategoryBadge);
    }

    /// <summary>
    /// Verifies that category badges matching the content type (e.g. Content Bundle vs ContentBundle) are suppressed.
    /// </summary>
    [Fact]
    public void Constructor_ContentBundleCategoryMatchingContentType_HidesDuplicateCategoryBadge()
    {
        var searchResult = new ContentSearchResult
        {
            Id = "bundle-test",
            Name = "TheSuperHackers Latest Stack",
            ContentType = ContentType.ContentBundle,
            ProviderName = "TheSuperHackers",
        };
        ContentCardBadgeHelper.ApplyCategory(searchResult, "Content Bundle");

        var viewModel = CreateViewModel(searchResult);

        Assert.Equal("Content Bundle", viewModel.ContentTypeDisplay);
        Assert.False(viewModel.HasCategoryBadge);
    }

    /// <summary>
    /// Verifies version badge prepends 'v' for numeric versions and collapses build-stamp
    /// tags (e.g. weekly-2026-07-17) down to their trailing date.
    /// </summary>
    /// <param name="version">The raw version string.</param>
    /// <param name="expectedBadge">The expected version badge text.</param>
    [Theory]
    [InlineData("1.04", "v1.04")]
    [InlineData("weekly-2026-07-17", "2026-07-17")]
    [InlineData("nightly-2026-08-01", "2026-08-01")]
    [InlineData("v1.2", "v1.2")]
    public void VersionBadge_FormatsNumericAndWeeklyVersionsCorrectly(string version, string expectedBadge)
    {
        var searchResult = new ContentSearchResult
        {
            Id = "version-test",
            Name = "Version Test Item",
            Version = version,
        };

        var viewModel = CreateViewModel(searchResult);

        Assert.Equal(expectedBadge, viewModel.VersionBadge);
    }

    /// <summary>
    /// Verifies catalog banners win over publisher avatars for card thumbnails.
    /// </summary>
    [Fact]
    public void ThumbnailUrl_PrefersBannerOverIcon()
    {
        var searchResult = new ContentSearchResult
        {
            Id = "bundle-test",
            Name = "Bundle Test",
            IconUrl = "avares://GenHub/Assets/Logos/publisher.png",
            BannerUrl = "avares://GenHub/Assets/Covers/china-cover.png",
        };

        var viewModel = CreateViewModel(searchResult);

        Assert.Equal(searchResult.BannerUrl, viewModel.ThumbnailUrl);
    }

    /// <summary>
    /// Verifies includes summaries from catalog metadata surface on the card.
    /// </summary>
    [Fact]
    public void IncludesSummary_FromMetadata_IsExposedOnCard()
    {
        var searchResult = new ContentSearchResult
        {
            Id = "bundle-test",
            Name = "Ultimate Stack",
        };
        ContentCardBadgeHelper.ApplyIncludesSummary(
            searchResult,
            ["GenTool 8.6", "Control Bar Pro", "Legionnaire Hotkeys"]);

        var viewModel = CreateViewModel(searchResult);

        Assert.True(viewModel.HasIncludesSummary);
        Assert.Equal("GenTool 8.6, Control Bar Pro, Legionnaire Hotkeys", viewModel.IncludesSummary);
    }

    /// <summary>
    /// Verifies card tag chips are capped for glanceability.
    /// </summary>
    [Fact]
    public void DisplayCardTags_CapsAtThree()
    {
        var searchResult = new ContentSearchResult
        {
            Id = "taggy",
            Name = "Taggy",
            Version = "1.0",
        };
        foreach (var tag in new[] { "a", "b", "c", "d", "e" })
        {
            searchResult.Tags.Add(tag);
        }

        var viewModel = CreateViewModel(searchResult);

        Assert.Equal(3, viewModel.DisplayCardTags.Count);
        Assert.Equal(5, viewModel.CardTags.Count);
    }

    /// <summary>
    /// A ContentBundle card must list each member and keep Add to Profile hidden until every
    /// required selected variant is acquired.
    /// </summary>
    [Fact]
    public void BundleCard_AddToProfile_RequiresEverySelectedVariantDownloaded()
    {
        var viewModel = CreateViewModel(CreateBundleSearchResult());
        viewModel.LoadBundleComponents();

        Assert.True(viewModel.HasBundleComponents);
        Assert.False(viewModel.HasIncludesSummary);
        Assert.Equal(2, viewModel.BundleComponents.Count);
        var lemon = Assert.Single(viewModel.BundleComponents, c => c.CatalogContentId == "lemon-controlbar");
        Assert.True(lemon.HasVariants);
        Assert.True(viewModel.ShowDownloadButton);
        Assert.False(viewModel.ShowAddToProfileButton);

        MarkAllSelectedDownloaded(viewModel);
        Assert.True(viewModel.AreBundleComponentsReadyForProfile);
        Assert.True(viewModel.ShowAddToProfileButton);
        Assert.False(viewModel.ShowDownloadButton);

        var variant720 = lemon.Variants.Single(v => v.Name.Contains("720p", StringComparison.OrdinalIgnoreCase));
        variant720.CurrentState = ContentState.NotDownloaded;
        lemon.SelectedVariant = variant720;

        Assert.False(viewModel.AreBundleComponentsReadyForProfile);
        Assert.True(viewModel.ShowDownloadButton);
        Assert.False(viewModel.ShowAddToProfileButton);
    }

    /// <summary>
    /// Verifies Description strips HTML tags and decodes entities.
    /// </summary>
    [Fact]
    public void Description_WithHtmlTags_NormalizesAndStripsTags()
    {
        var searchResult = new ContentSearchResult
        {
            Id = "test-map",
            Name = "Test Map",
            Description = "<p>The Ships &amp; Boats War map is a game map that takes place almost</p>",
            ProviderName = CNCLabsConstants.SourceName,
        };

        var viewModel = CreateViewModel(searchResult);

        Assert.Equal("The Ships & Boats War map is a game map that takes place almost", viewModel.Description);
    }

    /// <summary>
    /// Verifies ShortDescription cleans HTML and multi-line text to single line and truncates with ellipsis.
    /// </summary>
    [Fact]
    public void ShortDescription_WithHtmlAndNewlines_CleansToSingleLineAndTruncates()
    {
        var longHtmlDescription = "<p>The Ships and Boats War map is a game map that takes place almost in the ocean where ships battle continuously and naval warfare dominates the landscape.</p>";
        var searchResult = new ContentSearchResult
        {
            Id = "test-map",
            Name = "Test Map",
            Description = longHtmlDescription,
            ProviderName = CNCLabsConstants.SourceName,
        };

        var viewModel = CreateViewModel(searchResult);

        Assert.True(viewModel.HasShortDescription);
        Assert.Equal(90, viewModel.ShortDescription.Length);
        Assert.EndsWith("...", viewModel.ShortDescription, StringComparison.Ordinal);
        Assert.DoesNotContain("<p>", viewModel.ShortDescription, StringComparison.Ordinal);
        Assert.DoesNotContain("</p>", viewModel.ShortDescription, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", viewModel.ShortDescription, StringComparison.Ordinal);
    }

    private static void MarkAllSelectedDownloaded(ContentGridItemViewModel viewModel)
    {
        foreach (var component in viewModel.BundleComponents)
        {
            if (component.SelectedVariant != null)
            {
                component.SelectedVariant.CurrentState = ContentState.Downloaded;
            }

            foreach (var variant in component.Variants)
            {
                if (component.SelectedVariant != null &&
                    ReferenceEquals(variant, component.SelectedVariant))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(variant.Name) ||
                    string.Equals(variant.Name, component.Name, StringComparison.OrdinalIgnoreCase))
                {
                    variant.CurrentState = ContentState.Downloaded;
                }
            }

            component.CurrentState = ContentState.Downloaded;
        }
    }

    private static ContentSearchResult CreateBundleSearchResult()
    {
        var descriptors = new List<CatalogBundleComponentDescriptor>
        {
            new()
            {
                ContentId = "gentool-suite-86",
                Name = "GenTool 8.9 Suite",
                ContentType = nameof(ContentType.Addon),
                Variants =
                [
                    new CatalogBundleComponentVariantDescriptor
                    {
                        Label = string.Empty,
                        CatalogId = "1.89.genhubtestpublishers.addon.gentoolsuite86",
                        IsDefault = true,
                    },
                ],
            },
            new()
            {
                ContentId = "lemon-controlbar",
                Name = "Control Bar Pro Lemon Edition ZH",
                ContentType = nameof(ContentType.Addon),
                Variants =
                [
                    new CatalogBundleComponentVariantDescriptor
                    {
                        Label = "720p",
                        Axis = "resolution",
                        CatalogId = "1.13.genhubtestpublishers.addon.lemoncontrolbar720p",
                    },
                    new CatalogBundleComponentVariantDescriptor
                    {
                        Label = "1080p",
                        Axis = "resolution",
                        CatalogId = "1.13.genhubtestpublishers.addon.lemoncontrolbar1080p",
                        IsDefault = true,
                    },
                ],
            },
        };

        var searchResult = new ContentSearchResult
        {
            Id = "1.20260731.genhubtestpublishers.contentbundle.bundleultimatezhcommunitystack",
            Name = "Ultimate ZH Community Stack",
            ContentType = ContentType.ContentBundle,
        };
        searchResult.ResolverMetadata[CatalogConstants.BundleComponentsJsonMetadataKey] =
            JsonSerializer.Serialize(descriptors);
        searchResult.ResolverMetadata[CatalogConstants.PublisherProfileJsonMetadataKey] =
            JsonSerializer.Serialize(new PublisherProfile { Id = "genhub-test-publishers", Name = "Test" });
        return searchResult;
    }

    private static ContentGridItemViewModel CreateViewModel(ContentSearchResult searchResult) =>
        new(
            searchResult,
            new Mock<IContentStateService>().Object,
            new Mock<ILogger<ContentGridItemViewModel>>().Object);
}
