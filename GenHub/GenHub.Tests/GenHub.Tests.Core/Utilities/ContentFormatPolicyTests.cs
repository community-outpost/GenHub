using GenHub.Core.Utilities;
using Xunit;

namespace GenHub.Tests.Core.Utilities;

/// <summary>
/// Unit tests for <see cref="ContentFormatPolicy"/>.
/// </summary>
public class ContentFormatPolicyTests
{
    /// <summary>
    /// Archive containers the pipeline extracts must all be recognized as usable content.
    /// </summary>
    /// <param name="fileName">The archive file name.</param>
    [Theory]
    [InlineData("release.zip")]
    [InlineData("release.7z")]
    [InlineData("release.tar")]
    [InlineData("release.tar.gz")]
    [InlineData("release.tgz")]
    [InlineData("release.tar.xz")]
    [InlineData("release.tar.bz2")]
    public void IsUnderstoodAsset_ArchiveContainer_ReturnsTrue(string fileName)
    {
        Assert.True(ContentFormatPolicy.IsUnderstoodAsset(fileName));
        Assert.True(ContentFormatPolicy.IsArchiveContainer(fileName));
        Assert.False(ContentFormatPolicy.IsGuidedRejection(fileName));
        Assert.Null(ContentFormatPolicy.GetRejectionMessage(fileName));
    }

    /// <summary>
    /// Bare single-file assets must be understood so native binaries flow to detection.
    /// </summary>
    /// <param name="fileName">The asset file name.</param>
    [Theory]
    [InlineData("GeneralsOnlineZH")]
    [InlineData("game.AppImage")]
    [InlineData("setup.bin")]
    [InlineData("installer.run")]
    [InlineData("client.exe")]
    [InlineData("client.flatpak")]
    public void IsUnderstoodAsset_BareSingleFile_ReturnsTrue(string fileName)
    {
        Assert.True(ContentFormatPolicy.IsUnderstoodAsset(fileName));
        Assert.False(ContentFormatPolicy.IsGuidedRejection(fileName));
        Assert.Null(ContentFormatPolicy.GetRejectionMessage(fileName));
    }

    /// <summary>
    /// Formats needing external tooling must be rejected with actionable guidance.
    /// </summary>
    /// <param name="fileName">The asset file name.</param>
    [Theory]
    [InlineData("client.dmg")]
    [InlineData("client.pkg")]
    [InlineData("client.snap")]
    [InlineData("client.deb")]
    [InlineData("client.rpm")]
    [InlineData("client.msi")]
    [InlineData("client.msix")]
    [InlineData("client.DMG")]
    [InlineData("client.dmg.gz")]
    public void IsGuidedRejection_ExternalToolingFormat_ReturnsTrueWithGuidance(string fileName)
    {
        Assert.False(ContentFormatPolicy.IsUnderstoodAsset(fileName));
        Assert.True(ContentFormatPolicy.IsGuidedRejection(fileName));

        var message = ContentFormatPolicy.GetRejectionMessage(fileName);
        Assert.NotNull(message);
        Assert.Contains(fileName, message, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Empty names are never usable content.
    /// </summary>
    /// <param name="name">The candidate name.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ContentFormatPolicy_WhitespaceOrNull_ReturnsFalse(string? name)
    {
        Assert.False(ContentFormatPolicy.IsUnderstoodAsset(name));
        Assert.False(ContentFormatPolicy.IsArchiveContainer(name));
        Assert.False(ContentFormatPolicy.IsGuidedRejection(name));
        Assert.Null(ContentFormatPolicy.GetRejectionMessage(name));
    }

    /// <summary>
    /// Partitioning must keep usable assets and collect one message per rejection.
    /// </summary>
    [Fact]
    public void PartitionUsableAssets_MixedAssets_SplitsBothSides()
    {
        var (usable, rejections) = ContentFormatPolicy.PartitionUsableAssets(
            ["client-mac.zip", "client.dmg", "game.AppImage", "client.flatpak", "client.deb", "  "]);

        Assert.Equal(["client-mac.zip", "game.AppImage", "client.flatpak"], usable);
        Assert.Equal(2, rejections.Count);
        Assert.All(rejections, message => Assert.Contains("cannot be installed directly", message));
    }

    /// <summary>
    /// Release notes beside real content are understood but never cards of their own.
    /// </summary>
    /// <param name="fileName">The candidate asset name.</param>
    /// <param name="expected">Whether the asset is card-worthy content.</param>
    [Theory]
    [InlineData("client-mac.zip", true)]
    [InlineData("client.exe", true)]
    [InlineData("GeneralsOnlineZH", true)]
    [InlineData("game.flatpak", true)]
    [InlineData("mod.big", true)]
    [InlineData("install.zip", true)]
    [InlineData("history.big", true)]
    [InlineData("changes.zip", true)]
    [InlineData("patch-notes.txt", false)]
    [InlineData("README.md", false)]
    [InlineData("README", false)]
    [InlineData("LICENSE", false)]
    [InlineData("COPYING", false)]
    [InlineData("CHANGELOG", false)]
    [InlineData("client.dmg", false)]
    public void IsContentAsset_MixedNames_FiltersDocumentation(string fileName, bool expected)
    {
        Assert.Equal(expected, ContentFormatPolicy.IsContentAsset(fileName));
    }

    /// <summary>
    /// Nested containers strip to their inner format for the rejection check.
    /// </summary>
    [Fact]
    public void IsGuidedRejection_NestedContainers_StripsToInnerFormat()
    {
        Assert.True(ContentFormatPolicy.IsGuidedRejection("client.dmg.gz"));
        Assert.False(ContentFormatPolicy.IsGuidedRejection("data.tar.gz"));
        Assert.False(ContentFormatPolicy.IsGuidedRejection("release.zip"));
    }

    /// <summary>
    /// Strips known archive and package extensions from content and asset names.
    /// </summary>
    /// <param name="input">The input file name or path.</param>
    /// <param name="expected">The expected stripped name.</param>
    [Theory]
    [InlineData("GLA Campaign by TKlyo.rar", "GLA Campaign by TKlyo")]
    [InlineData("GLA Campaign by TKlyo.RAR", "GLA Campaign by TKlyo")]
    [InlineData("Mappack.zip", "Mappack")]
    [InlineData("Archive.tar.gz", "Archive")]
    [InlineData("Shockwave.big", "Shockwave")]
    [InlineData("Normal Content", "Normal Content")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void StripArchiveExtensions_StripsKnownExtensions(string? input, string expected)
    {
        Assert.Equal(expected, ContentFormatPolicy.StripArchiveExtensions(input));
    }
}
