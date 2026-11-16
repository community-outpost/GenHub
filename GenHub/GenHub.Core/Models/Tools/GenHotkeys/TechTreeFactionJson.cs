using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.GenHotkeys;

/// <summary>
/// JSON representation of a faction in TechTree.json.
/// </summary>
public class TechTreeFactionJson
{
    /// <summary>Gets or sets the short code.</summary>
    public string ShortName { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the descriptive title.</summary>
    public string DisplayNameDescription { get; set; } = string.Empty;

    /// <summary>Gets or sets the list of buildings.</summary>
    public List<TechTreeGameObjectJson> Buildings { get; set; } = [];

    /// <summary>Gets or sets the list of infantry.</summary>
    public List<TechTreeGameObjectJson> Infantry { get; set; } = [];

    /// <summary>Gets or sets the list of vehicles.</summary>
    public List<TechTreeGameObjectJson> Vehicles { get; set; } = [];

    /// <summary>Gets or sets the list of aircraft.</summary>
    public List<TechTreeGameObjectJson> Aircrafts { get; set; } = [];
}
