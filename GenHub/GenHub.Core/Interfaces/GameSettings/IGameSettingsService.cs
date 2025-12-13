using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameSettings;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameSettings;

/// <summary>
/// Defines the contract for managing game settings (Options.ini) for Generals and Zero Hour.
/// </summary>
public interface IGameSettingsService
{
    /// <summary>
    /// Loads the Options.ini file for the specified game type.
    /// </summary>
    /// <param name="gameType">The game type (Generals or ZeroHour).</param>
    /// <returns>An operation result containing the loaded options or errors.</returns>
    Task<OperationResult<IniOptions>> LoadOptionsAsync(GameType gameType);

    /// <summary>
    /// Saves the Options.ini file for the specified game type.
    /// </summary>
    /// <param name="gameType">The game type (Generals or ZeroHour).</param>
    /// <param name="options">The options to save.</param>
    /// <returns>An operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> SaveOptionsAsync(GameType gameType, IniOptions options);

    /// <summary>
    /// Gets the path to the Options.ini file for the specified game type.
    /// </summary>
    /// <param name="gameType">The game type (Generals or ZeroHour).</param>
    /// <returns>The full path to the Options.ini file.</returns>
    string GetOptionsFilePath(GameType gameType);

    /// <summary>
    /// Checks if the Options.ini file exists for the specified game type.
    /// </summary>
    /// <param name="gameType">The game type (Generals or ZeroHour).</param>
    /// <returns>True if the file exists, false otherwise.</returns>
    bool OptionsFileExists(GameType gameType);

    /// <summary>
    /// Loads TheSuperHackers-specific settings from Options.ini.
    /// </summary>
    /// <param name="gameType">The game type (Generals or ZeroHour).</param>
    /// <returns>An operation result containing the loaded TheSuperHackers settings or errors.</returns>
    Task<OperationResult<TheSuperHackersSettings>> LoadTheSuperHackersSettingsAsync(GameType gameType);

    /// <summary>
    /// Saves TheSuperHackers-specific settings to Options.ini.
    /// </summary>
    /// <param name="gameType">The game type (Generals or ZeroHour).</param>
    /// <param name="settings">The TheSuperHackers settings to save.</param>
    /// <returns>An operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> SaveTheSuperHackersSettingsAsync(GameType gameType, TheSuperHackersSettings settings);

    /// <summary>
    /// Loads GeneralsOnline-specific settings from settings.json.
    /// </summary>
    /// <returns>An operation result containing the loaded GeneralsOnline settings or errors.</returns>
    Task<OperationResult<GeneralsOnlineSettings>> LoadGeneralsOnlineSettingsAsync();

    /// <summary>
    /// Saves GeneralsOnline-specific settings to settings.json.
    /// </summary>
    /// <param name="settings">The GeneralsOnline settings to save.</param>
    /// <returns>An operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> SaveGeneralsOnlineSettingsAsync(GeneralsOnlineSettings settings);
}
