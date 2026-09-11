using System;
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

    /// <summary>Gets the primary faction family ("USA", "China", or "GLA").</summary>
    public string FactionGroup
    {
        get
        {
            if (ShortName.Contains("China", StringComparison.OrdinalIgnoreCase) ||
                ShortName.Contains("PRC", StringComparison.OrdinalIgnoreCase) ||
                ShortName.Contains("Inf", StringComparison.OrdinalIgnoreCase) ||
                ShortName.Contains("Nuke", StringComparison.OrdinalIgnoreCase) ||
                ShortName.Contains("Tank", StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains("China", StringComparison.OrdinalIgnoreCase))
            {
                return "China";
            }

            if (ShortName.Contains("GLA", StringComparison.OrdinalIgnoreCase) ||
                ShortName.Contains("Tox", StringComparison.OrdinalIgnoreCase) ||
                ShortName.Contains("Demo", StringComparison.OrdinalIgnoreCase) ||
                ShortName.Contains("Stealth", StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains("GLA", StringComparison.OrdinalIgnoreCase))
            {
                return "GLA";
            }

            return "USA";
        }
    }

    /// <summary>Gets a cleanly formatted title for dropdown selection.</summary>
    public string FormattedTitle
    {
        get
        {
            if (string.Equals(DisplayName, FactionGroup, StringComparison.OrdinalIgnoreCase))
            {
                return DisplayName;
            }

            return $"{FactionGroup} • {DisplayName}";
        }
    }
}
