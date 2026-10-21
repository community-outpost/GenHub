// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using System;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Maps W3dShaderStruct bytes to render state. Unrecognized enum values fall
/// back to the engine defaults (opaque LEQUAL modulate) rather than failing.
/// </summary>
public static class W3dShaderMap
{
    /// <summary>
    /// Alpha reference matching the engine cutout threshold (~0x60/255).
    /// </summary>
    public const float AlphaReference = 0.375f;

    /// <summary>
    /// Maps one shader record to render state.
    /// </summary>
    /// <param name="shader">The shader record.</param>
    /// <returns>The mapped state.</returns>
    public static W3dGlState Map(W3dShader shader)
    {
        ArgumentNullException.ThrowIfNull(shader);
        var depth = shader.DepthCompare <= (byte)W3dDepthFunction.Always
            ? (W3dDepthFunction)shader.DepthCompare
            : W3dDepthFunction.Lequal;
        var src = MapBlendFactor(shader.SrcBlend, W3dBlendFactor.One);
        var dst = MapBlendFactor(shader.DestBlend, W3dBlendFactor.Zero);
        return new W3dGlState(
            depth,
            shader.DepthMask == WorldBuilderConstants.W3D.DepthMaskWriteEnable,
            src != W3dBlendFactor.One || dst != W3dBlendFactor.Zero,
            src,
            dst,
            shader.AlphaTest == WorldBuilderConstants.W3D.AlphaTestEnable,
            shader.Texturing == WorldBuilderConstants.W3D.TexturingEnable,
            MapCombine(shader.PriGradient));
    }

    private static W3dBlendFactor MapBlendFactor(byte value, W3dBlendFactor fallback)
    {
        return value switch
        {
            0 => W3dBlendFactor.Zero,
            1 => W3dBlendFactor.One,
            2 => W3dBlendFactor.SrcColor,
            3 => W3dBlendFactor.OneMinusSrcColor,
            4 => W3dBlendFactor.SrcAlpha,
            5 => W3dBlendFactor.OneMinusSrcAlpha,
            _ => fallback,
        };
    }

    private static W3dColorCombine MapCombine(byte priGradient)
    {
        if (priGradient == WorldBuilderConstants.W3D.PriGradientModulate)
        {
            return W3dColorCombine.Modulate;
        }

        if (priGradient == WorldBuilderConstants.W3D.PriGradientAdd)
        {
            return W3dColorCombine.Add;
        }

        if (priGradient == WorldBuilderConstants.W3D.PriGradientModulate2X)
        {
            return W3dColorCombine.Modulate2X;
        }

        return W3dColorCombine.Decal;
    }
}
