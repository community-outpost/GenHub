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

    /// <summary>
    /// Luminance coefficient for red channel in brightness calculation.
    /// </summary>
    public const double LuminanceRedCoefficient = 0.299;

    /// <summary>
    /// Luminance coefficient for green channel in brightness calculation.
    /// </summary>
    public const double LuminanceGreenCoefficient = 0.587;

    /// <summary>
    /// Luminance coefficient for blue channel in brightness calculation.
    /// </summary>
    public const double LuminanceBlueCoefficient = 0.114;

    /// <summary>
    /// Brightness threshold for determining contrast text color (0.5 = 50% brightness).
    /// </summary>
    public const double BrightnessThreshold = 0.5;
}
