using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.RmlEditor;

/// <summary>
/// The cascaded style of a single element.
/// </summary>
public sealed class RmlComputedStyle
{
    /// <summary>
    /// Gets the cascaded property values keyed by lowercase property name.
    /// </summary>
    public Dictionary<string, string> Properties { get; } = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the rules that matched the element in cascade order.
    /// </summary>
    public List<RmlMatchedRule> MatchedRules { get; } = [];

    /// <summary>
    /// Gets a property value, or null when the property is not set.
    /// </summary>
    /// <param name="property">The property name.</param>
    /// <returns>The property value or null.</returns>
    public string? GetProperty(string property)
    {
        return Properties.TryGetValue(property, out var value) ? value : null;
    }
}
