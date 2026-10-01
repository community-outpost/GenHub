// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System.Collections.Generic;
using System.Numerics;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Resolved lighting for one time-of-day slot: terrain bake inputs plus the
/// object sun angles shared by the 3D model pass.
/// </summary>
/// <param name="Ambient">Terrain ambient color.</param>
/// <param name="LightDirections">Directions toward each terrain light.</param>
/// <param name="LightDiffuse">Diffuse color per terrain light.</param>
/// <param name="SunPitchDegrees">Sun pitch above the horizon in degrees.</param>
/// <param name="SunYawDegrees">Sun yaw in degrees.</param>
public sealed record WbLightingSetup(
    Vector3 Ambient,
    IReadOnlyList<Vector3> LightDirections,
    IReadOnlyList<Vector3> LightDiffuse,
    float SunPitchDegrees,
    float SunYawDegrees);
