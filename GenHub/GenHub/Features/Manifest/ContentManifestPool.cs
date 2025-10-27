using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Manifest;

/// <summary>
/// Persistent storage and management of acquired ContentManifests using the content storage service.
/// </summary>
public class ContentManifestPool(IContentStorageService storageService, ILogger<ContentManifestPool> logger) : IContentManifestPool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(), new ManifestIdJsonConverter() },
    };

    private readonly IContentStorageService _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
    private readonly ILogger<ContentManifestPool> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> AddManifestAsync(ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate manifest before adding
            var validationResult = ValidateManifest(manifest);
            if (!validationResult.Success)
            {
                return OperationResult<bool>.CreateFailure($"Manifest validation failed: {validationResult.FirstError}");
            }

            var isStoredResult = await _storageService.IsContentStoredAsync(manifest.Id, cancellationToken);
            if (!isStoredResult.Success || !isStoredResult.Data)
            {
                return OperationResult<bool>.CreateFailure(
                    $"Cannot add manifest {manifest.Id} without source directory. Content must be stored first using AddManifestAsync(ContentManifest, string, CancellationToken).");
            }

            // Update the manifest metadata even if content already exists
            var manifestPath = _storageService.GetManifestStoragePath(manifest.Id);
            var manifestDir = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrEmpty(manifestDir))
                Directory.CreateDirectory(manifestDir);

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

            // Validate manifest before processing
            var validationResult = ValidateManifest(manifest);
            _logger.LogDebug("Manifest validation result for {ManifestId}: success={Success} firstError={FirstError}", manifest.Id, validationResult.Success, validationResult.FirstError);
            if (!validationResult.Success)
            {
                return OperationResult<bool>.CreateFailure($"Manifest validation failed: {validationResult.FirstError}");
            }

            // Validate source directory
            if (string.IsNullOrEmpty(sourceDirectory) || !Directory.Exists(sourceDirectory))
            {
                _logger.LogDebug("Source directory '{SourceDirectory}' exists: {Exists}", sourceDirectory, Directory.Exists(sourceDirectory));
                return OperationResult<bool>.CreateFailure($"Source directory {sourceDirectory} does not exist");
            }

            // Delegate content storage to the storage service which may perform its own validation
            var result = await _storageService.StoreContentAsync(manifest, sourceDirectory, cancellationToken);
            _logger.LogDebug("Storage service returned for {ManifestId}: success={Success} firstError={FirstError}", manifest.Id, result?.Success, result?.FirstError);
            if (result == null || !result.Success)
            {
                return OperationResult<bool>.CreateFailure($"Failed to store content for manifest {manifest.Id}: {result?.FirstError}");
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
    public async Task<OperationResult<ContentManifest?>> GetManifestAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
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
        var manifests = new List<ContentManifest>();
        var manifestsDir = Path.Combine(_storageService.GetContentStorageRoot(), FileTypes.ManifestsDirectory);

        if (!Directory.Exists(manifestsDir))
            return OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifests);

        var manifestFiles = Directory.GetFiles(manifestsDir, FileTypes.ManifestFilePattern);

        foreach (var manifestFile in manifestFiles)
        {
            try
            {
                var manifestJson = await File.ReadAllTextAsync(manifestFile, cancellationToken);
                var manifest = JsonSerializer.Deserialize<ContentManifest>(manifestJson, JsonOptions);

                if (manifest != null)
                {
                    manifests.Add(manifest);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read manifest from {ManifestFile}", manifestFile);
            }
        }

        return OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifests);
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
                    !manifest.Id.Value.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase))
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
    public async Task<OperationResult<bool>> RemoveManifestAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
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
    public async Task<OperationResult<bool>> IsManifestAcquiredAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
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
    public async Task<OperationResult<string?>> GetContentDirectoryAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
    {
        try
        {
            var contentDir = _storageService.GetContentDirectoryPath(manifestId);

            // If a mapping file exists, return its value (this points to the original source directory)
            var mappingFile = Path.Combine(contentDir, "source.path");
            if (File.Exists(mappingFile))
            {
                var sourcePath = await File.ReadAllTextAsync(mappingFile, cancellationToken);
                if (!string.IsNullOrWhiteSpace(sourcePath))
                    return OperationResult<string?>.CreateSuccess(sourcePath);
            }

            var result = Directory.Exists(contentDir) ? contentDir : null;
            return await Task.FromResult(OperationResult<string?>.CreateSuccess(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get content directory for manifest {ManifestId}", manifestId);
            return OperationResult<string?>.CreateFailure($"Failed to get content directory: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates a content manifest for completeness and consistency.
    /// </summary>
    /// <param name="manifest">The manifest to validate.</param>
    /// <returns>A validation result.</returns>
    private OperationResult<bool> ValidateManifest(ContentManifest manifest)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(manifest.Id.Value))
            errors.Add("Manifest ID is required");

        if (string.IsNullOrEmpty(manifest.Name))
            errors.Add("Manifest name is required");

        if (string.IsNullOrEmpty(manifest.Version))
            errors.Add("Manifest version is required");

        var hasFiles = manifest.Files != null && manifest.Files.Any();
        var hasDirs = manifest.RequiredDirectories != null && manifest.RequiredDirectories.Any();
        var isBase = manifest.ContentType == ContentType.GameInstallation || manifest.ContentType == ContentType.GameClient;

        if (!hasFiles && !hasDirs && !isBase)
            errors.Add("Manifest must contain at least one file");

        // Validate file entries
        if (manifest.Files != null)
        {
            foreach (var file in manifest.Files)
            {
                if (string.IsNullOrEmpty(file.RelativePath))
                    errors.Add("File entries must have a relative path");

                // Check for path traversal attacks
                if (file.RelativePath.Contains(".."))
                {
                    errors.Add($"File {file.RelativePath} contains illegal path traversal");
                }

                if (file.SourceType == ContentSourceType.Unknown)
                    errors.Add($"File {file.RelativePath} has unknown source type");

                // Validate file properties based on source type
                // For content-addressable files we require a hash (the file will be stored in CAS)
                if (file.SourceType == ContentSourceType.ContentAddressable && string.IsNullOrEmpty(file.Hash))
                    errors.Add($"Content file {file.RelativePath} must have a hash for content-addressable storage");

                // Remote downloads must include a DownloadUrl
                if (file.SourceType == ContentSourceType.RemoteDownload && string.IsNullOrEmpty(file.DownloadUrl))
                    errors.Add($"Remote download file {file.RelativePath} must have a DownloadUrl");

                if (file.SourceType == ContentSourceType.PatchFile && string.IsNullOrEmpty(file.PatchSourceFile))
                    errors.Add($"Patch file {file.RelativePath} must have a patch source file");
            }
        }

        return errors.Any()
            ? OperationResult<bool>.CreateFailure(string.Join(", ", errors))
            : OperationResult<bool>.CreateSuccess(true);
    }
}
