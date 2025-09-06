namespace GenHub.Core.Constants;

/// <summary>
/// Constants for unit conversions used throughout the application.
/// </summary>
public static class ConversionConstants
{
    /// <summary>
    /// Number of bytes in one kilobyte.
    /// </summary>
    public const int BytesPerKilobyte = 1024;

    /// <summary>
    /// Number of bytes in one megabyte.
    /// </summary>
    public const int BytesPerMegabyte = BytesPerKilobyte * BytesPerKilobyte;

    /// <summary>
    /// Number of bytes in one gigabyte.
    /// </summary>
    public const long BytesPerGigabyte = (long)BytesPerMegabyte * BytesPerKilobyte;
}
