using System.Net.Http;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Downloads;

/// <summary>
/// Verifies game release variant display names, swap targeting, and card UI state.
/// </summary>
public sealed class ReleaseVariantSelectionTests
{
    /// <summary>
    /// Verifies that Zero Hour variant is marked default and clearly labeled in release variants.
    /// </summary>
    [Fact]
    public void Variants_ZeroHourIsDefaultAndClearlyLabeled()
    {
        var (generalsCard, zeroHourCard, variantList) = CreateSuperHackersPair();

        Assert.Equal(GameType.ZeroHour, zeroHourCard.TargetGame);
        Assert.Equal("weekly-2026-07-17 — Zero Hour", variantList[0].Name);
        Assert.True(variantList[0].IsDefault);
        Assert.Equal("weekly-2026-07-17 — Generals", variantList[1].Name);
        Assert.False(variantList[1].IsDefault);
        Assert.Equal(generalsCard.Id, variantList[1].ManifestId);
    }

    /// <summary>
    /// Selecting Generals must swap TargetGame and asset-name onto the card SearchResult
    /// so download resolves the Generals zip, not Zero Hour.
    /// </summary>
    [Fact]
    public void SelectedVariant_SwapsAssetMetadataOntoCardSearchResult()
    {
        var (generalsCard, zeroHourCard, _) = CreateSuperHackersPair();
        var vm = CreateCard(zeroHourCard);

        var zhInfo = zeroHourCard.Variants![0];
        var genInfo = zeroHourCard.Variants![1];

        vm.AddVariant(
            new InstallableVariant
            {
                Name = VariantSwap.ResolveDisplayName(zeroHourCard, zhInfo),
                ManifestId = VariantSwap.ResolveCatalogKey(zeroHourCard, zhInfo),
            },
            zeroHourCard);
        vm.AddVariant(
            new InstallableVariant
            {
                Name = VariantSwap.ResolveDisplayName(generalsCard, genInfo),
                ManifestId = VariantSwap.ResolveCatalogKey(generalsCard, genInfo),
            },
            generalsCard);

        vm.SelectedVariant = vm.Variants[0];
        Assert.Equal(GameType.ZeroHour, vm.SearchResult.TargetGame);
        Assert.Equal("generalszh-weekly-2026-07-17.zip", vm.SearchResult.ResolverMetadata["asset-name"]);
        Assert.Equal("weekly-2026-07-17 — Zero Hour", vm.Name);

        vm.SelectedVariant = vm.Variants[1];
        Assert.Equal(GameType.Generals, vm.SearchResult.TargetGame);
        Assert.Equal("generals-weekly-2026-07-17.zip", vm.SearchResult.ResolverMetadata["asset-name"]);
        Assert.Equal("weekly-2026-07-17 — Generals", vm.Name);

        // Dictionary snapshots must remain intact after swaps (no shared-reference corruption).
        vm.SelectedVariant = vm.Variants[0];
        Assert.Equal(GameType.ZeroHour, vm.SearchResult.TargetGame);
        Assert.Equal("generalszh-weekly-2026-07-17.zip", vm.SearchResult.ResolverMetadata["asset-name"]);
    }

    /// <summary>
    /// After one variant is downloaded, switching to an undownloaded sibling must show Download
    /// instead of Add to Profile.
    /// </summary>
    [Fact]
    public void SelectedVariant_ButtonStateTracksPerVariantInstallState()
    {
        var (generalsCard, zeroHourCard, _) = CreateSuperHackersPair();
        var vm = CreateCard(zeroHourCard);

        var zhKey = VariantSwap.ResolveCatalogKey(zeroHourCard, zeroHourCard.Variants![0]);
        var genKey = VariantSwap.ResolveCatalogKey(generalsCard, zeroHourCard.Variants![1]);

        vm.AddVariant(new InstallableVariant { Name = "ZH", ManifestId = zhKey }, zeroHourCard);
        vm.AddVariant(new InstallableVariant { Name = "Generals", ManifestId = genKey }, generalsCard);

        vm.Variants[0].CurrentState = ContentState.Downloaded;
        vm.SelectedVariant = vm.Variants[0];
        vm.IsDownloaded = true;
        vm.CurrentState = ContentState.Downloaded;

        Assert.True(vm.ShowAddToProfileButton);
        Assert.False(vm.ShowDownloadButton);

        vm.SelectedVariant = vm.Variants[1];
        Assert.True(vm.ShowDownloadButton);
        Assert.False(vm.ShowAddToProfileButton);

        vm.SelectedVariant = vm.Variants[0];
        Assert.True(vm.ShowAddToProfileButton);
        Assert.False(vm.ShowDownloadButton);
    }

    /// <summary>
    /// MarkVariantDownloaded must flip the matching dropdown checkmark immediately.
    /// </summary>
    [Fact]
    public void MarkVariantDownloaded_UpdatesDropdownCheckmark()
    {
        var (_, zeroHourCard, _) = CreateSuperHackersPair();
        var vm = CreateCard(zeroHourCard);

        var zhKey = VariantSwap.ResolveCatalogKey(zeroHourCard, zeroHourCard.Variants![0]);
        vm.AddVariant(new InstallableVariant { Name = "ZH", ManifestId = zhKey, VariantType = "game-type" }, zeroHourCard);
        vm.SelectedVariant = vm.Variants[0];

        Assert.Equal(ContentState.NotDownloaded, vm.Variants[0].CurrentState);

        vm.MarkVariantDownloaded(zhKey, "1.20260717.thesuperhackers.gameclient.zerohour");

        Assert.Equal(ContentState.Downloaded, vm.Variants[0].CurrentState);
        Assert.True(vm.ShowAddToProfileButton);
    }

    /// <summary>
    /// Single-axis variants (e.g. resolution) collapse to one unlabeled axis group.
    /// </summary>
    [Fact]
    public void VariantAxes_SingleAxis_OneGroupWithoutLabel()
    {
        var card = new ContentSearchResult
        {
            Id = "catalog.lemon.1080p",
            Name = "Control Bar Pro Lemon Edition ZH - 1080p",
            VariantFamilyName = "Control Bar Pro Lemon Edition ZH",
            ProviderName = "catalog",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };

        var vm = CreateCard(card);
        vm.AddVariant(new InstallableVariant { Name = "720p", ManifestId = "lemon.720p", VariantType = "resolution" }, card);
        vm.AddVariant(new InstallableVariant { Name = "1080p", ManifestId = "lemon.1080p", VariantType = "resolution" }, card);
        vm.AddVariant(new InstallableVariant { Name = "4K", ManifestId = "lemon.4k", VariantType = "resolution" }, card);
        vm.SelectedVariant = vm.Variants[1];

        Assert.Single(vm.VariantAxes);
        Assert.False(vm.HasMultipleVariantAxes);
        Assert.False(vm.VariantAxes[0].ShowAxisLabel);
        Assert.Equal("Resolution", vm.VariantAxes[0].AxisLabel);
        Assert.Equal(3, vm.VariantAxes[0].Options.Count);
        Assert.Same(vm.Variants[1], vm.VariantAxes[0].SelectedOption);
    }

    /// <summary>
    /// Distinct VariantType values produce multiple labeled axis groups.
    /// </summary>
    [Fact]
    public void VariantAxes_MultipleAxes_PartitionsAndLabels()
    {
        var card = new ContentSearchResult
        {
            Id = "multi.axis",
            Name = "Multi",
            ProviderName = "test",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };

        var vm = CreateCard(card);
        vm.AddVariant(new InstallableVariant { Name = "1080p", ManifestId = "a.1080", VariantType = "resolution" }, card);
        vm.AddVariant(new InstallableVariant { Name = "4K", ManifestId = "a.4k", VariantType = "resolution" }, card);
        vm.AddVariant(new InstallableVariant { Name = "EN", ManifestId = "a.en", VariantType = "language" }, card);
        vm.AddVariant(new InstallableVariant { Name = "DE", ManifestId = "a.de", VariantType = "language" }, card);

        Assert.Equal(2, vm.VariantAxes.Count);
        Assert.True(vm.HasMultipleVariantAxes);
        Assert.All(vm.VariantAxes, g => Assert.True(g.ShowAxisLabel));
        Assert.Equal("Resolution", vm.VariantAxes[0].AxisLabel);
        Assert.Equal(2, vm.VariantAxes[0].Options.Count);
        Assert.Equal("Language", vm.VariantAxes[1].AxisLabel);
        Assert.Equal(2, vm.VariantAxes[1].Options.Count);
    }

    /// <summary>
    /// Empty VariantType values group into a single default axis (legacy content).
    /// </summary>
    [Fact]
    public void VariantAxes_EmptyVariantType_GroupsAsDefault()
    {
        var (generalsCard, zeroHourCard, _) = CreateSuperHackersPair();
        var vm = CreateCard(zeroHourCard);

        vm.AddVariant(new InstallableVariant { Name = "ZH", ManifestId = "zh" }, zeroHourCard);
        vm.AddVariant(new InstallableVariant { Name = "Generals", ManifestId = "gen" }, generalsCard);

        Assert.Single(vm.VariantAxes);
        Assert.Equal("default", vm.VariantAxes[0].AxisKey);
        Assert.False(vm.HasMultipleVariantAxes);
    }

    /// <summary>
    /// Picking an axis option sets SelectedVariant and swaps SearchResult metadata.
    /// </summary>
    [Fact]
    public void VariantAxes_SelectingOption_SetsSelectedVariantAndSwaps()
    {
        var (generalsCard, zeroHourCard, _) = CreateSuperHackersPair();
        var vm = CreateCard(zeroHourCard);

        var zhInfo = zeroHourCard.Variants![0];
        var genInfo = zeroHourCard.Variants![1];

        vm.AddVariant(
            new InstallableVariant
            {
                Name = VariantSwap.ResolveDisplayName(zeroHourCard, zhInfo),
                ManifestId = VariantSwap.ResolveCatalogKey(zeroHourCard, zhInfo),
                VariantType = "game-type",
            },
            zeroHourCard);
        vm.AddVariant(
            new InstallableVariant
            {
                Name = VariantSwap.ResolveDisplayName(generalsCard, genInfo),
                ManifestId = VariantSwap.ResolveCatalogKey(generalsCard, genInfo),
                VariantType = "game-type",
            },
            generalsCard);

        vm.SelectedVariant = vm.Variants[0];
        Assert.Equal(GameType.ZeroHour, vm.SearchResult.TargetGame);

        vm.VariantAxes[0].SelectedOption = vm.Variants[1];
        Assert.Same(vm.Variants[1], vm.SelectedVariant);
        Assert.Equal(GameType.Generals, vm.SearchResult.TargetGame);
    }

    /// <summary>
    /// Display names must keep the Generals / Zero Hour suffix even when the sibling Name
    /// was stripped to the family name.
    /// </summary>
    [Fact]
    public void ResolveDisplayName_KeepsVariantLabelWhenNameStrippedToFamily()
    {
        var (_, zeroHourCard, variantList) = CreateSuperHackersPair();
        zeroHourCard.Name = zeroHourCard.VariantFamilyName!;

        var name = VariantSwap.ResolveDisplayName(zeroHourCard, variantList[0]);
        Assert.Equal("weekly-2026-07-17 — Zero Hour", name);
    }

    /// <summary>
    /// Resolution-only variant labels (Control Bar Pro) must be prefixed with the family name
    /// so cards do not show bare titles like "1080p (Recommended)".
    /// </summary>
    [Fact]
    public void ResolveDisplayName_PrefixesFamilyForShortResolutionLabels()
    {
        var card = new ContentSearchResult
        {
            Id = "1.0.communityoutpost.addon.cbpx",
            Name = "Control Bar Pro (Xezon)",
            VariantFamilyName = "Control Bar Pro (Xezon)",
            ProviderName = "communityoutpost",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };

        var info = new ContentVariantInfo
        {
            Id = "1080p",
            Name = "1080p (Recommended)",
            ManifestId = "1.0.communityoutpost.addon.cbpx-1080p",
            VariantType = "resolution",
            IsDefault = true,
        };

        card.Name = "Control Bar Pro (Xezon) - 1080p (Recommended)";
        Assert.Equal(
            "Control Bar Pro (Xezon) - 1080p (Recommended)",
            VariantSwap.ResolveDisplayName(card, info));

        card.Name = "Control Bar Pro (Xezon)";
        Assert.Equal(
            "Control Bar Pro (Xezon) - 1080p (Recommended)",
            VariantSwap.ResolveDisplayName(card, info));
    }

    /// <summary>
    /// VariantSwap.Apply must copy Name and TargetGame so UI and resolvers stay aligned.
    /// </summary>
    [Fact]
    public void VariantSwap_Apply_CopiesNameAndTargetGame()
    {
        var (generalsCard, zeroHourCard, _) = CreateSuperHackersPair();
        VariantSwap.Apply(zeroHourCard, generalsCard);

        Assert.Equal(GameType.Generals, zeroHourCard.TargetGame);
        Assert.Equal("weekly-2026-07-17 — Generals", zeroHourCard.Name);
        Assert.Equal("generals-weekly-2026-07-17.zip", zeroHourCard.ResolverMetadata["asset-name"]);
        Assert.Equal("thesuperhackers.gameclient.weekly-2026-07-17", zeroHourCard.VariantGroupId);
    }

    /// <summary>
    /// Verifies that VariantSwap.FindMatchingVariant resolves variants using the multi-tier heuristic.
    /// </summary>
    [Fact]
    public void VariantSwap_FindMatchingVariant_ResolvesAcrossTiers()
    {
        var v1 = new InstallableVariant { Name = "Control Bar 1080p (Recommended)", ManifestId = "1.0.addon.cbpx-1080p" };
        var v2 = new InstallableVariant { Name = "Control Bar 4K", ManifestId = "1.0.addon.cbpx-4k" };
        var v3 = new InstallableVariant { Name = "Russian Language Pack", ManifestId = "1.0.addon.lang-ru" };
        var list = new[] { v1, v2, v3 };

        // 1. Exact manifest ID
        Assert.Same(v1, VariantSwap.FindMatchingVariant(list, "1.0.addon.cbpx-1080p"));

        // 2. Exact name
        Assert.Same(v2, VariantSwap.FindMatchingVariant(list, "Control Bar 4K"));

        // 3. ManifestId suffix
        Assert.Same(v1, VariantSwap.FindMatchingVariant(list, "1080p"));
        Assert.Same(v3, VariantSwap.FindMatchingVariant(list, "ru"));

        // 4. Identifier suffix
        Assert.Same(v2, VariantSwap.FindMatchingVariant(list, "prefix.1.0.addon.cbpx-4k"));

        // 5. Name contains
        Assert.Same(v3, VariantSwap.FindMatchingVariant(list, "Russian"));

        // Edge cases
        Assert.Null(VariantSwap.FindMatchingVariant(list, null));
        Assert.Null(VariantSwap.FindMatchingVariant(list, string.Empty));
        Assert.Null(VariantSwap.FindMatchingVariant(list, "nonexistent"));
        Assert.Null(VariantSwap.FindMatchingVariant(null, "1080p"));
    }

    /// <summary>
    /// Verifies that normalized synthesized manifest IDs without hyphens match hyphenated or bare identifiers,
    /// while rejecting mismatched package/version prefixes.
    /// </summary>
    [Fact]
    public void VariantSwap_FindMatchingVariant_SynthesizedManifestIdWithoutHyphens()
    {
        var v1 = new InstallableVariant { Name = "Control Bar 1080p", ManifestId = "1.0.ea.gameinstallation.cbpx1080p" };
        var v2 = new InstallableVariant { Name = "Control Bar 4K", ManifestId = "1.0.ea.gameinstallation.cbpx4k" };
        var list = new[] { v1, v2 };

        Assert.Same(v1, VariantSwap.FindMatchingVariant(list, "1080p"));
        Assert.Same(v1, VariantSwap.FindMatchingVariant(list, "cbpx-1080p"));
        Assert.Same(v2, VariantSwap.FindMatchingVariant(list, "4k"));
        Assert.Same(v2, VariantSwap.FindMatchingVariant(list, "cbpx-4k"));

        // Does not match across mismatched version prefixes
        Assert.Null(VariantSwap.FindMatchingVariant(list, "1.10.ea.gameinstallation.cbpx-1080p"));
    }

    /// <summary>
    /// Verifies that short identifiers (e.g. 'ru') exercise the name-scoring tier without resolving earlier
    /// manifest ID suffix tiers, and do not select unrelated names containing the substring (e.g. 'Belarusian').
    /// </summary>
    [Fact]
    public void VariantSwap_FindMatchingVariant_ShortLanguageIdentifier_DoesNotCollideWithSubstring()
    {
        // Manifest IDs avoid ending with .ru or .be so resolution exercises the Tier 6 name scoring path
        var vBelarusian = new InstallableVariant { Name = "Belarusian Language Pack", ManifestId = "1.0.addon.belarusian-lang" };
        var vRussian = new InstallableVariant { Name = "Russian Language Pack", ManifestId = "1.0.addon.russian-lang" };
        var list = new[] { vBelarusian, vRussian };

        // Should resolve Russian via token-prefix score, not Belarusian (even though Belarusian contains 'ru' and is first in list)
        Assert.Same(vRussian, VariantSwap.FindMatchingVariant(list, "ru"));
        Assert.Same(vBelarusian, VariantSwap.FindMatchingVariant(list, "be"));
    }

    private static (ContentSearchResult Generals, ContentSearchResult ZeroHour, ContentVariantInfo[] Variants) CreateSuperHackersPair()
    {
        var generalsCard = new ContentSearchResult
        {
            Id = "github.thesuperhackers.generalsgamecode.weekly-2026-07-17.generals",
            Name = "weekly-2026-07-17 — Generals",
            Version = "weekly-2026-07-17",
            TargetGame = GameType.Generals,
            ProviderName = "GitHub",
            ContentType = ContentType.GameClient,
            RequiresResolution = true,
            ResolverId = "GitHubRelease",
            VariantGroupId = "thesuperhackers.gameclient.weekly-2026-07-17",
            VariantFamilyName = "weekly-2026-07-17",
            ResolverMetadata =
            {
                ["owner"] = "TheSuperHackers",
                ["repo"] = "GeneralsGameCode",
                ["tag"] = "weekly-2026-07-17",
                ["asset-name"] = "generals-weekly-2026-07-17.zip",
                ["RequestedGameType"] = "Generals",
            },
        };

        var zeroHourCard = new ContentSearchResult
        {
            Id = "github.thesuperhackers.generalsgamecode.weekly-2026-07-17.zerohour",
            Name = "weekly-2026-07-17 — Zero Hour",
            Version = "weekly-2026-07-17",
            TargetGame = GameType.ZeroHour,
            ProviderName = "GitHub",
            ContentType = ContentType.GameClient,
            RequiresResolution = true,
            ResolverId = "GitHubRelease",
            VariantGroupId = "thesuperhackers.gameclient.weekly-2026-07-17",
            VariantFamilyName = "weekly-2026-07-17",
            ResolverMetadata =
            {
                ["owner"] = "TheSuperHackers",
                ["repo"] = "GeneralsGameCode",
                ["tag"] = "weekly-2026-07-17",
                ["asset-name"] = "generalszh-weekly-2026-07-17.zip",
                ["RequestedGameType"] = "ZeroHour",
            },
        };

        var variantList = new[]
        {
            new ContentVariantInfo
            {
                Id = zeroHourCard.Id,
                Name = "weekly-2026-07-17 — Zero Hour",
                ManifestId = zeroHourCard.Id,
                VariantType = "game-type",
                IsDefault = true,
            },
            new ContentVariantInfo
            {
                Id = generalsCard.Id,
                Name = "weekly-2026-07-17 — Generals",
                ManifestId = generalsCard.Id,
                VariantType = "game-type",
                IsDefault = false,
            },
        };

        generalsCard.Variants = variantList;
        zeroHourCard.Variants = variantList;
        return (generalsCard, zeroHourCard, variantList);
    }

    private static ContentGridItemViewModel CreateCard(ContentSearchResult searchResult) =>
        new(
            searchResult,
            new Mock<IContentStateService>().Object,
            new Mock<ILogger<ContentGridItemViewModel>>().Object);
}
