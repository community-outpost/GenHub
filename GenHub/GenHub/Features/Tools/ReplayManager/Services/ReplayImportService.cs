using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
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
            var directUrl = await urlParserService.GetDirectDownloadUrlAsync(url, ct);
            if (string.IsNullOrEmpty(directUrl))
            {
                return new ImportResult
                {
                    Success = false,
                    FilesImported = 0,
                    FilesSkipped = 0,
                    Errors = [ErrorMessages.CouldNotExtractDownloadUrl],
                };
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"{ReplayManagerConstants.TempImportFilePrefix}{Guid.NewGuid()}.rep");
            try
            {
                var downloadProgress = progress != null ? new Progress<DownloadProgress>(p => progress.Report(p.Percentage / 100.0)) : null;
                var result = await downloadService.DownloadFileAsync(new Uri(directUrl), tempPath, progress: downloadProgress, cancellationToken: ct);

                if (!result.Success)
                {
                    return new ImportResult
                    {
                        Success = false,
                        FilesImported = 0,
                        FilesSkipped = 0,
                        Errors = [ErrorMessages.DownloadFailed],
                    };
                }

                var info = new FileInfo(tempPath);
                if (info.Length > ReplayManagerConstants.MaxReplaySizeBytes)
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }

                    return new ImportResult
                    {
                        Success = false,
                        FilesImported = 0,
                        FilesSkipped = 0,
                        Errors = [string.Format(ErrorMessages.ReplayExceedsMaxSize, info.Length / 1024.0)],
                    };
                }

                // Detect if the downloaded file is a ZIP by checking magic bytes
                if (IsZipFile(tempPath))
                {
                    logger.LogInformation(LogMessages.DetectedZipFile);
                    return await ImportFromZipAsync(tempPath, targetVersion, progress, ct);
                }

                var importedFileName = GetFileNameFromUrl(directUrl);
                using var stream = File.OpenRead(tempPath);
                return await ImportFromStreamAsync(stream, importedFileName, targetVersion, progress, ct);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to import from URL: {Url}", url);
            return new ImportResult { Success = false, FilesImported = 0, FilesSkipped = 0, Errors = [ex.Message] };
        }
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportFromFilesAsync(
        IEnumerable<string> filePaths,
        GameType targetVersion,
        IProgress<double>? progress = null,
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
                var result = await ImportFromStreamAsync(stream, Path.GetFileName(path), targetVersion, null, ct);
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
            catch (Exception ex)
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
        var (isValid, errorMessage) = await ValidateZipAsync(zipPath, ct);
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

            foreach (var entry in entries)
            {
                count++;
                progress?.Report((double)count / total);

                using var stream = entry.Open();
                var result = await ImportFromStreamAsync(stream, entry.Name, targetVersion, progress: null, ct: ct);
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
        }
        catch (Exception ex)
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
        IProgress<double>? progress = null,
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
        catch (Exception ex)
        {
            logger.LogError(ex, LogMessages.FailedToImportStream, fileName);
            return new ImportResult { Success = false, FilesImported = 0, FilesSkipped = 1, Errors = [ex.Message] };
        }
    }

    /// <inheritdoc />
    public async Task<(bool IsValid, string? ErrorMessage)> ValidateZipAsync(string zipPath, CancellationToken ct = default)
    {
        // Honor cancellation token before performing the validation
        ct.ThrowIfCancellationRequested();
        var result = zipValidationService.ValidateZip(zipPath);

        // Check cancellation again after validation completes
        ct.ThrowIfCancellationRequested();
        return result;
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
            stream.Read(buffer, 0, 4);

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

    private static string GetFileNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var fileName = Path.GetFileName(uri.LocalPath);
            return string.IsNullOrEmpty(fileName) ? ReplayManagerConstants.DefaultImportedReplayFileName : fileName;
        }
        catch
        {
            return "imported_replay.rep";
        }
    }
}
