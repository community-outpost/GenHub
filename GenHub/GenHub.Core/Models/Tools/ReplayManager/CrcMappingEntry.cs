namespace GenHub.Core.Models.Tools.ReplayManager;

/// <summary>
/// Represents a single game client CRC mapping entry in the replay catalog.
/// </summary>
public sealed record CrcMappingEntry
{
    /// <summary>
    /// Gets the executable CRC in hexadecimal format (e.g., "0x27533BB0").
    /// </summary>
    public string ExeCrc { get; init; } = string.Empty;

    /// <summary>
    /// Gets the configuration INI CRC in hexadecimal format (e.g., "0x76B251A3").
    /// </summary>
    public string IniCrc { get; init; } = string.Empty;

    /// <summary>
    /// Gets the SHA-256 hash of the primary executable if known.
    /// </summary>
    public string? Sha256 { get; init; }

    /// <summary>
    /// Gets the 5-segment manifest ID (e.g., "1.20260821.thesuperhackers.gameclient.zerohour").
    /// </summary>
    public string ManifestId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional data patch or core INI manifest ID (e.g., "1.0828261.generalsonline.gamedata.zerohour").
    /// </summary>
    public string? DataPatchManifestId { get; init; }

    /// <summary>
    /// Gets the user-friendly name of the data patch or INI configuration (e.g., "Community Patch Core INI (81FB5632)").
    /// </summary>
    public string? DataPatchName { get; init; }

    /// <summary>
    /// Gets the publisher identifier (e.g., "steam", "ea", "thesuperhackers", "generalsonline").
    /// </summary>
    public string Publisher { get; init; } = string.Empty;

    /// <summary>
    /// Gets the game type (e.g., "ZeroHour", "Generals").
    /// </summary>
    public string GameType { get; init; } = string.Empty;

    /// <summary>
    /// Gets the version string (e.g., "1.04", "2026-08-21", "082826_QFE1").
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Gets the build date string (e.g., "2026-08-21").
    /// </summary>
    public string? BuildDate { get; init; }

    /// <summary>
    /// Gets the human-readable description of the game client release.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets the direct CDN or GitHub release asset download URL for the game client if available.
    /// </summary>
    public string? CdnUrl { get; init; }

    /// <summary>
    /// Gets the direct CDN or patch download URL for the data patch BIG/INI if available.
    /// </summary>
    public string? DataPatchCdnUrl { get; init; }
}
