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
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Manifest;

/// <summary>
/// Persistent storage and management of acquired GameManifests using the content storage service.
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
    public async Task AddManifestAsync(ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        // Delegate to storage service without source directory
        // This will be used when manifest already exists in storage
        var isStoredResult = await _storageService.IsContentStoredAsync(manifest.Id, cancellationToken);
        if (!isStoredResult.Success || !isStoredResult.Data)
        {
            throw new InvalidOperationException(
                $"Cannot add manifest {manifest.Id} without source directory. Content must be stored first using AddManifestAsync(ContentManifest, string, CancellationToken).");
        }

        _logger.LogDebug("Manifest {ManifestId} already exists in storage", manifest.Id);
    }

    /// <summary>
    /// Adds a ContentManifest to the pool with its content files from a source directory.
    /// </summary>
    /// <param name="manifest">The game manifest to store.</param>
    /// <param name="sourceDirectory">The directory containing the content files.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AddManifestAsync(ContentManifest manifest, string sourceDirectory, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Adding manifest {ManifestId} to pool with content from {SourceDirectory}", manifest.Id, sourceDirectory);

        var result = await _storageService.StoreContentAsync(manifest, sourceDirectory, cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to store content for manifest {manifest.Id}: {result.ErrorMessage}");
        }

        _logger.LogDebug("Successfully added manifest {ManifestId} to pool", manifest.Id);
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
