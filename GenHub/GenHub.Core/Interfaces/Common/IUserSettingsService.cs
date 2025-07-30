using GenHub.Core.Models.Common;

namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Service responsible for managing application configuration settings.
/// </summary>
public interface IUserSettingsService
{
    /// <summary>
    /// Gets a deep copy of the current application settings.
    /// </summary>
    /// <returns>A copy of the current AppSettings instance.</returns>
    AppSettings GetSettings();

    /// <summary>
    /// Updates the in-memory settings using the provided action.
    /// Changes are not persisted until SaveAsync is called.
    /// </summary>
    /// <param name="applyChanges">Action to apply changes to the settings.</param>
    void UpdateSettings(Action<AppSettings> applyChanges);

    /// <summary>
    /// Asynchronously persists the current settings to disk.
    /// </summary>
    /// <returns>A task representing the save operation.</returns>
    Task SaveAsync();
}
