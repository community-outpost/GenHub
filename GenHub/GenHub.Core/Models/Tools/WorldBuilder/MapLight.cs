namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A single global light: ambient color, diffuse color, and position.
/// </summary>
public sealed class MapLight
{
    /// <summary>Gets or sets the ambient red component.</summary>
    public float AmbientR { get; set; }

    /// <summary>Gets or sets the ambient green component.</summary>
    public float AmbientG { get; set; }

    /// <summary>Gets or sets the ambient blue component.</summary>
    public float AmbientB { get; set; }

    /// <summary>Gets or sets the diffuse red component.</summary>
    public float DiffuseR { get; set; }

    /// <summary>Gets or sets the diffuse green component.</summary>
    public float DiffuseG { get; set; }

    /// <summary>Gets or sets the diffuse blue component.</summary>
    public float DiffuseB { get; set; }

    /// <summary>Gets or sets the light X position.</summary>
    public float PosX { get; set; }

    /// <summary>Gets or sets the light Y position.</summary>
    public float PosY { get; set; }

    /// <summary>Gets or sets the light Z position.</summary>
    public float PosZ { get; set; }
}
