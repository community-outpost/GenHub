// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System.Numerics;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// One texture stage of a material pass: texture choice and per-vertex UVs.
/// </summary>
/// <param name="TextureIds">Texture ids, one entry or one per triangle.</param>
/// <param name="TexCoords">UV per vertex.</param>
public sealed record W3dTextureStage(IList<uint> TextureIds, IList<Vector2> TexCoords);
