namespace GenHub.Core.Models.Enums;

using System.Text.Json.Serialization;

/// <summary>
/// Defines the type of dependency relationship between content items.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DependencyType
{
    /// <summary>
    /// Required dependency - must be installed for content to work.
    /// </summary>
    Required,

    /// <summary>
    /// Recommended dependency - enhances functionality but not required.
    /// </summary>
    Recommended,

    /// <summary>
    /// Bundled dependency - included with this content.
    /// </summary>
    Bundled,

    /// <summary>
    /// Optional dependency - provides additional features if installed.
    /// </summary>
    Optional,
}
