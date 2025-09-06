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

    // CAS maintenance constants

    /// <summary>
    /// Default automatic garbage collection interval in days.
    /// </summary>
    public const int AutoGcIntervalDays = 1;
}
