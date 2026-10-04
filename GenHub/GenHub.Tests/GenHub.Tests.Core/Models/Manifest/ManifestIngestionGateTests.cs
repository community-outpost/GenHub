using GenHub.Core.Constants;
using GenHub.Core.Models.Manifest;
using System.Globalization;
using Xunit;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Tests for <see cref="ManifestIngestionGate"/>, which accepts every manifest format this
/// build understands, including artifact variants, and rejects newer formats.
/// </summary>
public class ManifestIngestionGateTests
{
    /// <summary>
    /// A manifest without variants is the common published shape and is accepted.
    /// </summary>
    [Fact]
    public void TryAccept_WithoutVariants_Accepts()
    {
        var manifest = new ContentManifest { Id = new("1.0.genhub.mod.legacy") };

        Assert.True(ManifestIngestionGate.TryAccept(manifest, out var reason));
        Assert.Null(reason);
    }

    /// <summary>
    /// A manifest declaring variants is accepted: every consumer resolves the host variant.
    /// </summary>
    [Fact]
    public void TryAccept_WithVariants_Accepts()
    {
        var manifest = new ContentManifest
        {
            Id = new("1.0.genhub.mod.variant"),
            SchemaVersion = ManifestConstants.VariantsManifestFormatVersion.ToString(CultureInfo.InvariantCulture),
        };
        manifest.Variants.Add(new ArtifactVariant { RuntimeIdentifiers = ["win-x64"] });

        Assert.True(ManifestIngestionGate.TryAccept(manifest, out var reason));
        Assert.Null(reason);
    }

    /// <summary>
    /// Variants under the legacy format version are accepted too; the files resolve the same way.
    /// </summary>
    [Fact]
    public void TryAccept_WithVariantsAndLegacyVersion_Accepts()
    {
        var manifest = new ContentManifest
        {
            Id = new("1.0.genhub.mod.legacyvariants"),
            SchemaVersion = ManifestConstants.DefaultManifestVersion,
        };
        manifest.Variants.Add(new ArtifactVariant());

        Assert.True(ManifestIngestionGate.TryAccept(manifest, out _));
    }

    /// <summary>
    /// A null manifest is not the gate's concern; callers already treat null as a failed parse.
    /// </summary>
    [Fact]
    public void TryAccept_WithNull_Accepts()
    {
        Assert.True(ManifestIngestionGate.TryAccept(null, out var reason));
        Assert.Null(reason);
    }

    /// <summary>
    /// A manifest declaring a format newer than this build supports is rejected with a message
    /// that names it, the declared format and the supported one.
    /// </summary>
    [Fact]
    public void TryAccept_WithNewerFormatVersion_RejectsWithActionableReason()
    {
        var newer = (ManifestConstants.MaxSupportedManifestFormatVersion + 1).ToString(CultureInfo.InvariantCulture);
        var manifest = new ContentManifest { Id = new("1.0.genhub.mod.futureformat"), SchemaVersion = newer };

        Assert.False(ManifestIngestionGate.TryAccept(manifest, out var reason));
        Assert.NotNull(reason);
        Assert.Contains("1.0.genhub.mod.futureformat", reason);
        Assert.Contains(newer, reason);
        Assert.Contains(ManifestConstants.MaxSupportedManifestFormatVersion.ToString(CultureInfo.InvariantCulture), reason);
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

        Assert.False(ManifestIngestionGate.TryAccept(manifest, out var reason));
        Assert.NotNull(reason);
    }
}
