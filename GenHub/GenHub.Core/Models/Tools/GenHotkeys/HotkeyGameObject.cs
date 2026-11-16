using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.GenHotkeys;

/// <summary>
/// Represents a building, unit, or structure with one or more keyboard command layouts.
/// </summary>
public class HotkeyGameObject
{
    /// <summary>Gets or sets the entity name (e.g. "USACommandCenter").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the CSF localized name label (e.g. "OBJECT:CommandCenter").</summary>
    public string IngameName { get; set; } = string.Empty;

    /// <summary>Gets or sets the human-readable display name (e.g. "Command Center").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the category of the entity.</summary>
    public HotkeyCategory Category { get; set; } = HotkeyCategory.Buildings;

    /// <summary>Gets or sets the icon asset name (matches WebP icon).</summary>
    public string IconName { get; set; } = string.Empty;

    /// <summary>Gets or sets the list of keyboard layouts for this entity.</summary>
    public List<List<HotkeyAction>> KeyboardLayouts { get; set; } = [];
}
