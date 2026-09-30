using GenHub.Core.Models.Results;
using GenHub.Core.Models.Security;

namespace GenHub.Core.Interfaces.Security;

/// <summary>
/// Persists the public keys the user trusts for each publisher ID.
/// Publisher IDs compare case-insensitively.
/// </summary>
public interface IPublisherKeyStore
{
    /// <summary>
    /// Gets every trusted publisher key.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The trusted keys, or a failure when the store cannot be read.</returns>
    Task<OperationResult<IReadOnlyList<TrustedPublisherKey>>> GetKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the trusted key for a publisher.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The trusted key, null when none is stored, or a failure when the store cannot be read.</returns>
    Task<OperationResult<TrustedPublisherKey?>> GetKeyAsync(string publisherId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a trusted key, replacing any key already stored for the same publisher.
    /// </summary>
    /// <param name="key">The key to trust.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The operation result. An unreadable store is never overwritten.</returns>
    Task<OperationResult> SaveKeyAsync(TrustedPublisherKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the trusted key for a publisher.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when a key was removed, false when none was stored, or a failure.</returns>
    Task<OperationResult<bool>> RemoveKeyAsync(string publisherId, CancellationToken cancellationToken = default);
}
