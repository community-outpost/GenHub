using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ModBuilder;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Provides MD5 hash computation for files with efficient streaming.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "MD5 is required for legacy C&C game asset compatibility and non-cryptographic checksum verification")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("SonarSource.Security", "S4790", Justification = "MD5 is required for legacy game engine checksum compatibility")]
public sealed class Md5HashProvider : IMd5HashProvider
{
    /// <summary>
    /// Computes the MD5 hash of a file asynchronously.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The MD5 hash as a lowercase hex string.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "MD5 is required for legacy C&C game asset compatibility")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarSource.Security", "S4790", Justification = "MD5 is required for legacy game engine checksum compatibility")]
    public async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            IoConstants.DefaultFileBufferSize,
            useAsync: true);

        using var md5 = MD5.Create();
        var hashBytes = await md5.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
