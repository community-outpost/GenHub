using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Manifest;

/// <summary>
/// Persistent storage and management of acquired ContentManifests using the content storage service.
/// </summary>
public class ContentManifestPool : IContentManifestPool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IContentStorageService _storageService;
    private readonly ILogger<ContentManifestPool> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentManifestPool"/> class.
    /// </summary>
    /// <param name="storageService">The content storage service.</param>
    /// <param name="logger">The logger instance.</param>
    public ContentManifestPool(IContentStorageService storageService, ILogger<ContentManifestPool> logger)
    {
        _storageService = storageService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> AddManifestAsync(ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        try
        {
            var isStoredResult = await _storageService.IsContentStoredAsync(manifest.Id, cancellationToken);
            if (!isStoredResult.Success || !isStoredResult.Data)
            {
                return OperationResult<bool>.CreateFailure(
                    $"Cannot add manifest {manifest.Id} without source directory. Content must be stored first using AddManifestAsync(ContentManifest, string, CancellationToken).");
            }

            // Update the manifest metadata even if content already exists
            var manifestPath = _storageService.GetManifestStoragePath(manifest.Id);
            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken);

            _logger.LogDebug("Updated manifest {ManifestId} in storage", manifest.Id);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add manifest {ManifestId}", manifest.Id);
            return OperationResult<bool>.CreateFailure($"Failed to add manifest: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a ContentManifest to the pool with its content files from a source directory.
    /// </summary>
    /// <param name="manifest">The game manifest to store.</param>
    /// <param name="sourceDirectory">The directory containing the content files.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task<OperationResult<bool>> AddManifestAsync(ContentManifest manifest, string sourceDirectory, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Adding manifest {ManifestId} to pool with content from {SourceDirectory}", manifest.Id, sourceDirectory);

            var result = await _storageService.StoreContentAsync(manifest, sourceDirectory, cancellationToken);
            if (!result.Success)
            {
                return OperationResult<bool>.CreateFailure($"Failed to store content for manifest {manifest.Id}: {result.FirstError}");
            }

            _logger.LogDebug("Successfully added manifest {ManifestId} to pool", manifest.Id);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add manifest {ManifestId} with source directory", manifest.Id);
            return OperationResult<bool>.CreateFailure($"Failed to add manifest: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<ContentManifest?>> GetManifestAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        try
        {
            var manifestPath = _storageService.GetManifestStoragePath(manifestId);

            if (!File.Exists(manifestPath))
                return OperationResult<ContentManifest?>.CreateSuccess(null);

            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<ContentManifest>(manifestJson, JsonOptions);
            if (manifest == null)
            {
                _logger.LogWarning("Manifest file {ManifestPath} exists but deserialization returned null", manifestPath);
                return OperationResult<ContentManifest?>.CreateFailure("Manifest file is corrupted or invalid");
            }

            return OperationResult<ContentManifest?>.CreateSuccess(manifest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read manifest {ManifestId} from storage", manifestId);
            return OperationResult<ContentManifest?>.CreateFailure($"Failed to read manifest: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<IEnumerable<ContentManifest>>> GetAllManifestsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var manifests = new List<ContentManifest>();
            var manifestsDir = Path.Combine(_storageService.GetContentStorageRoot(), "Manifests");

            if (!Directory.Exists(manifestsDir))
                return OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifests);

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

            return OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifests);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all manifests");
            return OperationResult<IEnumerable<ContentManifest>>.CreateFailure($"Failed to get all manifests: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<IEnumerable<ContentManifest>>> SearchManifestsAsync(ContentSearchQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            var allManifestsResult = await GetAllManifestsAsync(cancellationToken);
            if (!allManifestsResult.Success)
                return allManifestsResult;

            var manifests = allManifestsResult.Data ?? Enumerable.Empty<ContentManifest>();
            var filteredManifests = manifests.Where(manifest =>
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

            return OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(filteredManifests);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search manifests");
            return OperationResult<IEnumerable<ContentManifest>>.CreateFailure($"Failed to search manifests: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> RemoveManifestAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Removing manifest {ManifestId} from pool", manifestId);

            var result = await _storageService.RemoveContentAsync(manifestId, cancellationToken);
            if (!result.Success)
            {
                return OperationResult<bool>.CreateFailure($"Failed to remove content for manifest {manifestId}: {result.FirstError}");
            }

            _logger.LogDebug("Successfully removed manifest {ManifestId} from pool", manifestId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove manifest {ManifestId}", manifestId);
            return OperationResult<bool>.CreateFailure($"Failed to remove manifest: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> IsManifestAcquiredAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _storageService.IsContentStoredAsync(manifestId, cancellationToken);
            if (!result.Success)
                return OperationResult<bool>.CreateFailure($"Failed to check if manifest is acquired: {result.FirstError}");

            return OperationResult<bool>.CreateSuccess(result.Data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if manifest {ManifestId} is acquired", manifestId);
            return OperationResult<bool>.CreateFailure($"Failed to check if manifest is acquired: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string?>> GetContentDirectoryAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        try
        {
            var contentDir = _storageService.GetContentDirectoryPath(manifestId);
            var result = Directory.Exists(contentDir) ? contentDir : null;
            return await Task.FromResult(OperationResult<string?>.CreateSuccess(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get content directory for manifest {ManifestId}", manifestId);
            return OperationResult<string?>.CreateFailure($"Failed to get content directory: {ex.Message}");
        }
    }
}
