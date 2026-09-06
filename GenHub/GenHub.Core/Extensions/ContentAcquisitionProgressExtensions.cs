using GenHub.Core.Helpers;
using GenHub.Core.Models.Content;

namespace GenHub.Core.Extensions;

/// <summary>
/// Extension methods for ContentAcquisitionProgress.
/// </summary>
public static class ContentAcquisitionProgressExtensions
{
    /// <summary>
    /// Formats a user-friendly progress status message with stage indicators.
    /// </summary>
    /// <param name="progress">The progress object to format.</param>
    /// <returns>A formatted progress status string.</returns>
    public static string FormatProgressStatus(this ContentAcquisitionProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (progress.TotalStages > 0 && progress.CurrentStage > 0)
        {
            return FormatStagedProgress(progress);
        }

        var phaseName = GetPhaseName(progress.Phase);
        return FormatPhaseProgress(progress, phaseName);
    }

    private static string FormatStagedProgress(ContentAcquisitionProgress progress)
    {
        string stagePart = $"{progress.CurrentStage}/{progress.TotalStages}";
        string description = !string.IsNullOrEmpty(progress.CurrentOperation) &&
                             !string.Equals(progress.CurrentOperation, progress.StageDescription, StringComparison.Ordinal)
            ? $"{progress.StageDescription}: {progress.CurrentOperation}"
            : progress.StageDescription;

        string percentPart = progress.StageProgress is > 0 and < 100
            ? $" ({progress.StageProgress:F0}%)"
            : string.Empty;

        string bottleneckPart = progress.IsBottleneck && !string.IsNullOrEmpty(progress.BottleneckReason)
            ? $" - {progress.BottleneckReason}"
            : string.Empty;

        string filesPart = progress.TotalFiles > 1
            ? $" [{progress.FilesProcessed}/{progress.TotalFiles}]"
            : string.Empty;

        return $"{stagePart} - {description}{percentPart}{filesPart}{bottleneckPart}";
    }

    private static string GetPhaseName(ContentAcquisitionPhase phase) => phase switch
    {
        ContentAcquisitionPhase.None => "Processing",
        ContentAcquisitionPhase.Downloading => "Downloading",
        ContentAcquisitionPhase.Extracting => "Extracting",
        ContentAcquisitionPhase.Copying => "Copying",
        ContentAcquisitionPhase.ValidatingManifest => "Validating manifest",
        ContentAcquisitionPhase.ValidatingFiles => "Validating files",
        ContentAcquisitionPhase.Delivering => "Installing",
        ContentAcquisitionPhase.StoringInCas => "Storing",
        ContentAcquisitionPhase.Completed => "Complete",
        _ => "Processing",
    };

    private static string FormatPhaseProgress(ContentAcquisitionProgress progress, string phaseName)
    {
        if (!string.IsNullOrEmpty(progress.CurrentOperation))
        {
            return $"{phaseName}: {progress.CurrentOperation}";
        }

        string percentText = progress.ProgressPercentage >= 0 ? $"{progress.ProgressPercentage:F0}%" : string.Empty;

        if (progress.TotalBytes > 0 && progress.Phase == ContentAcquisitionPhase.Downloading)
        {
            string downloaded = ByteFormatHelper.FormatBytes(progress.BytesProcessed);
            string total = ByteFormatHelper.FormatBytes(progress.TotalBytes);
            return !string.IsNullOrEmpty(percentText)
                ? $"{phaseName}: {downloaded} / {total} ({percentText})"
                : $"{phaseName}: {downloaded} / {total}";
        }

        if (progress.TotalFiles > 0)
        {
            int phasePercent = (int)((double)progress.FilesProcessed / progress.TotalFiles * 100);
            return $"{phaseName}: {progress.FilesProcessed}/{progress.TotalFiles} files ({phasePercent}%)";
        }

        return !string.IsNullOrEmpty(percentText) ? $"{phaseName}... {percentText}" : $"{phaseName}...";
    }
}
