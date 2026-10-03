using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Service for managing file hash registry to skip processing of irrelevant files.
/// Implements the FileHashRegistry optimization from Python ModBuilder.
/// </summary>
public interface IFileHashRegistryService
{
    /// <summary>
    /// Loads the hash registry from a CSV file.
    /// </summary>
    /// <param name="csvPath">Path to the CSV file containing file hashes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    Task LoadRegistryAsync(string csvPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a file is irrelevant (unchanged from registry).
    /// </summary>
    /// <param name="filePath">Path to the file to check.</param>
    /// <param name="currentMd5">Current MD5 hash of the file.</param>
    /// <returns>True if the file matches the registry hash and can be skipped.</returns>
    bool IsFileIrrelevant(string filePath, string currentMd5);
}
