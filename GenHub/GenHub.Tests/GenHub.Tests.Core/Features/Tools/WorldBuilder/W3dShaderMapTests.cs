// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for the W3D shader to render-state mapping.
/// </summary>
public sealed class W3dShaderMapTests
{
    /// <summary>
    /// Verifies the opaque preset maps to depth-write LEQUAL modulate without blending.
    /// </summary>
    [Fact]
    public void Map_OpaquePreset_MapsOpaqueState()
    {
        var state = W3dShaderMap.Map(Opaque());

        Assert.Equal(W3dDepthFunction.Lequal, state.DepthFunction);
        Assert.True(state.DepthWrite);
        Assert.False(state.BlendEnabled);
        Assert.False(state.AlphaTest);
        Assert.True(state.Textured);
        Assert.Equal(W3dColorCombine.Modulate, state.Combine);
    }

    /// <summary>
    /// Verifies standard alpha blending enables blending with alpha factors.
    /// </summary>
    [Fact]
    public void Map_AlphaPreset_EnablesAlphaBlending()
    {
        var state = W3dShaderMap.Map(Opaque() with { SrcBlend = 4, DestBlend = 5 });

        Assert.True(state.BlendEnabled);
        Assert.Equal(W3dBlendFactor.SrcAlpha, state.SrcFactor);
        Assert.Equal(W3dBlendFactor.OneMinusSrcAlpha, state.DstFactor);
    }

    /// <summary>
    /// Verifies additive blending maps both factors to one.
    /// </summary>
    [Fact]
    public void Map_AdditivePreset_MapsOneOne()
    {
        var state = W3dShaderMap.Map(Opaque() with { SrcBlend = 1, DestBlend = 1, DepthMask = 0 });

        Assert.True(state.BlendEnabled);
        Assert.False(state.DepthWrite);
        Assert.Equal(W3dBlendFactor.One, state.SrcFactor);
        Assert.Equal(W3dBlendFactor.One, state.DstFactor);
    }

    /// <summary>
    /// Verifies the alpha-test flag survives with opaque blending.
    /// </summary>
    [Fact]
    public void Map_AlphaTestPreset_ReportsCutout()
    {
        var state = W3dShaderMap.Map(Opaque() with { AlphaTest = 1 });

        Assert.True(state.AlphaTest);
        Assert.False(state.BlendEnabled);
    }

    /// <summary>
    /// Verifies unrecognized values fall back to engine defaults.
    /// </summary>
    [Fact]
    public void Map_UnknownValues_FallsBackToDefaults()
    {
        var state = W3dShaderMap.Map(Opaque() with { DepthCompare = 255, PriGradient = 99, SrcBlend = 200, DestBlend = 200 });

        Assert.Equal(W3dDepthFunction.Lequal, state.DepthFunction);
        Assert.Equal(W3dColorCombine.Decal, state.Combine);
        Assert.Equal(W3dBlendFactor.One, state.SrcFactor);
        Assert.Equal(W3dBlendFactor.Zero, state.DstFactor);
        Assert.False(state.BlendEnabled);
    }

    private static W3dShader Opaque()
    {
        return new W3dShader(3, 1, 0, 0, 0, 1, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0);
    }
}
