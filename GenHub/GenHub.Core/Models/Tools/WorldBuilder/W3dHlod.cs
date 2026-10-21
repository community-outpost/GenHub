// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Parsed HLOD: per-LOD mesh attachments plus aggregate and proxy meshes.
/// </summary>
/// <param name="ModelName">The model name.</param>
/// <param name="HierarchyName">The hierarchy name.</param>
/// <param name="Lods">Sub-objects per LOD level in order.</param>
/// <param name="AggregateMeshNames">Aggregate mesh names.</param>
/// <param name="ProxyMeshNames">Proxy mesh names.</param>
public sealed record W3dHlod(
    string ModelName,
    string HierarchyName,
    IReadOnlyList<IReadOnlyList<W3dHlodSubObject>> Lods,
    IReadOnlyList<string> AggregateMeshNames,
    IReadOnlyList<string> ProxyMeshNames);
