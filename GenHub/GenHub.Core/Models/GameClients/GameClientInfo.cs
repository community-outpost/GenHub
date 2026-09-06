using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.GameClients;

/// <summary>
/// Represents information about a game executable including its hash, game type, version, and metadata.
/// Used for extensible game client detection across official and 3rd party distributions.
/// </summary>
public readonly struct GameClientInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GameClientInfo"/> struct.
    /// </summary>
    /// <param name="gameType">The type of game.</param>
    /// <param name="version">The version string.</param>
    /// <param name="publisher">The publisher/distributor (e.g., "EA", "Steam", "ThirdParty", "Community-Outpost").</param>
    /// <param name="description">Optional description of this executable variant.</param>
    /// <param name="isOfficial">Whether this is an official release or community modification.</param>
    public GameClientInfo(GameType gameType, string version, string publisher = GameClientConstants.UnknownVersion, string description = "", bool isOfficial = true)
    {
        GameType = gameType;
        Version = version ?? GameClientConstants.UnknownVersion;
        Publisher = publisher ?? GameClientConstants.UnknownVersion;
        Description = description ?? string.Empty;
        IsOfficial = isOfficial;
        DetectedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the game type (Generals, ZeroHour, etc.).
    /// </summary>
    public GameType GameType { get; }

    /// <summary>
    /// Gets the version string (e.g., "1.08", "1.04").
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the publisher or distributor name.
    /// </summary>
    public string Publisher { get; }

    /// <summary>
    /// Gets the optional description of this executable variant.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets a value indicating whether this is an official release.
    /// </summary>
    public bool IsOfficial { get; }

    /// <summary>
    /// Gets the UTC timestamp when this hash was first detected or added.
    /// </summary>
    public DateTime DetectedAt { get; }

    /// <summary>
    /// Validates that this GameClientInfo instance has valid data.
    /// </summary>
    /// <returns>True if the instance is valid, false otherwise.</returns>
    public readonly bool Validate()
    {
        return !string.IsNullOrEmpty(Version)
            && GameType != GameType.Unknown
            && !string.IsNullOrEmpty(Publisher);
    }

    /// <summary>
    /// Returns a string representation of this game executable info.
    /// </summary>
    /// <returns>A formatted string with game type, version, and publisher.</returns>
    public override readonly string ToString()
    {
        return $"{GameType} {Version} ({Publisher})";
    }
}