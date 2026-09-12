using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Services;
using GenHub.Core.Interfaces.Tools.MapManager;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.MapManager;
using GenHub.Core.Models.Tools.UploadThing;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.MapManager.Services;

/// <summary>
/// Implementation of <see cref="IMapExportService"/> for exporting and sharing maps.
/// </summary>
public sealed class MapExportService(
    IUploadThingService uploadThingService,
    IMapImportService importService,
    ILogger<MapExportService> logger) : IMapExportService
{
    /// <summary>
    /// Maximum single upload size (10 MB gateway limit).
    /// </summary>
    private const long MaxTotalUploadBytes = MapManagerConstants.MaxMapSizeBytes;

    /// <inheritdoc />
    public async Task<OperationResult<UploadResult>> UploadToUploadThingAsync(
        IEnumerable<MapFile> maps,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var mapList = maps.ToList();
        if (mapList.Count == 0)
        {
            return OperationResult<UploadResult>.CreateFailure("No maps selected for upload.");
        }

        string? zipToUpload = null;
        bool isTemporaryZip = false;

        try
        {
            var (path, isTemp, uploadProgress) = await ResolveZipToUploadAsync(mapList, progress, ct);
            zipToUpload = path;
            isTemporaryZip = isTemp;

            if (string.IsNullOrEmpty(zipToUpload) || !File.Exists(zipToUpload))
            {
                return OperationResult<UploadResult>.CreateFailure("Failed to prepare map archive for upload.");
            }

            if (new FileInfo(zipToUpload).Length > MaxTotalUploadBytes)
            {
                logger.LogError("File exceeds size limit of 10MB: {Path}", zipToUpload);
                return OperationResult<UploadResult>.CreateFailure("Exported archive exceeds maximum size limit of 10MB.");
            }

            return await uploadThingService.UploadFileAsync(zipToUpload, uploadProgress, ct);
        }
        catch (ArgumentException ex)
        {
            logger.LogError(ex, "Invalid map argument for upload");
            return OperationResult<UploadResult>.CreateFailure(ex.Message);
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException or InvalidOperationException) && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to upload to UploadThing");
            return OperationResult<UploadResult>.CreateFailure($"Map export failed: {ex.Message}");
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
        IEnumerable<MapFile> maps,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            return await Task.Run(
                () =>
                {
                    var mapList = maps.ToList();
                    if (mapList.Count == 0) return null;

                    using var zipFile = File.Create(destinationPath);
                    using var archive = new ZipArchive(zipFile, ZipArchiveMode.Create);

                    int total = mapList.Count;
                    int count = 0;

                    foreach (var map in mapList)
                    {
                        count++;
                        progress?.Report((double)count / total);

                        if (map.IsDirectory)
                        {
                            // Add directory-based map with all its assets
                            var dirPath = Path.GetDirectoryName(map.FullPath);
                            if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
                                continue;

                            var dirInfo = new DirectoryInfo(dirPath);
                            var dirName = dirInfo.Name;

                            // Add the .map file
                            if (File.Exists(map.FullPath))
                            {
                                var entryName = $"{dirName}/{Path.GetFileName(map.FullPath)}";
                                archive.CreateEntryFromFile(map.FullPath, entryName);
                            }

                            // Add all asset files
                            foreach (var assetPath in map.AssetFiles)
                            {
                                if (File.Exists(assetPath))
                                {
                                    var entryName = $"{dirName}/{Path.GetFileName(assetPath)}";
                                    archive.CreateEntryFromFile(assetPath, entryName);
                                }
                            }
                        }
                        else
                        {
                            // Add standalone .map file wrapped in a directory
                            if (!File.Exists(map.FullPath)) continue;

                            var mapName = Path.GetFileNameWithoutExtension(map.FileName);
                            var entryName = $"{mapName}/{map.FileName}";
                            archive.CreateEntryFromFile(map.FullPath, entryName);
                        }
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
        IReadOnlyList<MapFile> mapList,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        if (mapList.Count == 1 && mapList[0].FileName.EndsWith(Path.GetExtension(MapManagerConstants.ZipFilePattern), StringComparison.OrdinalIgnoreCase))
        {
            var (isValid, errorMessage) = importService.ValidateZip(mapList[0].FullPath);
            if (!isValid)
            {
                logger.LogError("ZIP validation failed for upload: {Error}", errorMessage);
                throw new ArgumentException(errorMessage ?? "Invalid ZIP archive for upload.");
            }

            return (mapList[0].FullPath, false, progress);
        }

        var tempZip = Path.Combine(Path.GetTempPath(), $"{MapManagerConstants.TempShareFilePrefix}{Guid.NewGuid()}{FileTypes.ZipFileExtension}");
        var zipProgress = progress != null ? new Progress<double>(p => progress.Report(p * 0.25)) : null;
        var uploadProgress = progress != null ? new Progress<double>(p => progress.Report(0.25 + (p * 0.75))) : null;

        var createdZip = await ExportToZipAsync(mapList, tempZip, zipProgress, ct);
        return (createdZip, true, uploadProgress);
    }
}
