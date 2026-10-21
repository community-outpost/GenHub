// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// One material pass: material and shader selection plus baked vertex colors.
/// </summary>
/// <param name="VertexMaterialIds">Material ids, one entry or one per vertex.</param>
/// <param name="ShaderIds">Shader ids, one entry or one per triangle.</param>
/// <param name="Diffuse">Per-vertex diffuse colors; empty when absent.</param>
/// <param name="Stages">Texture stages in order.</param>
public sealed record W3dMaterialPass(
    IList<uint> VertexMaterialIds,
    IList<uint> ShaderIds,
    IList<W3dRgba> Diffuse,
    IReadOnlyList<W3dTextureStage> Stages);
