using GenHub.Core.Helpers;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using System;

namespace GenHub.Features.Content.Services.Common;

/// <summary>
/// Builds synchronous progress adapters that map file-level download progress onto
/// aggregate content-acquisition progress shared by all content deliverers.
/// </summary>
public static class DownloadProgressAdapter
{
    /// <summary>
    /// Creates a download progress adapter reporting aggregate acquisition progress for one file.
    /// </summary>
    /// <param name="progress">The aggregate progress sink, or null when progress is not tracked.</param>
    /// <param name="relativePath">The manifest-relative path of the file being downloaded.</param>
    /// <param name="fileIndex">The 1-based index of the file within the download set.</param>
    /// <param name="totalFiles">The total number of files in the download set.</param>
    /// <param name="previousFilesBytes">Bytes already downloaded for earlier files.</param>
    /// <param name="totalBytesAllFiles">Total expected bytes across all files, or zero when unknown.</param>
    /// <returns>A download progress adapter, or null when <paramref name="progress"/> is null.</returns>
    public static IProgress<DownloadProgress>? CreateDownloadAdapter(
        IProgress<ContentAcquisitionProgress>? progress,
        string relativePath,
        int fileIndex,
        int totalFiles,
        long previousFilesBytes,
        long totalBytesAllFiles)
    {
        if (progress == null)
        {
            return null;
        }

        return new SynchronousProgress<DownloadProgress>(downloadProgress =>
        {
            double currentProgress = 0.0;
            long aggregateBytesProcessed = 0L;
            long aggregateTotalBytes = 0L;

            if (totalBytesAllFiles > 0)
            {
                aggregateBytesProcessed = previousFilesBytes + downloadProgress.BytesReceived;
                aggregateTotalBytes = totalBytesAllFiles;
                currentProgress = Math.Clamp((double)aggregateBytesProcessed / totalBytesAllFiles * 100.0, 0, 100);
            }
            else
            {
                double fileProgressRange = 100.0 / totalFiles;
                double baseProgress = (fileIndex - 1) * fileProgressRange;
                currentProgress = Math.Clamp(baseProgress + (downloadProgress.Percentage / 100.0 * fileProgressRange), 0, 100);
                aggregateBytesProcessed = downloadProgress.BytesReceived;
                aggregateTotalBytes = downloadProgress.TotalBytes;
            }

            progress.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Downloading,
                ProgressPercentage = currentProgress,
                CurrentOperation = totalFiles > 1
                    ? $"{relativePath} ({fileIndex}/{totalFiles}) - {downloadProgress.Percentage:F0}% ({downloadProgress.FormattedSpeed})"
                    : $"{relativePath} - {downloadProgress.Percentage:F0}% ({downloadProgress.FormattedSpeed})",
                FilesProcessed = fileIndex - 1,
                TotalFiles = totalFiles,
                TotalBytes = aggregateTotalBytes,
                BytesProcessed = aggregateBytesProcessed,
                CurrentFile = relativePath,
            });
        });
    }

    /// <summary>
    /// Reports the initial connecting progress event for one file.
    /// </summary>
    /// <param name="progress">The aggregate progress sink, or null when progress is not tracked.</param>
    /// <param name="relativePath">The manifest-relative path of the file being downloaded.</param>
    /// <param name="fileIndex">The 1-based index of the file within the download set.</param>
    /// <param name="totalFiles">The total number of files in the download set.</param>
    /// <param name="previousFilesBytes">Bytes already downloaded for earlier files.</param>
    /// <param name="totalBytesAllFiles">Total expected bytes across all files, or zero when unknown.</param>
    public static void ReportConnecting(
        IProgress<ContentAcquisitionProgress>? progress,
        string relativePath,
        int fileIndex,
        int totalFiles,
        long previousFilesBytes,
        long totalBytesAllFiles)
    {
        var startingProgress = totalBytesAllFiles > 0
            ? Math.Clamp((double)previousFilesBytes / totalBytesAllFiles * 100.0, 0, 100)
            : (double)(fileIndex - 1) / totalFiles * 100;

        progress?.Report(new ContentAcquisitionProgress
        {
            Phase = ContentAcquisitionPhase.Downloading,
            ProgressPercentage = startingProgress,
            CurrentOperation = totalFiles > 1
                ? $"Connecting to download {relativePath} ({fileIndex}/{totalFiles})..."
                : $"Connecting to download {relativePath}...",
            CurrentFile = relativePath,
            FilesProcessed = fileIndex - 1,
            TotalFiles = totalFiles,
            TotalBytes = totalBytesAllFiles > 0 ? totalBytesAllFiles : 0,
            BytesProcessed = previousFilesBytes,
        });
    }
}
