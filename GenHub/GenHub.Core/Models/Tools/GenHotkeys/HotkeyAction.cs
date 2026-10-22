using System;

namespace GenHub.Core.Models.Tools.GenHotkeys;

/// <summary>
/// Represents a single action/command button with an assigned hotkey.
/// </summary>
public class HotkeyAction
{
    /// <summary>Gets or sets the icon asset name (e.g. "USADozer").</summary>
    public string IconName { get; set; } = string.Empty;

    /// <summary>Gets or sets the CSF string identifier (e.g. "CONTROLBAR:ConstructAmericaDozer").</summary>
    public string HotkeyString { get; set; } = string.Empty;

    /// <summary>Gets or sets the friendly display name for the action.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the currently assigned hotkey character (e.g. 'D'), or null if unassigned.</summary>
    public char? Hotkey { get; set; }

    /// <summary>Gets or sets the original default hotkey character from the game.</summary>
    public char? DefaultHotkey { get; set; }

    /// <summary>Gets or sets a value indicating whether this hotkey conflicts with another action in the same layout.</summary>
    public bool IsConflict { get; set; }

    /// <summary>Gets or sets an explanation if there is a conflict.</summary>
    public string? ConflictReason { get; set; }

    /// <summary>Gets the display text for the hotkey badge (e.g. "[D]" or "[-]").</summary>
    public string HotkeyBadge => Hotkey.HasValue ? $"[{char.ToUpperInvariant(Hotkey.Value)}]" : "[-]";
}
