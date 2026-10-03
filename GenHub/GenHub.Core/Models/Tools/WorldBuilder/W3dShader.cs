// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Fixed-pipeline shader record: 16 raw render-state bytes read by field position.
/// </summary>
/// <param name="DepthCompare">Depth comparison function.</param>
/// <param name="DepthMask">Depth write control.</param>
/// <param name="ColorMask">Obsolete color mask.</param>
/// <param name="DestBlend">Destination blend factor.</param>
/// <param name="FogFunc">Obsolete fog function.</param>
/// <param name="PriGradient">Primary gradient (color combine).</param>
/// <param name="SecGradient">Secondary gradient.</param>
/// <param name="SrcBlend">Source blend factor.</param>
/// <param name="Texturing">Texturing enable.</param>
/// <param name="DetailColorFunc">Detail color function.</param>
/// <param name="DetailAlphaFunc">Detail alpha function.</param>
/// <param name="ShaderPreset">Obsolete preset.</param>
/// <param name="AlphaTest">Alpha test enable.</param>
/// <param name="PostDetailColorFunc">Post-detail color function.</param>
/// <param name="PostDetailAlphaFunc">Post-detail alpha function.</param>
/// <param name="Pad">Padding byte.</param>
public sealed record W3dShader(
    byte DepthCompare,
    byte DepthMask,
    byte ColorMask,
    byte DestBlend,
    byte FogFunc,
    byte PriGradient,
    byte SecGradient,
    byte SrcBlend,
    byte Texturing,
    byte DetailColorFunc,
    byte DetailAlphaFunc,
    byte ShaderPreset,
    byte AlphaTest,
    byte PostDetailColorFunc,
    byte PostDetailAlphaFunc,
    byte Pad);
