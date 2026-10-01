// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Selects the active GlobalLighting time-of-day slot for rendering. The
/// editor stores four slots (morning, afternoon, evening, night); terrain
/// uses the slot terrain lights and objects share the first sun direction.
/// Missing or degenerate data falls back to the neutral noon setup so maps
/// without lighting chunks still render.
/// </summary>
public static class WbLightingService
{
    private static readonly Vector3 DefaultAmbient = new(0.45f, 0.45f, 0.45f);
    private static readonly Vector3 DefaultDirection = Vector3.Normalize(new Vector3(0.25f, 0.25f, 1.0f));
    private static readonly Vector3 DefaultDiffuse = new(1.0f, 1.0f, 1.0f);

    /// <summary>
    /// Resolves the lighting setup for a map's active time of day.
    /// </summary>
    /// <param name="lighting">The map lighting data.</param>
    /// <returns>The resolved setup.</returns>
    public static WbLightingSetup SelectSlot(MapLightingData lighting)
    {
        ArgumentNullException.ThrowIfNull(lighting);
        var slot = SelectTimeSlot(lighting);
        if (slot == null || slot.TerrainLights.Count == 0)
        {
            return DefaultSetup();
        }

        var sun = slot.TerrainLights[0];
        var ambient = new Vector3(sun.AmbientR, sun.AmbientG, sun.AmbientB);
        var directions = new List<Vector3>();
        var diffuse = new List<Vector3>();
        foreach (var light in slot.TerrainLights)
        {
            if (directions.Count >= WorldBuilderConstants.Limits.MaxGlobalLights)
            {
                break;
            }

            var direction = NormalizeDirection(light.PosX, light.PosY, light.PosZ);
            if (direction == null)
            {
                continue;
            }

            directions.Add(direction.Value);
            diffuse.Add(new Vector3(light.DiffuseR, light.DiffuseG, light.DiffuseB));
        }

        if (directions.Count == 0)
        {
            return DefaultSetup();
        }

        var sunDirection = directions[0];
        var horizontal = MathF.Sqrt((sunDirection.X * sunDirection.X) + (sunDirection.Y * sunDirection.Y));
        var pitch = (MathF.Atan2(sunDirection.Z, horizontal) * 180.0f) / MathF.PI;
        var yaw = (MathF.Atan2(sunDirection.Y, sunDirection.X) * 180.0f) / MathF.PI;
        return new WbLightingSetup(ambient, directions, diffuse, pitch, yaw);
    }

    private static MapTimeOfDayLighting? SelectTimeSlot(MapLightingData lighting)
    {
        if (lighting.TimesOfDay.Count == 0)
        {
            return null;
        }

        var index = Math.Clamp(lighting.TimeOfDay - 1, 0, lighting.TimesOfDay.Count - 1);
        return lighting.TimesOfDay[index];
    }

    private static Vector3? NormalizeDirection(float x, float y, float z)
    {
        var direction = new Vector3(x, y, z);
        if (direction.LengthSquared() <= float.Epsilon)
        {
            return null;
        }

        return Vector3.Normalize(direction);
    }

    private static WbLightingSetup DefaultSetup()
    {
        return new WbLightingSetup(
            DefaultAmbient,
            [DefaultDirection],
            [DefaultDiffuse],
            45.0f,
            45.0f);
    }
}
