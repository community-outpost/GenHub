namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Global lighting for all times of day plus the shadow color.
/// </summary>
public sealed class MapLightingData
{
    /// <summary>Gets or sets the active time of day.</summary>
    public int TimeOfDay { get; set; }

    /// <summary>Gets the per-time-of-day lighting sets.</summary>
    public List<MapTimeOfDayLighting> TimesOfDay { get; } = [];

    /// <summary>Gets or sets the shadow color.</summary>
    public int ShadowColor { get; set; }
}
