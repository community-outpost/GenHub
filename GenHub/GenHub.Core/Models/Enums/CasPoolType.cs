using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Enums;

/// <summary>
/// Identifies which CAS (Content-Addressable Storage) pool to use for content storage.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CasPoolType
{
    /// <summary>
    /// Primary pool for maps, mods, and user content.
    /// Stored on app data drive (typically C:) for easy hardlinking.
    /// </summary>
    Primary,

    /// <summary>
    /// Installation pool for game clients and installations.
    /// Stored on same drive as game installation for hardlink support.
    /// </summary>
    Installation,
}
