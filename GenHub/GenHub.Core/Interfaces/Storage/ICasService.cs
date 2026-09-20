using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.CAS;
using GenHub.Core.Models.Storage;

namespace GenHub.Core.Interfaces.Storage;

/// <summary>
/// High-level interface for Content-Addressable Storage operations.
/// </summary>
public interface ICasService
{
    /// <summary>
    /// Stores content from a file path in the CAS and returns the content hash.
    /// </summary>
    /// <param name="sourcePath">The path to the source file.</param>
    /// <param name="expectedHash">Optional expected hash for verification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The content hash if successful.</returns>
    Task<OperationResult<string>> StoreContentAsync(string sourcePath, string? expectedHash = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores content from a stream in the CAS and returns the content hash.
    /// </summary>
    /// <param name="contentStream">The content stream.</param>
    /// <param name="expectedHash">Optional expected hash for verification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The content hash if successful.</returns>
    Task<OperationResult<string>> StoreContentAsync(Stream contentStream, string? expectedHash = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the file system path to content stored in CAS by hash.
    /// </summary>
    /// <param name="hash">The content hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file system path if the content exists.</returns>
    Task<OperationResult<string>> GetContentPathAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether content with the given hash exists in CAS.
    /// </summary>
    /// <param name="hash">The content hash to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the content exists, false otherwise.</returns>
    Task<OperationResult<bool>> ExistsAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a read-only stream to content stored in CAS.
    /// </summary>
    /// <param name="hash">The content hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A stream to read the content.</returns>
    Task<OperationResult<Stream>> OpenContentStreamAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the integrity of content in the CAS, spanning every configured pool.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the validation operation.</returns>
    Task<CasValidationResult> ValidateIntegrityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics about the CAS system, aggregated across every configured pool.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>CAS statistics.</returns>
    Task<CasStats> GetStatsAsync(CancellationToken cancellationToken = default);

    // ===== Pool-Aware Operations =====

    /// <summary>
    /// Stores content from a file path in the appropriate CAS pool based on content type.
    /// </summary>
    /// <param name="sourcePath">The path to the source file.</param>
    /// <param name="contentType">The content type for pool routing.</param>
    /// <param name="expectedHash">Optional expected hash for verification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The content hash if successful.</returns>
    Task<OperationResult<string>> StoreContentAsync(
        string sourcePath,
        ContentType contentType,
        string? expectedHash = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores content from a file path in the appropriate CAS pool using a pre-computed hash.
    /// Unlike the file-path StoreContentAsync overload, the source file is not re-hashed:
    /// integrity is still verified while the bytes are copied into CAS.
    /// </summary>
    /// <param name="sourcePath">The path to the source file.</param>
    /// <param name="knownHash">The pre-computed content hash of the source file.</param>
    /// <param name="contentType">The content type for pool routing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The content hash if successful.</returns>
    Task<OperationResult<string>> StoreContentWithKnownHashAsync(
        string sourcePath,
        string knownHash,
        ContentType contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores content from a stream in the appropriate CAS pool based on content type.
    /// </summary>
    /// <param name="contentStream">The content stream.</param>
    /// <param name="contentType">The content type for pool routing.</param>
    /// <param name="expectedHash">Optional expected hash for verification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The content hash if successful.</returns>
    Task<OperationResult<string>> StoreContentAsync(
        Stream contentStream,
        ContentType contentType,
        string? expectedHash = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the file system path to content stored in CAS, searching the appropriate pool for the content type.
    /// </summary>
    /// <param name="hash">The content hash.</param>
    /// <param name="contentType">The content type for pool routing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file system path if the content exists.</returns>
    Task<OperationResult<string>> GetContentPathAsync(
        string hash,
        ContentType contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether content with the given hash exists in the appropriate CAS pool.
    /// </summary>
    /// <param name="hash">The content hash to check.</param>
    /// <param name="contentType">The content type for pool routing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the content exists, false otherwise.</returns>
    Task<OperationResult<bool>> ExistsAsync(
        string hash,
        ContentType contentType,
        CancellationToken cancellationToken = default);
}
