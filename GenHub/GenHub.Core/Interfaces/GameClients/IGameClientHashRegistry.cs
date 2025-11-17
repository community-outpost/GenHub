using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;

namespace GenHub.Core.Interfaces.GameClients;

/// <summary>
/// Interface for game client hash registry to support dependency injection and testability.
/// </summary>
public interface IGameClientHashRegistry
{
    /// <summary>
    /// Attempts to get game client information for the specified hash.
    /// </summary>
    /// <param name="hash">The SHA-256 hash of the executable.</param>
    /// <param name="info">The game client information if found.</param>
    /// <returns>True if the hash was found, false otherwise.</returns>
    bool TryGetInfo(string hash, out GameClientInfo? info);

    /// <summary>
    /// Attempts to add a new hash to the registry.
    /// </summary>
    /// <param name="hash">The SHA-256 hash of the executable.</param>
    /// <param name="info">The game client information.</param>
    /// <returns>True if the hash was added, false if it already existed.</returns>
    bool TryAddHash(string hash, GameClientInfo info);

    /// <summary>
    /// Gets the version string for a hash and game type.
    /// </summary>
    /// <param name="hash">The SHA-256 hash of the executable.</param>
    /// <param name="gameType">The game type to match.</param>
    /// <returns>The version string or "Unknown" if not found.</returns>
    string GetVersionFromHash(string hash, GameType gameType);

    /// <summary>
    /// Gets both game type and version from a hash.
    /// </summary>
    /// <param name="hash">The SHA-256 hash of the executable.</param>
    /// <returns>A tuple with game type and version.</returns>
    (GameType GameType, string Version) GetGameInfoFromHash(string hash);

    /// <summary>
    /// Checks if a hash is known.
    /// </summary>
    /// <param name="hash">The SHA-256 hash to check.</param>
    /// <returns>True if the hash is known.</returns>
    bool IsKnownHash(string hash);

    /// <summary>
    /// Gets all hashes for a specific game type.
    /// </summary>
    /// <param name="gameType">The game type to filter by.</param>
    /// <returns>A dictionary of hashes and versions.</returns>
    Dictionary<string, string> GetHashesForGameType(GameType gameType);

    /// <summary>
    /// Gets all executable info for a specific game type.
    /// </summary>
    /// <param name="gameType">The game type to filter by.</param>
    /// <returns>A dictionary of hashes and their info.</returns>
    Dictionary<string, GameClientInfo> GetExecutableInfoForGameType(GameType gameType);

    /// <summary>
    /// Gets the list of possible executable names.
    /// </summary>
    IReadOnlyList<string> PossibleExecutableNames { get; }

    /// <summary>
    /// Adds a possible executable name.
    /// </summary>
    /// <param name="executableName">The executable name to add.</param>
    /// <returns>True if added, false if already exists.</returns>
    bool AddPossibleExecutableName(string executableName);
}