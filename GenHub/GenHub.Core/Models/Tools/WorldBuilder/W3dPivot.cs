// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System.Numerics;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// One skeleton pivot: parent link and rest-pose transform.
/// </summary>
/// <param name="Name">The pivot name.</param>
/// <param name="ParentIndex">Parent pivot index, or -1 for a root.</param>
/// <param name="Translation">Rest-pose translation.</param>
/// <param name="Rotation">Rest-pose rotation quaternion (x, y, z, w).</param>
public sealed record W3dPivot(string Name, int ParentIndex, Vector3 Translation, Quaternion Rotation);
