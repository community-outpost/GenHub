using GenHub.Core.Models.GeneralsOnline;

namespace GenHub.Core.Interfaces.GeneralsOnline;

/// <summary>
/// Interface for managing GeneralsOnline credentials file storage.
/// </summary>
public interface ICredentialsStorageService
{
    /// <summary>
    /// Loads the credentials from the credentials.json file.
    /// </summary>
    /// <returns>The credentials model if the file exists and is valid, otherwise null.</returns>
    Task<CredentialsModel?> LoadCredentialsAsync();

    /// <summary>
    /// Saves the credentials to the credentials.json file.
    /// </summary>
    /// <param name="credentials">The credentials to save.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SaveCredentialsAsync(CredentialsModel credentials);

    /// <summary>
    /// Deletes the credentials.json file if it exists.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteCredentialsAsync();

    /// <summary>
    /// Gets the full path to the credentials.json file.
    /// </summary>
    /// <returns>The absolute path to the credentials file.</returns>
    string GetCredentialsPath();

    /// <summary>
    /// Checks if the credentials file exists.
    /// </summary>
    /// <returns>True if the file exists, otherwise false.</returns>
    bool CredentialsFileExists();
}
