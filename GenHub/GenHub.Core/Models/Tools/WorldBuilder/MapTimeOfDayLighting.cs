namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Lighting for one time of day: terrain set plus object set.
/// </summary>
public sealed class MapTimeOfDayLighting
{
    /// <summary>Gets the terrain lights.</summary>
    public List<MapLight> TerrainLights { get; } = [];

    /// <summary>Gets the object lights.</summary>
    public List<MapLight> ObjectLights { get; } = [];
}
