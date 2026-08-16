using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Tar;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Service for creating various archive formats (BIG, ZIP, TAR, TAR.GZ).
/// </summary>
public sealed class ArchiveService(
    ILogger<ArchiveService> logger) : IArchiveService
{
    /// <inheritdoc/>
    public async Task<OperationResult<bool>> CreateBigArchiveAsync(
        string sourceDirectory,
        string targetBigPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(sourceDirectory))
            {
                logger.LogError("Source directory not found: {Path}", sourceDirectory);
                return OperationResult<bool>.CreateFailure($"Source directory not found: {sourceDirectory}");
            }

            logger.LogInformation("Creating BIG archive: {Source} -> {Target}", sourceDirectory, targetBigPath);

            // ensure target directory exists
            var targetDir = Path.GetDirectoryName(targetBigPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // use existing BigFilePacker
            await BigFilePacker.PackAsync(sourceDirectory, targetBigPath);

            if (!File.Exists(targetBigPath))
            {
                logger.LogError("BIG archive creation completed but file was not created: {Path}", targetBigPath);
                return OperationResult<bool>.CreateFailure("BIG archive creation failed: target file was not created");
            }

            logger.LogInformation("Successfully created BIG archive: {Target}", targetBigPath);
            progress?.Report(1.0);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating BIG archive: {Source} -> {Target}", sourceDirectory, targetBigPath);
            return OperationResult<bool>.CreateFailure($"Error creating BIG archive: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> CreateZipArchiveAsync(
        string sourceDirectory,
        string targetZipPath,
        CompressionLevel compressionLevel = CompressionLevel.Optimal,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(sourceDirectory))
            {
                logger.LogError("Source directory not found: {Path}", sourceDirectory);
                return OperationResult<bool>.CreateFailure($"Source directory not found: {sourceDirectory}");
            }

            logger.LogInformation("Creating ZIP archive: {Source} -> {Target} (Compression: {Level})",
                sourceDirectory, targetZipPath, compressionLevel);

            // ensure target directory exists
            var targetDir = Path.GetDirectoryName(targetZipPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // delete existing file if it exists
            if (File.Exists(targetZipPath))
            {
                File.Delete(targetZipPath);
            }

            progress?.Report(0.0);

            var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
            var totalFiles = files.Length;
            var processedFiles = 0;

            // separate files by size for optimal processing
            var fileInfos = files.Select(f => new FileInfo(f)).ToArray();
            var smallFiles = fileInfos.Where(f => f.Length <= ModBuilderConstants.DefaultStreamingThresholdBytes).ToArray();
            var largeFiles = fileInfos.Where(f => f.Length > ModBuilderConstants.DefaultStreamingThresholdBytes).ToArray();

            if (largeFiles.Length > 0)
            {
                logger.LogInformation("Processing {SmallCount} small files (<10MB) and {LargeCount} large files (>10MB) with streaming",
                    smallFiles.Length, largeFiles.Length);
            }

            // pre-read small files in parallel for better i/o performance
            var fileDataCache = new Dictionary<string, byte[]>();

            if (smallFiles.Length > 0)
            {
                await Parallel.ForEachAsync(
                    smallFiles,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        CancellationToken = cancellationToken
                    },
                    async (fileInfo, ct) =>
                    {
                        await using var fileStream = new FileStream(
                            fileInfo.FullName,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            IoConstants.DefaultFileBufferSize,
                            useAsync: true);

                        var buffer = new byte[fileStream.Length];
                        await fileStream.ReadAsync(buffer, ct).ConfigureAwait(false);

                        lock (fileDataCache)
                        {
                            fileDataCache[fileInfo.FullName] = buffer;
                        }
                    })
                    .ConfigureAwait(false);
            }

            // create archive with pre-loaded data and streaming for large files
            using (var archive = ZipFile.Open(targetZipPath, ZipArchiveMode.Create))
            {
                // process small files from cache
                foreach (var fileInfo in smallFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var relativePath = Path.GetRelativePath(sourceDirectory, fileInfo.FullName);
                    var entry = archive.CreateEntry(relativePath, compressionLevel);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(fileDataCache[fileInfo.FullName], cancellationToken).ConfigureAwait(false);

                    processedFiles++;
                    progress?.Report((double)processedFiles / totalFiles);
                }

                // stream large files directly
                foreach (var fileInfo in largeFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var relativePath = Path.GetRelativePath(sourceDirectory, fileInfo.FullName);
                    logger.LogDebug("Streaming large file: {Path} ({Size:N0} bytes)", relativePath, fileInfo.Length);

                    var entry = archive.CreateEntry(relativePath, compressionLevel);
                    await using var entryStream = entry.Open();
                    await using var fileStream = new FileStream(
                        fileInfo.FullName,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        IoConstants.DefaultFileBufferSize,
                        useAsync: true);

                    await fileStream.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);

                    processedFiles++;
                    progress?.Report((double)processedFiles / totalFiles);
                }
            }

            if (!File.Exists(targetZipPath))
            {
                logger.LogError("ZIP archive creation completed but file was not created: {Path}", targetZipPath);
                return OperationResult<bool>.CreateFailure("ZIP archive creation failed: target file was not created");
            }

            progress?.Report(1.0);
            logger.LogInformation("Successfully created ZIP archive: {Target}", targetZipPath);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("ZIP archive creation cancelled: {Target}", targetZipPath);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating ZIP archive: {Source} -> {Target}", sourceDirectory, targetZipPath);
            return OperationResult<bool>.CreateFailure($"Error creating ZIP archive: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> CreateTarArchiveAsync(
        string sourceDirectory,
        string targetTarPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(sourceDirectory))
            {
                logger.LogError("Source directory not found: {Path}", sourceDirectory);
                return OperationResult<bool>.CreateFailure($"Source directory not found: {sourceDirectory}");
            }

            logger.LogInformation("Creating TAR archive: {Source} -> {Target}", sourceDirectory, targetTarPath);

            // ensure target directory exists
            var targetDir = Path.GetDirectoryName(targetTarPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // delete existing file if it exists
            if (File.Exists(targetTarPath))
            {
                File.Delete(targetTarPath);
            }

            var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
            var totalFiles = files.Length;
            var processedFiles = 0;

            // separate files by size for optimal processing
            var fileInfos = files.Select(f => new FileInfo(f)).ToArray();
            var smallFiles = fileInfos.Where(f => f.Length <= ModBuilderConstants.DefaultStreamingThresholdBytes).ToArray();
            var largeFiles = fileInfos.Where(f => f.Length > ModBuilderConstants.DefaultStreamingThresholdBytes).ToArray();

            if (largeFiles.Length > 0)
            {
                logger.LogInformation("Processing {SmallCount} small files (<10MB) and {LargeCount} large files (>10MB) with streaming",
                    smallFiles.Length, largeFiles.Length);
            }

            // pre-read small files in parallel for better i/o performance
            var fileDataCache = new Dictionary<string, byte[]>();

            if (smallFiles.Length > 0)
            {
                await Parallel.ForEachAsync(
                    smallFiles,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        CancellationToken = cancellationToken
                    },
                    async (fileInfo, ct) =>
                    {
                        await using var fileStream = new FileStream(
                            fileInfo.FullName,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            IoConstants.DefaultFileBufferSize,
                            useAsync: true);

                        var buffer = new byte[fileStream.Length];
                        await fileStream.ReadAsync(buffer, ct).ConfigureAwait(false);

                        lock (fileDataCache)
                        {
                            fileDataCache[fileInfo.FullName] = buffer;
                        }
                    })
                    .ConfigureAwait(false);
            }

            // create archive with direct stream writing without temp file disk churn
            await using (var stream = new FileStream(
                targetTarPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                IoConstants.DefaultFileBufferSize,
                useAsync: true))
            {
                using var writer = new TarWriter(stream, new TarWriterOptions(CompressionType.None, true));

                // process small files directly from memory stream
                foreach (var fileInfo in smallFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var relativePath = Path.GetRelativePath(sourceDirectory, fileInfo.FullName).Replace('\\', '/');
                    using var memoryStream = new MemoryStream(fileDataCache[fileInfo.FullName]);
                    writer.Write(relativePath, memoryStream, fileInfo.LastWriteTimeUtc);

                    processedFiles++;
                    progress?.Report((double)processedFiles / totalFiles);
                }

                // stream large files directly from source file stream
                foreach (var fileInfo in largeFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var relativePath = Path.GetRelativePath(sourceDirectory, fileInfo.FullName).Replace('\\', '/');
                    logger.LogDebug("Streaming large file: {Path} ({Size:N0} bytes)", relativePath, fileInfo.Length);

                    await using (var sourceStream = new FileStream(
                        fileInfo.FullName,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        IoConstants.DefaultFileBufferSize,
                        useAsync: true))
                    {
                        writer.Write(relativePath, sourceStream, fileInfo.LastWriteTimeUtc);
                    }

                    processedFiles++;
                    progress?.Report((double)processedFiles / totalFiles);
                }
            }

            if (!File.Exists(targetTarPath))
            {
                logger.LogError("TAR archive creation completed but file was not created: {Path}", targetTarPath);
                return OperationResult<bool>.CreateFailure("TAR archive creation failed: target file was not created");
            }

            logger.LogInformation("Successfully created TAR archive: {Target}", targetTarPath);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("TAR archive creation cancelled: {Target}", targetTarPath);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating TAR archive: {Source} -> {Target}", sourceDirectory, targetTarPath);
            return OperationResult<bool>.CreateFailure($"Error creating TAR archive: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> CreateTarGzArchiveAsync(
        string sourceDirectory,
        string targetTarGzPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(sourceDirectory))
            {
                logger.LogError("Source directory not found: {Path}", sourceDirectory);
                return OperationResult<bool>.CreateFailure($"Source directory not found: {sourceDirectory}");
            }

            logger.LogInformation("Creating TAR.GZ archive: {Source} -> {Target}", sourceDirectory, targetTarGzPath);

            // ensure target directory exists
            var targetDir = Path.GetDirectoryName(targetTarGzPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // delete existing file if it exists
            if (File.Exists(targetTarGzPath))
            {
                File.Delete(targetTarGzPath);
            }

            var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
            var totalFiles = files.Length;
            var processedFiles = 0;

            // separate files by size for optimal processing
            var fileInfos = files.Select(f => new FileInfo(f)).ToArray();
            var smallFiles = fileInfos.Where(f => f.Length <= ModBuilderConstants.DefaultStreamingThresholdBytes).ToArray();
            var largeFiles = fileInfos.Where(f => f.Length > ModBuilderConstants.DefaultStreamingThresholdBytes).ToArray();

            if (largeFiles.Length > 0)
            {
                logger.LogInformation("Processing {SmallCount} small files (<10MB) and {LargeCount} large files (>10MB) with streaming",
                    smallFiles.Length, largeFiles.Length);
            }

            // pre-read small files in parallel for better i/o performance
            var fileDataCache = new Dictionary<string, byte[]>();

            if (smallFiles.Length > 0)
            {
                await Parallel.ForEachAsync(
                    smallFiles,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        CancellationToken = cancellationToken
                    },
                    async (fileInfo, ct) =>
                    {
                        await using var fileStream = new FileStream(
                            fileInfo.FullName,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            IoConstants.DefaultFileBufferSize,
                            useAsync: true);

                        var buffer = new byte[fileStream.Length];
                        await fileStream.ReadAsync(buffer, ct).ConfigureAwait(false);

                        lock (fileDataCache)
                        {
                            fileDataCache[fileInfo.FullName] = buffer;
                        }
                    })
                    .ConfigureAwait(false);
            }

            // create archive with direct stream writing without temp file disk churn
            await using (var stream = new FileStream(
                targetTarGzPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                IoConstants.DefaultFileBufferSize,
                useAsync: true))
            {
                using var writer = new TarWriter(stream, new TarWriterOptions(CompressionType.GZip, true));

                // process small files directly from memory stream
                foreach (var fileInfo in smallFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var relativePath = Path.GetRelativePath(sourceDirectory, fileInfo.FullName).Replace('\\', '/');
                    using var memoryStream = new MemoryStream(fileDataCache[fileInfo.FullName]);
                    writer.Write(relativePath, memoryStream, fileInfo.LastWriteTimeUtc);

                    processedFiles++;
                    progress?.Report((double)processedFiles / totalFiles);
                }

                // stream large files directly from source file stream
                foreach (var fileInfo in largeFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var relativePath = Path.GetRelativePath(sourceDirectory, fileInfo.FullName).Replace('\\', '/');
                    logger.LogDebug("Streaming large file: {Path} ({Size:N0} bytes)", relativePath, fileInfo.Length);

                    await using (var sourceStream = new FileStream(
                        fileInfo.FullName,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        IoConstants.DefaultFileBufferSize,
                        useAsync: true))
                    {
                        writer.Write(relativePath, sourceStream, fileInfo.LastWriteTimeUtc);
                    }

                    processedFiles++;
                    progress?.Report((double)processedFiles / totalFiles);
                }
            }

            if (!File.Exists(targetTarGzPath))
            {
                logger.LogError("TAR.GZ archive creation completed but file was not created: {Path}", targetTarGzPath);
                return OperationResult<bool>.CreateFailure("TAR.GZ archive creation failed: target file was not created");
            }

            logger.LogInformation("Successfully created TAR.GZ archive: {Target}", targetTarGzPath);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("TAR.GZ archive creation cancelled: {Target}", targetTarGzPath);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating TAR.GZ archive: {Source} -> {Target}", sourceDirectory, targetTarGzPath);
            return OperationResult<bool>.CreateFailure($"Error creating TAR.GZ archive: {ex.Message}");
        }
    }
}
