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
    /// Default maximum concurrent CAS operations.
    /// </summary>
    public const int MaxConcurrentOperations = 4;
}
