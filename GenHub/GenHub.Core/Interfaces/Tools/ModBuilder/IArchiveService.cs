using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Service for creating and extracting various archive formats (BIG, ZIP, TAR, TAR.GZ).
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
    /// Extracts files from a BIG archive to the specified target directory.
    /// </summary>
    /// <param name="bigFilePath">Path to the .big archive.</param>
    /// <param name="targetDirectory">Target directory to extract files into.</param>
    /// <param name="overwrite">Whether to overwrite existing files.</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the number of extracted files.</returns>
    Task<OperationResult<int>> ExtractBigArchiveAsync(
        string bigFilePath,
        string targetDirectory,
        bool overwrite = true,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts files from multiple BIG archives sequentially to the specified target directory.
    /// </summary>
    /// <param name="bigFilePaths">Collection of paths to .big archives.</param>
    /// <param name="targetDirectory">Target directory to extract files into.</param>
    /// <param name="overwrite">Whether to overwrite existing files.</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the total number of extracted files.</returns>
    Task<OperationResult<int>> ExtractBigArchivesAsync(
        IEnumerable<string> bigFilePaths,
        string targetDirectory,
        bool overwrite = true,
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
