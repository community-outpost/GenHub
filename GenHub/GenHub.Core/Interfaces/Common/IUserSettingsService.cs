using GenHub.Core.Models.Common;

namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Manages user-specific settings, including loading, saving, and providing access to the UserSettings object.
/// Deals with the raw user settings file without applying application-level defaults.
/// </summary>
public interface IUserSettingsService
{
    /// <summary>
    /// Gets the current user settings.
    /// This method returns a copy of the settings to prevent direct modification.
    /// Use Update to modify the settings.
    /// </summary>
    /// <remarks>
    /// This returns the raw settings as loaded from the user's configuration file,
    /// without any application-level defaults applied. For effective settings,
    /// use IConfigurationProviderService.
    /// </remarks>
    /// <returns>The current user settings instance.</returns>
    UserSettings Get();

    /// <summary>
    /// Updates the in-memory user settings using the provided action. Not persisted until SaveAsync is called.
    /// </summary>
    /// <param name="applyChanges">Action to apply changes to the settings.</param>
    void Update(Action<UserSettings> applyChanges);

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
    /// <returns>A task that represents the asynchronous save operation.</returns>
    Task SaveAsync();
}
