// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Tools.WorldBuilder;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for W3D asset name resolution.
/// </summary>
public sealed class W3DAssetNamesTests
{
    /// <summary>
    /// Verifies full names resolve to the pre-dot file.
    /// </summary>
    /// <param name="modelName">The model name.</param>
    /// <param name="expected">The expected file name.</param>
    [Theory]
    [InlineData("AVTANK.AVTANK", "AVTANK.w3d")]
    [InlineData("TANK", "TANK.w3d")]
    [InlineData("#CACHED.PART", "CACHED.w3d")]
    [InlineData("  PADDED.X  ", "PADDED.w3d")]
    public void DeriveFileName_ValidNames_ResolvesFile(string modelName, string expected)
    {
        Assert.Equal(expected, W3DAssetNames.DeriveFileName(modelName));
    }

    /// <summary>
    /// Verifies unusable names resolve to null.
    /// </summary>
    /// <param name="modelName">The model name.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData(".X")]
    public void DeriveFileName_UnusableNames_ReturnsNull(string? modelName)
    {
        Assert.Null(W3DAssetNames.DeriveFileName(modelName));
    }

    /// <summary>
    /// Verifies mesh selectors take the post-dot part.
    /// </summary>
    /// <param name="modelName">The model name.</param>
    /// <param name="expected">The expected selector.</param>
    [Theory]
    [InlineData("AVTANK.TURRET", "TURRET")]
    [InlineData("TANK", "TANK")]
    [InlineData("#CACHED.PART", "PART")]
    public void DeriveMeshSelector_Names_SelectsMesh(string modelName, string expected)
    {
        Assert.Equal(expected, W3DAssetNames.DeriveMeshSelector(modelName));
    }
}
