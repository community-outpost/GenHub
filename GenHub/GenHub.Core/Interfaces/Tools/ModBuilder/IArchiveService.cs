using System;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Service for creating various archive formats (BIG, ZIP, TAR, TAR.GZ).
/// </summary>
public interface IArchiveService
{
    /// <summary>
    /// Creates a BIG archive from a source directory.
    /// </summary>
    /// <param name="sourceDirectory">Path to the source directory containing files to pack.</param>
    /// <param name="targetBigPath">Path to the target .big file.</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> CreateBigArchiveAsync(
        string sourceDirectory,
        string targetBigPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a ZIP archive from a source directory with configurable compression.
    /// </summary>
    /// <param name="sourceDirectory">Path to the source directory containing files to pack.</param>
    /// <param name="targetZipPath">Path to the target .zip file.</param>
    /// <param name="compressionLevel">Compression level to use. Fastest for dev builds, Optimal for release builds.</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    /// <remarks>
    /// Compression level trade-offs:
    /// - NoCompression: Fastest, largest file size. Use for debugging only.
    /// - Fastest: 20-30% faster than Optimal, slightly larger files. Recommended for dev builds.
    /// - Optimal: Best compression ratio, slower. Recommended for release builds.
    /// </remarks>
    Task<OperationResult<bool>> CreateZipArchiveAsync(
        string sourceDirectory,
        string targetZipPath,
        CompressionLevel compressionLevel = CompressionLevel.Optimal,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a TAR archive from a source directory.
    /// </summary>
    /// <param name="sourceDirectory">Path to the source directory containing files to pack.</param>
    /// <param name="targetTarPath">Path to the target .tar file.</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> CreateTarArchiveAsync(
        string sourceDirectory,
        string targetTarPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a TAR.GZ (gzipped tar) archive from a source directory.
    /// </summary>
    /// <param name="sourceDirectory">Path to the source directory containing files to pack.</param>
    /// <param name="targetTarGzPath">Path to the target .tar.gz file.</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> CreateTarGzArchiveAsync(
        string sourceDirectory,
        string targetTarGzPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
