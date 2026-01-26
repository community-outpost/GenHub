namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents progress during the content acquisition phase where packages are downloaded,
/// extracted, and scanned to transform manifests from package-level to file-level operations.
/// </summary>
public class ContentAcquisitionProgress
{
    /// <summary>
    /// Gets or sets the current phase of acquisition.
    /// </summary>
    public ContentAcquisitionPhase Phase { get; set; } = ContentAcquisitionPhase.Downloading;

    /// <summary>
    /// Gets or sets the overall progress percentage (0-100) for the current phase.
    /// </summary>
    public double ProgressPercentage { get; set; }

    /// <summary>
    /// Gets or sets a description of the current operation being performed.
    /// </summary>
    public string CurrentOperation { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of bytes processed (downloaded or extracted).
    /// </summary>
    public long BytesProcessed { get; set; }

    /// <summary>
    /// Gets or sets the total number of bytes to process.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Gets or sets the number of files processed during scanning/transformation.
    /// </summary>
    public int FilesProcessed { get; set; }

    /// <summary>
    /// Gets or sets the total number of files to process.
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Gets or sets the current file being processed (for detailed progress tracking).
    /// </summary>
    public string CurrentFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the estimated time remaining for the current phase.
    /// </summary>
    public TimeSpan EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Gets or sets the current stage number (1-based).
    /// Values are automatically clamped between 1 and TotalStages.
    /// </summary>
    public int CurrentStage
    {
        get => _currentStage;
        set => _currentStage = Math.Clamp(value, 1, TotalStages);
    }

    private int _currentStage = 1;

    /// <summary>
    /// Gets or sets the total number of stages in the acquisition process.
    /// Defaults to 5 and is clamped to a minimum of 1.
    /// </summary>
    public int TotalStages
    {
        get => _totalStages;
        set
        {
            _totalStages = Math.Max(1, value);

            // Re-clamp CurrentStage in case TotalStages was reduced below CurrentStage
            CurrentStage = _currentStage;
        }
    }

    private int _totalStages = 5;

    /// <summary>
    /// Gets or sets the progress within the current stage (0-100).
    /// Values are automatically clamped to the valid range.
    /// </summary>
    public double StageProgress
    {
        get => _stageProgress;
        set => _stageProgress = Math.Clamp(value, 0, 100);
    }

    private double _stageProgress;

    /// <summary>
    /// Gets or sets the description of the current stage.
    /// </summary>
    public string StageDescription { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the time elapsed since the last progress update.
    /// Used to detect stalled operations and provide feedback.
    /// </summary>
    public TimeSpan TimeSinceLastUpdate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the current operation is a bottleneck (e.g., hash calculation).
    /// </summary>
    public bool IsBottleneck { get; set; }

    /// <summary>
    /// Gets or sets a message explaining why the operation is slow (if IsBottleneck is true).
    /// </summary>
    public string? BottleneckReason { get; set; }

    /// <summary>
    /// Gets the formatted stage indicator (e.g., "2/5").
    /// </summary>
    public string StageIndicator => $"{CurrentStage}/{TotalStages}";

    /// <summary>
    /// Gets a formatted progress string combining stage and percentage.
    /// </summary>
    public string FormattedProgress
    {
        get
        {
            var stagePart = StageIndicator;
            var percentPart = StageProgress > 0 ? $" ({StageProgress:F0}%)" : string.Empty;
            var description = !string.IsNullOrEmpty(StageDescription) ? $" - {StageDescription}" : string.Empty;
            return $"{stagePart}{description}{percentPart}";
        }
    }
}