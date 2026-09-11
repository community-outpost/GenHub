using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;

namespace GenHub.Common.Services;

/// <summary>
/// Provides SHA256 hash computation for files and streams.
/// </summary>
public class Sha256HashProvider() : IFileHashProvider, IStreamHashProvider
{
    /// <summary>
    /// Computes the SHA256 hash of a file asynchronously.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The SHA256 hash as a lowercase hex string.</returns>
    public async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = IoConstants.FileHashBufferSize,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        };

        await using var stream = new FileStream(filePath, options);
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Computes the SHA256 hash of a stream asynchronously.
    /// </summary>
    /// <param name="stream">The stream to hash.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The SHA256 hash as a lowercase hex string.</returns>
    public async Task<string> ComputeStreamHashAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
