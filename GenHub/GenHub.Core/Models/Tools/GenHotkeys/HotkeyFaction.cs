namespace GenHub.Core.Models.Tools.GenHotkeys;

/// <summary>
/// Represents a faction in the game (e.g. USA, China, GLA) and its sub-factions/generals.
/// </summary>
public class HotkeyFaction
{
    /// <summary>Gets or sets the short internal name (e.g. "USA", "AIR", "PRC", "INF", "GLA", "TOX").</summary>
    public string ShortName { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name (e.g. "USA", "AIR", "CHINA", "INFANTRY").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the descriptive title (e.g. "United States of America", "Airforce General").</summary>
    public string DisplayNameDescription { get; set; } = string.Empty;

    /// <summary>Gets or sets the list of game objects belonging to this faction.</summary>
    public List<HotkeyGameObject> GameObjects { get; set; } = [];

    /// <summary>Gets the primary faction family ("USA", "China", or "GLA").</summary>
    public string FactionGroup
    {
        get
        {
            // China factions & generals: China (PRC), Infantry (INF), Nuke (NUK), Tank (TNK)
            if (string.Equals(ShortName, "PRC", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ShortName, "INF", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ShortName, "NUK", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ShortName, "TNK", StringComparison.OrdinalIgnoreCase) ||
                ShortName.Contains("China", StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains("China", StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains("Infantry", StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains("Nuke", StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains("Tank", StringComparison.OrdinalIgnoreCase) ||
                DisplayNameDescription.Contains("China", StringComparison.OrdinalIgnoreCase) ||
                DisplayNameDescription.Contains("Infantry", StringComparison.OrdinalIgnoreCase) ||
                DisplayNameDescription.Contains("Nuke", StringComparison.OrdinalIgnoreCase) ||
                DisplayNameDescription.Contains("Tank", StringComparison.OrdinalIgnoreCase))
            {
                return "China";
            }

            // GLA factions & generals: GLA (GLA), Toxic (TOX), Stealth (STL), Demo (DML)
            if (string.Equals(ShortName, "GLA", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ShortName, "TOX", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ShortName, "STL", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ShortName, "DML", StringComparison.OrdinalIgnoreCase) ||
                ShortName.Contains("GLA", StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains("GLA", StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains("Tox", StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains("Stealth", StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains("Demo", StringComparison.OrdinalIgnoreCase) ||
                DisplayNameDescription.Contains("GLA", StringComparison.OrdinalIgnoreCase) ||
                DisplayNameDescription.Contains("Toxic", StringComparison.OrdinalIgnoreCase) ||
                DisplayNameDescription.Contains("Stealth", StringComparison.OrdinalIgnoreCase) ||
                DisplayNameDescription.Contains("Demo", StringComparison.OrdinalIgnoreCase))
            {
                return "GLA";
            }

            // USA factions & generals: USA (USA), Superweapon (SWG), Air (AIR), Laser (LSR)
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
                return FactionGroup.ToUpperInvariant();
            }

            return $"{FactionGroup.ToUpperInvariant()} • {DisplayName}";
        }
    }
}
