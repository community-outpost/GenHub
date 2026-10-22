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
    /// Gets or sets a failure returned by every save and remove, simulating a store that cannot be written.
    /// </summary>
    public string? WriteFailure { get; set; }

    /// <summary>
    /// Gets or sets an exception thrown once by the next save after the key was stored, simulating a write that lands before a failure.
    /// </summary>
    public Exception? ThrowAfterNextSave { get; set; }

    /// <summary>
    /// Gets or sets how many more writes succeed before every write returns <see cref="WriteFailure"/>-style failures.
    /// Null means writes do not run out.
    /// </summary>
    public int? RemainingWrites { get; set; }

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
        if (WriteFailure is not null || !TryConsumeWrite())
        {
            return Task.FromResult(OperationResult.CreateFailure(WriteFailure ?? "out of writes"));
        }

        _keys[key.PublisherId] = key;
        if (ThrowAfterNextSave is { } exception)
        {
            ThrowAfterNextSave = null;
            throw exception;
        }

        return Task.FromResult(OperationResult.CreateSuccess());
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> RemoveKeyAsync(string publisherId, CancellationToken cancellationToken = default) =>
        Task.FromResult(WriteFailure is null && TryConsumeWrite()
            ? OperationResult<bool>.CreateSuccess(_keys.Remove(publisherId))
            : OperationResult<bool>.CreateFailure(WriteFailure ?? "out of writes"));

    /// <inheritdoc />
    public Task<OperationResult<string?>> QuarantineAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult<string?>.CreateSuccess(null));

    private bool TryConsumeWrite()
    {
        if (RemainingWrites is not { } remaining)
        {
            return true;
        }

        if (remaining <= 0)
        {
            return false;
        }

        RemainingWrites = remaining - 1;
        return true;
    }
}
