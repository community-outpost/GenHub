namespace GenHub.Core.Constants;

/// <summary>
/// Storage and CAS (Content-Addressable Storage) related constants.
/// </summary>
public static class StorageConstants
{
    // CAS retry constants

    /// <summary>
    /// Maximum number of retry attempts for CAS operations.
    /// </summary>
    public const int MaxRetries = 10;

    /// <summary>
    /// Delay in milliseconds between retry attempts.
    /// </summary>
    public const int RetryDelayMs = 100;

    /// <summary>
    /// Maximum delay in milliseconds for exponential backoff.
    /// </summary>
    public const int MaxRetryDelayMs = 5000;

    // CAS directory structure

    /// <summary>
    /// Directory name for CAS objects.
    /// </summary>
    public const string ObjectsDirectory = "objects";

    /// <summary>
    /// Directory name for CAS locks.
    /// </summary>
    public const string LocksDirectory = "locks";

    // CAS maintenance constants

    /// <summary>
    /// Default automatic garbage collection interval in days.
    /// </summary>
    public const int AutoGcIntervalDays = 1;
}
