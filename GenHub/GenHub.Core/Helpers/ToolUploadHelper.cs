using System;
using GenHub.Core.Constants;

namespace GenHub.Core.Helpers;

/// <summary>
/// Shared helper methods for upload workflows in tools (MapManager, ReplayManager).
/// </summary>
public static class ToolUploadHelper
{
    /// <summary>
    /// Formats the upload stage message based on progress percentage and archive mode.
    /// The percentage itself is rendered separately next to the progress bar.
    /// </summary>
    /// <param name="entityName">The entity name (e.g., "maps" or "replays").</param>
    /// <param name="isZip">Whether the upload is a single zip file.</param>
    /// <param name="percent">The completion percentage.</param>
    /// <returns>A formatted status string.</returns>
    public static string FormatUploadStageMessage(string entityName, bool isZip, int percent)
    {
        if (!isZip && percent < ToolConstants.UploadStageCompressionThresholdPercent)
        {
            return $"Compressing {entityName}...";
        }

        if (percent < ToolConstants.UploadStageCloudThresholdPercent)
        {
            return "Uploading to cloud...";
        }

        if (percent < ToolConstants.UploadStageCompletePercent)
        {
            return "Finalizing cloud upload...";
        }

        return "Upload complete!";
    }

    /// <summary>
    /// Formats the error message when upload rate limit is exceeded.
    /// </summary>
    /// <param name="totalSizeBytes">Total bytes of file being uploaded.</param>
    /// <param name="usedBytes">Used bytes in period.</param>
    /// <param name="limitBytes">Total allowed limit bytes.</param>
    /// <returns>A human-readable error description.</returns>
    public static string FormatUploadLimitExceededMessage(long totalSizeBytes, long usedBytes, long limitBytes)
    {
        var bytesPerMb = (double)ConversionConstants.BytesPerMegabyte;
        var remainingMb = Math.Max(0, (limitBytes - usedBytes) / bytesPerMb);
        var fileMb = totalSizeBytes / bytesPerMb;
        var limitMb = limitBytes / bytesPerMb;

        return $"Upload limit exceeded. You have {remainingMb:F1} MB remaining of your {limitMb:F0} MB limit. This file requires {fileMb:F1} MB.";
    }
}
