using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.GenHotkeys;

/// <summary>
/// Categories of entities in Generals and Zero Hour tech trees.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HotkeyCategory
{
    /// <summary>All categories.</summary>
    All,

    /// <summary>Base and production structures.</summary>
    Buildings,

    /// <summary>Infantry units.</summary>
    Infantry,

    /// <summary>Ground vehicles and tanks.</summary>
    Vehicles,

    /// <summary>Aircraft and air units.</summary>
    Aircrafts,
}

/// <summary>
/// Corner position for hotkey badge overlay on button icons.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OverlayCorner
{
    /// <summary>Top-left corner.</summary>
    TopLeft,

    /// <summary>Top-right corner.</summary>
    TopRight,

    /// <summary>Bottom-left corner.</summary>
    BottomLeft,

    /// <summary>Bottom-right corner.</summary>
    BottomRight,
}
