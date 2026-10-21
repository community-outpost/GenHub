namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A single map object: position, angle, flags, template name, and properties.
/// </summary>
public sealed class MapObjectEntry
{
    /// <summary>Gets or sets the world X coordinate.</summary>
    public float X { get; set; }

    /// <summary>Gets or sets the world Y coordinate.</summary>
    public float Y { get; set; }

    /// <summary>Gets or sets the height above the terrain surface.</summary>
    public float Z { get; set; }

    /// <summary>Gets or sets the facing angle in radians.</summary>
    public float Angle { get; set; }

    /// <summary>Gets or sets the flag bits.</summary>
    public int Flags { get; set; }

    /// <summary>Gets or sets the template or waypoint name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets the property dictionary.</summary>
    public MapDict Properties { get; } = new();
}
