using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Implementation of <see cref="IReplayImportService"/> for importing replay files.
/// </summary>
public sealed class ReplayImportService(
    IDownloadService downloadService,
    IReplayDirectoryService directoryService,
    IUrlParserService urlParserService,
    IZipValidationService zipValidationService,
    ILogger<ReplayImportService> logger) : IReplayImportService
{
    /// <inheritdoc />
    public async Task<ImportResult> ImportFromUrlAsync(
        string url,
        GameType targetVersion,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        logger.LogInformation("Importing replay from URL: {Url}", url);

        try
        {
            var directUrls = await urlParserService.GetDirectDownloadUrlsAsync(url, ct);
            if (directUrls.Count == 0)
            {
                return new ImportResult
                {
                    Success = false,
                    FilesImported = 0,
                    FilesSkipped = 0,
                    Errors = [ErrorMessages.CouldNotExtractDownloadUrl],
                };
            }

            var importedFiles = new List<string>();
            var errors = new List<string>();
            int skipped = 0;
            var source = urlParserService.IdentifySource(url);
            var userAgent = (source == ReplaySource.GeneralsOnline || source == ReplaySource.GenTool || source == ReplaySource.Strata)
                ? ApiConstants.BrowserUserAgent
                : ApiConstants.DefaultUserAgent;

            for (int i = 0; i < directUrls.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var fileIndex = i;
                var totalFiles = directUrls.Count;
                var downloadProgress = progress != null
                    ? new Progress<DownloadProgress>(p =>
                    {
                        var overallProgress = (fileIndex + (p.Percentage / 100.0)) / totalFiles;
                        progress.Report(overallProgress);
                    })
                    : null;

                var skippedCount = await DownloadAndImportReplayUrlAsync(
                    directUrls[i],
                    userAgent,
                    targetVersion,
                    downloadProgress,
                    importedFiles,
                    errors,
                    ct);

                skipped += skippedCount;
            }

            progress?.Report(1.0);

            return new ImportResult
            {
                Success = importedFiles.Count > 0,
                FilesImported = importedFiles.Count,
                FilesSkipped = skipped,
                ImportedFiles = importedFiles,
                Errors = errors,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to import from URL: {Url}", url);
            return new ImportResult { Success = false, FilesImported = 0, FilesSkipped = 0, Errors = [ex.Message] };
        }
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportFromFilesAsync(
        IEnumerable<string> filePaths,
        GameType targetVersion,
        CancellationToken ct = default)
    {
        var imported = new List<string>();
        var errors = new List<string>();
        int skipped = 0;

        foreach (var path in filePaths)
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var isZip = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                var info = new FileInfo(path);

                // Only enforce 1MB limit for individual .rep files, not for ZIP archives
                if (!isZip && info.Length > ReplayManagerConstants.MaxReplaySizeBytes)
                {
                    errors.Add($"File {Path.GetFileName(path)} skipped: exceeds 1 MB.");
                    skipped++;
                    continue;
                }

                if (isZip)
                {
                    var zipResult = await ImportFromZipAsync(path, targetVersion, null, ct);
                    imported.AddRange(zipResult.ImportedFiles);
                    errors.AddRange(zipResult.Errors);
                    skipped += zipResult.FilesSkipped;
                    continue;
                }

                using var stream = File.OpenRead(path);
                var result = await ImportFromStreamAsync(stream, Path.GetFileName(path), targetVersion, ct);
                if (result.Success)
                {
                    imported.AddRange(result.ImportedFiles);
                }
                else
                {
                    errors.AddRange(result.Errors);
                    skipped++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"Failed to import {Path.GetFileName(path)}: {ex.Message}");
                skipped++;
            }
        }

        return new ImportResult
        {
            Success = imported.Count > 0,
            FilesImported = imported.Count,
            FilesSkipped = skipped,
            ImportedFiles = imported,
            Errors = errors,
        };
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportFromZipAsync(
        string zipPath,
        GameType targetVersion,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var (isValid, errorMessage) = ValidateZip(zipPath);
        if (!isValid)
        {
            logger.LogWarning("Import from ZIP failed validation: {Error}", errorMessage);
            return new ImportResult
            {
                Success = false,
                FilesImported = 0,
                FilesSkipped = 0,
                Errors = [errorMessage ?? "Invalid ZIP archive."],
            };
        }

        var imported = new List<string>();
        var errors = new List<string>();
        int skipped = 0;

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            int total = entries.Count;
            int count = 0;
            long expandedBytes = 0;

            directoryService.EnsureDirectoryExists(targetVersion);
            var targetDir = directoryService.GetReplayDirectory(targetVersion);

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                count++;
                progress?.Report((double)count / total);

                var targetPath = GetUniquePath(Path.Combine(targetDir, Path.GetFileName(entry.Name)));

                try
                {
                    await using var stream = entry.Open();
                    expandedBytes += await BoundedArchiveExtractor.CopyEntryToFileAsync(
                        stream,
                        targetPath,
                        entry.FullName,
                        ReplayManagerConstants.MaxReplaySizeBytes,
                        ReplayManagerConstants.MaxAggregateUncompressedBytes - expandedBytes,
                        cancellationToken: ct);
                    imported.Add(targetPath);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Discarding replay entry {Entry} from {ZipPath}", entry.FullName, zipPath);
                    errors.Add(ex.Message);
                    skipped++;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, LogMessages.FailedToImportFromZip, zipPath);
            errors.Add(string.Format(ErrorMessages.FailedToProcessZip, ex.Message));
        }

        return new ImportResult
        {
            Success = imported.Count > 0,
            FilesImported = imported.Count,
            FilesSkipped = skipped,
            ImportedFiles = imported,
            Errors = errors,
        };
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportFromStreamAsync(
        Stream stream,
        string fileName,
        GameType targetVersion,
        CancellationToken ct = default)
    {
        try
        {
            directoryService.EnsureDirectoryExists(targetVersion);
            var targetDir = directoryService.GetReplayDirectory(targetVersion);

            // Handle filename conflict
            var targetPath = GetUniquePath(Path.Combine(targetDir, fileName));

            using var fileStream = File.Create(targetPath);
            await stream.CopyToAsync(fileStream, ct);

            return new ImportResult
            {
                Success = true,
                FilesImported = 1,
                FilesSkipped = 0,
                ImportedFiles = [targetPath],
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, LogMessages.FailedToImportStream, fileName);
            return new ImportResult { Success = false, FilesImported = 0, FilesSkipped = 1, Errors = [ex.Message] };
        }
    }

    /// <inheritdoc />
    public (bool IsValid, string? ErrorMessage) ValidateZip(string zipPath)
    {
        return zipValidationService.ValidateZip(zipPath);
    }

    private static bool IsZipFile(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            if (stream.Length < 4)
            {
                return false;
            }

            var buffer = new byte[4];
            stream.ReadExactly(buffer);

            // Check for ZIP magic bytes: 50 4B 03 04 (local file header) or 50 4B 05 06 (end of central directory)
            return (buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x03 && buffer[3] == 0x04) ||
                   (buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x05 && buffer[3] == 0x06);
        }
        catch
        {
            return false;
        }
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        int count = 1;

        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{name} ({count}){extension}");
            count++;
        }

        return path;
    }

    private static string ExtractFileName(Uri uri)
    {
        try
        {
            var fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrEmpty(fileName))
            {
                return ReplayManagerConstants.DefaultImportedReplayFileName;
            }

            if (!fileName.EndsWith(FileTypes.ReplayFileExtension, StringComparison.OrdinalIgnoreCase) &&
                !fileName.EndsWith(FileTypes.ZipFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                return $"{fileName}{FileTypes.ReplayFileExtension}";
            }

            return fileName;
        }
        catch
        {
            return ReplayManagerConstants.DefaultImportedReplayFileName;
        }
    }

    private async Task<int> DownloadAndImportReplayUrlAsync(
        string directUrl,
        string userAgent,
        GameType targetVersion,
        IProgress<DownloadProgress>? downloadProgress,
        List<string> importedFiles,
        List<string> errors,
        CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{ReplayManagerConstants.TempImportFilePrefix}{Guid.NewGuid()}{FileTypes.ReplayFileExtension}");

        try
        {
            var downloadConfig = new DownloadConfiguration
            {
                Url = new Uri(directUrl),
                DestinationPath = tempPath,
                UserAgent = userAgent,
            };

            var result = await downloadService.DownloadFileAsync(downloadConfig, progress: downloadProgress, cancellationToken: ct);
            if (!result.Success)
            {
                errors.Add($"{ErrorMessages.DownloadFailed}: {directUrl}");
                return 1;
            }

            var isZip = IsZipFile(tempPath);
            var maxAllowedBytes = isZip ? ReplayManagerConstants.MaxUploadBytesPerPeriod : ReplayManagerConstants.MaxReplaySizeBytes;
            var info = new FileInfo(tempPath);
            if (info.Length > maxAllowedBytes)
            {
                errors.Add(string.Format(ErrorMessages.ReplayExceedsMaxSize, info.Length / 1024.0));
                return 1;
            }

            if (isZip)
            {
                logger.LogInformation(LogMessages.DetectedZipFile);
                var zipResult = await ImportFromZipAsync(tempPath, targetVersion, null, ct);
                importedFiles.AddRange(zipResult.ImportedFiles);
                errors.AddRange(zipResult.Errors);
                return Math.Max(zipResult.FilesSkipped, zipResult.Success ? 0 : 1);
            }

            var importedFileName = ExtractFileName(new Uri(directUrl));
            using var stream = File.OpenRead(tempPath);
            var singleResult = await ImportFromStreamAsync(stream, importedFileName, targetVersion, ct);
            if (singleResult.Success)
            {
                importedFiles.AddRange(singleResult.ImportedFiles);
                return singleResult.FilesSkipped;
            }

            errors.AddRange(singleResult.Errors);
            return 1;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
