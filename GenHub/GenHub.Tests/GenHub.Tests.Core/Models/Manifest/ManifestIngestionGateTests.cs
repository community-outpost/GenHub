using System.Globalization;
using GenHub.Core.Constants;
using GenHub.Core.Models.Manifest;
using Xunit;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Tests for <see cref="ManifestIngestionGate"/>, the fail-closed gate that keeps
/// variant manifests out until the content pipeline is migrated.
/// </summary>
public class ManifestIngestionGateTests
{
    /// <summary>
    /// A manifest without variants is the current published shape and must be unaffected.
    /// </summary>
    [Fact]
    public void TryAccept_WithoutVariants_Accepts()
    {
        var manifest = new ContentManifest { Id = new("1.0.genhub.mod.legacy") };

        Assert.True(ManifestIngestionGate.TryAccept(manifest, out var reason));
        Assert.Null(reason);
    }

    /// <summary>
    /// A manifest declaring variants must be rejected: every consumer still reads
    /// <c>Files</c>, which is empty for a variant manifest, so accepting it would deliver
    /// nothing while reporting success.
    /// </summary>
    [Fact]
    public void TryAccept_WithVariants_RejectsWithActionableReason()
    {
        var manifest = new ContentManifest { Id = new("1.0.genhub.mod.variant") };
        manifest.Variants.Add(new ArtifactVariant());

        Assert.False(ManifestIngestionGate.TryAccept(manifest, out var reason));
        Assert.NotNull(reason);

        // The message must name the manifest, the version it requires, and the way out.
        Assert.Contains("1.0.genhub.mod.variant", reason);
        Assert.Contains("2", reason);
        Assert.Contains("without", reason);
    }

    /// <summary>
    /// A null manifest is not the gate's concern; callers already treat null as a failed
    /// parse, and reporting it here would attribute a parse failure to variants.
    /// </summary>
    [Fact]
    public void TryAccept_WithNull_Accepts()
    {
        Assert.True(ManifestIngestionGate.TryAccept(null, out var reason));
        Assert.Null(reason);
    }

    /// <summary>
    /// A manifest declaring the variants format version is rejected even with no variants
    /// present: that version may carry other features this pipeline cannot handle.
    /// </summary>
    [Fact]
    public void TryAccept_WithVariantFormatVersionButNoVariants_Rejects()
    {
        var manifest = new ContentManifest
        {
            Id = new("1.0.genhub.mod.futureformat"),
            SchemaVersion = ManifestConstants.VariantsManifestFormatVersion.ToString(CultureInfo.InvariantCulture),
        };

        Assert.False(ManifestIngestionGate.TryAccept(manifest, out var reason));
        Assert.Contains("format version", reason);
    }

    /// <summary>
    /// Variants are rejected even when the manifest claims the legacy version, so a
    /// mislabelled manifest cannot slip past by understating its format.
    /// </summary>
    [Fact]
    public void TryAccept_WithVariantsButLegacyVersion_StillRejects()
    {
        var manifest = new ContentManifest
        {
            Id = new("1.0.genhub.mod.mislabelled"),
            SchemaVersion = ManifestConstants.DefaultManifestVersion,
        };
        manifest.Variants.Add(new ArtifactVariant());

        Assert.False(ManifestIngestionGate.TryAccept(manifest, out var reason));
        Assert.Contains("variant", reason);
    }

    /// <summary>
    /// The default version must remain acceptable; every manifest published today carries it.
    /// </summary>
    [Fact]
    public void TryAccept_WithDefaultVersion_Accepts()
    {
        var manifest = new ContentManifest
        {
            Id = new("1.0.genhub.mod.legacy"),
            SchemaVersion = ManifestConstants.DefaultManifestVersion,
        };

        Assert.True(ManifestIngestionGate.TryAccept(manifest, out _));
    }

    /// <summary>
    /// Date-based content versions (e.g. 20260723) must never be accepted as a manifest
    /// format version; regression guard for the Community Outpost resolver bug.
    /// </summary>
    [Fact]
    public void TryAccept_DateBasedManifestVersion_IsRejected()
    {
        var manifest = new ContentManifest
        {
            Id = new("1.20260723.communityoutpost.gameclient.communitypatch"),
            SchemaVersion = "20260723",
        };

        var accepted = ManifestIngestionGate.TryAccept(manifest, out var reason);

        Assert.False(accepted);
        Assert.NotNull(reason);
    }
}
