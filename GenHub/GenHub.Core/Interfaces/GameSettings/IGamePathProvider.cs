using GenHub.Core.Models.Enums;

namespace GenHub.Core.Interfaces.GameSettings;

/// <summary>
/// Defines the contract for providing game-specific paths.
/// </summary>
public interface IGamePathProvider
{
    /// <summary>
    /// Gets the directory path where the Options.ini file should be located for the specified game type.
    /// </summary>
    /// <param name="gameType">The game type (Generals or ZeroHour).</param>
    /// <returns>The directory path containing the Options.ini file.</returns>
    string GetOptionsDirectory(GameType gameType);
}