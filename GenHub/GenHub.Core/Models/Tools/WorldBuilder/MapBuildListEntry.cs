namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A pre-placed building entry in a side build list.
/// </summary>
public sealed class MapBuildListEntry
{
    /// <summary>Gets or sets the building name.</summary>
    public string BuildingName { get; set; } = string.Empty;

    /// <summary>Gets or sets the template name.</summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>Gets or sets the world X coordinate.</summary>
    public float X { get; set; }

    /// <summary>Gets or sets the world Y coordinate.</summary>
    public float Y { get; set; }

    /// <summary>Gets or sets the world Z coordinate.</summary>
    public float Z { get; set; }

    /// <summary>Gets or sets the facing angle in degrees.</summary>
    public float Angle { get; set; }

    /// <summary>Gets or sets a value indicating whether the building starts built.</summary>
    public bool InitiallyBuilt { get; set; }

    /// <summary>Gets or sets the rebuild count.</summary>
    public int NumRebuilds { get; set; }

    /// <summary>Gets or sets the attached script name.</summary>
    public string Script { get; set; } = string.Empty;

    /// <summary>Gets or sets the starting health.</summary>
    public int Health { get; set; }

    /// <summary>Gets or sets a value indicating whether the whiner flag is set.</summary>
    public bool Whiner { get; set; }

    /// <summary>Gets or sets a value indicating whether the unsellable flag is set.</summary>
    public bool Unsellable { get; set; }

    /// <summary>Gets or sets a value indicating whether the repairable flag is set.</summary>
    public bool Repairable { get; set; }
}
