using GenHub.Core.Constants;
using System;

namespace GenHub.Core.Models.Tools.ReplayManager;

/// <summary>
/// Represents structured player slot metadata parsed from a replay match setup string (S= token).
/// </summary>
/// <param name="SlotIndex">The 0-indexed player slot in the match.</param>
/// <param name="PlayerName">The display name of the player in this slot.</param>
/// <param name="IsHuman">Whether the player is a human player (true) or computer AI (false).</param>
/// <param name="FactionIndex">The optional side/faction index chosen for this slot.</param>
/// <param name="ColorIndex">The optional player color index chosen for this slot.</param>
public sealed record ReplaySlotInfo(
    int SlotIndex,
    string PlayerName,
    bool IsHuman,
    int? FactionIndex = null,
    int? ColorIndex = null)
{
    /// <summary>
    /// Gets the formatted display label for the slot in UI dropdowns (using 1-based indexing for user display).
    /// </summary>
    public string DisplayLabel
    {
        get
        {
            if (IsHuman || HasAiIndicator(PlayerName))
            {
                return $"Slot {SlotIndex + 1}: {PlayerName}";
            }

            return $"Slot {SlotIndex + 1}: {PlayerName} ({ReplayManagerConstants.AiLabel})";
        }
    }

    /// <summary>
    /// Checks whether the specified player name indicates a computer AI.
    /// </summary>
    /// <param name="name">The player name.</param>
    /// <returns>True if the name indicates AI; otherwise, false.</returns>
    public static bool HasAiIndicator(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.Equals(ReplayManagerConstants.AiLabel, StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith(ReplayManagerConstants.AiLabel + " ", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith(ReplayManagerConstants.AiLabel + ":", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(" " + ReplayManagerConstants.AiLabel, StringComparison.OrdinalIgnoreCase) ||
               name.Contains($"({ReplayManagerConstants.AiLabel})", StringComparison.OrdinalIgnoreCase) ||
               name.Contains($"[{ReplayManagerConstants.AiLabel}]", StringComparison.OrdinalIgnoreCase);
    }
}
