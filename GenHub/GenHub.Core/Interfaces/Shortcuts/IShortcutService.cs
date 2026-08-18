using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Shortcuts;

/// <summary>
/// Provides services for creating and managing desktop shortcuts for game profiles.
/// </summary>
public interface IShortcutService
{
    /// <summary>
    /// Creates a desktop shortcut for the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile to create a shortcut for.</param>
    /// <param name="shortcutName">Optional custom name for the shortcut. If null, uses the profile name.</param>
    /// <returns>An operation result indicating success or failure, with the shortcut path on success.</returns>
    Task<OperationResult<string>> CreateDesktopShortcutAsync(GameProfile profile, string? shortcutName = null);

    /// <summary>
    /// Removes a desktop shortcut for the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile whose shortcut should be removed.</param>
    /// <returns>An operation result indicating success or failure.</returns>
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
    /// Creates a shortcut at the specified path.
    /// </summary>
    /// <param name="shortcutPath">The path where the shortcut will be created.</param>
    /// <param name="targetPath">The path to the target executable.</param>
    /// <param name="arguments">Optional command line arguments.</param>
    /// <param name="workingDirectory">Optional working directory.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="iconPath">Optional icon path.</param>
    /// <returns>An operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> CreateShortcutAsync(
        string shortcutPath,
        string targetPath,
        string? arguments = null,
        string? workingDirectory = null,
        string? description = null,
        string? iconPath = null);
}
