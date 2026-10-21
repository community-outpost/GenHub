// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// One parsed .w3d file: meshes, hierarchies, and HLOD data by name.
/// </summary>
/// <param name="FileName">The source file name.</param>
/// <param name="Meshes">Meshes in file order.</param>
/// <param name="Hierarchies">Hierarchies in file order.</param>
/// <param name="Hlods">HLOD blocks in file order.</param>
public sealed record W3DModel(
    string FileName,
    IReadOnlyList<W3dMesh> Meshes,
    IReadOnlyList<W3dHierarchy> Hierarchies,
    IReadOnlyList<W3dHlod> Hlods);
