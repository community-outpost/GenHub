// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// One HLOD sub-object: the bone a mesh is rigidly attached to.
/// </summary>
/// <param name="BoneIndex">Bone index into the hierarchy pivots.</param>
/// <param name="Identifier">Full identifier (usually container.name).</param>
/// <param name="MeshName">Identifier part after the first dot, or the whole identifier.</param>
public sealed record W3dHlodSubObject(uint BoneIndex, string Identifier, string MeshName);
