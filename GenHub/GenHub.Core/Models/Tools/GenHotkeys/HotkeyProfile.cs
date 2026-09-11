using System;
using System.Collections.Generic;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Tools.GenHotkeys;

/// <summary>
/// A persistent user profile containing customized hotkey mappings and settings.
/// </summary>
public class HotkeyProfile
{
    /// <summary>Gets or sets the unique profile ID.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Gets or sets the user-given profile name (e.g. "My Zero Hour Hotkeys").</summary>
    public string Name { get; set; } = "Custom Hotkeys";

    /// <summary>Gets or sets the target game (Generals or Zero Hour).</summary>
    public GameType TargetGame { get; set; } = GameType.ZeroHour;

    /// <summary>Gets or sets a value indicating whether to overlay hotkey badges directly on in-game button icons.</summary>
    public bool OverlayEnabled { get; set; } = true;

    /// <summary>Gets or sets the corner position for the icon hotkey badge overlay.</summary>
    public OverlayCorner OverlayCorner { get; set; } = OverlayCorner.TopLeft;

    /// <summary>Gets or sets the dictionary mapping CSF HotkeyString to the assigned hotkey character.</summary>
    public Dictionary<string, char> KeyMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the last modification timestamp.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
