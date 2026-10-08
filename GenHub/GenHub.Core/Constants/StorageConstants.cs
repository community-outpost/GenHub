namespace GenHub.Core.Constants;

/// <summary>
/// Storage and CAS (Content-Addressable Storage) related constants.
/// </summary>
public static class StorageConstants
{
    /// <summary>
    /// Default application data directory name in user profile.
    /// </summary>
    public const string DefaultDataDirectoryName = ".genhub";

    /// <summary>
    /// Prefix used for temporary files that verify a storage location is writable.
    /// </summary>
    public const string WriteProbeFilePrefix = ".genhub-write-probe-";

    // CAS retry constants

    /// <summary>
    /// Maximum number of retry attempts for CAS operations.
    /// </summary>
    public const int MaxRetries = 10;

    // CAS maintenance constants

    /// <summary>
    /// Default automatic garbage collection interval in days.
    /// </summary>
    public const int AutoGcIntervalDays = 1;
}
