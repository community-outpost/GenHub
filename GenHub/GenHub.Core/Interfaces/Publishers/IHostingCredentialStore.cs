namespace GenHub.Core.Interfaces.Publishers;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Securely stores, retrieves, and deletes authentication tokens for hosting providers.
/// Uses platform-native protection (DPAPI on Windows, user-scoped permissions and encryption on Unix/macOS)
/// to prevent plaintext credential leakage on disk.
/// </summary>
public interface IHostingCredentialStore
{
    /// <summary>
    /// Securely stores the credential for the specified provider.
    /// </summary>
    /// <param name="providerId">The hosting provider identifier (e.g., "github", "dropbox").</param>
    /// <param name="credential">The secret token or credential to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SaveCredentialAsync(string providerId, string credential, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the stored credential for the specified provider.
    /// </summary>
    /// <param name="providerId">The hosting provider identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored credential, or null if not found.</returns>
    Task<string?> GetCredentialAsync(string providerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the stored credential for the specified provider.
    /// </summary>
    /// <param name="providerId">The hosting provider identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteCredentialAsync(string providerId, CancellationToken cancellationToken = default);
}
