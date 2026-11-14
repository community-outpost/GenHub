using GenHub.Core.Models.Tools.ModelViewer;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace GenHub.Core.Services.Tools.ModelViewer;

/// <summary>
/// CPU ray-triangle picking over render scenes for click selection.
/// Tests both faces since the viewer disables face culling.
/// </summary>
public static class W3dRayPicker
{
    /// <summary>
    /// Picks the closest mesh intersected by a ray.
    /// </summary>
    /// <param name="scene">The render scene.</param>
    /// <param name="origin">The ray origin in world space.</param>
    /// <param name="direction">The normalized ray direction.</param>
    /// <returns>The pick result, or null when nothing is hit.</returns>
    public static W3dPickResult? Pick(W3dRenderScene scene, Vector3 origin, Vector3 direction)
    {
        return Pick(scene, origin, direction, null);
    }

    /// <summary>
    /// Picks the closest mesh intersected by a ray with per-mesh model transforms.
    /// </summary>
    /// <param name="scene">The render scene.</param>
    /// <param name="origin">The ray origin in world space.</param>
    /// <param name="direction">The normalized ray direction.</param>
    /// <param name="meshModels">The per-mesh model transforms, or null for the bind pose.</param>
    /// <returns>The pick result, or null when nothing is hit.</returns>
    public static W3dPickResult? Pick(W3dRenderScene scene, Vector3 origin, Vector3 direction, IReadOnlyList<Matrix4x4>? meshModels)
    {
        return Pick(scene, origin, direction, meshModels, null);
    }

    /// <summary>
    /// Picks the closest visible mesh intersected by a ray.
    /// </summary>
    /// <param name="scene">The render scene.</param>
    /// <param name="origin">The ray origin in world space.</param>
    /// <param name="direction">The normalized ray direction.</param>
    /// <param name="meshModels">The per-mesh model transforms, or null for the bind pose.</param>
    /// <param name="isHidden">Predicate excluding hidden meshes by name, or null for none.</param>
    /// <returns>The pick result, or null when nothing is hit.</returns>
    public static W3dPickResult? Pick(
        W3dRenderScene scene,
        Vector3 origin,
        Vector3 direction,
        IReadOnlyList<Matrix4x4>? meshModels,
        Func<string, bool>? isHidden)
    {
        ArgumentNullException.ThrowIfNull(scene);
        W3dPickResult? best = null;

        for (int m = 0; m < scene.Meshes.Count; m++)
        {
            if (scene.Meshes[m].IsHidden || (isHidden != null && isHidden(scene.Meshes[m].Name)))
            {
                continue;
            }

            var model = meshModels != null && m < meshModels.Count ? meshModels[m] : Matrix4x4.Identity;
            float? distance = RayMesh(scene.Meshes[m], model, origin, direction);
            if (distance.HasValue && (best == null || distance.Value < best.Distance))
            {
                best = new W3dPickResult(m, distance.Value);
            }
        }

        return best;
    }

    private static float? RayMesh(W3dRenderMesh mesh, Matrix4x4 model, Vector3 origin, Vector3 direction)
    {
        float? best = null;
        var vertices = mesh.Vertices;
        var indices = mesh.Indices;

        for (int t = 0; t + 2 < indices.Count; t += 3)
        {
            var p0 = Vector3.Transform(Corner(vertices, (int)indices[t]), model);
            var p1 = Vector3.Transform(Corner(vertices, (int)indices[t + 1]), model);
            var p2 = Vector3.Transform(Corner(vertices, (int)indices[t + 2]), model);
            float? distance = RayTriangle(origin, direction, p0, p1, p2);
            if (distance.HasValue && (best == null || distance.Value < best.Value))
            {
                best = distance;
            }
        }

        return best;
    }

    private static Vector3 Corner(IReadOnlyList<float> vertices, int vertexIndex)
    {
        int offset = vertexIndex * 12;
        return new Vector3(vertices[offset], vertices[offset + 1], vertices[offset + 2]);
    }

    private static float? RayTriangle(Vector3 origin, Vector3 direction, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        var edge1 = p1 - p0;
        var edge2 = p2 - p0;
        var ray = Vector3.Cross(direction, edge2);
        float determinant = Vector3.Dot(edge1, ray);
        if (Math.Abs(determinant) < 1e-8f)
        {
            return null;
        }

        float inverse = 1 / determinant;
        var s = origin - p0;
        float u = Vector3.Dot(s, ray) * inverse;
        if (u is < 0 or > 1)
        {
            return null;
        }

        var q = Vector3.Cross(s, edge1);
        float v = Vector3.Dot(direction, q) * inverse;
        if (v < 0 || u + v > 1)
        {
            return null;
        }

        float distance = Vector3.Dot(edge2, q) * inverse;
        return distance > 1e-6f ? distance : null;
    }
}
