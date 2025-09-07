namespace GenHub.Core.Constants;

/// <summary>
/// Default values and limits for Content-Addressable Storage (CAS).
/// </summary>
public static class CasDefaults
{
    /// <summary>
    /// Default maximum cache size in bytes (50GB).
    /// </summary>
    public const long MaxCacheSizeBytes = 50L * 1024 * 1024 * 1024;

    /// <summary>
    /// Default maximum cache size in gigabytes.
    /// </summary>
    public const long DefaultMaxCacheSizeGB = 50;

    /// <summary>
    /// Default maximum concurrent CAS operations.
    /// </summary>
    public const int MaxConcurrentOperations = 4;

    /// <summary>
    /// Default garbage collection grace period in days.
    /// </summary>
    public const int GcGracePeriodDays = 7;
}
