using GenHub.Core.Helpers;
using GenHub.Core.Models.Content;
using System;

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
        var desc = progress.StageDescription ?? string.Empty;
        var op = progress.CurrentOperation ?? string.Empty;

        // If op starts with the stage description or its first word, avoid repeating it
        if (!string.IsNullOrEmpty(desc) && !string.IsNullOrEmpty(op))
        {
            if (op.StartsWith(desc, StringComparison.OrdinalIgnoreCase) &&
                (op.Length == desc.Length || op[desc.Length] is ':' or ' '))
            {
                op = op.Substring(desc.Length).TrimStart(':', ' ');
            }
            else
            {
                var firstWord = desc.Split(' ')[0];
                if (firstWord.Length > 2 &&
                    op.StartsWith(firstWord, StringComparison.OrdinalIgnoreCase) &&
                    (op.Length == firstWord.Length || op[firstWord.Length] is ':' or ' '))
                {
                    op = op.Substring(firstWord.Length).TrimStart(':', ' ');
                }
            }
        }

        string description = !string.IsNullOrEmpty(op) &&
                             !string.Equals(op, desc, StringComparison.OrdinalIgnoreCase)
            ? $"{desc}: {op}"
            : desc;

        string percentPart = progress.StageProgress is > 0 and < 100 && progress.TotalFiles <= 1
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

    /// <summary>
    /// Formats phase-level progress into a human-readable string.
    /// Note: Phase prefix stripping expects operation producers to follow the standard convention
    /// of separating the phase name with ': ' or a space (or end of string), e.g. "Downloading: file.zip".
    /// </summary>
    private static string FormatPhaseProgress(ContentAcquisitionProgress progress, string phaseName)
    {
        if (!string.IsNullOrEmpty(progress.CurrentOperation))
        {
            var op = progress.CurrentOperation;
            if (op.StartsWith(phaseName, StringComparison.OrdinalIgnoreCase) &&
                (op.Length == phaseName.Length || op[phaseName.Length] is ':' or ' '))
            {
                op = op.Substring(phaseName.Length).TrimStart(':', ' ');
            }

            if (!string.IsNullOrWhiteSpace(op))
            {
                return $"{phaseName}: {op}";
            }
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
