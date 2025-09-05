using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.CAS;
using GenHub.Core.Models.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Storage.Services;

/// <summary>
/// High-level Content-Addressable Storage service with coordination and validation.
/// </summary>
public class CasService(
    ICasStorage storage,
    CasReferenceTracker referenceTracker,
    ILogger<CasService> logger,
    IOptions<CasConfiguration> config,
    IFileHashProvider fileHashProvider,
    IStreamHashProvider streamHashProvider) : ICasService
{
    private readonly ICasStorage _storage = storage;
    private readonly CasReferenceTracker _referenceTracker = referenceTracker;
    private readonly ILogger<CasService> _logger = logger;
    private readonly CasConfiguration _config = config.Value;
    private readonly IFileHashProvider _fileHashProvider = fileHashProvider;
    private readonly IStreamHashProvider _streamHashProvider = streamHashProvider;

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
                var actualHash = await _fileHashProvider.ComputeFileHashAsync(sourcePath, cancellationToken);
                if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    return OperationResult<string>.CreateFailure($"Hash mismatch: expected {expectedHash}, but got {actualHash}");
                }

                hash = expectedHash;
            }
            else
            {
                hash = await _fileHashProvider.ComputeFileHashAsync(sourcePath, cancellationToken);
            }

            // Check if content already exists in CAS
            if (await _storage.ObjectExistsAsync(hash, cancellationToken))
            {
                _logger.LogDebug("Content already exists in CAS: {Hash}", hash);
                return OperationResult<string>.CreateSuccess(hash);
            }

            // Store content in CAS
            await using var sourceStream = File.OpenRead(sourcePath);
            var storedPath = await _storage.StoreObjectAsync(sourceStream, hash, cancellationToken);

            if (storedPath == null)
            {
                return OperationResult<string>.CreateFailure($"Failed to store content in CAS");
            }

            _logger.LogInformation("Stored content in CAS: {Hash} from {SourcePath}", hash, sourcePath);
            return OperationResult<string>.CreateSuccess(hash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store content in CAS from {SourcePath}", sourcePath);
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

                var actualHash = await _streamHashProvider.ComputeStreamHashAsync(contentStream, cancellationToken);
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

                hash = await _streamHashProvider.ComputeStreamHashAsync(contentStream, cancellationToken);
                contentStream.Position = 0; // Reset stream for storage
            }

            // Check if content already exists in CAS
            if (await _storage.ObjectExistsAsync(hash, cancellationToken))
            {
                _logger.LogDebug("Content already exists in CAS: {Hash}", hash);
                return OperationResult<string>.CreateSuccess(hash);
            }

            // Store content in CAS
            var storedPath = await _storage.StoreObjectAsync(contentStream, hash, cancellationToken);

            if (storedPath == null)
            {
                return OperationResult<string>.CreateFailure($"Failed to store content in CAS");
            }

            _logger.LogInformation("Stored content in CAS: {Hash}", hash);
            return OperationResult<string>.CreateSuccess(hash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store stream content in CAS");
            return OperationResult<string>.CreateFailure($"Storage failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> GetContentPathAsync(string hash, CancellationToken cancellationToken = default)
    {
        try
        {
            if (await _storage.ObjectExistsAsync(hash, cancellationToken))
            {
                var path = _storage.GetObjectPath(hash);
                return OperationResult<string>.CreateSuccess(path);
            }

            return OperationResult<string>.CreateFailure($"Content not found in CAS: {hash}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get content path for hash {Hash}", hash);
            return OperationResult<string>.CreateFailure($"Path lookup failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> ExistsAsync(string hash, CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await _storage.ObjectExistsAsync(hash, cancellationToken);
            return OperationResult<bool>.CreateSuccess(exists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check existence of hash {Hash}", hash);
            return OperationResult<bool>.CreateFailure($"Existence check failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<Stream>> OpenContentStreamAsync(string hash, CancellationToken cancellationToken = default)
    {
        try
        {
            var stream = await _storage.OpenObjectStreamAsync(hash, cancellationToken);
            if (stream == null)
            {
                return OperationResult<Stream>.CreateFailure($"Content not found in CAS: {hash}");
            }

            return OperationResult<Stream>.CreateSuccess(stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open content stream for hash {Hash}", hash);
            return OperationResult<Stream>.CreateFailure($"Stream open failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<CasGarbageCollectionResult> RunGarbageCollectionAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new CasGarbageCollectionResult(true, (string?)null);

        try
        {
            _logger.LogInformation("Starting CAS garbage collection");

            // Get all objects in CAS
            var allHashes = await _storage.GetAllObjectHashesAsync(cancellationToken);
            result.ObjectsScanned = allHashes.Length;

            // Get all referenced hashes
            var referencedHashes = await _referenceTracker.GetAllReferencedHashesAsync(cancellationToken);
            result.ObjectsReferenced = referencedHashes.Count;

            // Find unreferenced objects
            var unreferencedHashes = System.Linq.Enumerable.Except(allHashes, referencedHashes);

            // Use configurable grace period
            var gracePeriod = _config.GcGracePeriod;
            long bytesFreed = 0;
            int objectsDeleted = 0;

            foreach (var hash in unreferencedHashes)
            {
                try
                {
                    var creationTime = await _storage.GetObjectCreationTimeAsync(hash, cancellationToken);
                    if (creationTime == null || DateTime.UtcNow - creationTime.Value > gracePeriod)
                    {
                        // Get size before deletion
                        var objectPath = _storage.GetObjectPath(hash);
                        if (File.Exists(objectPath))
                        {
                            var fileInfo = new FileInfo(objectPath);
                            bytesFreed += fileInfo.Length;
                        }

                        await _storage.DeleteObjectAsync(hash, cancellationToken);
                        objectsDeleted++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete unreferenced object {Hash}", hash);
                }
            }

            result.ObjectsDeleted = objectsDeleted;
            result.BytesFreed = bytesFreed;

            _logger.LogInformation("CAS garbage collection completed: {ObjectsDeleted} objects deleted, {BytesFreed} bytes freed", objectsDeleted, bytesFreed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CAS garbage collection failed");
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
            _logger.LogInformation("Starting CAS integrity validation");

            var allHashes = await _storage.GetAllObjectHashesAsync(cancellationToken);
            result.ObjectsValidated = allHashes.Length;

            foreach (var expectedHash in allHashes)
            {
                try
                {
                    var objectPath = _storage.GetObjectPath(expectedHash);

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

                    var actualHash = await _fileHashProvider.ComputeFileHashAsync(objectPath, cancellationToken);

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
                        ObjectPath = _storage.GetObjectPath(expectedHash),
                        ExpectedHash = expectedHash,
                        IssueType = CasValidationIssueType.CorruptedObject,
                        Details = $"Validation failed: {ex.Message}",
                    });
                }
            }

            _logger.LogInformation("CAS integrity validation completed: {ObjectsValidated} objects validated, {Issues} issues found", result.ObjectsValidated, result.ObjectsWithIssues);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CAS integrity validation failed");
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
            var allHashes = await _storage.GetAllObjectHashesAsync(cancellationToken);
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
                    var objectPath = _storage.GetObjectPath(hash);
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
            _logger.LogError(ex, "Failed to get CAS statistics");
            return new CasStats();
        }
    }
}
