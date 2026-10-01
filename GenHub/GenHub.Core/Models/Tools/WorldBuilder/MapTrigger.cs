namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A polygon trigger area.
/// </summary>
public sealed class MapTrigger
{
    /// <summary>Gets or sets the trigger name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the layer name (Zero Hour).</summary>
    public string LayerName { get; set; } = string.Empty;

    /// <summary>Gets or sets the trigger id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets a value indicating whether this is a water area.</summary>
    public bool IsWaterArea { get; set; }

    /// <summary>Gets or sets a value indicating whether this is a river.</summary>
    public bool IsRiver { get; set; }

    /// <summary>Gets or sets the river start point index.</summary>
    public int RiverStart { get; set; }

    /// <summary>Gets the polygon points (integer world coordinates).</summary>
    public List<(int X, int Y, int Z)> Points { get; } = [];
}
