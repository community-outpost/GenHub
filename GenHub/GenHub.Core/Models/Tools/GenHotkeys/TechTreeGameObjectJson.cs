using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.GenHotkeys;

/// <summary>
/// JSON representation of a game object in TechTree.json.
/// </summary>
public class TechTreeGameObjectJson
{
    /// <summary>Gets or sets the entity name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the localized string identifier.</summary>
    public string IngameName { get; set; } = string.Empty;

    /// <summary>Gets or sets the keyboard layouts for this entity.</summary>
    public List<List<TechTreeActionJson>> KeyboardLayouts { get; set; } = [];
}
