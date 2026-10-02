namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Embedded preview pixels (legacy chunk form).
/// </summary>
public sealed class MapPreviewData
{
    /// <summary>Gets or sets the width in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the height in pixels.</summary>
    public int Height { get; set; }

    /// <summary>Gets or sets the ARGB pixels row by row.</summary>
    public IList<int> Pixels { get; set; } = [];
}
