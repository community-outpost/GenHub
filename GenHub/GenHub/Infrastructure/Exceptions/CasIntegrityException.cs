namespace GenHub.Infrastructure.Exceptions;

/// <summary>
/// Represents integrity errors in CAS storage, such as hash mismatches.
/// </summary>
/// <param name="expectedHash">The expected hash value of the content (for example, the hash recorded when the content was stored).</param>
/// <param name="actualHash">The actual or computed hash value of the retrieved content that did not match the expected value.</param>
public class CasIntegrityException(string expectedHash, string actualHash)
    : CasStorageException($"Hash mismatch: expected {expectedHash}, got {actualHash}")
{
    /// <summary>
    /// Gets the expected hash value.
    /// </summary>
    public string ExpectedHash { get; } = expectedHash;

    /// <summary>
    /// Gets the actual hash value.
    /// </summary>
    public string ActualHash { get; } = actualHash;
}