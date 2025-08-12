using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
    Task<CasOperationResult<string>> StoreContentAsync(string sourcePath, string? expectedHash = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores content from a stream in the CAS and returns the content hash.
    /// </summary>
    /// <param name="contentStream">The content stream.</param>
    /// <param name="expectedHash">Optional expected hash for verification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The content hash if successful.</returns>
    Task<CasOperationResult<string>> StoreContentAsync(Stream contentStream, string? expectedHash = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the file system path to content stored in CAS by hash.
    /// </summary>
    /// <param name="hash">The content hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file system path if the content exists.</returns>
    Task<CasOperationResult<string>> GetContentPathAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether content with the given hash exists in CAS.
    /// </summary>
    /// <param name="hash">The content hash to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the content exists, false otherwise.</returns>
    Task<CasOperationResult<bool>> ExistsAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a read-only stream to content stored in CAS.
    /// </summary>
    /// <param name="hash">The content hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A stream to read the content.</returns>
    Task<CasOperationResult<Stream>> OpenContentStreamAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs garbage collection to remove unreferenced content.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the garbage collection operation.</returns>
    Task<CasGarbageCollectionResult> RunGarbageCollectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the integrity of content in the CAS.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the validation operation.</returns>
    Task<CasValidationResult> ValidateIntegrityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics about the CAS system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>CAS statistics.</returns>
    Task<CasStats> GetStatsAsync(CancellationToken cancellationToken = default);
}
