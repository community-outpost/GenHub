namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A water wave track from a .wak companion file.
/// </summary>
public sealed class WaveTrackRecord
{
    /// <summary>Gets or sets the start X coordinate.</summary>
    public float StartX { get; set; }

    /// <summary>Gets or sets the start Y coordinate.</summary>
    public float StartY { get; set; }

    /// <summary>Gets or sets the end X coordinate.</summary>
    public float EndX { get; set; }

    /// <summary>Gets or sets the end Y coordinate.</summary>
    public float EndY { get; set; }

    /// <summary>Gets or sets the wave type id.</summary>
    public int WaveType { get; set; }
}
