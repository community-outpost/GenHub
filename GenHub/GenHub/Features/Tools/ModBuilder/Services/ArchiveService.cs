using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Tar;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Service for creating and extracting various archive formats (BIG, ZIP, TAR, TAR.GZ).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "S6966:Await async methods", Justification = "ZipArchiveEntry.Open and TarWriter.Write do not provide async overloads in standard BCL / SharpCompress")]
public sealed class ArchiveService(
    ILogger<ArchiveService> logger) : IArchiveService
{
    private const string SourceDirectoryNotFoundMessage = "Source directory not found: {Path}";

    /// <inheritdoc/>
    public Task<OperationResult<bool>> CreateBigArchiveAsync(
        string sourceDirectory,
        string targetBigPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
        => CreateBigArchiveAsync(sourceDirectory, targetBigPath, manifestFilePath: null, progress, cancellationToken);

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> CreateBigArchiveAsync(
        string sourceDirectory,
        string targetBigPath,
        string? manifestFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(sourceDirectory))
            {
                logger.LogError(SourceDirectoryNotFoundMessage, sourceDirectory);
                return OperationResult<bool>.CreateFailure($"Source directory not found: {sourceDirectory}");
            }

            logger.LogInformation("Creating BIG archive: {Source} -> {Target}", sourceDirectory, targetBigPath);

            var targetDir = Path.GetDirectoryName(targetBigPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var tempBigPath = targetBigPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";

            try
            {
                var manifest = await ResolveManifestAsync(sourceDirectory, targetBigPath, manifestFilePath, cancellationToken).ConfigureAwait(false);

                var duplicateCount = await BigFilePacker.PackAsync(sourceDirectory, tempBigPath, targetBigPath, manifest, cancellationToken).ConfigureAwait(false);
                if (duplicateCount > 0)
                {
                    logger.LogWarning("BIG archive creation dropped {Count} duplicate/colliding entry paths in {Source}", duplicateCount, sourceDirectory);
                }

                if (!File.Exists(tempBigPath))
                {
                    logger.LogError("BIG archive creation completed but temporary file was not created: {Path}", tempBigPath);
                    return OperationResult<bool>.CreateFailure("BIG archive creation failed: temporary file was not created");
                }

                File.Move(tempBigPath, targetBigPath, overwrite: true);
            }
            finally
            {
                CleanupTempFile(tempBigPath);
            }

            logger.LogInformation("Successfully created BIG archive: {Target}", targetBigPath);
            progress?.Report(1.0);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create BIG archive: {Target}", targetBigPath);
            return OperationResult<bool>.CreateFailure($"Failed to create BIG archive: {ex.Message}");
        }
    }

    private async Task<BigArchiveManifest?> ResolveManifestAsync(
        string sourceDirectory,
        string targetBigPath,
        string? manifestFilePath,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(manifestFilePath))
        {
            return await LoadExplicitManifestAsync(sourceDirectory, targetBigPath, manifestFilePath, cancellationToken).ConfigureAwait(false);
        }

        return await DiscoverManifestAsync(sourceDirectory, targetBigPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<BigArchiveManifest?> LoadExplicitManifestAsync(
        string sourceDirectory,
        string targetBigPath,
        string manifestFilePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(manifestFilePath))
        {
            logger.LogWarning("Configured manifest file does not exist: {Path}. Falling back to manifest discovery.", manifestFilePath);
            return await DiscoverManifestAsync(sourceDirectory, targetBigPath, cancellationToken).ConfigureAwait(false);
        }

        var manifest = await BigFilePacker.LoadManifestAsync(manifestFilePath, cancellationToken).ConfigureAwait(false);
        if (manifest != null)
        {
            logger.LogInformation("Using explicit BIG archive manifest from {Path}", manifestFilePath);
            return manifest;
        }

        throw new InvalidOperationException($"Explicitly configured manifest at '{manifestFilePath}' could not be loaded.");
    }

    private async Task<BigArchiveManifest?> DiscoverManifestAsync(
        string sourceDirectory,
        string targetBigPath,
        CancellationToken cancellationToken)
    {
        var targetFileName = Path.GetFileName(targetBigPath);
        var candidateLocations = new[]
        {
            Path.ChangeExtension(targetBigPath, ".manifest.json"),
            Path.Combine(sourceDirectory, "..", ModBuilderConstants.LowercaseConfigDir, $"{targetFileName}.manifest.json"),
            Path.Combine(sourceDirectory, "..", "..", ModBuilderConstants.LowercaseConfigDir, $"{targetFileName}.manifest.json"),
            Path.Combine(sourceDirectory, "..", ModBuilderConstants.LowercaseConfigDir, "BigLayout.json"),
        };

        foreach (var candidate in candidateLocations)
        {
            var manifest = await TryLoadManifestCandidateAsync(
                candidate,
                "Discovered BIG archive manifest at {Path}",
                "Failed to load discovered BIG archive manifest at {Path}; skipping candidate",
                cancellationToken).ConfigureAwait(false);

            if (manifest != null)
            {
                return manifest;
            }
        }

        return null;
    }

    private async Task<BigArchiveManifest?> TryLoadManifestCandidateAsync(
        string manifestPath,
        string logSuccessMessage,
        string logWarningMessage,
        CancellationToken cancellationToken)
    {
        var fullCandidate = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullCandidate))
        {
            return null;
        }

        try
        {
            var manifest = await BigFilePacker.LoadManifestAsync(fullCandidate, cancellationToken).ConfigureAwait(false);
            if (manifest != null)
            {
                logger.LogInformation(logSuccessMessage, fullCandidate);
                return manifest;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, logWarningMessage, fullCandidate);
        }

        return null;
    }

    private static void CleanupTempFile(string tempPath)
    {
        if (File.Exists(tempPath))
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Ignore cleanup errors on temp file
            }
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<int>> ExtractBigArchiveAsync(
        string bigFilePath,
        string targetDirectory,
        bool overwrite = true,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(bigFilePath))
            {
                logger.LogError("BIG archive not found: {Path}", bigFilePath);
                return OperationResult<int>.CreateFailure($"BIG archive not found: {bigFilePath}");
            }

            logger.LogInformation("Extracting BIG archive: {Source} -> {Target}", bigFilePath, targetDirectory);

            var unpackResult = await BigFilePacker.UnpackAsync(bigFilePath, targetDirectory, overwrite, progress, cancellationToken).ConfigureAwait(false);
            if (!unpackResult.Success)
            {
                logger.LogError("Error extracting BIG archive {Source}: {Error}", bigFilePath, unpackResult.FirstError);
                return unpackResult;
            }

            logger.LogInformation("Successfully extracted {Count} files from BIG archive {Source}", unpackResult.Data, bigFilePath);
            return unpackResult;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error extracting BIG archive: {Source} -> {Target}", bigFilePath, targetDirectory);
            return OperationResult<int>.CreateFailure($"Error extracting BIG archive: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<int>> ExtractBigArchivesAsync(
        IEnumerable<string> bigFilePaths,
        string targetDirectory,
        bool overwrite = true,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filesList = bigFilePaths.ToList();
            if (filesList.Count == 0)
            {
                return OperationResult<int>.CreateSuccess(0);
            }

            if (filesList.Any(p => string.IsNullOrWhiteSpace(p)))
            {
                logger.LogError("BIG archive path cannot be null or empty");
                return OperationResult<int>.CreateFailure("BIG archive path cannot be null or empty");
            }

            var missingArchive = filesList.FirstOrDefault(p => !File.Exists(p));
            if (missingArchive != null)
            {
                logger.LogError("BIG archive not found: {Path}", missingArchive);
                return OperationResult<int>.CreateFailure($"BIG archive not found: {missingArchive}");
            }

            logger.LogInformation("Extracting {Count} BIG archives to {Target}", filesList.Count, targetDirectory);

            var unpackResult = await BigFilePacker.UnpackMultipleAsync(filesList, targetDirectory, overwrite, progress, cancellationToken).ConfigureAwait(false);
            if (!unpackResult.Success)
            {
                logger.LogError("Error extracting multiple BIG archives to {Target}: {Error}", targetDirectory, unpackResult.FirstError);
                return unpackResult;
            }

            logger.LogInformation("Successfully extracted total of {FileCount} files from {ArchiveCount} BIG archives", unpackResult.Data, filesList.Count);
            return unpackResult;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error extracting multiple BIG archives to {Target}", targetDirectory);
            return OperationResult<int>.CreateFailure($"Error extracting BIG archives: {ex.Message}");
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
                logger.LogError(SourceDirectoryNotFoundMessage, sourceDirectory);
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
                logger.LogError(SourceDirectoryNotFoundMessage, sourceDirectory);
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
                logger.LogError(SourceDirectoryNotFoundMessage, sourceDirectory);
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
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating TAR.GZ archive: {Source} -> {Target}", sourceDirectory, targetTarGzPath);
            return OperationResult<bool>.CreateFailure($"Error creating TAR.GZ archive: {ex.Message}");
        }
    }
}
