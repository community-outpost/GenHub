// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System.Numerics;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// One triangle: vertex indices, surface type, and face plane.
/// </summary>
/// <param name="V0">First vertex index.</param>
/// <param name="V1">Second vertex index.</param>
/// <param name="V2">Third vertex index.</param>
/// <param name="SurfaceType">Surface type id.</param>
/// <param name="Normal">Face normal.</param>
/// <param name="Distance">Face plane distance.</param>
public sealed record W3dTriangle(
    uint V0,
    uint V1,
    uint V2,
    uint SurfaceType,
    Vector3 Normal,
    float Distance);
