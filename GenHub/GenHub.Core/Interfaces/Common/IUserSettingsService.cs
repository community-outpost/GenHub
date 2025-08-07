using GenHub.Core.Models.Common;

namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Service responsible for managing user-scoped settings (persisted per user).
/// </summary>
public interface IUserSettingsService
{
    /// <summary>
    /// Gets a <see cref="UserSettings"/> object representing the current user settings.
    /// </summary>
    /// <remarks>
    /// Kept for backward compatibility. Prefer <see cref="GetSettings"/> for clarity.
    /// </remarks>
    /// <returns>The current user settings instance.</returns>
    ///
    UserSettings GetSettings();

    /// <summary>
    /// Updates the in-memory user settings using the provided action. Not persisted until SaveAsync is called.
    /// </summary>
    /// <param name="applyChanges">Action to apply changes to the settings.</param>
    void UpdateSettings(Action<UserSettings> applyChanges);

    /// <summary>
    /// Attempts to apply updates and persist to disk in one operation.
    /// Returns false if validation fails; no changes are persisted in that case.
    /// </summary>
    /// <param name="applyChanges">Action to apply changes to the settings.</param>
    /// <returns>True if saved successfully, otherwise false.</returns>
    Task<bool> TryUpdateAndSaveAsync(Func<UserSettings, bool> applyChanges);

    /// <summary>
    /// Asynchronously persists the current settings to disk.
    /// </summary>
    /// <returns>A task representing the asynchronous save operation.</returns>
    Task SaveAsync();
}
