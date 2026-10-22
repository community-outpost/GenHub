using System.Security;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.GeneralsOnline;

/// <summary>
/// Secure storage for the Generals Online refresh token.
/// Mirrors the GitHub token storage contract with platform-specific implementations.
/// </summary>
public interface IGeneralsOnlineTokenStorage
{
    /// <summary>
    /// Saves the refresh token securely.
    /// </summary>
    /// <param name="token">The secure refresh token to save.</param>
    /// <returns>A task representing the save operation.</returns>
    Task SaveTokenAsync(SecureString token);

    /// <summary>
    /// Loads the saved refresh token.
    /// </summary>
    /// <returns>The secure refresh token, or null if none saved.</returns>
    Task<SecureString?> LoadTokenAsync();

    /// <summary>
    /// Deletes the saved refresh token.
    /// </summary>
    /// <returns>A task representing the delete operation.</returns>
    Task DeleteTokenAsync();

    /// <summary>
    /// Checks if a refresh token is stored.
    /// </summary>
    /// <returns>True if a token is stored, false otherwise.</returns>
    bool HasToken();
}
