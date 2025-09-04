using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Manifest;

/// <summary>
/// Persistent storage and management of acquired ContentManifests using the content storage service.
/// </summary>
public class ContentManifestPool(IContentStorageService storageService, ILogger<ContentManifestPool> logger) : IContentManifestPool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IContentStorageService _storageService = storageService;
    private readonly ILogger<ContentManifestPool> _logger = logger;

    /// <inheritdoc/>
    public async Task AddManifestAsync(ContentManifest manifest, string sourceDirectory, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Storing manifest {ManifestId} with content from {SourceDirectory}", manifest.Id, sourceDirectory);

        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory does not exist: {sourceDirectory}");
        }

        var result = await _storageService.StoreContentAsync(manifest, sourceDirectory, cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to store content for manifest {manifest.Id}: {result.ErrorMessage}");
        }

        _logger.LogDebug("Successfully stored manifest {ManifestId} in pool", manifest.Id);
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
        var manifestsDir = Path.Combine(_storageService.GetContentStorageRoot(), FileTypes.ManifestsDirectory);

        if (!Directory.Exists(manifestsDir))
            return manifests;

        var manifestFiles = Directory.GetFiles(manifestsDir, FileTypes.ManifestFilePattern);

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
        var result = await _storageService.IsContentStoredAsync(manifestId, cancellationToken);
        return result.Success && result.Data;
    }

    /// <summary>
    /// Gets the content directory path for a specific manifest.
    /// </summary>
    /// <param name="manifestId">The unique identifier of the manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The path to the content directory if it exists, null otherwise.</returns>
    public async Task<string?> GetContentDirectoryAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        var contentDir = _storageService.GetContentDirectoryPath(manifestId);
        return await Task.FromResult(Directory.Exists(contentDir) ? contentDir : null);
    }
}
