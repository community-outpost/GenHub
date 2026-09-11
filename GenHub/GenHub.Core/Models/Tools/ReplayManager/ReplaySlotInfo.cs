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
    /// Gets the formatted display label for the slot in UI dropdowns.
    /// </summary>
    public string DisplayLabel => IsHuman
        ? $"Slot {SlotIndex}: {PlayerName}"
        : $"Slot {SlotIndex}: {PlayerName} (AI)";
}
