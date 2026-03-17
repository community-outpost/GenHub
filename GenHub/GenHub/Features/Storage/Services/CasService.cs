using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.CAS;
using GenHub.Core.Models.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenHub.Features.Storage.Services;

/// <summary>
/// High-level Content-Addressable Storage service with coordination and validation.
/// </summary>
public class CasService(
    ICasStorage storage,
    ICasReferenceTracker referenceTracker,
    ILogger<CasService> logger,
    IOptions<CasConfiguration> config,
    IFileHashProvider fileHashProvider,
    IStreamHashProvider streamHashProvider,
    ICasPoolManager? poolManager = null) : ICasService
{
    private readonly CasConfiguration _config = config.Value;

    /// <inheritdoc/>
    public async Task<OperationResult<string>> StoreContentAsync(string sourcePath, string? expectedHash = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                return OperationResult<string>.CreateFailure($"Source file not found: {sourcePath}");
            }

            // Compute hash if not provided
            string hash;
            if (!string.IsNullOrEmpty(expectedHash))
            {
                // Verify the expected hash matches the actual file
                var actualHash = await fileHashProvider.ComputeFileHashAsync(sourcePath, cancellationToken);
                if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    return OperationResult<string>.CreateFailure($"Hash mismatch: expected {expectedHash}, but got {actualHash}");
                }

                hash = expectedHash;
            }
            else
            {
                hash = await fileHashProvider.ComputeFileHashAsync(sourcePath, cancellationToken);
            }

            // Check if content already exists in CAS
            if (await storage.ObjectExistsAsync(hash, cancellationToken))
            {
                logger.LogDebug("Content already exists in CAS: {Hash}", hash);
                return OperationResult<string>.CreateSuccess(hash);
            }

            // Store content in CAS
            await using var sourceStream = File.OpenRead(sourcePath);
            var storedPath = await storage.StoreObjectAsync(sourceStream, hash, cancellationToken);

            if (storedPath == null)
            {
                return OperationResult<string>.CreateFailure($"Failed to store content in CAS");
            }

            logger.LogInformation("Stored content in CAS: {Hash} from {SourcePath}", hash, sourcePath);
            return OperationResult<string>.CreateSuccess(hash);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to store content in CAS from {SourcePath}", sourcePath);
            return OperationResult<string>.CreateFailure($"Storage failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> StoreContentAsync(Stream contentStream, string? expectedHash = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Compute hash from stream if not provided
            string hash;
            if (!string.IsNullOrEmpty(expectedHash))
            {
                // We need to compute the hash to verify it matches
                if (!contentStream.CanSeek)
                {
                    return OperationResult<string>.CreateFailure("Stream must be seekable when expectedHash is provided");
                }

                var actualHash = await streamHashProvider.ComputeStreamHashAsync(contentStream, cancellationToken);
                contentStream.Position = 0;
                if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    return OperationResult<string>.CreateFailure($"Hash mismatch: expected {expectedHash}, but got {actualHash}");
                }

                hash = expectedHash;
            }
            else
            {
                if (!contentStream.CanSeek)
                {
                    return OperationResult<string>.CreateFailure("Stream must be seekable to compute hash");
                }

                hash = await streamHashProvider.ComputeStreamHashAsync(contentStream, cancellationToken);
                contentStream.Position = 0; // Reset stream for storage
            }

            // Check if content already exists in CAS
            if (await storage.ObjectExistsAsync(hash, cancellationToken))
            {
                logger.LogDebug("Content already exists in CAS: {Hash}", hash);
                return OperationResult<string>.CreateSuccess(hash);
            }

            // Store content in CAS
            var storedPath = await storage.StoreObjectAsync(contentStream, hash, cancellationToken);

            if (storedPath == null)
            {
                return OperationResult<string>.CreateFailure($"Failed to store content in CAS");
            }

            logger.LogInformation("Stored content in CAS: {Hash}", hash);
            return OperationResult<string>.CreateSuccess(hash);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to store stream content in CAS");
            return OperationResult<string>.CreateFailure($"Storage failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> GetContentPathAsync(string hash, CancellationToken cancellationToken = default)
    {
        try
        {
            // If pool manager is available, check all pools for the content
            if (poolManager != null)
            {
                poolManager.EnsureAllPoolsInitialized();
                var allStorages = poolManager.GetAllStorages();

                foreach (var poolStorage in allStorages)
                {
                    if (await poolStorage.ObjectExistsAsync(hash, cancellationToken))
                    {
                        var path = poolStorage.GetObjectPath(hash);
                        logger.LogDebug("Found content {Hash} in pool storage", hash);
                        return OperationResult<string>.CreateSuccess(path);
                    }
                }

                return OperationResult<string>.CreateFailure($"Content not found in any CAS pool: {hash}");
            }

            // No pool manager - use default storage only
            if (await storage.ObjectExistsAsync(hash, cancellationToken))
            {
                var path = storage.GetObjectPath(hash);
                return OperationResult<string>.CreateSuccess(path);
            }

            return OperationResult<string>.CreateFailure($"Content not found in CAS: {hash}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get content path for hash {Hash}", hash);
            return OperationResult<string>.CreateFailure($"Path lookup failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> ExistsAsync(string hash, CancellationToken cancellationToken = default)
    {
        try
        {
            // If pool manager is available, check all pools for the content
            if (poolManager != null)
            {
                poolManager.EnsureAllPoolsInitialized();
                var allStorages = poolManager.GetAllStorages();

                foreach (var poolStorage in allStorages)
                {
                    if (await poolStorage.ObjectExistsAsync(hash, cancellationToken))
                    {
                        return OperationResult<bool>.CreateSuccess(true);
                    }
                }

                return OperationResult<bool>.CreateSuccess(false);
            }

            // No pool manager - use default storage only
            var exists = await storage.ObjectExistsAsync(hash, cancellationToken);
            return OperationResult<bool>.CreateSuccess(exists);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check existence of hash {Hash}", hash);
            return OperationResult<bool>.CreateFailure($"Existence check failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<Stream>> OpenContentStreamAsync(string hash, CancellationToken cancellationToken = default)
    {
        try
        {
            // If pool manager is available, check all pools for the content
            if (poolManager != null)
            {
                poolManager.EnsureAllPoolsInitialized();
                var allStorages = poolManager.GetAllStorages();

                foreach (var poolStorage in allStorages)
                {
                    if (await poolStorage.ObjectExistsAsync(hash, cancellationToken))
                    {
                        var stream = await poolStorage.OpenObjectStreamAsync(hash, cancellationToken);
                        if (stream != null)
                        {
                            return OperationResult<Stream>.CreateSuccess(stream);
                        }
                    }
                }

                return OperationResult<Stream>.CreateFailure($"Content not found in any CAS pool: {hash}");
            }

            // No pool manager - use default storage only
            var defaultStream = await storage.OpenObjectStreamAsync(hash, cancellationToken);
            if (defaultStream == null)
            {
                return OperationResult<Stream>.CreateFailure($"Content not found in CAS: {hash}");
            }

            return OperationResult<Stream>.CreateSuccess(defaultStream);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open content stream for hash {Hash}", hash);
            return OperationResult<Stream>.CreateFailure($"Stream open failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<CasGarbageCollectionResult> RunGarbageCollectionAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new CasGarbageCollectionResult(true, (string?)null);

        try
        {
            logger.LogInformation("Starting CAS garbage collection (force={Force})", force);

            // Get all objects in CAS
            var allHashes = await storage.GetAllObjectHashesAsync(cancellationToken);
            result.ObjectsScanned = allHashes.Length;

            // Get all referenced hashes
            var referencedHashes = await referenceTracker.GetAllReferencedHashesAsync(cancellationToken);
            result.ObjectsReferenced = referencedHashes.Count;

            // Find unreferenced objects
            var unreferencedHashes = System.Linq.Enumerable.Except(allHashes, referencedHashes);

            // Use configurable grace period unless forced
            var gracePeriod = force ? TimeSpan.Zero : _config.GcGracePeriod;
            long bytesFreed = 0;
            int objectsDeleted = 0;

            foreach (var hash in unreferencedHashes)
            {
                try
                {
                    var creationTime = await storage.GetObjectCreationTimeAsync(hash, cancellationToken);
                    if (force || creationTime == null || DateTime.UtcNow - creationTime.Value > gracePeriod)
                    {
                        // Get size before deletion
                        var objectPath = storage.GetObjectPath(hash);
                        if (File.Exists(objectPath))
                        {
                            var fileInfo = new FileInfo(objectPath);
                            bytesFreed += fileInfo.Length;
                        }

                        await storage.DeleteObjectAsync(hash, cancellationToken);
                        objectsDeleted++;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete unreferenced object {Hash}", hash);
                }
            }

            result.ObjectsDeleted = objectsDeleted;
            result.BytesFreed = bytesFreed;

            logger.LogInformation("CAS garbage collection completed: {ObjectsDeleted} objects deleted, {BytesFreed} bytes freed", objectsDeleted, bytesFreed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CAS garbage collection failed");
            result = new CasGarbageCollectionResult(false, ex.Message, DateTime.UtcNow - startTime);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<CasValidationResult> ValidateIntegrityAsync(CancellationToken cancellationToken = default)
    {
        var result = new CasValidationResult();

        try
        {
            logger.LogInformation("Starting CAS integrity validation");

            var allHashes = await storage.GetAllObjectHashesAsync(cancellationToken);
            result.ObjectsValidated = allHashes.Length;

            foreach (var expectedHash in allHashes)
            {
                try
                {
                    var objectPath = storage.GetObjectPath(expectedHash);

                    if (!File.Exists(objectPath))
                    {
                        result.Issues.Add(new CasValidationIssue
                        {
                            ObjectPath = objectPath,
                            ExpectedHash = expectedHash,
                            IssueType = CasValidationIssueType.MissingObject,
                            Details = "Object file is missing from filesystem",
                        });
                        continue;
                    }

                    var actualHash = await fileHashProvider.ComputeFileHashAsync(objectPath, cancellationToken);

                    if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Issues.Add(new CasValidationIssue
                        {
                            ObjectPath = objectPath,
                            ExpectedHash = expectedHash,
                            ActualHash = actualHash,
                            IssueType = CasValidationIssueType.HashMismatch,
                            Details = "Computed hash does not match expected hash",
                        });
                    }
                }
                catch (Exception ex)
                {
                    result.Issues.Add(new CasValidationIssue
                    {
                        ObjectPath = storage.GetObjectPath(expectedHash),
                        ExpectedHash = expectedHash,
                        IssueType = CasValidationIssueType.CorruptedObject,
                        Details = $"Validation failed: {ex.Message}",
                    });
                }
            }

            logger.LogInformation("CAS integrity validation completed: {ObjectsValidated} objects validated, {Issues} issues found", result.ObjectsValidated, result.ObjectsWithIssues);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CAS integrity validation failed");
            result.Issues.Add(new CasValidationIssue
            {
                IssueType = CasValidationIssueType.Warning,
                Details = $"Validation process failed: {ex.Message}",
            });
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<CasStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var allHashes = await storage.GetAllObjectHashesAsync(cancellationToken);
            var stats = new CasStats
            {
                ObjectCount = allHashes.Length,
            };

            // Calculate total size
            long totalSize = 0;
            foreach (var hash in allHashes)
            {
                try
                {
                    var objectPath = storage.GetObjectPath(hash);
                    if (File.Exists(objectPath))
                    {
                        var fileInfo = new FileInfo(objectPath);
                        totalSize += fileInfo.Length;
                    }
                }
                catch
                {
                    // Skip files that can't be accessed
                }
            }

            stats.TotalSize = totalSize;
            return stats;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get CAS statistics");
            return new CasStats();
        }
    }

    // ===== Pool-Aware Operations =====

    /// <inheritdoc/>
    public async Task<OperationResult<string>> StoreContentAsync(
        string sourcePath,
        ContentType contentType,
        string? expectedHash = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use pool manager if available, otherwise fall back to default storage
            if (poolManager == null)
            {
                return await StoreContentAsync(sourcePath, expectedHash, cancellationToken);
            }

            if (!File.Exists(sourcePath))
            {
                return OperationResult<string>.CreateFailure($"Source file not found: {sourcePath}");
            }

            // Ensure all pools are properly initialized
            poolManager.EnsureAllPoolsInitialized();

            var storage = poolManager.GetStorage(contentType);

            // Compute hash
            string hash;
            if (!string.IsNullOrEmpty(expectedHash))
            {
                var actualHash = await fileHashProvider.ComputeFileHashAsync(sourcePath, cancellationToken);
                if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    return OperationResult<string>.CreateFailure($"Hash mismatch: expected {expectedHash}, but got {actualHash}");
                }

                hash = expectedHash;
            }
            else
            {
                hash = await fileHashProvider.ComputeFileHashAsync(sourcePath, cancellationToken);
            }

            // Check if content already exists in the pool
            if (await storage.ObjectExistsAsync(hash, cancellationToken))
            {
                logger.LogDebug("Content already exists in CAS pool ({ContentType}): {Hash}", contentType, hash);
                return OperationResult<string>.CreateSuccess(hash);
            }

            // Store content in the appropriate pool
            await using var sourceStream = File.OpenRead(sourcePath);
            var storedPath = await storage.StoreObjectAsync(sourceStream, hash, cancellationToken);

            if (storedPath == null)
            {
                return OperationResult<string>.CreateFailure("Failed to store content in CAS pool");
            }

            logger.LogInformation("Stored content in CAS pool ({ContentType}): {Hash} from {SourcePath}", contentType, hash, sourcePath);
            return OperationResult<string>.CreateSuccess(hash);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to store content in CAS pool ({ContentType}) from {SourcePath}", contentType, sourcePath);
            return OperationResult<string>.CreateFailure($"Storage failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> StoreContentAsync(
        Stream contentStream,
        ContentType contentType,
        string? expectedHash = null,
        CancellationToken cancellationToken = default)
    {
        // Use pool manager if available, otherwise fall back to default storage
        if (poolManager == null)
        {
            return await StoreContentAsync(contentStream, expectedHash, cancellationToken);
        }

        try
        {
            // Ensure all pools are properly initialized
            poolManager.EnsureAllPoolsInitialized();

            var storage = poolManager.GetStorage(contentType);

            // Compute hash from stream
            string hash;
            if (!string.IsNullOrEmpty(expectedHash))
            {
                if (!contentStream.CanSeek)
                {
                    return OperationResult<string>.CreateFailure("Stream must be seekable when expectedHash is provided");
                }

                var actualHash = await streamHashProvider.ComputeStreamHashAsync(contentStream, cancellationToken);
                contentStream.Position = 0;
                if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    return OperationResult<string>.CreateFailure($"Hash mismatch: expected {expectedHash}, but got {actualHash}");
                }

                hash = expectedHash;
            }
            else
            {
                if (!contentStream.CanSeek)
                {
                    return OperationResult<string>.CreateFailure("Stream must be seekable to compute hash");
                }

                hash = await streamHashProvider.ComputeStreamHashAsync(contentStream, cancellationToken);
                contentStream.Position = 0;
            }

            // Check if content already exists
            if (await storage.ObjectExistsAsync(hash, cancellationToken))
            {
                logger.LogDebug("Content already exists in CAS pool ({ContentType}): {Hash}", contentType, hash);
                return OperationResult<string>.CreateSuccess(hash);
            }

            // Store content in the appropriate pool
            var storedPath = await storage.StoreObjectAsync(contentStream, hash, cancellationToken);

            if (storedPath == null)
            {
                return OperationResult<string>.CreateFailure("Failed to store content in CAS pool");
            }

            logger.LogInformation("Stored content in CAS pool ({ContentType}): {Hash}", contentType, hash);
            return OperationResult<string>.CreateSuccess(hash);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to store stream content in CAS pool ({ContentType})", contentType);
            return OperationResult<string>.CreateFailure($"Storage failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> GetContentPathAsync(
        string hash,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        // Use pool manager if available, otherwise fall back to default storage
        if (poolManager == null)
        {
            return await GetContentPathAsync(hash, cancellationToken);
        }

        try
        {
            // Ensure all pools are properly initialized before checking
            // This is important because the Installation Pool path may have been set after construction
            poolManager.EnsureAllPoolsInitialized();

            var storage = poolManager.GetStorage(contentType);

            if (await storage.ObjectExistsAsync(hash, cancellationToken))
            {
                var path = storage.GetObjectPath(hash);
                return OperationResult<string>.CreateSuccess(path);
            }

            // Not found in the expected pool, try primary pool as fallback
            logger.LogDebug("Content {Hash} not found in {ContentType} pool, checking primary pool as fallback", hash, contentType);
            var primaryStorage = poolManager.GetStorage(CasPoolType.Primary);
            if (await primaryStorage.ObjectExistsAsync(hash, cancellationToken))
            {
                var path = primaryStorage.GetObjectPath(hash);
                logger.LogInformation("Found content {Hash} in primary pool (expected in {ContentType} pool)", hash, contentType);
                return OperationResult<string>.CreateSuccess(path);
            }

            return OperationResult<string>.CreateFailure($"Content not found in CAS: {hash}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get content path for hash {Hash} in pool ({ContentType})", hash, contentType);
            return OperationResult<string>.CreateFailure($"Path lookup failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> ExistsAsync(
        string hash,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        // Use pool manager if available, otherwise fall back to default storage
        if (poolManager == null)
        {
            return await ExistsAsync(hash, cancellationToken);
        }

        try
        {
            // Ensure all pools are properly initialized before checking
            // This is important because the Installation Pool path may have been set after construction
            poolManager.EnsureAllPoolsInitialized();

            var storage = poolManager.GetStorage(contentType);
            var exists = await storage.ObjectExistsAsync(hash, cancellationToken);

            if (!exists)
            {
                // Not found in the pool for this content type
                // As a fallback, check if it exists in the primary pool (may have been stored there before pool routing was implemented)
                logger.LogDebug("Content {Hash} not found in {ContentType} pool, checking primary pool as fallback", hash, contentType);
                var primaryStorage = poolManager.GetStorage(CasPoolType.Primary);
                exists = await primaryStorage.ObjectExistsAsync(hash, cancellationToken);

                if (exists)
                {
                    logger.LogInformation("Found content {Hash} in primary pool (expected in {ContentType} pool)", hash, contentType);
                }
            }

            return OperationResult<bool>.CreateSuccess(exists);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check existence of hash {Hash} in pool ({ContentType})", hash, contentType);
            return OperationResult<bool>.CreateFailure($"Existence check failed: {ex.Message}");
        }
    }
}
