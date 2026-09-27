using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Tools.MapManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.MapManager;
using GenHub.Infrastructure.Imaging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.MapManager.Services;

/// <summary>
/// Implementation of <see cref="IMapDirectoryService"/> for managing map directories.
/// </summary>
public sealed class MapDirectoryService(
    MapNameParser mapNameParser,
    ILogger<MapDirectoryService> logger,
    IGamePathProvider? pathProvider = null,
    ILocalizationService? localizationService = null) : IMapDirectoryService
{
    private const string GeneralsMapFolder = MapManagerConstants.GeneralsDataDirectoryName;
    private const string ZeroHourMapFolder = MapManagerConstants.ZeroHourDataDirectoryName;
    private const string MapSubfolder = MapManagerConstants.MapsSubdirectoryName;

    /// <inheritdoc />
    public string GetMapDirectory(GameType version)
    {
        if (version is not (GameType.Generals or GameType.ZeroHour))
        {
            throw new ArgumentException("Unsupported game version", nameof(version));
        }

        if (pathProvider is not null)
        {
            return Path.Combine(pathProvider.GetOptionsDirectory(version), MapSubfolder);
        }

        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var gameFolder = version == GameType.Generals ? GeneralsMapFolder : ZeroHourMapFolder;
        return Path.Combine(documentsPath, gameFolder, MapSubfolder);
    }

    /// <inheritdoc />
    public void EnsureDirectoryExists(GameType version)
    {
        var directory = GetMapDirectory(version);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            logger.LogInformation("Created map directory: {Directory}", directory);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MapFile>> GetMapsAsync(GameType version, CancellationToken ct = default)
    {
        var directory = GetMapDirectory(version);
        EnsureDirectoryExists(version);

        return await Task.Run(
            () =>
            {
                var mapFiles = new List<MapFile>();
                var processedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Find all .map files recursively
                var allMapFiles = Directory.GetFiles(directory, MapManagerConstants.MapFilePattern, SearchOption.AllDirectories);

                foreach (var mapFilePath in allMapFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var fileInfo = new FileInfo(mapFilePath);
                        var parentDir = fileInfo.Directory?.FullName ?? string.Empty;

                        // Check if this .map file is in a subdirectory
                        var isInSubdirectory = !string.Equals(parentDir, directory, StringComparison.OrdinalIgnoreCase);

                        if (isInSubdirectory && !processedDirectories.Contains(parentDir))
                        {
                            // This is a directory-based map - process the entire directory
                            processedDirectories.Add(parentDir);

                            var dirInfo = new DirectoryInfo(parentDir);
                            var allFilesInDir = dirInfo.GetFiles();
                            var mapFilesInDir = allFilesInDir.Where(f => f.Extension.Equals(Path.GetExtension(MapManagerConstants.MapFilePattern), StringComparison.OrdinalIgnoreCase)).ToList();

                            if (mapFilesInDir.Count == 0)
                                continue;

                            // Use the first .map file as the primary
                            var primaryMap = mapFilesInDir[0];
                            var assetFiles = allFilesInDir
                                .Where(f => !f.Extension.Equals(Path.GetExtension(MapManagerConstants.MapFilePattern), StringComparison.OrdinalIgnoreCase))
                                .Where(f => IsValidAssetFile(f.Extension))
                                .Select(f => f.FullName)
                                .ToList();

                            var totalSize = allFilesInDir.Sum(f => f.Length);

                            // Find thumbnail TGA file
                            var thumbnailPath = FindThumbnail(allFilesInDir);

                            // Parse display name and player count in a single pass
                            var (displayName, playerCount) = mapNameParser.ParseMapDetails(primaryMap.FullName, ct);

                            mapFiles.Add(new MapFile
                            {
                                FileName = primaryMap.Name,
                                FullPath = primaryMap.FullName,
                                SizeBytes = totalSize,
                                GameType = version,
                                LastModified = dirInfo.LastWriteTime,
                                DirectoryName = dirInfo.Name,
                                IsDirectory = true,
                                AssetFiles = assetFiles,
                                IsExpanded = false,
                                DisplayName = displayName,
                                ThumbnailPath = thumbnailPath,
                                ThumbnailBitmap = null, // Loaded lazily in ViewModel
                                PlayerCount = playerCount,
                            });
                        }
                        else if (!isInSubdirectory)
                        {
                            // This is a standalone .map file in the root Maps directory
                            var (displayName, playerCount) = mapNameParser.ParseMapDetails(fileInfo.FullName, ct);

                            mapFiles.Add(new MapFile
                            {
                                FileName = fileInfo.Name,
                                FullPath = fileInfo.FullName,
                                SizeBytes = fileInfo.Length,
                                GameType = version,
                                LastModified = fileInfo.LastWriteTime,
                                DirectoryName = null,
                                IsDirectory = false,
                                AssetFiles = new List<string>(),
                                IsExpanded = false,
                                DisplayName = displayName,
                                ThumbnailPath = null,
                                ThumbnailBitmap = null,
                                PlayerCount = playerCount,
                            });
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to read map file: {File}", mapFilePath);
                    }
                }

                // Also scan for ZIP files in the root directory
                try
                {
                    var zipFiles = Directory.GetFiles(directory, MapManagerConstants.ZipFilePattern, SearchOption.TopDirectoryOnly);
                    foreach (var zipPath in zipFiles)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            var fileInfo = new FileInfo(zipPath);
                            var playerCount = MapNameParser.ExtractPlayerCountFromString(fileInfo.Name);
                            mapFiles.Add(new MapFile
                            {
                                FileName = fileInfo.Name,
                                FullPath = fileInfo.FullName,
                                SizeBytes = fileInfo.Length,
                                GameType = version,
                                LastModified = fileInfo.LastWriteTime,
                                DirectoryName = null,
                                IsDirectory = false,
                                AssetFiles = new List<string>(),
                                IsExpanded = false,
                                DisplayName = fileInfo.Name, // Use filename for ZIPs
                                ThumbnailPath = null,
                                ThumbnailBitmap = null,
                                PlayerCount = playerCount,
                            });
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to read zip file: {File}", zipPath);
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to scan for zip files");
                }

                logger.LogDebug("Found {Count} maps for {GameType}", mapFiles.Count, version);
                return mapFiles;
            },
            ct);
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteMapsAsync(IEnumerable<MapFile> maps, CancellationToken ct = default)
    {
        return await Task.Run(
            () =>
            {
                foreach (var map in maps)
                {
                    ct.ThrowIfCancellationRequested();

                    if (DeleteSingleMap(map) is { } failure)
                    {
                        return failure;
                    }
                }

                return OperationResult.CreateSuccess();
            },
            ct);
    }

    /// <inheritdoc />
    public void OpenInExplorer(GameType version)
    {
        var directory = GetMapDirectory(version);
        EnsureDirectoryExists(version);

        try
        {
            PathHelper.OpenInExplorer(directory);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open map directory in Explorer: {Directory}", directory);
        }
    }

    /// <inheritdoc />
    public void RevealInExplorer(MapFile map)
    {
        try
        {
            PathHelper.RevealInExplorer(map.FullPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reveal map in Explorer: {FileName}", map.FileName);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> RenameMapAsync(MapFile map, string newName, CancellationToken ct = default)
    {
        var renameFailed = OperationResult.CreateFailure(
            Localize(MapManagerConstants.RenameFailedMessageKey, MapManagerConstants.RenameFailedFallbackMessage, GetMapName(map)));

        if (string.IsNullOrWhiteSpace(newName))
        {
            return renameFailed;
        }

        // Validate name for illegal characters
        var invalidChars = Path.GetInvalidFileNameChars();
        if (newName.IndexOfAny(invalidChars) >= 0)
        {
            logger.LogWarning("Invalid characters in map name: {Name}", newName);
            return renameFailed;
        }

        return await Task.Run(
            () =>
            {
                try
                {
                    return map.IsDirectory
                        ? RenameMapDirectory(map, newName, renameFailed)
                        : RenameStandaloneMapFile(map, newName, renameFailed);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to rename map: {FileName}", map.FileName);
                    return renameFailed;
                }
            },
            ct);
    }

    /// <summary>
    /// Plans companion asset moves matching a single base name.
    /// </summary>
    /// <param name="currentDirPath">The current directory path.</param>
    /// <param name="baseName">The asset base name to match.</param>
    /// <param name="newName">The new map name.</param>
    /// <param name="seenTargets">Targets already planned, to avoid collisions.</param>
    /// <param name="plannedMoves">The planned moves to append to.</param>
    private static void PlanAssetsForBaseName(string currentDirPath, string baseName, string newName, HashSet<string> seenTargets, List<(string Source, string Target)> plannedMoves)
    {
        foreach (var assetPath in Directory.GetFiles(currentDirPath, baseName + ".*"))
        {
            if (!Path.GetFileNameWithoutExtension(assetPath).Equals(baseName, PathHelper.PathComparison))
            {
                continue;
            }

            var ext = Path.GetExtension(assetPath);
            if (ext.Equals(".map", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var newAssetPath = Path.Combine(currentDirPath, newName + ext);
            if (!seenTargets.Contains(newAssetPath) && !File.Exists(newAssetPath))
            {
                seenTargets.Add(newAssetPath);
                plannedMoves.Add((assetPath, newAssetPath));
            }
        }
    }

    /// <summary>
    /// Rolls back executed file moves, ignoring rollback failures.
    /// </summary>
    /// <param name="executedMoves">The executed moves to roll back.</param>
    private static void RollbackExecutedMoves(List<(string Source, string Target)> executedMoves)
    {
        foreach (var (src, dst) in executedMoves)
        {
            try
            {
                if (File.Exists(dst) && !File.Exists(src))
                {
                    File.Move(dst, src);
                }
            }
            catch
            {
                // Ignore rollback failures
            }
        }
    }

    /// <summary>
    /// Finds the best thumbnail file in a map directory.
    /// </summary>
    /// <param name="files">Files in the map directory.</param>
    /// <returns>Path to the thumbnail file, or null if none found.</returns>
    private static string? FindThumbnail(FileInfo[] files)
    {
        if (files.Length == 0)
        {
            return null;
        }

        var dirName = files[0].Directory?.Name;
        if (!string.IsNullOrEmpty(dirName))
        {
            var dirTga = files.FirstOrDefault(f => f.Name.Equals(dirName + ".tga", StringComparison.OrdinalIgnoreCase));
            if (dirTga != null)
            {
                return dirTga.FullName;
            }
        }

        // Priority: <dirName>.tga > map.tga > any .tga file
        var mapTga = files.FirstOrDefault(f => f.Name.Equals(MapManagerConstants.DefaultThumbnailName, StringComparison.OrdinalIgnoreCase));
        if (mapTga != null)
        {
            return mapTga.FullName;
        }

        var anyTga = files.FirstOrDefault(f => f.Extension.Equals(".tga", StringComparison.OrdinalIgnoreCase));
        return anyTga?.FullName;
    }

    private static string GetMapName(MapFile map) =>
        string.IsNullOrEmpty(map.DirectoryName) ? map.FileName : map.DirectoryName;

    private static bool IsValidAssetFile(string extension) =>
        MapManagerConstants.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Renames a map directory including its .map file and companion assets.
    /// </summary>
    /// <param name="map">The directory map to rename.</param>
    /// <param name="newName">The new map name.</param>
    /// <param name="renameFailed">The prebuilt failure result.</param>
    /// <returns>The rename result.</returns>
    private OperationResult RenameMapDirectory(MapFile map, string newName, OperationResult renameFailed)
    {
        var currentDirPath = Path.GetDirectoryName(map.FullPath);
        if (string.IsNullOrEmpty(currentDirPath))
        {
            return renameFailed;
        }

        var parentPath = Path.GetDirectoryName(currentDirPath);
        if (string.IsNullOrEmpty(parentPath))
        {
            return renameFailed;
        }

        var newDirPath = Path.Combine(parentPath, newName);

        // Check if target directory already exists
        if (Directory.Exists(newDirPath))
        {
            logger.LogWarning("Target directory already exists: {Path}", newDirPath);
            return renameFailed;
        }

        if (EnsureMapFolderWritable(currentDirPath) is { } accessFailure)
        {
            return accessFailure;
        }

        var plannedMoves = PlanDirectoryRenameMoves(map, currentDirPath, newName);
        if (plannedMoves == null)
        {
            return renameFailed;
        }

        return ExecuteDirectoryRename(plannedMoves, currentDirPath, newDirPath, map, newName);
    }

    /// <summary>
    /// Plans the .map file and companion asset moves for a directory rename, with preflight checks.
    /// </summary>
    /// <param name="map">The directory map to rename.</param>
    /// <param name="currentDirPath">The current directory path.</param>
    /// <param name="newName">The new map name.</param>
    /// <returns>The planned moves, or null when a preflight check failed.</returns>
    private List<(string Source, string Target)>? PlanDirectoryRenameMoves(MapFile map, string currentDirPath, string newName)
    {
        var plannedMoves = new List<(string Source, string Target)>();

        // Preflight and plan .map file rename
        var newMapFilePath = Path.Combine(currentDirPath, newName + ".map");
        if (!string.Equals(map.FullPath, newMapFilePath, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(newMapFilePath))
            {
                logger.LogWarning("Target map file already exists: {Path}", newMapFilePath);
                return null;
            }

            plannedMoves.Add((map.FullPath, newMapFilePath));
        }

        // Also plan companion asset files (e.g. OldName.tga -> NewName.tga, OldName.ini -> NewName.ini)
        // Assets may match either the old map file base name or the old directory name
        // Ordered: map file base name takes priority over directory name.
        var candidateBases = new List<string> { Path.GetFileNameWithoutExtension(map.FileName) };
        if (!string.IsNullOrEmpty(map.DirectoryName) &&
            !candidateBases.Contains(map.DirectoryName, PathHelper.PathComparer))
        {
            candidateBases.Add(map.DirectoryName);
        }

        var seenTargets = new HashSet<string>(PathHelper.PathComparer) { newMapFilePath };
        foreach (var baseName in candidateBases)
        {
            PlanAssetsForBaseName(currentDirPath, baseName, newName, seenTargets, plannedMoves);
        }

        return plannedMoves;
    }

    /// <summary>
    /// Executes planned file moves and the directory rename, rolling back file moves on failure.
    /// </summary>
    /// <param name="plannedMoves">The planned file moves.</param>
    /// <param name="currentDirPath">The current directory path.</param>
    /// <param name="newDirPath">The target directory path.</param>
    /// <param name="map">The directory map being renamed.</param>
    /// <param name="newName">The new map name.</param>
    /// <returns>The rename result.</returns>
    private OperationResult ExecuteDirectoryRename(List<(string Source, string Target)> plannedMoves, string currentDirPath, string newDirPath, MapFile map, string newName)
    {
        var executedMoves = new List<(string Source, string Target)>();
        try
        {
            foreach (var (src, dst) in plannedMoves)
            {
                File.Move(src, dst);
                executedMoves.Add((src, dst));
                logger.LogDebug("Renamed companion asset {Old} to {New}", src, dst);
            }

            // Then rename the directory
            Directory.Move(currentDirPath, newDirPath);
            logger.LogInformation("Renamed map directory from {OldName} to {NewName}", map.DirectoryName, newName);
            return OperationResult.CreateSuccess();
        }
        catch
        {
            RollbackExecutedMoves(executedMoves);
            throw;
        }
    }

    /// <summary>
    /// Renames a standalone .map file.
    /// </summary>
    /// <param name="map">The map file to rename.</param>
    /// <param name="newName">The new map name.</param>
    /// <param name="renameFailed">The prebuilt failure result.</param>
    /// <returns>The rename result.</returns>
    private OperationResult RenameStandaloneMapFile(MapFile map, string newName, OperationResult renameFailed)
    {
        var directory = Path.GetDirectoryName(map.FullPath);
        if (string.IsNullOrEmpty(directory))
        {
            return renameFailed;
        }

        var newFileName = newName + ".map";
        var newFilePath = Path.Combine(directory, newFileName);

        if (File.Exists(newFilePath))
        {
            logger.LogWarning("Target file already exists: {Path}", newFilePath);
            return renameFailed;
        }

        File.Move(map.FullPath, newFilePath);
        logger.LogInformation("Renamed map from {OldName} to {NewName}", map.FileName, newFileName);
        return OperationResult.CreateSuccess();
    }

    /// <summary>
    /// Deletes a single map file or directory.
    /// </summary>
    /// <param name="map">The map to delete.</param>
    /// <returns>The failure result, or null when deletion succeeded.</returns>
    private OperationResult? DeleteSingleMap(MapFile map)
    {
        try
        {
            if (map.IsDirectory)
            {
                return DeleteMapDirectory(map);
            }

            if (File.Exists(map.FullPath))
            {
                WriteAccessHelper.EnsureFileWritable(map.FullPath);
                File.Delete(map.FullPath);
                logger.LogInformation("Deleted map: {FileName}", map.FileName);
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete map: {FileName}", map.FileName);
            return OperationResult.CreateFailure(
                Localize(MapManagerConstants.DeleteFailedMessageKey, MapManagerConstants.DeleteFailedFallbackMessage, GetMapName(map)));
        }
    }

    /// <summary>
    /// Deletes a map directory and its contents.
    /// </summary>
    /// <param name="map">The directory map to delete.</param>
    /// <returns>The failure result, or null when deletion succeeded.</returns>
    private OperationResult? DeleteMapDirectory(MapFile map)
    {
        var dirPath = Path.GetDirectoryName(map.FullPath);
        if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
        {
            return null;
        }

        if (EnsureMapFolderWritable(dirPath) is { } accessFailure)
        {
            return accessFailure;
        }

        Directory.Delete(dirPath, true);
        logger.LogInformation("Deleted map directory: {DirectoryName}", map.DirectoryName);
        return null;
    }

    private OperationResult? EnsureMapFolderWritable(string folderPath)
    {
        try
        {
            WriteAccessHelper.EnsureDirectoryWritable(folderPath);
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger.LogError(ex, "Failed to make map folder writable: {Folder}", folderPath);
            return OperationResult.CreateFailure(
                Localize(MapManagerConstants.FolderNotWritableMessageKey, MapManagerConstants.FolderNotWritableFallbackMessage, folderPath));
        }
    }

    private string Localize(string key, string fallback, params object?[] arguments) =>
        localizationService != null && localizationService.TryGetString(key, out var localized, arguments)
            ? localized
            : string.Format(CultureInfo.CurrentCulture, fallback, arguments);
}
