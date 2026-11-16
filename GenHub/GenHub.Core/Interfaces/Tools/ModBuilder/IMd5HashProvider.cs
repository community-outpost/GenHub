using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Provides MD5 hash computation for files with modification time optimization.
/// </summary>
public interface IMd5HashProvider
{
    /// <summary>
    /// Computes the MD5 hash of a file asynchronously.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The MD5 hash as a lowercase hex string.</returns>
    Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default);
}
