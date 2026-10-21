// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Per-vertex terrain lighting. Ports BaseHeightMapRenderObjClass::doTheLight
/// on the no-normals path: only global light 0 contributes ambient, each
/// global directional light adds saturate(dot(direction-to-light, normal))
/// times its diffuse, clamped to [0,1]. Baked CPU-side per vertex like the
/// original; dynamic point lights attach later with the scene lighting.
/// </summary>
public static class WbTerrainLighting
{
    /// <summary>
    /// Computes the vertex color for a normal.
    /// </summary>
    /// <param name="normal">The unit surface normal.</param>
    /// <param name="ambient">The global ambient color (light 0).</param>
    /// <param name="lightDirections">Directions toward each global light.</param>
    /// <param name="lightDiffuse">Diffuse color per global light.</param>
    /// <returns>The clamped RGB color.</returns>
    public static Vector3 LightVertex(
        Vector3 normal,
        Vector3 ambient,
        IReadOnlyList<Vector3> lightDirections,
        IReadOnlyList<Vector3> lightDiffuse)
    {
        ArgumentNullException.ThrowIfNull(lightDirections);
        ArgumentNullException.ThrowIfNull(lightDiffuse);
        var shade = ambient;
        var count = Math.Min(lightDirections.Count, lightDiffuse.Count);
        for (var i = 0; i < count; i++)
        {
            var amount = Math.Max(0.0f, Vector3.Dot(lightDirections[i], normal));
            shade += amount * lightDiffuse[i];
        }

        return new Vector3(
            Math.Clamp(shade.X, 0.0f, 1.0f),
            Math.Clamp(shade.Y, 0.0f, 1.0f),
            Math.Clamp(shade.Z, 0.0f, 1.0f));
    }

    /// <summary>
    /// Computes the corner normal from neighbor height deltas, mirroring the
    /// HeightMap updateVB block: l2r and n2f spans crossed and normalized.
    /// </summary>
    /// <param name="heightLeft">The height one cell left in feet.</param>
    /// <param name="heightRight">The height one cell right in feet.</param>
    /// <param name="heightNear">The height one cell toward -Y in feet.</param>
    /// <param name="heightFar">The height one cell toward +Y in feet.</param>
    /// <returns>The unit normal.</returns>
    public static Vector3 CornerNormal(float heightLeft, float heightRight, float heightNear, float heightFar)
    {
        var leftToRight = new Vector3(20.0f, 0.0f, heightRight - heightLeft);
        var nearToFar = new Vector3(0.0f, 20.0f, heightFar - heightNear);
        var normal = Vector3.Cross(leftToRight, nearToFar);
        return normal.LengthSquared() <= float.Epsilon ? Vector3.UnitZ : Vector3.Normalize(normal);
    }
}
