using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Services;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Tools.ReplayManager;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    public async Task<string?> UploadToUploadThingAsync(
        IEnumerable<ReplayFile> replays,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        string? zipToUpload = null;
        bool isTemporaryZip = false;

        try
        {
            var replayList = replays.ToList();
            if (replayList.Count == 0) return null;

            if (replayList.Count == 1 && replayList[0].FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var (isValid, errorMessage) = zipValidationService.ValidateZip(replayList[0].FullPath);
                if (!isValid)
                {
                    logger.LogError("ZIP validation failed for upload: {Error}", errorMessage);
                    throw new ArgumentException(errorMessage ?? "Invalid ZIP archive for upload.");
                }

                zipToUpload = replayList[0].FullPath;
            }
            else
            {
                var tempZip = Path.Combine(Path.GetTempPath(), $"{ReplayManagerConstants.TempShareFilePrefix}{Guid.NewGuid()}.zip");
                var createdZip = await ExportToZipAsync(replayList, tempZip, progress, ct);
                if (createdZip == null) return null;

                zipToUpload = createdZip;
                isTemporaryZip = true;
            }

            if (new FileInfo(zipToUpload).Length > ReplayManagerConstants.MaxReplaySizeBytes)
            {
                logger.LogError("File exceeds size limit: {Path}", zipToUpload);
                return null;
            }

            return await uploadThingService.UploadFileAsync(zipToUpload, progress, ct);
        }
        catch (ArgumentException)
        {
            throw; // Bubble up validation errors
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload to UploadThing");
            return null;
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
                        progress?.Report((double)count / total * 0.4);

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
}
