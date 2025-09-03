using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Storage;

/// <summary>
/// Low-level interface for Content-Addressable Storage operations.
/// </summary>
public interface ICasStorage
{
    /// <summary>
    /// Gets the file system path for an object with the given hash.
    /// </summary>
    /// <param name="hash">The content hash.</param>
    /// <returns>The file system path where the object should be stored.</returns>
    string GetObjectPath(string hash);

    /// <summary>
    /// Checks if an object with the given hash exists in storage.
    /// </summary>
    /// <param name="hash">The content hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the object exists, false otherwise.</returns>
    Task<bool> ObjectExistsAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores content from a stream and returns the storage path.
    /// </summary>
    /// <param name="content">The content stream.</param>
    /// <param name="hash">The content hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The path where the content was stored, or null if storage failed.</returns>
    Task<string?> StoreObjectAsync(Stream content, string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a read-only stream to an object in storage.
    /// </summary>
    /// <param name="hash">The content hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A stream to read the object content, or null if the object cannot be opened.</returns>
    Task<Stream?> OpenObjectStreamAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an object from storage.
    /// </summary>
    /// <param name="hash">The content hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous delete operation.</returns>
    Task DeleteObjectAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all object hashes currently in storage.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enumerable of all object hashes.</returns>
    Task<string[]> GetAllObjectHashesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the creation time of an object in storage.
    /// </summary>
    /// <param name="hash">The content hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The creation time of the object, or null if the object cannot be accessed.</returns>
    Task<System.DateTime?> GetObjectCreationTimeAsync(string hash, CancellationToken cancellationToken = default);
}
