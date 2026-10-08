using System;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents the current stage of the build process.
/// </summary>
public enum BuildStage
{
    /// <summary>
    /// Loading configuration and initializing build structure.
    /// </summary>
    Loading,

    /// <summary>
    /// Processing and converting source files.
    /// </summary>
    Processing,

    /// <summary>
    /// Converting images and other assets.
    /// </summary>
    Converting,

    /// <summary>
    /// Staging files into temporary build directories.
    /// </summary>
    Staging,

    /// <summary>
    /// Packing staged files into an archive payload.
    /// </summary>
    Packing,

    /// <summary>
    /// Compressing an archive payload into its final file (.big, .zip).
    /// </summary>
    Compressing,

    /// <summary>
    /// Creating archive files (.big, .zip).
    /// </summary>
    Archiving,

    /// <summary>
    /// Verifying archive integrity against expected hashes.
    /// </summary>
    Verifying,

    /// <summary>
    /// Hashing files while generating the content manifest.
    /// </summary>
    Hashing,

    /// <summary>
    /// Storing manifest content in CAS.
    /// </summary>
    Storing,

    /// <summary>
    /// Build completed successfully.
    /// </summary>
    Complete,
}

/// <summary>
/// Represents progress information during a build operation.
/// </summary>
public class BuildProgress
{
    /// <summary>
    /// Gets or sets the current build step description.
    /// </summary>
    public string CurrentStep { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current build index (stage).
    /// </summary>
    public BuildIndex? CurrentIndex { get; set; }

    /// <summary>
    /// Gets or sets the current build stage.
    /// </summary>
    public BuildStage CurrentStage { get; set; }

    /// <summary>
    /// Gets or sets the current file being processed.
    /// </summary>
    public string CurrentFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional message describing the current operation.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets the number of files processed.
    /// </summary>
    public int ProcessedFiles { get; set; }

    /// <summary>
    /// Gets or sets the total number of files to process.
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Gets or sets the progress percentage (0.0 to 100.0).
    /// </summary>
    public double PercentComplete { get; set; }

    /// <summary>
    /// Gets or sets the estimated time remaining.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Gets or sets the number of items processed (legacy).
    /// </summary>
    public int ProcessedItems { get; set; }

    /// <summary>
    /// Gets or sets the total number of items (legacy).
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Gets or sets the progress percentage (0.0 to 1.0) (legacy).
    /// </summary>
    public double Percentage { get; set; }
}
