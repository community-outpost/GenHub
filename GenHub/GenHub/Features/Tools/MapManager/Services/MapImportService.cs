using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.MapManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.MapManager;
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;

namespace GenHub.Features.Tools.MapManager.Services;

#pragma warning disable S6966 // OpenAsync is only available in .NET 10+; net8.0 target provides Open()

/// <summary>
/// Implementation of <see cref="IMapImportService"/> for importing maps.
/// </summary>
public sealed class MapImportService(
    IMapDirectoryService directoryService,
    HttpClient httpClient,
    MapNameParser mapNameParser,
    ILogger<MapImportService> logger) : IMapImportService
{
    private sealed record SharpCompressExtractionContext(
        string ArchivePath,
        string TargetDir,
        GameType TargetVersion,
        ImportResult Result,
        Action<long> OnBytesExpanded,
        CancellationToken CancellationToken);

    private static readonly char[] PathSeparators = ['/', '\\'];

    /// <inheritdoc />
    public async Task<ImportResult> ImportFromUrlAsync(
        string url,
        GameType targetVersion,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var result = new ImportResult();
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHub", "MapImports", Guid.NewGuid().ToString("N"));

        try
        {
            logger.LogInformation("Importing map from URL: {Url}", url);

            var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var fileName = ExtractFileName(new Uri(url), response);
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, fileName);

            await using (var fileStream = File.Create(tempPath))
            await using (var httpStream = await response.Content.ReadAsStreamAsync(ct))
            {
                await httpStream.CopyToAsync(fileStream, ct);
            }

            var isArchive = IsArchiveFile(tempPath) ||
                fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase);

            if (isArchive)
            {
                result = await ImportFromZipAsync(tempPath, targetVersion, progress, ct);
            }
            else
            {
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
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to import from URL: {Url}", url);
            result.Errors.Add($"Import failed: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

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
                if (IsArchiveFile(filePath) ||
                    filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    filePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
                    filePath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
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

                // Import single .map file: create a folder named after the map
                var mapName = Path.GetFileNameWithoutExtension(filePath);
                var mapDirPath = GetUniqueDirectoryPath(Path.Combine(targetDir, mapName));
                Directory.CreateDirectory(mapDirPath);

                var destPath = Path.Combine(mapDirPath, Path.GetFileName(filePath));
                File.Copy(filePath, destPath, overwrite: true);

                // Look for associated files in the same directory as the source file
                var sourceDir = Path.GetDirectoryName(filePath);
                var assetFiles = new List<string>();
                string? thumbnailPath = null;

                if (!string.IsNullOrEmpty(sourceDir))
                {
                    foreach (var ext in MapManagerConstants.AllowedExtensions)
                    {
                        if (ext.Equals(Path.GetExtension(MapManagerConstants.MapFilePattern), StringComparison.OrdinalIgnoreCase))
                            continue;

                        var matchingFiles = Directory.GetFiles(sourceDir, $"{mapName}*{ext}");
                        foreach (var asset in matchingFiles)
                        {
                            var assetDest = Path.Combine(mapDirPath, Path.GetFileName(asset));
                            if (!File.Exists(assetDest))
                            {
                                File.Copy(asset, assetDest, overwrite: true);
                            }

                            assetFiles.Add(assetDest);

                            // Check for thumbnail (.tga)
                            if (asset.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) && thumbnailPath == null)
                            {
                                thumbnailPath = assetDest;
                            }
                        }
                    }
                }

                var totalSize = fileInfo.Length + assetFiles.Sum(f => new FileInfo(f).Length);

                result.FilesImported++;
                logger.LogInformation("Imported map file to directory: {DirectoryName}/{FileName}", mapName, Path.GetFileName(filePath));

                // Create MapFile object
                var displayName = mapNameParser.ParseMapName(destPath);
                var mapFile = new MapFile
                {
                    FileName = Path.GetFileName(filePath),
                    FullPath = destPath,
                    SizeBytes = totalSize,
                    GameType = targetVersion,
                    LastModified = File.GetLastWriteTime(destPath),
                    DirectoryName = Path.GetFileName(mapDirPath),
                    IsDirectory = true,
                    AssetFiles = assetFiles,
                    DisplayName = displayName,
                    ThumbnailPath = thumbnailPath,
                    ThumbnailBitmap = null,
                };
                result.ImportedMaps.Add(mapFile);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to import map: {Path}", filePath);
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
        var result = new ImportResult();

        var (isValid, errorMessage) = ValidateZip(zipPath);
        if (!isValid)
        {
            logger.LogWarning("ZIP validation failed: {Error}", errorMessage);
            result.Errors.Add(errorMessage ?? "Invalid ZIP file");
            return result;
        }

        var targetDir = directoryService.GetMapDirectory(targetVersion);
        directoryService.EnsureDirectoryExists(targetVersion);

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var allEntries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();

            var entriesByDirectory = allEntries
                .GroupBy(e =>
                {
                    var parts = e.FullName.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
                    return parts.Length > 1 ? parts[0] : string.Empty;
                })
                .ToDictionary(g => g.Key, g => g.ToList());

            int totalMaps = 0;
            int processedMaps = 0;
            long expandedBytes = 0;

            // Count total maps for progress
            foreach (var group in entriesByDirectory)
            {
                totalMaps += group.Value.Count(e => e.Name.EndsWith(".map", StringComparison.OrdinalIgnoreCase));
            }

            if (totalMaps == 0)
            {
                return await ImportWithSharpCompressAsync(zipPath, targetVersion, progress, ct);
            }

            foreach (var (directoryName, entries) in entriesByDirectory)
            {
                var mapEntries = entries.Where(e => e.Name.EndsWith(".map", StringComparison.OrdinalIgnoreCase)).ToList();
                if (mapEntries.Count == 0)
                    continue;

                foreach (var mapEntry in mapEntries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (mapEntry.Length > IMapImportService.MaxMapSizeBytes)
                    {
                        result.Errors.Add($"Map too large: {mapEntry.Name}");
                        continue;
                    }

                    // Determine the directory name for this map
                    var mapDirName = string.IsNullOrEmpty(directoryName)
                        ? Path.GetFileNameWithoutExtension(mapEntry.Name)
                        : Path.GetFileName(directoryName);

                    if (string.IsNullOrWhiteSpace(mapDirName) || mapDirName == "." || mapDirName == "..")
                    {
                        mapDirName = Path.GetFileNameWithoutExtension(mapEntry.Name);
                    }

                    var mapDirPath = GetUniqueDirectoryPath(Path.Combine(targetDir, mapDirName));
                    var mapDestPath = Path.Combine(mapDirPath, mapEntry.Name);
                    var assetFiles = new List<string>();
                    string? thumbnailPath = null;

                    long mapExpandedBytes = 0;

                    try
                    {
                        Directory.CreateDirectory(mapDirPath);

                        try
                        {
                            await using var mapStream = mapEntry.Open();
                            mapExpandedBytes += await BoundedArchiveExtractor.CopyEntryToFileAsync(
                                mapStream,
                                mapDestPath,
                                mapEntry.FullName,
                                IMapImportService.MaxMapSizeBytes,
                                MapManagerConstants.MaxAggregateUncompressedBytes - expandedBytes - mapExpandedBytes,
                                cancellationToken: ct);
                        }
                        catch (InvalidDataException ex)
                        {
                            logger.LogInformation(ex, "Decompression via ZipArchive failed for map {Entry}, attempting SharpCompress fallback", mapEntry.FullName);
                            var written = await ExtractEntryWithSharpCompressAsync(
                                zipPath,
                                mapEntry.FullName,
                                mapDestPath,
                                IMapImportService.MaxMapSizeBytes,
                                MapManagerConstants.MaxAggregateUncompressedBytes - expandedBytes - mapExpandedBytes,
                                ct);
                            if (written > 0)
                            {
                                mapExpandedBytes += written;
                            }
                            else
                            {
                                throw;
                            }
                        }

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
                                    try
                                    {
                                        await using var assetStream = assetEntry.Open();
                                        mapExpandedBytes += await BoundedArchiveExtractor.CopyEntryToFileAsync(
                                            assetStream,
                                            assetDestPath,
                                            assetEntry.FullName,
                                            MapManagerConstants.MaxAssetSizeBytes,
                                            MapManagerConstants.MaxAggregateUncompressedBytes - expandedBytes - mapExpandedBytes,
                                            cancellationToken: ct);
                                    }
                                    catch (InvalidDataException ex)
                                    {
                                        logger.LogInformation(ex, "Decompression via ZipArchive failed for asset {Entry}, attempting SharpCompress fallback", assetEntry.FullName);
                                        var written = await ExtractEntryWithSharpCompressAsync(
                                            zipPath,
                                            assetEntry.FullName,
                                            assetDestPath,
                                            MapManagerConstants.MaxAssetSizeBytes,
                                            MapManagerConstants.MaxAggregateUncompressedBytes - expandedBytes - mapExpandedBytes,
                                            ct);
                                        if (written > 0)
                                        {
                                            mapExpandedBytes += written;
                                        }
                                        else
                                        {
                                            throw;
                                        }
                                    }
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

                        expandedBytes += mapExpandedBytes;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(
                            "Discarding map {Entry} from {ZipPath}: {Reason}",
                            mapEntry.FullName,
                            zipPath,
                            ex.Message);
                        result.Errors.Add(ex.Message);
                        DeleteDirectoryBestEffort(mapDirPath);
                        continue;
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
                        LastModified = File.GetLastWriteTime(mapDestPath),
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
        catch (InvalidDataException ex)
        {
            logger.LogInformation(ex, "ZipFile.OpenRead failed for {ZipPath}, falling back to SharpCompress", zipPath);
            return await ImportWithSharpCompressAsync(zipPath, targetVersion, progress, ct);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Import from ZIP was cancelled: {ZipPath}", zipPath);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to import from ZIP: {ZipPath}", zipPath);
            result.Errors.Add($"ZIP extraction failed: {ex.Message}");
        }

        result.Success = result.FilesImported > 0;
        return result;
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

            if (entries.Count > MapManagerConstants.MaxZipEntries)
            {
                return (false, $"ZIP contains too many entries ({entries.Count} > {MapManagerConstants.MaxZipEntries}).");
            }

            long totalUncompressedBytes = 0;
            var allowedExtensions = MapManagerConstants.AllowedExtensions;
            var directoriesWithMaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                if (entry.Length > MapManagerConstants.MaxAssetSizeBytes)
                {
                    return (false, $"ZIP entry '{entry.FullName}' exceeds maximum allowed size ({entry.Length} > {MapManagerConstants.MaxAssetSizeBytes} bytes).");
                }

                totalUncompressedBytes += entry.Length;
                if (totalUncompressedBytes > MapManagerConstants.MaxAggregateUncompressedBytes)
                {
                    return (false, $"ZIP aggregate uncompressed size exceeds maximum allowed limit ({totalUncompressedBytes} > {MapManagerConstants.MaxAggregateUncompressedBytes} bytes).");
                }

                var segments = entry.FullName.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Any(s => s == "." || s == ".." || s.Contains(':') || Path.IsPathRooted(s)))
                {
                    return (false, $"ZIP contains invalid path traversal segment in '{entry.FullName}'.");
                }

                var separatorCount = entry.FullName.Count(c => c == '/' || c == '\\');
                if (separatorCount > 1)
                {
                    return (false, "ZIP contains nested directories beyond 1 level. Only flat ZIPs or 1-level deep directories are supported.");
                }

                var extension = Path.GetExtension(entry.Name);
                if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    return (false, $"ZIP contains invalid file type: {extension}. Only .map, .tga, .ini, .str, and .txt files are allowed.");
                }

                if (extension.Equals(Path.GetExtension(MapManagerConstants.MapFilePattern), StringComparison.OrdinalIgnoreCase))
                {
                    if (separatorCount == 1)
                    {
                        var dirName = segments[0];
                        directoriesWithMaps.Add(dirName);
                    }
                    else
                    {
                        directoriesWithMaps.Add(string.Empty);
                    }
                }
            }

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
        catch (InvalidDataException)
        {
            return ValidateWithSharpCompress(zipPath);
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
        var sanitizedFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(sanitizedFileName))
        {
            sanitizedFileName = $"map_{Guid.NewGuid():N}.map";
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "GenHub", "MapImports", Guid.NewGuid().ToString("N"));
        var tempPath = Path.Combine(tempDir, sanitizedFileName);

        try
        {
            Directory.CreateDirectory(tempDir);
            await using (var fileStream = File.Create(tempPath))
            {
                await stream.CopyToAsync(fileStream, ct);
            }

            return await ImportFromFilesAsync([tempPath], targetVersion, ct);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }
    }

    private static bool IsArchiveFile(string filePath)
    {
        try
        {
            var ext = Path.GetExtension(filePath);
            if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".7z", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".rar", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            using var stream = File.OpenRead(filePath);
            if (stream.Length < 4)
            {
                return false;
            }

            var buffer = new byte[6];
            var read = stream.Read(buffer, 0, 6);
            if (read >= 4)
            {
                // ZIP magic bytes: 50 4B 03 04, 50 4B 05 06, 50 4B 07 08
                if (buffer[0] == 0x50 && buffer[1] == 0x4B &&
                    (buffer[2] == 0x03 || buffer[2] == 0x05 || buffer[2] == 0x07))
                {
                    return true;
                }

                // 7-Zip magic bytes: 37 7A BC AF 27 1C
                if (read >= 6 && buffer[0] == 0x37 && buffer[1] == 0x7A && buffer[2] == 0xBC &&
                    buffer[3] == 0xAF && buffer[4] == 0x27 && buffer[5] == 0x1C)
                {
                    return true;
                }

                // RAR magic bytes: 52 61 72 21
                if (buffer[0] == 0x52 && buffer[1] == 0x61 && buffer[2] == 0x72 && buffer[3] == 0x21)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static (bool IsValid, string? ErrorMessage) ValidateSharpCompressEntry(
        IArchiveEntry entry,
        ref long totalUncompressedBytes,
        HashSet<string> directoriesWithMaps)
    {
        var entryKey = entry.Key ?? string.Empty;
        var fileName = Path.GetFileName(entryKey);

        if (entry.Size > MapManagerConstants.MaxAssetSizeBytes)
        {
            return (false, $"Archive entry '{entryKey}' exceeds maximum allowed size ({entry.Size} > {MapManagerConstants.MaxAssetSizeBytes} bytes).");
        }

        totalUncompressedBytes += entry.Size;
        if (totalUncompressedBytes > MapManagerConstants.MaxAggregateUncompressedBytes)
        {
            return (false, $"Archive aggregate uncompressed size exceeds maximum allowed limit ({totalUncompressedBytes} > {MapManagerConstants.MaxAggregateUncompressedBytes} bytes).");
        }

        var segments = entryKey.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s == "." || s == ".." || s.Contains(':') || Path.IsPathRooted(s)))
        {
            return (false, $"Archive contains invalid path traversal segment in '{entryKey}'.");
        }

        var separatorCount = entryKey.Count(c => c == '/' || c == '\\');
        if (separatorCount > 1)
        {
            return (false, "Archive contains nested directories beyond 1 level. Only flat archives or 1-level deep directories are supported.");
        }

        var extension = Path.GetExtension(fileName);
        if (!MapManagerConstants.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return (false, $"Archive contains invalid file type: {extension}. Only .map, .tga, .ini, .str, and .txt files are allowed.");
        }

        if (extension.Equals(Path.GetExtension(MapManagerConstants.MapFilePattern), StringComparison.OrdinalIgnoreCase))
        {
            directoriesWithMaps.Add(separatorCount == 1 ? segments[0] : string.Empty);
        }

        return (true, null);
    }

    private static (bool IsValid, string? ErrorMessage) ValidateDirectoryMapCoverage(
        IEnumerable<IArchiveEntry> entries,
        HashSet<string> directoriesWithMaps)
    {
        var allDirectories = entries
            .Where(e => (e.Key ?? string.Empty).Contains('/') || (e.Key ?? string.Empty).Contains('\\'))
            .Select(e => (e.Key ?? string.Empty).Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries)[0])
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var missingDirectory = allDirectories.FirstOrDefault(dir => !directoriesWithMaps.Contains(dir));
        if (missingDirectory != null)
        {
            return (false, $"Directory '{missingDirectory}' does not contain a .map file. Each directory must have at least one .map file.");
        }

        return (true, null);
    }

    private static async Task<long> ExtractEntryWithSharpCompressAsync(
        string archivePath,
        string entryFullName,
        string destinationPath,
        long maxEntryBytes,
        long remainingAggregateBytes,
        CancellationToken ct)
    {
        using var archive = ArchiveFactory.OpenArchive(archivePath);
        var normalizedEntryFullName = entryFullName.Replace('\\', '/');
        var entry = archive.Entries.FirstOrDefault(e =>
            !e.IsDirectory &&
            (string.Equals(e.Key?.Replace('\\', '/'), normalizedEntryFullName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(Path.GetFileName(e.Key), Path.GetFileName(entryFullName), StringComparison.OrdinalIgnoreCase)));

        if (entry == null)
        {
            return 0;
        }

        using var entryStream = entry.OpenEntryStream();
        return await BoundedArchiveExtractor.CopyEntryToFileAsync(
            entryStream,
            destinationPath,
            entry.Key ?? entryFullName,
            maxEntryBytes,
            remainingAggregateBytes,
            overwrite: true,
            cancellationToken: ct);
    }

    private static async Task<string?> ExtractSharpCompressAssetsAsync(
        List<IArchiveEntry> entries,
        string mapDirPath,
        List<string> assetFiles,
        long currentTotalExpandedBytes,
        Action<long> onAssetBytesExpanded,
        CancellationToken ct)
    {
        string? thumbnailPath = null;
        var assetEntries = entries.Where(e =>
        {
            var fn = Path.GetFileName(e.Key ?? string.Empty);
            return !fn.EndsWith(".map", StringComparison.OrdinalIgnoreCase) &&
                   MapManagerConstants.AllowedExtensions.Contains(Path.GetExtension(fn), StringComparer.OrdinalIgnoreCase);
        });

        foreach (var assetEntry in assetEntries)
        {
            var assetFileName = Path.GetFileName(assetEntry.Key ?? string.Empty);
            var assetDestPath = Path.Combine(mapDirPath, assetFileName);
            if (!File.Exists(assetDestPath))
            {
                using var assetStream = assetEntry.OpenEntryStream();
                var written = await BoundedArchiveExtractor.CopyEntryToFileAsync(
                    assetStream,
                    assetDestPath,
                    assetEntry.Key ?? assetFileName,
                    MapManagerConstants.MaxAssetSizeBytes,
                    MapManagerConstants.MaxAggregateUncompressedBytes - currentTotalExpandedBytes,
                    cancellationToken: ct);
                onAssetBytesExpanded(written);
            }

            assetFiles.Add(assetDestPath);

            if (assetFileName.Equals(MapManagerConstants.DefaultThumbnailName, StringComparison.OrdinalIgnoreCase) ||
                (thumbnailPath == null && assetFileName.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)))
            {
                thumbnailPath = assetDestPath;
            }
        }

        return thumbnailPath;
    }

    private static string ExtractFileName(Uri uri, HttpResponseMessage response)
    {
        var rawName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName;

        if (!string.IsNullOrWhiteSpace(rawName))
        {
            var trimmed = rawName.Trim('"', '\'');
            var fileName = Path.GetFileName(trimmed);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }

        try
        {
            var localName = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(localName))
            {
                return localName;
            }
        }
        catch
        {
            // fallback below
        }

        return $"map_{Guid.NewGuid():N}.zip";
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup
        }
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

    private (bool IsValid, string? ErrorMessage) ValidateWithSharpCompress(string archivePath)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            var entries = archive.Entries.Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key)).ToList();

            if (entries.Count == 0)
            {
                return (false, "Archive is empty");
            }

            if (entries.Count > MapManagerConstants.MaxZipEntries)
            {
                return (false, $"Archive contains too many entries ({entries.Count} > {MapManagerConstants.MaxZipEntries}).");
            }

            long totalUncompressedBytes = 0;
            var directoriesWithMaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                var (isValid, errorMessage) = ValidateSharpCompressEntry(entry, ref totalUncompressedBytes, directoriesWithMaps);
                if (!isValid)
                {
                    return (false, errorMessage);
                }
            }

            return ValidateDirectoryMapCoverage(entries, directoriesWithMaps);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SharpCompress failed to validate archive: {ArchivePath}", archivePath);
            return (false, $"Failed to read archive: {ex.Message}");
        }
    }

    private async Task<ImportResult> ImportWithSharpCompressAsync(
        string archivePath,
        GameType targetVersion,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var result = new ImportResult();
        var targetDir = directoryService.GetMapDirectory(targetVersion);
        directoryService.EnsureDirectoryExists(targetVersion);

        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            var allEntries = archive.Entries.Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key)).ToList();

            var entriesByDirectory = allEntries
                .GroupBy(e =>
                {
                    var parts = (e.Key ?? string.Empty).Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
                    return parts.Length > 1 ? parts[0] : string.Empty;
                })
                .ToDictionary(g => g.Key, g => g.ToList());

            int totalMaps = entriesByDirectory.Sum(group =>
                group.Value.Count(e => Path.GetFileName(e.Key ?? string.Empty).EndsWith(".map", StringComparison.OrdinalIgnoreCase)));
            int processedMaps = 0;
            long expandedBytes = 0;

            var extractionContext = new SharpCompressExtractionContext(
                archivePath,
                targetDir,
                targetVersion,
                result,
                bytes => expandedBytes += bytes,
                ct);

            foreach (var (directoryName, entries) in entriesByDirectory)
            {
                var mapEntries = entries.Where(e => Path.GetFileName(e.Key ?? string.Empty).EndsWith(".map", StringComparison.OrdinalIgnoreCase)).ToList();
                if (mapEntries.Count == 0)
                {
                    continue;
                }

                foreach (var mapEntry in mapEntries)
                {
                    ct.ThrowIfCancellationRequested();

                    var mapFile = await ExtractSharpCompressMapAsync(
                        mapEntry,
                        entries,
                        directoryName,
                        extractionContext,
                        expandedBytes);

                    if (mapFile != null)
                    {
                        result.FilesImported++;
                        processedMaps++;
                        progress?.Report((double)processedMaps / Math.Max(1, totalMaps));
                        result.ImportedMaps.Add(mapFile);
                    }
                }
            }

            progress?.Report(1.0);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Import from archive was cancelled: {ArchivePath}", archivePath);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to import from archive: {ArchivePath}", archivePath);
            result.Errors.Add($"Archive extraction failed: {ex.Message}");
        }

        result.Success = result.FilesImported > 0;
        return result;
    }

    private async Task<MapFile?> ExtractSharpCompressMapAsync(
        IArchiveEntry mapEntry,
        List<IArchiveEntry> entries,
        string directoryName,
        SharpCompressExtractionContext context,
        long currentExpandedBytes)
    {
        var mapFileName = Path.GetFileName(mapEntry.Key ?? string.Empty);
        if (mapEntry.Size > IMapImportService.MaxMapSizeBytes)
        {
            context.Result.Errors.Add($"Map too large: {mapFileName}");
            return null;
        }

        var mapDirName = string.IsNullOrEmpty(directoryName)
            ? Path.GetFileNameWithoutExtension(mapFileName)
            : Path.GetFileName(directoryName);

        if (string.IsNullOrWhiteSpace(mapDirName) || mapDirName == "." || mapDirName == "..")
        {
            mapDirName = Path.GetFileNameWithoutExtension(mapFileName);
        }

        var mapDirPath = GetUniqueDirectoryPath(Path.Combine(context.TargetDir, mapDirName));
        var mapDestPath = Path.Combine(mapDirPath, mapFileName);
        var assetFiles = new List<string>();
        string? thumbnailPath = null;
        long mapExpandedBytes = 0;

        try
        {
            Directory.CreateDirectory(mapDirPath);

            using (var mapStream = mapEntry.OpenEntryStream())
            {
                mapExpandedBytes += await BoundedArchiveExtractor.CopyEntryToFileAsync(
                    mapStream,
                    mapDestPath,
                    mapEntry.Key ?? mapFileName,
                    IMapImportService.MaxMapSizeBytes,
                    MapManagerConstants.MaxAggregateUncompressedBytes - currentExpandedBytes - mapExpandedBytes,
                    cancellationToken: context.CancellationToken);
            }

            if (!string.IsNullOrEmpty(directoryName))
            {
                thumbnailPath = await ExtractSharpCompressAssetsAsync(
                    entries,
                    mapDirPath,
                    assetFiles,
                    currentExpandedBytes + mapExpandedBytes,
                    bytes => mapExpandedBytes += bytes,
                    context.CancellationToken);
            }

            context.OnBytesExpanded(mapExpandedBytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Discarding map {Entry} from {ArchivePath}: {Reason}", mapEntry.Key, context.ArchivePath, ex.Message);
            context.Result.Errors.Add(ex.Message);
            DeleteDirectoryBestEffort(mapDirPath);
            return null;
        }

        var totalSize = new FileInfo(mapDestPath).Length + assetFiles.Sum(f => new FileInfo(f).Length);
        logger.LogInformation("Extracted map to directory: {DirectoryName}/{FileName}", mapDirName, mapFileName);

        var displayName = mapNameParser.ParseMapName(mapDestPath);
        return new MapFile
        {
            FileName = mapFileName,
            FullPath = mapDestPath,
            SizeBytes = totalSize,
            GameType = context.TargetVersion,
            LastModified = File.GetLastWriteTime(mapDestPath),
            DirectoryName = Path.GetFileName(mapDirPath),
            IsDirectory = true,
            AssetFiles = assetFiles,
            DisplayName = displayName,
            ThumbnailPath = thumbnailPath,
            ThumbnailBitmap = null,
        };
    }
}
