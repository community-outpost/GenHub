namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Provides functionality to compute a hash for a file.
/// </summary>
public interface IFileHashProvider
{
    /// <summary>
    /// Computes the hash of the specified file asynchronously.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The computed hash as a string.</returns>
    Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default);
}