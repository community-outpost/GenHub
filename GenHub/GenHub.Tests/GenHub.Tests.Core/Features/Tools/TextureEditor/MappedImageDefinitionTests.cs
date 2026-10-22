using GenHub.Core.Models.Tools.TextureEditor;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.TextureEditor;

/// <summary>
/// Unit tests for <see cref="MappedImageDefinition"/> coordinate predicates.
/// </summary>
public sealed class MappedImageDefinitionTests
{
    /// <summary>
    /// Verifies that disordered coordinates fail the guard border check.
    /// </summary>
    [Fact]
    public void HasGuardBorder_DisorderedCoords_ReturnsFalse()
    {
        var definition = new MappedImageDefinition("Bad", "a.tga", 64, 64, 1, 1, 0, 0);

        Assert.False(definition.HasGuardBorder);
    }

    /// <summary>
    /// Verifies that interior coordinates pass the guard border check.
    /// </summary>
    [Fact]
    public void HasGuardBorder_InteriorCoords_ReturnsTrue()
    {
        var definition = new MappedImageDefinition("Good", "a.tga", 64, 64, 10, 10, 20, 20);

        Assert.True(definition.HasGuardBorder);
    }

    /// <summary>
    /// Verifies that a zero-area slice fails the guard border check like IsWithinTexture.
    /// </summary>
    [Fact]
    public void HasGuardBorder_ZeroAreaSlice_ReturnsFalse()
    {
        var definition = new MappedImageDefinition("Empty", "a.tga", 64, 64, 10, 10, 10, 20);

        Assert.False(definition.IsWithinTexture);
        Assert.False(definition.HasGuardBorder);
    }

    /// <summary>
    /// Verifies that dimensions follow the engine rule without an inclusive plus one.
    /// </summary>
    [Fact]
    public void Dimensions_ExclusiveEdges_SubtractWithoutPlusOne()
    {
        var definition = new MappedImageDefinition("Cameo", "a.tga", 512, 512, 10, 10, 74, 74);

        Assert.Equal(64, definition.Width);
        Assert.Equal(64, definition.Height);
    }

    /// <summary>
    /// Verifies that zero-area regions are not within the texture.
    /// </summary>
    [Fact]
    public void IsWithinTexture_ZeroArea_ReturnsFalse()
    {
        var definition = new MappedImageDefinition("Empty", "a.tga", 64, 64, 10, 10, 10, 20);

        Assert.False(definition.IsWithinTexture);
    }

    /// <summary>
    /// Verifies that edges exactly at the texture size are valid like shipped atlas files.
    /// </summary>
    [Fact]
    public void IsWithinTexture_EdgeAtTextureSize_ReturnsTrue()
    {
        var definition = new MappedImageDefinition("Full", "a.tga", 512, 512, 0, 0, 512, 512);

        Assert.True(definition.IsWithinTexture);
        Assert.Equal(512, definition.Width);
        Assert.Equal(512, definition.Height);
    }
}
