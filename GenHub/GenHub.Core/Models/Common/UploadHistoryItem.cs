using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using System;

namespace GenHub.Core.Models.Common;

/// <summary>
/// Represents a single item in the upload history.
/// </summary>
/// <param name="Timestamp">The UTC timestamp of the upload.</param>
/// <param name="SizeBytes">The size of the uploaded file in bytes.</param>
/// <param name="Url">The public URL of the upload.</param>
/// <param name="FileName">The name of the uploaded file.</param>
/// <param name="Category">Optional tool or file category.</param>
/// <param name="Game">The game selected when the upload began, if recorded.</param>
public record UploadHistoryItem(
    DateTime Timestamp,
    long SizeBytes,
    string Url,
    string FileName,
    string? Category = null,
    GameType? Game = null)
{
    /// <summary>
    /// Gets the formatted file size string.
    /// </summary>
    public string FormattedSize => SizeBytes switch
    {
        >= ConversionConstants.BytesPerMegabyte => $"{SizeBytes / (double)ConversionConstants.BytesPerMegabyte:F1} MB",
        >= ConversionConstants.BytesPerKilobyte => $"{SizeBytes / (double)ConversionConstants.BytesPerKilobyte:F1} KB",
        _ => $"{SizeBytes} B",
    };

    /// <summary>
    /// Gets the formatted upload date string.
    /// </summary>
    public string FormattedDate => Timestamp.ToLocalTime().ToString("g");
}
