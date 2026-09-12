using System;
using System.Collections.Generic;
using GenHub.Core.Constants;

namespace GenHub.Core.Models.Tools.GenHotkeys;

/// <summary>
/// Represents a faction in the game (e.g. USA, China, GLA) and its sub-factions/generals.
/// </summary>
public class HotkeyFaction
{
    /// <summary>Primary faction group name for China.</summary>
    public const string ChinaGroup = "China";

    /// <summary>Primary faction group name for GLA.</summary>
    public const string GlaGroup = "GLA";

    /// <summary>Primary faction group name for USA.</summary>
    public const string UsaGroup = "USA";

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
            if (string.Equals(ShortName, GenHotkeysConstants.FactionCodes.Prc, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ShortName, GenHotkeysConstants.FactionCodes.Infantry, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ShortName, GenHotkeysConstants.FactionCodes.Nuke, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ShortName, GenHotkeysConstants.FactionCodes.Tank, StringComparison.OrdinalIgnoreCase) ||
                ShortName.Contains(ChinaGroup, StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains(ChinaGroup, StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains(GenHotkeysConstants.FactionCodes.KeywordInfantry, StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains(GenHotkeysConstants.FactionCodes.KeywordNuke, StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains(GenHotkeysConstants.FactionCodes.KeywordTank, StringComparison.OrdinalIgnoreCase) ||
                DisplayNameDescription.Contains(ChinaGroup, StringComparison.OrdinalIgnoreCase) ||
                DisplayNameDescription.Contains(GenHotkeysConstants.FactionCodes.KeywordInfantry, StringComparison.OrdinalIgnoreCase) ||
                DisplayNameDescription.Contains(GenHotkeysConstants.FactionCodes.KeywordNuke, StringComparison.OrdinalIgnoreCase) ||
                DisplayNameDescription.Contains(GenHotkeysConstants.FactionCodes.KeywordTank, StringComparison.OrdinalIgnoreCase))
            {
                return ChinaGroup;
            }

            // GLA factions & generals: GLA (GLA), Toxic (TOX), Stealth (STL), Demo (DML)
            if (string.Equals(ShortName, GenHotkeysConstants.FactionCodes.Gla, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ShortName, GenHotkeysConstants.FactionCodes.Toxic, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ShortName, GenHotkeysConstants.FactionCodes.Stealth, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ShortName, GenHotkeysConstants.FactionCodes.Demo, StringComparison.OrdinalIgnoreCase) ||
                ShortName.Contains(GlaGroup, StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains(GlaGroup, StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains(GenHotkeysConstants.FactionCodes.KeywordTox, StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains(GenHotkeysConstants.FactionCodes.KeywordStealth, StringComparison.OrdinalIgnoreCase) ||
                DisplayName.Contains(GenHotkeysConstants.FactionCodes.KeywordDemo, StringComparison.OrdinalIgnoreCase) ||
                DisplayNameDescription.Contains(GlaGroup, StringComparison.OrdinalIgnoreCase) ||
                DisplayNameDescription.Contains(GenHotkeysConstants.FactionCodes.KeywordTox, StringComparison.OrdinalIgnoreCase) ||
                DisplayNameDescription.Contains(GenHotkeysConstants.FactionCodes.KeywordStealth, StringComparison.OrdinalIgnoreCase) ||
                DisplayNameDescription.Contains(GenHotkeysConstants.FactionCodes.KeywordDemo, StringComparison.OrdinalIgnoreCase))
            {
                return GlaGroup;
            }

            // USA factions & generals: USA (USA), Superweapon (SWG), Air (AIR), Laser (LSR)
            return UsaGroup;
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
