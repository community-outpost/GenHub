using System;
using System.ComponentModel.DataAnnotations;
using System.IO;

namespace GenHub.Core.Models.Storage;

/// <summary>
/// Configuration settings for the Content-Addressable Storage (CAS) system.
/// </summary>
public class CasConfiguration
{
    private const long DefaultMaxCacheSizeBytes = 50L * 1024 * 1024 * 1024; // 50GB
    private const int DefaultMaxConcurrentOperations = 4;
    private static readonly TimeSpan DefaultAutoGcInterval = TimeSpan.FromDays(1);
    private static readonly TimeSpan DefaultGarbageCollectionGracePeriod = TimeSpan.FromDays(7);

    private TimeSpan _garbageCollectionGracePeriod = DefaultGarbageCollectionGracePeriod;
    private TimeSpan _autoGcInterval = DefaultAutoGcInterval;
    private int _maxConcurrentOperations = DefaultMaxConcurrentOperations;
    private long _maxCacheSizeBytes = DefaultMaxCacheSizeBytes;

    /// <summary>
    /// Gets or sets a value indicating whether automatic garbage collection is enabled.
    /// </summary>
    public bool EnableAutomaticGarbageCollection { get; set; } = true;

    /// <summary>
    /// Gets or sets the root path for the CAS pool.
    /// </summary>
    public string CasRootPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GenHub",
        "cas-pool");

    /// <summary>
    /// Gets or sets the hash algorithm to use for content addressing.
    /// </summary>
    public string HashAlgorithm { get; set; } = "SHA256";

    /// <summary>
    /// Gets or sets the grace period before unreferenced objects can be garbage collected.
    /// </summary>
    public TimeSpan GarbageCollectionGracePeriod
    {
        get => _garbageCollectionGracePeriod;
        set => _garbageCollectionGracePeriod = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Must be positive");
    }

    /// <summary>
    /// Gets or sets the maximum cache size in bytes.
    /// </summary>
    public long MaxCacheSizeBytes
    {
        get => _maxCacheSizeBytes;
        set => _maxCacheSizeBytes = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Must be positive");
    }

    /// <summary>
    /// Gets or sets the interval for automatic garbage collection.
    /// </summary>
    public TimeSpan AutoGcInterval
    {
        get => _autoGcInterval;
        set => _autoGcInterval = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Must be positive");
    }

    /// <summary>
    /// Gets or sets the maximum number of concurrent CAS operations.
    /// </summary>
    public int MaxConcurrentOperations
    {
        get => _maxConcurrentOperations;
        set => _maxConcurrentOperations = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Must be positive");
    }

    /// <summary>
    /// Gets or sets a value indicating whether to verify file integrity during operations.
    /// </summary>
    public bool VerifyIntegrity { get; set; } = true;

    /// <summary>
    /// Validates the CAS configuration settings, ensuring required properties are set and directories exist.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CasRootPath))
            throw new ArgumentException("CasRootPath cannot be null or empty");

        try
        {
            var parentDir = Path.GetDirectoryName(Path.GetFullPath(CasRootPath));
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                throw new DirectoryNotFoundException($"Parent directory of CasRootPath does not exist: {parentDir}");
        }
        catch (Exception ex) when (!(ex is ArgumentException || ex is DirectoryNotFoundException))
        {
            throw new ArgumentException($"Invalid CasRootPath: {CasRootPath}", ex);
        }
    }
}
