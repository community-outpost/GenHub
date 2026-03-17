using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;

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

        // Use the new staged progress format if available
        if (progress.TotalStages > 0 && progress.CurrentStage > 0)
        {
            var stagePart = $"{progress.CurrentStage}/{progress.TotalStages}";
            var description = !string.IsNullOrEmpty(progress.StageDescription)
                ? progress.StageDescription
                : progress.CurrentOperation;

            // Add percentage for stages that have measurable progress
            var percentPart = progress.StageProgress > 0 && progress.StageProgress < 100
                ? $" ({progress.StageProgress:F0}%)"
                : string.Empty;

            // Add bottleneck indicator if applicable
            var bottleneckPart = progress.IsBottleneck && !string.IsNullOrEmpty(progress.BottleneckReason)
                ? $" - {progress.BottleneckReason}"
                : string.Empty;

            // Add file count if processing multiple files
            var filesPart = progress.TotalFiles > 1
                ? $" [{progress.FilesProcessed}/{progress.TotalFiles}]"
                : string.Empty;

            return $"{stagePart} - {description}{percentPart}{filesPart}{bottleneckPart}";
        }

        // Fallback to phase-based format
        var phaseName = progress.Phase switch
        {
            ContentAcquisitionPhase.Downloading => "Downloading",
            ContentAcquisitionPhase.Extracting => "Extracting",
            ContentAcquisitionPhase.Copying => "Copying",
            ContentAcquisitionPhase.ValidatingManifest => "Validating manifest",
            ContentAcquisitionPhase.ValidatingFiles => "Validating files",
            ContentAcquisitionPhase.Delivering => "Installing",
            ContentAcquisitionPhase.Completed => "Complete",
            _ => "Processing",
        };

        if (!string.IsNullOrEmpty(progress.CurrentOperation))
        {
            return $"{phaseName}: {progress.CurrentOperation}";
        }

        var percentText = progress.ProgressPercentage > 0 ? $"{progress.ProgressPercentage:F0}%" : string.Empty;

        if (progress.TotalFiles > 0)
        {
            var phasePercent = progress.TotalFiles > 0
                ? (int)((double)progress.FilesProcessed / progress.TotalFiles * 100)
                : 0;
            return $"{phaseName}: {progress.FilesProcessed}/{progress.TotalFiles} files ({phasePercent}%)";
        }

        return !string.IsNullOrEmpty(percentText) ? $"{phaseName}... {percentText}" : $"{phaseName}...";
    }
}
