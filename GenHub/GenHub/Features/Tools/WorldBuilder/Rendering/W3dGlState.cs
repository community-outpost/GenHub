// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// GL-ready render state mapped from one fixed-pipeline W3D shader.
/// </summary>
/// <param name="DepthFunction">Depth comparison.</param>
/// <param name="DepthWrite">True when depth writes are enabled.</param>
/// <param name="BlendEnabled">True when blending differs from opaque (ONE, ZERO).</param>
/// <param name="SrcFactor">Source blend factor.</param>
/// <param name="DstFactor">Destination blend factor.</param>
/// <param name="AlphaTest">True when the fragment shader must discard below the cutout reference.</param>
/// <param name="Textured">True when texture stage zero must be sampled.</param>
/// <param name="Combine">Color combine mode.</param>
public sealed record W3dGlState(
    W3dDepthFunction DepthFunction,
    bool DepthWrite,
    bool BlendEnabled,
    W3dBlendFactor SrcFactor,
    W3dBlendFactor DstFactor,
    bool AlphaTest,
    bool Textured,
    W3dColorCombine Combine);
