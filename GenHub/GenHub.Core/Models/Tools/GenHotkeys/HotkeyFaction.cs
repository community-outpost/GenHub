using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.GenHotkeys;

/// <summary>
/// Represents a faction or sub-faction in Generals / Zero Hour.
/// </summary>
public class HotkeyFaction
{
    /// <summary>Gets or sets the short code (e.g. "USA", "AirF", "Laser", "SuperW", "China", "GLA").</summary>
    public string ShortName { get; set; } = string.Empty;

    /// <summary>Gets or sets the primary display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the full descriptive title.</summary>
    public string DisplayNameDescription { get; set; } = string.Empty;

    /// <summary>Gets or sets the list of game objects belonging to this faction.</summary>
    public List<HotkeyGameObject> GameObjects { get; set; } = [];
}
