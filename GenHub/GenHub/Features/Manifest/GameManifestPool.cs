using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Manifest;

/// <summary>
/// Persistent storage and management of acquired GameManifests using the content storage service.
/// </summary>
public class GameManifestPool : IGameManifestPool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly IContentStorageService _storageService;
    private readonly ICasService _casService;
    private readonly ILogger<GameManifestPool> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameManifestPool"/> class.
    /// </summary>
    /// <param name="storageService">The content storage service.</param>
    /// <param name="casService">The Content-Addressable Storage service.</param>
    /// <param name="logger">The logger instance.</param>
    public GameManifestPool(IContentStorageService storageService, ICasService casService, ILogger<GameManifestPool> logger)
    {
        _storageService = storageService;
        _casService = casService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task AddManifestAsync(ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        var manifestPath = _storageService.GetManifestStoragePath(manifest.Id);
        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken);
        _logger.LogDebug("Manifest {ManifestId} metadata stored.", manifest.Id);
    }

    /// <summary>
    /// Adds a ContentManifest to the pool, storing its content files in CAS.
    /// </summary>
    /// <param name="manifest">The game manifest to store.</param>
    /// <param name="sourceDirectory">The directory containing the content files.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AddManifestAsync(ContentManifest manifest, string sourceDirectory, CancellationToken cancellationToken = default)
    {
        // CAS-backed content storage
        _logger.LogInformation("Adding manifest {ManifestId} to pool and storing content in CAS from {SourceDirectory}", manifest.Id, sourceDirectory);

        // Store content files in CAS and get an updated manifest
        var result = await _storageService.StoreContentAsync(manifest, sourceDirectory, cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to store content for manifest {manifest.Id}: {result.ErrorMessage}");
        }

        _logger.LogInformation("Successfully added manifest {ManifestId} to pool with content stored in CAS.", manifest.Id);
    }

    /// <inheritdoc/>
    public async Task<ContentManifest?> GetManifestAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        var manifestPath = _storageService.GetManifestStoragePath(manifestId);

        if (!File.Exists(manifestPath))
            return null;

        try
        {
            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            return JsonSerializer.Deserialize<ContentManifest>(manifestJson, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read manifest {ManifestId} from storage", manifestId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ContentManifest>> GetAllManifestsAsync(CancellationToken cancellationToken = default)
    {
        var manifests = new List<ContentManifest>();
        var manifestsDir = Path.Combine(_storageService.GetContentStorageRoot(), "Manifests");

        if (!Directory.Exists(manifestsDir))
            return manifests;

        var manifestFiles = Directory.GetFiles(manifestsDir, "*.manifest.json");

        foreach (var manifestFile in manifestFiles)
        {
            try
            {
                var manifestJson = await File.ReadAllTextAsync(manifestFile, cancellationToken);
                var manifest = JsonSerializer.Deserialize<ContentManifest>(manifestJson, JsonOptions);

                if (manifest != null)
                    manifests.Add(manifest);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read manifest from {ManifestFile}", manifestFile);
            }
        }

        return manifests;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ContentManifest>> SearchManifestsAsync(ContentSearchQuery query, CancellationToken cancellationToken = default)
    {
        var allManifests = await GetAllManifestsAsync(cancellationToken);

        return allManifests.Where(manifest =>
        {
            if (!string.IsNullOrWhiteSpace(query.SearchTerm) &&
                !manifest.Name.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) &&
                !manifest.Id.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase))
                return false;

            if (query.ContentType.HasValue && manifest.ContentType != query.ContentType.Value)
                return false;

            if (query.TargetGame.HasValue && manifest.TargetGame != query.TargetGame.Value)
                return false;

            return true;
        });
    }

    /// <inheritdoc/>
    public async Task RemoveManifestAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Removing manifest {ManifestId} from pool", manifestId);

        var result = await _storageService.RemoveContentAsync(manifestId, cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to remove content for manifest {manifestId}: {result.ErrorMessage}");
        }

        _logger.LogDebug("Successfully removed manifest {ManifestId} from pool", manifestId);
    }

    /// <inheritdoc/>
    public async Task<bool> IsManifestAcquiredAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        var manifestPath = _storageService.GetManifestStoragePath(manifestId);
        return await Task.FromResult(File.Exists(manifestPath));
    }

    /// <summary>
    /// Gets the content directory path for a specific manifest.
    /// Note: Legacy path; CAS-backed content is materialized on demand via workspace strategies.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The path to the content directory if it exists, null otherwise.</returns>
    public async Task<string?> GetContentDirectoryAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        // Legacy path; CAS-backed content is materialized on demand via workspace strategies.
        var contentDir = _storageService.GetContentDirectoryPath(manifestId);
        return await Task.FromResult(Directory.Exists(contentDir) ? contentDir : null);
    }
}
