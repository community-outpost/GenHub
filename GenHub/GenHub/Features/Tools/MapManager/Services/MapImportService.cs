using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.MapManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.MapManager;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.MapManager.Services;

/// <summary>
/// Implementation of <see cref="IMapImportService"/> for importing maps.
/// </summary>
public sealed class MapImportService(
    IMapDirectoryService directoryService,
    HttpClient httpClient,
    MapNameParser mapNameParser,
    ILogger<MapImportService> logger) : IMapImportService
{
    private static readonly char[] PathSeparators = ['/', '\''];

    /// <inheritdoc />
    public async Task<ImportResult> ImportFromUrlAsync(
        string url,
        GameType targetVersion,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var result = new ImportResult();

        try
        {
            logger.LogInformation("Importing map from URL: {Url}", url);

            var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var fileName = GetFileNameFromUrl(url, response);
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);

            await using (var fileStream = File.Create(tempPath))
            await using (var httpStream = await response.Content.ReadAsStreamAsync(ct))
            {
                await httpStream.CopyToAsync(fileStream, ct);
            }

            // Detect file type by magic bytes
            bool isZip = false;
            try
            {
                using var stream = File.OpenRead(tempPath);
                var buffer = new byte[4];
                if (await stream.ReadAsync(buffer.AsMemory(0, 4), ct) == 4)
                {
                    // ZIP signature: 50 4B 03 04
                    if (buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x03 && buffer[3] == 0x04)
                    {
                        isZip = true;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to detect file type for {File}", tempPath);
            }

            if (isZip || fileName.EndsWith(Path.GetExtension(MapManagerConstants.ZipFilePattern), StringComparison.OrdinalIgnoreCase))
            {
                // Ensure extension is .zip for the import service if it was detected by magic bytes but has wrong extension
                if (!tempPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var newPath = tempPath + ".zip";
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(tempPath, newPath);
                    tempPath = newPath;
                }

                result = await ImportFromZipAsync(tempPath, targetVersion, progress, ct);
            }
            else
            {
                // Assume it's a map file (or text-based map file)
                // Ensure extension is .map so ImportFromFilesAsync picks it up
                if (!tempPath.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
                {
                    var newPath = tempPath + ".map";
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(tempPath, newPath);
                    tempPath = newPath;
                }

                result = await ImportFromFilesAsync([tempPath], targetVersion, ct);
            }

            // Cleanup temp file
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to import from URL: {Url}", url);
            result.Errors.Add($"Import failed: {ex.Message}");
        }

        result.Success = result.FilesImported > 0;
        return result;
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportFromFilesAsync(
        IEnumerable<string> filePaths,
        GameType targetVersion,
        CancellationToken ct = default)
    {
        var result = new ImportResult();
        var targetDir = directoryService.GetMapDirectory(targetVersion);
        directoryService.EnsureDirectoryExists(targetVersion);

        // Expand directories
        var expandedPaths = new List<string>();
        foreach (var path in filePaths)
        {
            if (Directory.Exists(path))
            {
                try
                {
                    expandedPaths.AddRange(Directory.GetFiles(path, "*", SearchOption.AllDirectories));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to expand directory: {Path}", path);
                }
            }
            else
            {
                expandedPaths.Add(path);
            }
        }

        foreach (var filePath in expandedPaths)
        {
            try
            {
                if (filePath.EndsWith(Path.GetExtension(MapManagerConstants.ZipFilePattern), StringComparison.OrdinalIgnoreCase))
                {
                    var zipResult = await ImportFromZipAsync(filePath, targetVersion, null, ct);
                    result.FilesImported += zipResult.FilesImported;
                    result.Errors.AddRange(zipResult.Errors);
                    result.ImportedMaps.AddRange(zipResult.ImportedMaps);
                    continue;
                }

                if (!filePath.EndsWith(Path.GetExtension(MapManagerConstants.MapFilePattern), StringComparison.OrdinalIgnoreCase))
                {
                    result.Errors.Add($"Skipped non-map file: {Path.GetFileName(filePath)}");
                    continue;
                }

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > IMapImportService.MaxMapSizeBytes)
                {
                    result.Errors.Add($"File too large: {fileInfo.Name} ({fileInfo.Length / 1024 / 1024}MB)");
                    continue;
                }

                // Create a directory for the map (all maps must be in directories)
                var mapName = Path.GetFileNameWithoutExtension(fileInfo.Name);
                var mapDirPath = GetUniqueDirectoryPath(Path.Combine(targetDir, mapName));
                Directory.CreateDirectory(mapDirPath);

                var destPath = Path.Combine(mapDirPath, fileInfo.Name);
                File.Copy(filePath, destPath, false);

                result.FilesImported++;
                logger.LogInformation("Imported map to directory: {DirectoryName}/{FileName}", mapName, fileInfo.Name);

                // Create MapFile object
                var displayName = mapNameParser.ParseMapName(destPath);
                var mapFile = new MapFile
                {
                    FileName = fileInfo.Name,
                    FullPath = destPath,
                    SizeBytes = fileInfo.Length,
                    GameType = targetVersion,
                    LastModified = File.GetLastWriteTime(destPath),
                    DirectoryName = Path.GetFileName(mapDirPath),
                    IsDirectory = true, // We forced it into a directory
                    AssetFiles = [], // Single file import has no assets
                    DisplayName = displayName,
                    ThumbnailPath = null,
                    ThumbnailBitmap = null,
                };
                result.ImportedMaps.Add(mapFile);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to import file: {FilePath}", filePath);
                result.Errors.Add($"Failed to import {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        result.Success = result.FilesImported > 0;
        return result;
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportFromZipAsync(
        string zipPath,
        GameType targetVersion,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(
            () =>
            {
                var result = new ImportResult();
                var (isValid, errorMessage) = ValidateZip(zipPath);

                if (!isValid)
                {
                    result.Errors.Add(errorMessage ?? "Invalid ZIP file");
                    return result;
                }

                var targetDir = directoryService.GetMapDirectory(targetVersion);
                directoryService.EnsureDirectoryExists(targetVersion);

                try
                {
                    using var archive = ZipFile.OpenRead(zipPath);
                    var allEntries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();

                    // Group entries by their parent directory (if any)
                    var entriesByDirectory = allEntries
                        .GroupBy(e =>
                        {
                            var parts = e.FullName.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
                            return parts.Length > 1 ? parts[0] : string.Empty;
                        })
                        .ToDictionary(g => g.Key, g => g.ToList());

                    int totalMaps = 0;
                    int processedMaps = 0;

                    // Count total maps for progress
                    foreach (var group in entriesByDirectory)
                    {
                        totalMaps += group.Value.Count(e => e.Name.EndsWith(".map", StringComparison.OrdinalIgnoreCase));
                    }

                    foreach (var (directoryName, entries) in entriesByDirectory)
                    {
                        var mapEntries = entries.Where(e => e.Name.EndsWith(".map", StringComparison.OrdinalIgnoreCase)).ToList();
                        if (mapEntries.Count == 0)
                            continue;

                        foreach (var mapEntry in mapEntries)
                        {
                            if (mapEntry.Length > IMapImportService.MaxMapSizeBytes)
                            {
                                result.Errors.Add($"Map too large: {mapEntry.Name}");
                                continue;
                            }

                            // Determine the directory name for this map
                            string mapDirName;
                            if (string.IsNullOrEmpty(directoryName))
                            {
                                // Root-level map - create a directory using the map name
                                mapDirName = Path.GetFileNameWithoutExtension(mapEntry.Name);
                            }
                            else
                            {
                                // Directory-based map - use the existing directory name
                                mapDirName = directoryName;
                            }

                            var mapDirPath = GetUniqueDirectoryPath(Path.Combine(targetDir, mapDirName));
                            Directory.CreateDirectory(mapDirPath);

                            // Extract the .map file
                            var mapDestPath = Path.Combine(mapDirPath, mapEntry.Name);
                            mapEntry.ExtractToFile(mapDestPath, false);

                            var assetFiles = new List<string>();
                            string? thumbnailPath = null;

                            // Extract related asset files from the same directory in the ZIP
                            if (!string.IsNullOrEmpty(directoryName))
                            {
                                var assetEntries = entries.Where(e =>
                                    !e.Name.EndsWith(".map", StringComparison.OrdinalIgnoreCase) &&
                                    MapManagerConstants.AllowedExtensions.Contains(Path.GetExtension(e.Name), StringComparer.OrdinalIgnoreCase));

                                foreach (var assetEntry in assetEntries)
                                {
                                    var assetDestPath = Path.Combine(mapDirPath, assetEntry.Name);
                                    if (!File.Exists(assetDestPath))
                                    {
                                        assetEntry.ExtractToFile(assetDestPath, false);
                                    }

                                    assetFiles.Add(assetDestPath);

                                    // Check for thumbnail
                                    if (assetEntry.Name.Equals(MapManagerConstants.DefaultThumbnailName, StringComparison.OrdinalIgnoreCase) ||
                                        (thumbnailPath == null && assetEntry.Name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        thumbnailPath = assetDestPath;
                                    }
                                }
                            }

                            var totalSize = new FileInfo(mapDestPath).Length + assetFiles.Sum(f => new FileInfo(f).Length);

                            result.FilesImported++;
                            processedMaps++;
                            progress?.Report((double)processedMaps / totalMaps);
                            logger.LogInformation("Extracted map to directory: {DirectoryName}/{FileName}", mapDirName, mapEntry.Name);

                            // Create MapFile object
                            var displayName = mapNameParser.ParseMapName(mapDestPath);
                            var mapFile = new MapFile
                            {
                                FileName = mapEntry.Name,
                                FullPath = mapDestPath,
                                SizeBytes = totalSize,
                                GameType = targetVersion,
                                LastModified = DateTime.Now,
                                DirectoryName = Path.GetFileName(mapDirPath),
                                IsDirectory = true,
                                AssetFiles = assetFiles,
                                DisplayName = displayName,
                                ThumbnailPath = thumbnailPath,
                                ThumbnailBitmap = null,
                            };
                            result.ImportedMaps.Add(mapFile);
                        }
                    }

                    progress?.Report(1.0);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to import from ZIP: {ZipPath}", zipPath);
                    result.Errors.Add($"ZIP extraction failed: {ex.Message}");
                }

                result.Success = result.FilesImported > 0;
                return result;
            },
            ct);
    }

    /// <inheritdoc />
    public (bool IsValid, string? ErrorMessage) ValidateZip(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();

            if (entries.Count == 0)
            {
                return (false, "ZIP file is empty");
            }

            var allowedExtensions = MapManagerConstants.AllowedExtensions;
            var directoriesWithMaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                // Calculate nesting depth
                var separatorCount = entry.FullName.Count(c => c == '/' || c == '\\');

                // Allow files at root (depth 0) or in one subdirectory (depth 1)
                if (separatorCount > 1)
                {
                    return (false, "ZIP contains nested directories beyond 1 level. Only flat archives or 1-level deep directories are supported.");
                }

                // Validate file extension
                var extension = Path.GetExtension(entry.Name);
                if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    return (false, $"ZIP contains invalid file type: {extension}. Only .map, .tga, .ini, .str, and .txt files are allowed.");
                }

                // Track which directories contain .map files
                if (extension.Equals(Path.GetExtension(MapManagerConstants.MapFilePattern), StringComparison.OrdinalIgnoreCase))
                {
                    if (separatorCount == 1)
                    {
                        // Extract directory name from path like "MapName/MapName.map"
                        var dirName = entry.FullName.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries)[0];
                        directoriesWithMaps.Add(dirName);
                    }
                    else
                    {
                        // Root-level .map file
                        directoriesWithMaps.Add(string.Empty);
                    }
                }
            }

            // Verify that every subdirectory contains at least one .map file
            var allDirectories = entries
                .Where(e => e.FullName.Contains('/') || e.FullName.Contains('\\'))
                .Select(e => e.FullName.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries)[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var dir in allDirectories)
            {
                if (!directoriesWithMaps.Contains(dir))
                {
                    return (false, $"Directory '{dir}' does not contain a .map file. Each directory must have at least one .map file.");
                }
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to validate ZIP: {ZipPath}", zipPath);
            return (false, $"Failed to read ZIP file: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportFromStreamAsync(
        Stream stream,
        string fileName,
        GameType targetVersion,
        CancellationToken ct = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);

        try
        {
            await using (var fileStream = File.Create(tempPath))
            {
                await stream.CopyToAsync(fileStream, ct);
            }

            return await ImportFromFilesAsync([tempPath], targetVersion, ct);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string GetFileNameFromUrl(string url, HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentDisposition?.FileName != null)
        {
            return response.Content.Headers.ContentDisposition.FileName.Trim('"');
        }

        var uri = new Uri(url);
        return Path.GetFileName(uri.LocalPath);
    }

    private static string GetUniqueFilePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        int counter = 1;

        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{fileNameWithoutExt} ({counter}){extension}");
            counter++;
        }

        return path;
    }

    private static string GetUniqueDirectoryPath(string path)
    {
        if (!Directory.Exists(path))
        {
            return path;
        }

        var parentDirectory = Path.GetDirectoryName(path) ?? string.Empty;
        var dirName = Path.GetFileName(path);
        int counter = 1;

        while (Directory.Exists(path))
        {
            path = Path.Combine(parentDirectory, $"{dirName} ({counter})");
            counter++;
        }

        return path;
    }
}
