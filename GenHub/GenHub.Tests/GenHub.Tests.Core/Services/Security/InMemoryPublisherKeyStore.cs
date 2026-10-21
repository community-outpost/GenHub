using GenHub.Core.Interfaces.Security;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Security;

namespace GenHub.Tests.Core.Services.Security;

/// <summary>
/// In-memory <see cref="IPublisherKeyStore"/> for identity tests.
/// </summary>
internal sealed class InMemoryPublisherKeyStore : IPublisherKeyStore
{
    private readonly Dictionary<string, TrustedPublisherKey> _keys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets a failure returned by every read, simulating an unreadable store.
    /// </summary>
    public string? ReadFailure { get; set; }

    /// <summary>
    /// Gets the stored keys.
    /// </summary>
    public IReadOnlyDictionary<string, TrustedPublisherKey> Keys => _keys;

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<TrustedPublisherKey>>> GetKeysAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ReadFailure is null
            ? OperationResult<IReadOnlyList<TrustedPublisherKey>>.CreateSuccess(_keys.Values.ToList())
            : OperationResult<IReadOnlyList<TrustedPublisherKey>>.CreateFailure(ReadFailure));

    /// <inheritdoc />
    public Task<OperationResult<TrustedPublisherKey?>> GetKeyAsync(string publisherId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ReadFailure is null
            ? OperationResult<TrustedPublisherKey?>.CreateSuccess(_keys.GetValueOrDefault(publisherId))
            : OperationResult<TrustedPublisherKey?>.CreateFailure(ReadFailure));

    /// <inheritdoc />
    public Task<OperationResult> SaveKeyAsync(TrustedPublisherKey key, CancellationToken cancellationToken = default)
    {
        _keys[key.PublisherId] = key;
        return Task.FromResult(OperationResult.CreateSuccess());
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> RemoveKeyAsync(string publisherId, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult<bool>.CreateSuccess(_keys.Remove(publisherId)));

    /// <inheritdoc />
    public Task<OperationResult<string?>> QuarantineAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult<string?>.CreateSuccess(null));
}
