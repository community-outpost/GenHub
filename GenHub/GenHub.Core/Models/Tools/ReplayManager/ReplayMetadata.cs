using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ReplayManager;

/// <summary>
/// Contains metadata parsed from a replay file header.
/// </summary>
public sealed class ReplayMetadata
{
    /// <summary>
    /// Gets the map name.
    /// </summary>
    public string? MapName { get; init; }

    /// <summary>
    /// Gets the replay title or description embedded in the header.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the list of players.
    /// </summary>
    public IReadOnlyList<string>? Players { get; init; }

    /// <summary>
    /// Gets the game duration.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Gets the date the game was played.
    /// </summary>
    public DateTime? GameDate { get; init; }

    /// <summary>
    /// Gets the version string from the replay header (e.g., "1.04").
    /// </summary>
    public string? VersionString { get; init; }

    /// <summary>
    /// Gets the version timestamp/build string from the replay header (e.g., "Mar 13 2026").
    /// </summary>
    public string? BuildTimeString { get; init; }

    /// <summary>
    /// Gets the numeric version from the replay header.
    /// </summary>
    public uint? VersionNumber { get; init; }

    /// <summary>
    /// Gets the executable CRC from the replay header.
    /// </summary>
    public uint? ExeCrc { get; init; }

    /// <summary>
    /// Gets the INI configuration CRC from the replay header.
    /// </summary>
    public uint? IniCrc { get; init; }

    /// <summary>
    /// Gets the formatted executable CRC as a hex string (e.g., "0x27533BB0").
    /// </summary>
    public string? FormattedExeCrc => ExeCrc.HasValue ? $"0x{ExeCrc.Value:X8}" : null;

    /// <summary>
    /// Gets the formatted INI CRC as a hex string (e.g., "0x76B251A3").
    /// </summary>
    public string? FormattedIniCrc => IniCrc.HasValue ? $"0x{IniCrc.Value:X8}" : null;
}
