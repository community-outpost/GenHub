using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools.MapManager;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.MapManager;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.MapManager.Services;

/// <summary>
/// Implementation of <see cref="IMapPackService"/> for managing MapPacks.
/// NOTE: MapPacks store metadata only. The actual map file activation is handled
/// by the userdata system (IProfileContentLinker) when profiles are launched.
/// </summary>
public sealed class MapPackService : IMapPackService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var subManager in Directory.GetDirectories(sourceDir))
        {
            var destSub = Path.Combine(destDir, Path.GetFileName(subManager));
            CopyDirectory(subManager, destSub);
        }
    }

    private readonly IAppConfiguration _appConfig;
    private readonly ILocalContentService _localContentService;
    private readonly IContentManifestPool _manifestPool;
    private readonly ILogger<MapPackService> _logger;
    private readonly string _mapPacksDirectory;
    private readonly object _fileLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MapPackService"/> class.
    /// </summary>
    /// <param name="appConfig">The application configuration.</param>
    /// <param name="localContentService">Provides operations for local content manifest creation.</param>
    /// <param name="manifestPool">Pool for content manifests and CAS operations.</param>
    /// <param name="logger">Logger instance.</param>
    public MapPackService(
        IAppConfiguration appConfig,
        ILocalContentService localContentService,
        IContentManifestPool manifestPool,
        ILogger<MapPackService> logger)
    {
        _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        _localContentService = localContentService ?? throw new ArgumentNullException(nameof(localContentService));
        _manifestPool = manifestPool ?? throw new ArgumentNullException(nameof(manifestPool));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mapPacksDirectory = Path.Combine(_appConfig.GetConfiguredDataPath(), MapManagerConstants.MapPacksSubdirectoryName);
    }

    /// <inheritdoc />
    public Task<MapPack> CreateMapPackAsync(
        string name,
        Guid? profileId,
        IEnumerable<string> mapFilePaths,
        CancellationToken ct = default)
    {
        // Legacy method - we should probably discourage its use or map it to CAS
        var mapPack = new MapPack
        {
            Id = ManifestId.Create($"1.0.local.mappack.{name.ToLowerInvariant().Replace(" ", "-")}"),
            Name = name,
            ProfileId = profileId,
            MapFilePaths = mapFilePaths.ToList(),
            CreatedDate = DateTime.UtcNow,
            IsLoaded = false,
        };

        SaveMapPack(mapPack);
        _logger.LogInformation("Created Legacy MapPack: {Name} with {Count} maps", name, mapPack.MapFilePaths.Count);

        return Task.FromResult(mapPack);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> CreateCasMapPackAsync(
        string name,
        GameType targetGame,
        IEnumerable<MapFile> selectedMaps,
        IProgress<ContentStorageProgress>? progress = null,
        CancellationToken ct = default)
    {
        // Create a temporary directory
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHub_MapPack_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Copy each map directory to the temp directory
            foreach (var map in selectedMaps)
            {
                var sourcePath = map.FullPath;
                if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                {
                    _logger.LogWarning("Skipping map {Map} because file not found at {Path}", map.FileName, sourcePath);
                    continue;
                }

                if (map.IsDirectory)
                {
                    var sourceDir = Path.GetDirectoryName(sourcePath);
                    if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
                    {
                        _logger.LogWarning("Skipping map {Map} because directory not found", map.FileName);
                        continue;
                    }

                    var dirName = new DirectoryInfo(sourceDir).Name;
                    var destDir = Path.Combine(tempDir, dirName);

                    // Copy directory recursively
                    CopyDirectory(sourceDir, destDir);
                }
                else
                {
                    // Standalone map file - create a directory for it as per game requirements
                    // Maps/MapName/MapName.map
                    var mapNameWithoutExt = Path.GetFileNameWithoutExtension(map.FileName);
                    var destDir = Path.Combine(tempDir, mapNameWithoutExt);
                    Directory.CreateDirectory(destDir);
                    File.Copy(sourcePath, Path.Combine(destDir, map.FileName), true);
                }
            }

            // Create manifest using LocalContentService
            // The ContentManifestBuilder now automatically sets InstallTarget to UserMapsDirectory
            // for ContentType.MapPack, complying with userdata.md.
            var result = await _localContentService.CreateLocalContentManifestAsync(
                tempDir,
                name,
                ContentType.MapPack,
                targetGame,
                progress,
                ct);

            return result;
        }
        finally
        {
            // Cleanup temp
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up temp directory {Dir}", tempDir);
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MapPack>> GetAllMapPacksAsync(CancellationToken ct = default)
    {
        var result = await _manifestPool.GetAllManifestsAsync(ct);
        var mapPacks = new List<MapPack>();

        if (result.Success)
        {
            mapPacks.AddRange(result.Data
                .Where(m => m.ContentType == ContentType.MapPack)
                .Select(m => new MapPack
                {
                    Id = m.Id,
                    Name = m.Name,
                    MapFilePaths = m.Files.Select(f => f.RelativePath).ToList(),
                    CreatedDate = m.Metadata.ReleaseDate,
                    IsLoaded = false, // Managed by Profile system
                }));
        }

        // Also load legacy JSON MapPacks if any exist
        EnsureDirectoryExists();
        lock (_fileLock)
        {
            var files = Directory.GetFiles(_mapPacksDirectory, FileTypes.JsonFilePattern);
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var mapPack = JsonSerializer.Deserialize<MapPack>(json, _jsonOptions);
                    if (mapPack != null && !mapPacks.Any(p => p.Id == mapPack.Id))
                    {
                        mapPacks.Add(mapPack);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load legacy MapPack from {File}", file);
                }
            }
        }

        return mapPacks;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MapPack>> GetMapPacksForProfileAsync(Guid profileId, CancellationToken ct = default)
    {
        var allPacks = await GetAllMapPacksAsync();
        return allPacks.Where(p => p.ProfileId == profileId).ToList();
    }

    /// <inheritdoc />
    public Task<bool> LoadMapPackAsync(ManifestId mapPackId, CancellationToken ct = default)
    {
        lock (_fileLock)
        {
            var mapPack = LoadMapPackById(mapPackId);
            if (mapPack == null)
            {
                _logger.LogWarning("MapPack not found: {Id}", mapPackId);
                return Task.FromResult(false);
            }

            // Mark as loaded - the actual map activation is handled by the userdata system
            // when the profile is launched/switched
            mapPack.IsLoaded = true;
            SaveMapPack(mapPack);

            _logger.LogInformation("Loaded MapPack: {Name}", mapPack.Name);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> UnloadMapPackAsync(ManifestId mapPackId, CancellationToken ct = default)
    {
        lock (_fileLock)
        {
            var mapPack = LoadMapPackById(mapPackId);
            if (mapPack == null)
            {
                _logger.LogWarning("MapPack not found: {Id}", mapPackId);
                return Task.FromResult(false);
            }

            // Mark as unloaded - the userdata system will handle removing maps
            // on the next profile switch
            mapPack.IsLoaded = false;
            SaveMapPack(mapPack);

            _logger.LogInformation("Unloaded MapPack: {Name}", mapPack.Name);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteMapPackAsync(ManifestId mapPackId, CancellationToken ct = default)
    {
        // Try to delete from CAS pool first
        var casResult = await _manifestPool.RemoveManifestAsync(mapPackId);

        lock (_fileLock)
        {
            var filePath = GetMapPackFilePath(mapPackId);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Deleted legacy MapPack: {Id}", mapPackId);
                return true;
            }
        }

        if (casResult.Success && casResult.Data)
        {
            _logger.LogInformation("Deleted CAS MapPack: {Id}", mapPackId);
            return true;
        }

        _logger.LogWarning("MapPack not found for deletion: {Id}", mapPackId);
        return false;
    }

    /// <inheritdoc />
    public Task<bool> UpdateMapPackAsync(MapPack mapPack, CancellationToken ct = default)
    {
        lock (_fileLock)
        {
            SaveMapPack(mapPack);
            _logger.LogInformation("Updated MapPack: {Name}", mapPack.Name);
            return Task.FromResult(true);
        }
    }

    private void SaveMapPack(MapPack mapPack)
    {
        EnsureDirectoryExists();

        lock (_fileLock)
        {
            var filePath = GetMapPackFilePath(mapPack.Id);
            var json = JsonSerializer.Serialize(mapPack, _jsonOptions);
            File.WriteAllText(filePath, json);
        }
    }

    private MapPack? LoadMapPackById(ManifestId id)
    {
        var filePath = GetMapPackFilePath(id);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<MapPack>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load MapPack: {Id}", id);
            return null;
        }
    }

    private string GetMapPackFilePath(ManifestId id)
    {
        // Sanitize ID for filename
        var safeId = id.ToString().Replace(".", "_").Replace(":", "_");
        return Path.Combine(_mapPacksDirectory, $"{safeId}{FileTypes.JsonFileExtension}");
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_mapPacksDirectory))
        {
            Directory.CreateDirectory(_mapPacksDirectory);
        }
    }
}