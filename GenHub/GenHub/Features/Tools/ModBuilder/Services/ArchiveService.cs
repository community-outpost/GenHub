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

            var tempBigPath = targetBigPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";

            try
            {
                // use existing BigFilePacker
                await BigFilePacker.PackAsync(sourceDirectory, tempBigPath, targetBigPath, cancellationToken).ConfigureAwait(false);

                if (!File.Exists(tempBigPath))
                {
                    logger.LogError("BIG archive creation completed but temporary file was not created: {Path}", tempBigPath);
                    return OperationResult<bool>.CreateFailure("BIG archive creation failed: temporary file was not created");
                }

                File.Move(tempBigPath, targetBigPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempBigPath))
                {
                    try
                    {
                        File.Delete(tempBigPath);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }

            logger.LogInformation("Successfully created BIG archive: {Target}", targetBigPath);
            progress?.Report(1.0);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("BIG archive creation cancelled: {Target}", targetBigPath);
            throw;
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

            var tempZipPath = targetZipPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            var targetFullPath = Path.GetFullPath(targetZipPath);

            progress?.Report(0.0);

            var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                .Where(file => !string.Equals(Path.GetFullPath(file), targetFullPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var totalFiles = files.Length;
            var processedFiles = 0;

            try
            {
                await using (var zipStream = new FileStream(
                    tempZipPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    IoConstants.DefaultFileBufferSize,
                    useAsync: true))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
                {
                    foreach (var filePath in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var fileInfo = new FileInfo(filePath);
                        var relativePath = Path.GetRelativePath(sourceDirectory, fileInfo.FullName).Replace('\\', '/');

                        var entry = archive.CreateEntry(relativePath, compressionLevel);
                        await using (var entryStream = entry.Open())
                        await using (var fileStream = new FileStream(
                            fileInfo.FullName,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            IoConstants.DefaultFileBufferSize,
                            useAsync: true))
                        {
                            await fileStream.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
                        }

                        processedFiles++;
                        progress?.Report((double)processedFiles / totalFiles);
                    }
                }

                if (!File.Exists(tempZipPath))
                {
                    logger.LogError("ZIP archive creation completed but temporary file was not created: {Path}", tempZipPath);
                    return OperationResult<bool>.CreateFailure("ZIP archive creation failed: temporary file was not created");
                }

                File.Move(tempZipPath, targetZipPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempZipPath))
                {
                    try
                    {
                        File.Delete(tempZipPath);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
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

            var tempTarPath = targetTarPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            var targetFullPath = Path.GetFullPath(targetTarPath);

            var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                .Where(file => !string.Equals(Path.GetFullPath(file), targetFullPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var totalFiles = files.Length;
            var processedFiles = 0;

            try
            {
                await using (var stream = new FileStream(
                    tempTarPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    IoConstants.DefaultFileBufferSize,
                    useAsync: true))
                {
                    using var writer = new TarWriter(stream, new TarWriterOptions(CompressionType.None, true));

                    foreach (var filePath in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var fileInfo = new FileInfo(filePath);
                        var relativePath = Path.GetRelativePath(sourceDirectory, fileInfo.FullName).Replace('\\', '/');

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

                if (!File.Exists(tempTarPath))
                {
                    logger.LogError("TAR archive creation completed but temporary file was not created: {Path}", tempTarPath);
                    return OperationResult<bool>.CreateFailure("TAR archive creation failed: temporary file was not created");
                }

                File.Move(tempTarPath, targetTarPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempTarPath))
                {
                    try
                    {
                        File.Delete(tempTarPath);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }

            progress?.Report(1.0);
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

            var tempTarGzPath = targetTarGzPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            var targetFullPath = Path.GetFullPath(targetTarGzPath);

            var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                .Where(file => !string.Equals(Path.GetFullPath(file), targetFullPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var totalFiles = files.Length;
            var processedFiles = 0;

            try
            {
                await using (var stream = new FileStream(
                    tempTarGzPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    IoConstants.DefaultFileBufferSize,
                    useAsync: true))
                {
                    using var writer = new TarWriter(stream, new TarWriterOptions(CompressionType.GZip, true));

                    foreach (var filePath in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var fileInfo = new FileInfo(filePath);
                        var relativePath = Path.GetRelativePath(sourceDirectory, fileInfo.FullName).Replace('\\', '/');

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

                if (!File.Exists(tempTarGzPath))
                {
                    logger.LogError("TAR.GZ archive creation completed but temporary file was not created: {Path}", tempTarGzPath);
                    return OperationResult<bool>.CreateFailure("TAR.GZ archive creation failed: temporary file was not created");
                }

                File.Move(tempTarGzPath, targetTarGzPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempTarGzPath))
                {
                    try
                    {
                        File.Delete(tempTarGzPath);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }

            progress?.Report(1.0);
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
