using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.CAS;
using GenHub.Core.Models.Storage;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Storage.Services;

/// <summary>
/// High-level Content-Addressable Storage service with coordination and validation.
/// </summary>
public class CasService(
    ICasStorage storage,
    ILogger<CasService> logger,
    IFileHashProvider fileHashProvider,
    IStreamHashProvider streamHashProvider,
    ICasPoolManager? poolManager = null) : ICasService
{
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
                return OperationResult<string>.CreateFailure("Failed to store content in CAS");
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
                return OperationResult<string>.CreateFailure("Failed to store content in CAS");
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
    public async Task<CasValidationResult> ValidateIntegrityAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var issues = new List<CasValidationIssue>();
        var objectsValidated = 0;

        try
        {
            logger.LogInformation("Starting CAS integrity validation");

            foreach (var poolStorage in CasPoolStorages.GetStoragesForEnumeration(poolManager, storage))
            {
                var allHashes = await poolStorage.GetAllObjectHashesAsync(cancellationToken);
                foreach (var expectedHash in allHashes)
                {
                    objectsValidated++;
                    await ValidateObjectAsync(poolStorage, expectedHash, issues, cancellationToken);
                }
            }

            logger.LogInformation("CAS integrity validation completed: {ObjectsValidated} objects validated, {Issues} issues found", objectsValidated, issues.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CAS integrity validation failed");
            issues.Add(new CasValidationIssue
            {
                IssueType = CasValidationIssueType.Critical,
                Details = $"Validation process failed: {ex.Message}",
            });
        }

        return new CasValidationResult(issues, objectsValidated, stopwatch.Elapsed);
    }

    /// <inheritdoc/>
    public async Task<CasStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var uniqueHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalSize = 0;

            foreach (var poolStorage in CasPoolStorages.GetStoragesForEnumeration(poolManager, storage))
            {
                var allHashes = await poolStorage.GetAllObjectHashesAsync(cancellationToken);
                foreach (var hash in allHashes)
                {
                    uniqueHashes.Add(hash);
                    totalSize += GetObjectSize(poolStorage, hash);
                }
            }

            return new CasStats
            {
                ObjectCount = uniqueHashes.Count,
                TotalSize = totalSize,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
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

            foreach (var fallbackStorage in poolManager.GetAllStorages())
            {
                if (ReferenceEquals(fallbackStorage, storage) || ReferenceEquals(fallbackStorage, primaryStorage))
                {
                    continue;
                }

                if (await fallbackStorage.ObjectExistsAsync(hash, cancellationToken))
                {
                    var path = fallbackStorage.GetObjectPath(hash);
                    logger.LogDebug("Found content {Hash} in a legacy CAS pool", hash);
                    return OperationResult<string>.CreateSuccess(path);
                }
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
            ICasStorage? primaryStorage = null;

            if (!exists)
            {
                // Not found in the pool for this content type
                // As a fallback, check if it exists in the primary pool (may have been stored there before pool routing was implemented)
                logger.LogDebug("Content {Hash} not found in {ContentType} pool, checking primary pool as fallback", hash, contentType);
                primaryStorage = poolManager.GetStorage(CasPoolType.Primary);
                exists = await primaryStorage.ObjectExistsAsync(hash, cancellationToken);

                if (exists)
                {
                    logger.LogInformation("Found content {Hash} in primary pool (expected in {ContentType} pool)", hash, contentType);
                }
            }

            if (!exists)
            {
                foreach (var fallbackStorage in poolManager.GetAllStorages())
                {
                    if (ReferenceEquals(fallbackStorage, storage) ||
                        ReferenceEquals(fallbackStorage, primaryStorage))
                    {
                        continue;
                    }

                    if (await fallbackStorage.ObjectExistsAsync(hash, cancellationToken))
                    {
                        logger.LogDebug("Found content {Hash} in a legacy CAS pool", hash);
                        exists = true;
                        break;
                    }
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

    private static long GetObjectSize(ICasStorage poolStorage, string hash)
    {
        try
        {
            var objectPath = poolStorage.GetObjectPath(hash);
            if (File.Exists(objectPath))
            {
                return new FileInfo(objectPath).Length;
            }
        }
        catch
        {
            // Skip files that can't be accessed
        }

        return 0;
    }

    private async Task ValidateObjectAsync(
        ICasStorage poolStorage,
        string expectedHash,
        List<CasValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        try
        {
            var objectPath = poolStorage.GetObjectPath(expectedHash);

            if (!File.Exists(objectPath))
            {
                issues.Add(new CasValidationIssue
                {
                    ObjectPath = objectPath,
                    ExpectedHash = expectedHash,
                    IssueType = CasValidationIssueType.MissingObject,
                    Details = "Object file is missing from filesystem",
                });
                return;
            }

            var actualHash = await fileHashProvider.ComputeFileHashAsync(objectPath, cancellationToken);

            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new CasValidationIssue
                {
                    ObjectPath = objectPath,
                    ExpectedHash = expectedHash,
                    ActualHash = actualHash,
                    IssueType = CasValidationIssueType.HashMismatch,
                    Details = "Computed hash does not match expected hash",
                });
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            issues.Add(new CasValidationIssue
            {
                ObjectPath = poolStorage.GetObjectPath(expectedHash),
                ExpectedHash = expectedHash,
                IssueType = CasValidationIssueType.CorruptedObject,
                Details = $"Validation failed: {ex.Message}",
            });
        }
    }
}
