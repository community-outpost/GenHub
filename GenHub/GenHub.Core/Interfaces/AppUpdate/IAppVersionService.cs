namespace GenHub.Core.Interfaces.AppUpdate;

/// <summary>
/// Interface for getting the current application version.
/// </summary>
public interface IAppVersionService
{
    /// <summary>
    /// Gets the current application version.
    /// </summary>
    /// <returns>The current version string.</returns>
    string GetCurrentVersion();
}