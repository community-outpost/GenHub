using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Provides functionality to compute a hash for a stream.
/// </summary>
public interface IStreamHashProvider
{
    /// <summary>
    /// Computes the hash of the specified stream asynchronously.
    /// </summary>
    /// <param name="stream">The stream to hash.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The computed hash as a string.</returns>
    Task<string> ComputeStreamHashAsync(Stream stream, CancellationToken cancellationToken = default);
}
