using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Features.Tools.TextureEditor.ViewModels;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.TextureEditor;

/// <summary>
/// Unit tests for <see cref="TextureSliceViewModel"/>.
/// </summary>
public sealed class TextureSliceViewModelTests
{
    /// <summary>
    /// Verifies that dimensions follow exclusive SAGE edge rules.
    /// </summary>
    [Fact]
    public void Constructor_ExclusiveCoords_ComputesDimensions()
    {
        var slice = new TextureSliceViewModel(new MappedImageDefinition("Solo", "a.tga", 512, 512, 10, 10, 74, 74));

        Assert.Equal(64, slice.Width);
        Assert.Equal(64, slice.Height);
        Assert.True(slice.IsWithinTexture);
        Assert.True(slice.HasGuardBorder);
    }

    /// <summary>
    /// Verifies that out-of-bounds coordinates are reported.
    /// </summary>
    [Fact]
    public void Coordinates_OutOfBounds_ReportsInvalid()
    {
        var slice = new TextureSliceViewModel(new MappedImageDefinition("Solo", "a.tga", 64, 64, 0, 0, 63, 63));

        slice.Right = 80;

        Assert.False(slice.IsWithinTexture);
        Assert.False(slice.HasGuardBorder);
    }

    /// <summary>
    /// Verifies that edge-touching slices fail only the guard border check.
    /// </summary>
    [Fact]
    public void Coordinates_AtEdge_FailsGuardBorderOnly()
    {
        var slice = new TextureSliceViewModel(new MappedImageDefinition("Solo", "a.tga", 64, 64, 0, 0, 63, 63));

        Assert.True(slice.IsWithinTexture);
        Assert.False(slice.HasGuardBorder);
    }

    /// <summary>
    /// Verifies that zoom updates display coordinates proportionally.
    /// </summary>
    [Fact]
    public void UpdateZoom_DoubleZoom_ScalesDisplayRect()
    {
        var slice = new TextureSliceViewModel(new MappedImageDefinition("Solo", "a.tga", 512, 512, 10, 20, 74, 84));

        slice.UpdateZoom(2);

        Assert.Equal(20, slice.DisplayX);
        Assert.Equal(40, slice.DisplayY);
        Assert.Equal(128, slice.DisplayWidth);
        Assert.Equal(128, slice.DisplayHeight);
    }

    /// <summary>
    /// Verifies that texture updates revalidate against the new bounds.
    /// </summary>
    [Fact]
    public void UpdateTexture_SmallerAtlas_ReportsOutOfBounds()
    {
        var slice = new TextureSliceViewModel(new MappedImageDefinition("Solo", "a.tga", 512, 512, 10, 10, 73, 73));

        slice.UpdateTexture("small.tga", 32, 32);

        Assert.False(slice.IsWithinTexture);
        Assert.Equal("small.tga", slice.ToDefinition().TextureFileName);
    }

    /// <summary>
    /// Verifies that disordered coordinates fail the guard border check.
    /// </summary>
    [Fact]
    public void Coordinates_Disordered_FailsGuardBorder()
    {
        var slice = new TextureSliceViewModel(new MappedImageDefinition("Solo", "a.tga", 64, 64, 1, 1, 0, 0));

        Assert.False(slice.IsWithinTexture);
        Assert.False(slice.HasGuardBorder);
    }

    /// <summary>
    /// Verifies that zero-area coordinates are reported as outside the texture.
    /// </summary>
    [Fact]
    public void Coordinates_ZeroArea_ReportsOutOfBounds()
    {
        var slice = new TextureSliceViewModel(new MappedImageDefinition("Solo", "a.tga", 64, 64, 10, 10, 10, 10));

        Assert.False(slice.IsWithinTexture);
    }

    /// <summary>
    /// Verifies that edited state round-trips through definitions.
    /// </summary>
    [Fact]
    public void ToDefinition_AfterEdits_PreservesState()
    {
        var slice = new TextureSliceViewModel(new MappedImageDefinition("Solo", "a.tga", 64, 64, 0, 0, 31, 31));
        slice.Name = "Renamed";
        slice.Left = 4;
        slice.Top = 4;
        slice.Right = 35;
        slice.Bottom = 35;

        var definition = slice.ToDefinition();

        Assert.Equal("Renamed", definition.Name);
        Assert.Equal(4, definition.Left);
        Assert.Equal(35, definition.Right);
        Assert.Equal(31, definition.Width);

        var restored = new TextureSliceViewModel(definition);
        Assert.Equal("Renamed", restored.Name);
        Assert.Equal(slice.Width, restored.Width);
        Assert.Equal(slice.Height, restored.Height);
    }
}
