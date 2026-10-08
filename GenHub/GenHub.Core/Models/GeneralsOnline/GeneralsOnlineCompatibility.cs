namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Compatibility between a local game profile and a Generals Online lobby,
/// decided by comparing executable and INI CRC values.
/// </summary>
public enum GeneralsOnlineCompatibility
{
    /// <summary>
    /// Compatibility could not be determined.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Executable and INI CRC values both match.
    /// </summary>
    Compatible = 1,

    /// <summary>
    /// Executable matches but INI differs; desyncs are likely.
    /// </summary>
    IniMismatch = 2,

    /// <summary>
    /// Executable differs; the game binary is incompatible.
    /// </summary>
    ExeMismatch = 3,
}
