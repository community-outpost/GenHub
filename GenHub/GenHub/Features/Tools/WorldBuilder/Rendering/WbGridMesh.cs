// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System;
using System.Collections.Generic;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Builds the ground grid line mesh for the 3D viewport: one quad outline
/// plus interior lines at the grid spacing, lifted slightly off the ground
/// to avoid depth fighting.
/// </summary>
public static class WbGridMesh
{
    /// <summary>
    /// Builds interleaved XYZ line vertices for the grid.
    /// </summary>
    /// <param name="minX">The minimum world X in feet.</param>
    /// <param name="minY">The minimum world Y in feet.</param>
    /// <param name="maxX">The maximum world X in feet.</param>
    /// <param name="maxY">The maximum world Y in feet.</param>
    /// <param name="spacingFeet">The line spacing in feet.</param>
    /// <param name="liftFeet">The height above the ground plane.</param>
    /// <returns>The vertex positions (triples).</returns>
    public static float[] Build(float minX, float minY, float maxX, float maxY, float spacingFeet, float liftFeet)
    {
        var spacing = Math.Max(1.0f, spacingFeet);
        var vertices = new List<float>();
        for (var x = minX; x <= maxX + 0.001f; x += spacing)
        {
            var clamped = Math.Min(x, maxX);
            vertices.Add(clamped);
            vertices.Add(minY);
            vertices.Add(liftFeet);
            vertices.Add(clamped);
            vertices.Add(maxY);
            vertices.Add(liftFeet);
        }

        for (var y = minY + spacing; y < maxY - 0.001f; y += spacing)
        {
            vertices.Add(minX);
            vertices.Add(y);
            vertices.Add(liftFeet);
            vertices.Add(maxX);
            vertices.Add(y);
            vertices.Add(liftFeet);
        }

        // Border horizontals (the min/max horizontals are not covered above).
        vertices.Add(minX);
        vertices.Add(minY);
        vertices.Add(liftFeet);
        vertices.Add(maxX);
        vertices.Add(minY);
        vertices.Add(liftFeet);
        vertices.Add(minX);
        vertices.Add(maxY);
        vertices.Add(liftFeet);
        vertices.Add(maxX);
        vertices.Add(maxY);
        vertices.Add(liftFeet);
        return [.. vertices];
    }
}
