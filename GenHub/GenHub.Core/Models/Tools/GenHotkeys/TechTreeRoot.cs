using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.GenHotkeys;

/// <summary>
/// Root structure of TechTree.json.
/// </summary>
public class TechTreeRoot
{
    /// <summary>Gets or sets the list of factions in the tech tree.</summary>
    [JsonPropertyName("TechTree")]
    public List<TechTreeFactionJson> TechTree { get; set; } = [];
}
