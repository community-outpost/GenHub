using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.GenHotkeys;

namespace GenHub.Core.Interfaces.Tools.GenHotkeys;

/// <summary>
/// Service for managing persistent hotkey profiles and presets.
/// </summary>
public interface IHotkeyProfileStorageService
{
    /// <summary>
    /// Gets all saved user profiles for the specified game.
    /// </summary>
    /// <param name="gameType">The target game.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of saved profiles.</returns>
    Task<IReadOnlyList<HotkeyProfile>> GetProfilesAsync(GameType gameType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates a profile to persistent storage.
    /// </summary>
    /// <param name="profile">The profile to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The saved profile.</returns>
    Task<HotkeyProfile> SaveProfileAsync(HotkeyProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a profile from persistent storage.
    /// </summary>
    /// <param name="profileId">The profile ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted; otherwise false.</returns>
    Task<bool> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a built-in preset (e.g. "Vanilla", "Legionnaire", "Leikeze").
    /// </summary>
    /// <param name="presetName">The preset name.</param>
    /// <param name="gameType">The target game.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new profile populated with the preset mappings.</returns>
    Task<HotkeyProfile> LoadPresetAsync(string presetName, GameType gameType, CancellationToken cancellationToken = default);
}
