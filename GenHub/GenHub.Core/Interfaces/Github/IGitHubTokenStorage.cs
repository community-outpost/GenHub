using System.Security;

namespace GenHub.Core.Interfaces.GitHub;

/// <summary>
/// Interface for GitHub token storage.
/// </summary>
public interface IGitHubTokenStorage
{
    /// <summary>
    /// Saves a GitHub token securely.
    /// </summary>
    /// <param name="token">The secure token to save.</param>
    /// <returns>A task representing the save operation.</returns>
    Task SaveTokenAsync(SecureString token);

    /// <summary>
    /// Loads the saved GitHub token.
    /// </summary>
    /// <returns>The secure token, or null if none saved.</returns>
    Task<SecureString?> LoadTokenAsync();

    /// <summary>
    /// Deletes the saved token.
    /// </summary>
    /// <returns>A task representing the delete operation.</returns>
    Task DeleteTokenAsync();

    /// <summary>
    /// Checks if a token is stored.
    /// </summary>
    /// <returns>True if a token is stored, false otherwise.</returns>
    bool HasToken();
}
