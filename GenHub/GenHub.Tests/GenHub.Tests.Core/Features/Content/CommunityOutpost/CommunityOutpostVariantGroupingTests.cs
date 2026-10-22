using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.CommunityOutpost;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.CommunityOutpost;

/// <summary>
/// Unit tests for Community Outpost downloaded-library variant grouping.
/// </summary>
public sealed class CommunityOutpostVariantGroupingTests
{
    /// <summary>
    /// Verifies the group id format pins content type, code, and version.
    /// </summary>
    [Fact]
    public void BuildVariantGroupId_WithVersion_ReturnsVersionedGroupId()
    {
        var groupId = CommunityOutpostVariantGrouping.BuildVariantGroupId(ContentType.Addon, "cbpr", "1.0");

        Assert.Equal("communityoutpost.addon.cbpr.1.0", groupId);
    }

    /// <summary>
    /// Verifies inputs are normalized to lowercase without surrounding whitespace.
    /// </summary>
    [Fact]
    public void BuildVariantGroupId_WithMixedCaseInput_NormalizesGroupId()
    {
        var groupId = CommunityOutpostVariantGrouping.BuildVariantGroupId(ContentType.Addon, "  CBPR ", " V2026.07.15 ");

        Assert.Equal("communityoutpost.addon.cbpr.v2026.07.15", groupId);
    }

    /// <summary>
    /// Verifies manifests without a version fail closed instead of sharing a versionless group across distinct releases.
    /// </summary>
    /// <param name="version">The version to check.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildVariantGroupId_WithoutVersion_ReturnsNull(string? version)
    {
        var firstRelease = CommunityOutpostVariantGrouping.BuildVariantGroupId(ContentType.Addon, "cbpr", version);
        var secondRelease = CommunityOutpostVariantGrouping.BuildVariantGroupId(ContentType.Addon, "cbpr", version);

        Assert.Null(firstRelease);
        Assert.Null(secondRelease);
    }

    /// <summary>
    /// Verifies a missing content code fails closed instead of grouping.
    /// </summary>
    /// <param name="contentCode">The content code to check.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildVariantGroupId_WithoutContentCode_ReturnsNull(string? contentCode)
    {
        var groupId = CommunityOutpostVariantGrouping.BuildVariantGroupId(ContentType.Addon, contentCode, "1.0");

        Assert.Null(groupId);
    }

    /// <summary>
    /// Verifies the family name resolves through the content registry.
    /// </summary>
    [Fact]
    public void BuildVariantFamilyName_WithKnownCode_ReturnsRegistryDisplayName()
    {
        var familyName = CommunityOutpostVariantGrouping.BuildVariantFamilyName("cbpr");

        Assert.Equal("Control Bar Pro (ExiLe)", familyName);
    }

    /// <summary>
    /// Verifies unknown or missing codes fail closed instead of guessing a name.
    /// </summary>
    /// <param name="contentCode">The content code to check.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-such-code")]
    public void BuildVariantFamilyName_WithUnknownCode_ReturnsNull(string? contentCode)
    {
        var familyName = CommunityOutpostVariantGrouping.BuildVariantFamilyName(contentCode);

        Assert.Null(familyName);
    }

    /// <summary>
    /// Verifies publisher detection through either provider field.
    /// </summary>
    [Fact]
    public void IsCommunityOutpostManifest_WithPublisherFields_ReturnsTrue()
    {
        var byPublisher = new ContentManifest { Publisher = new PublisherInfo { PublisherType = "communityoutpost" } };
        var byOriginalProvider = new ContentManifest { OriginalProviderName = "CommunityOutpost" };

        Assert.True(CommunityOutpostVariantGrouping.IsCommunityOutpostManifest(byPublisher));
        Assert.True(CommunityOutpostVariantGrouping.IsCommunityOutpostManifest(byOriginalProvider));
    }

    /// <summary>
    /// Verifies other publishers and null manifests are rejected.
    /// </summary>
    [Fact]
    public void IsCommunityOutpostManifest_WithOtherPublisher_ReturnsFalse()
    {
        var other = new ContentManifest { Publisher = new PublisherInfo { PublisherType = "github" } };

        Assert.False(CommunityOutpostVariantGrouping.IsCommunityOutpostManifest(other));
        Assert.False(CommunityOutpostVariantGrouping.IsCommunityOutpostManifest(null));
        Assert.False(CommunityOutpostVariantGrouping.IsCommunityOutpostManifest(new ContentManifest()));
    }

    /// <summary>
    /// Verifies variant detection through selection state, tags, and id suffix.
    /// </summary>
    [Fact]
    public void IsVariantManifest_WithVariantSignals_ReturnsTrue()
    {
        var bySelectedId = CreateManifest("1.0.communityoutpost.addon.cbpr");
        bySelectedId.Metadata.SelectedVariantId = "1080p";

        var byTag = CreateManifest("1.0.communityoutpost.addon.cbpr");
        byTag.Metadata.Tags.Add("variant:1080p");

        var bySelectedTag = CreateManifest("1.0.communityoutpost.addon.cbpr");
        bySelectedTag.Metadata.Tags.Add("selectedVariant:1080p");

        var byIdSuffix = CreateManifest("1.0.communityoutpost.addon.cbpr-1080p");

        Assert.True(CommunityOutpostVariantGrouping.IsVariantManifest(bySelectedId, "cbpr"));
        Assert.True(CommunityOutpostVariantGrouping.IsVariantManifest(byTag, "cbpr"));
        Assert.True(CommunityOutpostVariantGrouping.IsVariantManifest(bySelectedTag, "cbpr"));
        Assert.True(CommunityOutpostVariantGrouping.IsVariantManifest(byIdSuffix, "cbpr"));
    }

    /// <summary>
    /// Verifies singles without variant signals are excluded from grouping.
    /// </summary>
    [Fact]
    public void IsVariantManifest_WithSingleContent_ReturnsFalse()
    {
        var single = CreateManifest("1.0.communityoutpost.addon.cbhd");
        single.Metadata.Tags.Add("contentCode:cbhd");

        Assert.False(CommunityOutpostVariantGrouping.IsVariantManifest(single, "cbhd"));
        Assert.False(CommunityOutpostVariantGrouping.IsVariantManifest(single, null));
        Assert.False(CommunityOutpostVariantGrouping.IsVariantManifest(null, "cbhd"));
    }

    /// <summary>
    /// Verifies the content code tag takes precedence over id parsing.
    /// </summary>
    [Fact]
    public void GetContentCode_WithContentCodeTag_ReturnsTaggedCode()
    {
        var manifest = CreateManifest("1.0.communityoutpost.addon.cbhd-1080p");
        manifest.Metadata.Tags.Add("contentCode:cbpr");

        var code = CommunityOutpostVariantGrouping.GetContentCode(manifest);

        Assert.Equal("cbpr", code);
    }

    /// <summary>
    /// Verifies legacy manifests without tags resolve through the registry.
    /// </summary>
    [Fact]
    public void GetContentCode_WithoutTag_ResolvesFromManifestId()
    {
        var manifest = CreateManifest("1.0.communityoutpost.addon.cbpr-1080p");

        var code = CommunityOutpostVariantGrouping.GetContentCode(manifest);

        Assert.Equal("cbpr", code);
    }

    /// <summary>
    /// Verifies unresolvable manifests fail closed instead of guessing a code.
    /// </summary>
    [Fact]
    public void GetContentCode_WithUnknownContent_ReturnsNull()
    {
        var manifest = CreateManifest("1.0.test.mod.zzzunknown");

        Assert.Null(CommunityOutpostVariantGrouping.GetContentCode(manifest));
        Assert.Null(CommunityOutpostVariantGrouping.GetContentCode(null));
    }

    /// <summary>
    /// Verifies compound tagged codes normalize through the registry like factory-generated codes.
    /// </summary>
    [Fact]
    public void GetContentCode_WithCompoundTag_ReturnsNormalizedCode()
    {
        var manifest = CreateManifest("1.0.communityoutpost.addon.cbhd-1080p");
        manifest.Metadata.Tags.Add("contentCode:hleizerohourru");

        var code = CommunityOutpostVariantGrouping.GetContentCode(manifest);

        Assert.Equal("hlei", code);
    }

    /// <summary>
    /// Verifies unknown tagged codes fall through to the manifest id instead of grouping under an unknown code.
    /// </summary>
    [Fact]
    public void GetContentCode_WithUnknownTag_FallsBackToManifestId()
    {
        var manifest = CreateManifest("1.0.communityoutpost.addon.cbpr-1080p");
        manifest.Metadata.Tags.Add("contentCode:zzzunknown");

        var code = CommunityOutpostVariantGrouping.GetContentCode(manifest);

        Assert.Equal("cbpr", code);
    }

    /// <summary>
    /// Verifies unknown tagged codes with an unresolvable id fail closed.
    /// </summary>
    [Fact]
    public void GetContentCode_WithUnknownTagAndId_ReturnsNull()
    {
        var manifest = CreateManifest("1.0.test.mod.zzzunknown");
        manifest.Metadata.Tags.Add("contentCode:zzzunknown");

        Assert.Null(CommunityOutpostVariantGrouping.GetContentCode(manifest));
    }

    /// <summary>
    /// Verifies the combined gate resolves the code for variant manifests.
    /// </summary>
    [Fact]
    public void TryGetVariantContentCode_WithVariantManifest_ReturnsTrueAndCode()
    {
        var manifest = CreateManifest("1.0.communityoutpost.addon.cbpr-1080p");
        manifest.Metadata.Tags.Add("contentCode:cbpr");
        manifest.Metadata.Tags.Add("variant:1080p");

        var result = CommunityOutpostVariantGrouping.TryGetVariantContentCode(manifest, out var contentCode);

        Assert.True(result);
        Assert.Equal("cbpr", contentCode);
    }

    /// <summary>
    /// Verifies the combined gate rejects singles without variant signals.
    /// </summary>
    [Fact]
    public void TryGetVariantContentCode_WithSingle_ReturnsFalse()
    {
        var manifest = CreateManifest("1.0.communityoutpost.addon.cbhd");

        var result = CommunityOutpostVariantGrouping.TryGetVariantContentCode(manifest, out var contentCode);

        Assert.False(result);
        Assert.Null(contentCode);
    }

    /// <summary>
    /// Verifies the combined gate rejects other publishers and null manifests.
    /// </summary>
    [Fact]
    public void TryGetVariantContentCode_WithForeignOrNullManifest_ReturnsFalse()
    {
        var foreign = CreateManifest("1.0.test.addon.cbpr-1080p");
        foreign.Publisher = new PublisherInfo { PublisherType = "test" };
        foreign.Metadata.Tags.Add("contentCode:cbpr");
        foreign.Metadata.Tags.Add("variant:1080p");

        Assert.False(CommunityOutpostVariantGrouping.TryGetVariantContentCode(foreign, out var foreignCode));
        Assert.Null(foreignCode);
        Assert.False(CommunityOutpostVariantGrouping.TryGetVariantContentCode(null, out var nullCode));
        Assert.Null(nullCode);
    }

    private static ContentManifest CreateManifest(string id)
    {
        return new ContentManifest
        {
            Id = ManifestId.Create(id),
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = CommunityOutpostConstants.PublisherType },
        };
    }
}
