using System.Threading.Tasks;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Shortcuts;

/// <summary>
/// Service for managing desktop shortcuts for game profiles.
/// </summary>
public interface IShortcutService
{
    /// <summary>
    /// Creates a desktop shortcut for the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile to create a shortcut for.</param>
    /// <param name="shortcutName">Optional custom name for the shortcut. If null, uses the profile name.</param>
    /// <returns>An operation result containing the path to the created shortcut if successful.</returns>
    Task<OperationResult<string>> CreateDesktopShortcutAsync(GameProfile profile, string? shortcutName = null);

    /// <summary>
    /// Removes the desktop shortcut for the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile to remove the shortcut for.</param>
    /// <returns>An operation result indicating whether the shortcut was removed.</returns>
    Task<OperationResult<bool>> RemoveDesktopShortcutAsync(GameProfile profile);

    /// <summary>
    /// Checks if a desktop shortcut exists for the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile to check.</param>
    /// <returns>True if a shortcut exists, false otherwise.</returns>
    Task<bool> ShortcutExistsAsync(GameProfile profile);

    /// <summary>
    /// Gets the path where the shortcut would be created for the specified profile.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <param name="shortcutName">Optional custom name for the shortcut. If null, uses the profile name.</param>
    /// <returns>The full path to the shortcut file.</returns>
    string GetShortcutPath(GameProfile profile, string? shortcutName = null);

    /// <summary>
    /// Repairs main application shortcuts (e.g. desktop and start menu) to point to the current running executable.
    /// </summary>
    /// <returns>An operation result indicating whether repair succeeded.</returns>
    Task<OperationResult<bool>> RepairApplicationShortcutsAsync();
}
