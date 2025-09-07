using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Storage;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Storage.Services;

/// <summary>
/// Low-level Content-Addressable Storage implementation with atomic operations and concurrency safety.
/// </summary>
public class CasStorage(
    IOptions<CasConfiguration> config,
    ILogger<CasStorage> logger,
    IFileHashProvider hashProvider) : ICasStorage
{
    private readonly CasConfiguration _config = config.Value;
    private readonly ILogger<CasStorage> _logger = logger;
    private readonly string _objectsDirectory = Path.Combine(config.Value.CasRootPath, "objects");
    private readonly string _tempDirectory = Path.Combine(config.Value.CasRootPath, "temp");
    private readonly string _lockDirectory = Path.Combine(config.Value.CasRootPath, "locks");
    private readonly IFileHashProvider _hashProvider = hashProvider;

    // Ensure directory structure exists on first use
    private bool _directoriesEnsured = false;

    /// <inheritdoc/>
    public string GetObjectPath(string hash)
    {
        EnsureDirectoriesCreated();
        ValidateHashFormat(hash);
        var subDirectory = hash[..2].ToLowerInvariant();
        return Path.Combine(_objectsDirectory, subDirectory, hash.ToLowerInvariant());
    }

    /// <inheritdoc/>
    public async Task<bool> ObjectExistsAsync(string hash, CancellationToken cancellationToken = default)
    {
        try
        {
            var objectPath = GetObjectPath(hash);
            return await Task.Run(() => File.Exists(objectPath), cancellationToken);
        }
        catch (ArgumentException)
        {
            // Invalid hash format
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check existence of object {Hash}", hash);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<string?> StoreObjectAsync(Stream content, string hash, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateHashFormat(hash);

            // Ensure directory structure exists before acquiring locks
            EnsureDirectoriesCreated();

            var objectPath = GetObjectPath(hash);
            var tempPath = Path.Combine(_tempDirectory, $"store-{Guid.NewGuid():N}");
            var lockPath = Path.Combine(_lockDirectory, $"{hash}.lock");

            await using var lockFile = await AcquireLockAsync(lockPath, cancellationToken);

            try
            {
                // Check if object already exists (race condition protection)
                if (await ObjectExistsAsync(hash, cancellationToken))
                {
                    _logger.LogDebug("Object {Hash} already exists in CAS", hash);
                    return objectPath;
                }

                // Write to temporary file first (atomic operation)
                var tempDirectory = Path.GetDirectoryName(tempPath);
                if (!string.IsNullOrEmpty(tempDirectory))
                {
                    Directory.CreateDirectory(tempDirectory);
                }

                await using (var tempStream = File.Create(tempPath))
                {
                    if (content.CanSeek && content.Position != 0)
                    {
                        content.Position = 0;
                    }

                    await content.CopyToAsync(tempStream, cancellationToken);
                    await tempStream.FlushAsync(cancellationToken);
                } // tempStream is disposed here

                // Verify integrity if enabled
                if (_config.VerifyIntegrity)
                {
                    var actualHash = await _hashProvider.ComputeFileHashAsync(tempPath, cancellationToken);
                    if (!string.Equals(actualHash, hash, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"Hash mismatch: expected {hash}, got {actualHash}");
                    }
                }

                // Ensure target directory exists
                var targetDirectory = Path.GetDirectoryName(objectPath)!;
                Directory.CreateDirectory(targetDirectory);

                // Atomic move to final location
                File.Move(tempPath, objectPath);

                _logger.LogDebug("Stored object {Hash} in CAS at {Path}", hash, objectPath);
                return objectPath;
            }
            finally
            {
                try
                {
                    FileOperationsService.DeleteFileIfExists(tempPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup temp file {TempPath}", tempPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store object {Hash} in CAS", hash);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<Stream?> OpenObjectStreamAsync(string hash, CancellationToken cancellationToken = default)
    {
        try
        {
            var objectPath = GetObjectPath(hash);

            if (!await ObjectExistsAsync(hash, cancellationToken))
            {
                _logger.LogWarning("Object {Hash} not found in CAS", hash);
                return null;
            }

            return await Task.Run(() => File.OpenRead(objectPath), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open stream for object {Hash}", hash);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteObjectAsync(string hash, CancellationToken cancellationToken = default)
    {
        // Ensure directory structure exists before acquiring locks
        EnsureDirectoriesCreated();

        var objectPath = GetObjectPath(hash);
        var lockPath = Path.Combine(_lockDirectory, $"{hash}.lock");

        await using var lockFile = await AcquireLockAsync(lockPath, cancellationToken);

        try
        {
            if (await ObjectExistsAsync(hash, cancellationToken))
            {
                await Task.Run(() => File.Delete(objectPath), cancellationToken);
                _logger.LogDebug("Deleted object {Hash} from CAS", hash);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete object {Hash} from CAS", hash);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<string[]> GetAllObjectHashesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var hashes = await Task.Run(
                () =>
            {
                if (!Directory.Exists(_objectsDirectory))
                    return Array.Empty<string>();

                return Directory.GetFiles(_objectsDirectory, "*", SearchOption.AllDirectories)
                    .Select(Path.GetFileName)
                    .Where(name => name != null && !string.IsNullOrEmpty(name) && IsValidHash(name))
                    .Cast<string>()
                    .ToArray();
            }, cancellationToken);

            return hashes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate CAS objects");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Gets the creation time of the specified object in the CAS storage.
    /// </summary>
    /// <param name="hash">The hash of the object.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The creation time of the object, or null if the object cannot be accessed.</returns>
    public async Task<DateTime?> GetObjectCreationTimeAsync(string hash, CancellationToken cancellationToken = default)
    {
        try
        {
            var objectPath = GetObjectPath(hash);
            if (!await ObjectExistsAsync(hash, cancellationToken))
            {
                _logger.LogWarning("Object {Hash} not found in CAS", hash);
                return null;
            }

            return await Task.Run(() => File.GetCreationTime(objectPath), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get creation time for object {Hash}", hash);
            return null;
        }
    }

    private static void ValidateHashFormat(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            throw new ArgumentException("Hash cannot be null or empty", nameof(hash));

        if (!IsValidHash(hash))
            throw new ArgumentException($"Invalid hash format: {hash}", nameof(hash));

        if (hash.Contains("..") || hash.Contains(Path.DirectorySeparatorChar) || hash.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException($"Hash contains invalid characters: {hash}", nameof(hash));
    }

    private static bool IsValidHash(string hash)
    {
        return hash.Length == 64 && hash.All(c => char.IsAsciiHexDigit(c));
    }

    private void EnsureDirectoryStructure()
    {
        var requiredDirectories = new[]
        {
            _config.CasRootPath,
            _objectsDirectory,
            _tempDirectory,
            _lockDirectory,
        };

        foreach (var directory in requiredDirectories)
        {
            if (FileOperationsService.EnsureDirectoryExists(directory))
            {
                _logger.LogDebug("Created CAS directory: {Directory}", directory);
            }
        }
    }

    private void EnsureDirectoriesCreated()
    {
        if (!_directoriesEnsured)
        {
            EnsureDirectoryStructure();
            _directoriesEnsured = true;
        }
    }

    private async Task<CasLock> AcquireLockAsync(string lockPath, CancellationToken cancellationToken)
    {
        // Ensure the lock file's directory exists
        var lockDirectory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(lockDirectory))
        {
            Directory.CreateDirectory(lockDirectory);
        }

        for (int i = 0; i < StorageConstants.MaxRetries; i++)
        {
            try
            {
                var lockStream = new FileStream(lockPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await lockStream.WriteAsync(Encoding.UTF8.GetBytes(Environment.ProcessId.ToString()), cancellationToken);
                await lockStream.FlushAsync(cancellationToken);

                return new CasLock(lockPath, lockStream);
            }
            catch (IOException) when (i < 10 - 1)
            {
                // Lock file is in use, wait and retry
                await Task.Delay(100, cancellationToken);
            }
        }

        throw new InvalidOperationException($"Unable to acquire lock for CAS operation: {lockPath}");
    }

    private class CasLock : IAsyncDisposable
    {
        private readonly string _lockPath;
        private readonly FileStream _lockStream;

        public CasLock(string lockPath, FileStream lockStream)
        {
            _lockPath = lockPath;
            _lockStream = lockStream;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _lockStream?.Dispose();
                if (File.Exists(_lockPath))
                {
                    await Task.Run(() => File.Delete(_lockPath));
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
