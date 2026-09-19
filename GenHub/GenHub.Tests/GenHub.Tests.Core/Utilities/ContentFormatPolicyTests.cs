using GenHub.Core.Utilities;
using Xunit;

namespace GenHub.Tests.Core.Utilities;

/// <summary>
/// Unit tests for <see cref="ContentFormatPolicy"/>.
/// </summary>
public sealed class ContentFormatPolicyTests
{
    /// <summary>
    /// Archive containers must be understood so the pipeline extracts them before detection.
    /// </summary>
    /// <param name="fileName">The asset file name.</param>
    [Theory]
    [InlineData("client.zip")]
    [InlineData("client.7z")]
    [InlineData("client.rar")]
    [InlineData("client.tar")]
    [InlineData("client.tgz")]
    [InlineData("client.tar.gz")]
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
    [InlineData("GeneralsXZH.exe")]
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
    [InlineData("client.flatpak")]
    [InlineData("client.snap")]
    [InlineData("client.deb")]
    [InlineData("client.rpm")]
    [InlineData("client.DMG")]
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
    [Fact]
    public void IsUnderstoodAsset_EmptyName_ReturnsFalse()
    {
        Assert.False(ContentFormatPolicy.IsUnderstoodAsset(null));
        Assert.False(ContentFormatPolicy.IsUnderstoodAsset(string.Empty));
        Assert.Null(ContentFormatPolicy.GetRejectionMessage(null));
    }

    /// <summary>
    /// Partitioning must keep usable assets and collect one message per rejection.
    /// </summary>
    [Fact]
    public void PartitionUsableAssets_MixedAssets_SplitsBothSides()
    {
        var (usable, rejections) = ContentFormatPolicy.PartitionUsableAssets(
            ["client-mac.zip", "client.dmg", "game.AppImage", "client.flatpak"]);

        Assert.Equal(["client-mac.zip", "game.AppImage"], usable);
        Assert.Equal(2, rejections.Count);
        Assert.All(rejections, message => Assert.Contains("cannot be installed directly", message));
    }
}
