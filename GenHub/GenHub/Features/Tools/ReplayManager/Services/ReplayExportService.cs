using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Services;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Core.Models.Tools.UploadThing;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Implementation of <see cref="IReplayExportService"/> for exporting and sharing replays.
/// </summary>
public sealed class ReplayExportService(
    IUploadThingService uploadThingService,
    IZipValidationService zipValidationService,
    ILogger<ReplayExportService> logger) : IReplayExportService
{
    /// <inheritdoc />
    public async Task<OperationResult<UploadResult>> UploadToUploadThingAsync(
        IEnumerable<ReplayFile> replays,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var replayList = replays.ToList();
        if (replayList.Count == 0)
        {
            return OperationResult<UploadResult>.CreateFailure("No replays selected for upload.");
        }

        string? zipToUpload = null;
        bool isTemporaryZip = false;

        try
        {
            var (path, isTemp, uploadProgress) = await ResolveZipToUploadAsync(replayList, progress, ct);
            zipToUpload = path;
            isTemporaryZip = isTemp;

            if (string.IsNullOrEmpty(zipToUpload) || !File.Exists(zipToUpload))
            {
                return OperationResult<UploadResult>.CreateFailure("Failed to prepare replay archive for upload.");
            }

            if (new FileInfo(zipToUpload).Length > ReplayManagerConstants.MaxUploadBytesPerPeriod)
            {
                logger.LogError("File exceeds size limit: {Path}", zipToUpload);
                return OperationResult<UploadResult>.CreateFailure("Exported archive exceeds maximum size limit of 10MB.");
            }

            return await uploadThingService.UploadFileAsync(zipToUpload, uploadProgress, ct);
        }
        catch (ArgumentException ex)
        {
            logger.LogError(ex, "Invalid replay argument for upload");
            return OperationResult<UploadResult>.CreateFailure(ex.Message);
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException or InvalidOperationException) && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to upload to UploadThing");
            return OperationResult<UploadResult>.CreateFailure($"Replay export failed: {ex.Message}");
        }
        finally
        {
            if (isTemporaryZip && !string.IsNullOrEmpty(zipToUpload) && File.Exists(zipToUpload))
            {
                File.Delete(zipToUpload);
            }
        }
    }

    /// <inheritdoc />
    public async Task<string?> ExportToZipAsync(
        IEnumerable<ReplayFile> replays,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            return await Task.Run(
                () =>
                {
                    var replayList = replays.ToList();
                    if (replayList.Count == 0) return null;

                    using var zipFile = File.Create(destinationPath);
                    using var archive = new ZipArchive(zipFile, ZipArchiveMode.Create);

                    int total = replayList.Count;
                    int count = 0;

                    foreach (var replay in replayList)
                    {
                        count++;
                        progress?.Report((double)count / total);

                        if (!File.Exists(replay.FullPath)) continue;
                        archive.CreateEntryFromFile(replay.FullPath, replay.FileName);
                    }

                    return destinationPath;
                },
                ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create ZIP: {Path}", destinationPath);
            return null;
        }
    }

    private async Task<(string? Path, bool IsTemporary, IProgress<double>? UploadProgress)> ResolveZipToUploadAsync(
        IReadOnlyList<ReplayFile> replayList,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        if (replayList.Count == 1 && replayList[0].FileName.EndsWith(FileTypes.ZipFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            var (isValid, errorMessage) = zipValidationService.ValidateZip(replayList[0].FullPath);
            if (!isValid)
            {
                logger.LogError("ZIP validation failed for upload: {Error}", errorMessage);
                throw new ArgumentException(errorMessage ?? "Invalid ZIP archive for upload.");
            }

            return (replayList[0].FullPath, false, progress);
        }

        var tempZip = Path.Combine(Path.GetTempPath(), $"{ReplayManagerConstants.TempShareFilePrefix}{Guid.NewGuid()}{FileTypes.ZipFileExtension}");
        var zipProgress = progress != null ? new Progress<double>(p => progress.Report(p * 0.25)) : null;
        var uploadProgress = progress != null ? new Progress<double>(p => progress.Report(0.25 + (p * 0.75))) : null;

        var createdZip = await ExportToZipAsync(replayList, tempZip, zipProgress, ct);
        return (createdZip, true, uploadProgress);
    }
}
