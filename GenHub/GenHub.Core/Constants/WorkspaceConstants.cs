using GenHub.Core.Models.Enums;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants related to workspace management and configuration.
/// </summary>
public static class WorkspaceConstants
{
    /// <summary>
    /// The default workspace strategy to use when none is specified.
    /// Default is HardLink as it provides space-efficient file management with good compatibility.
    /// </summary>
    public const WorkspaceStrategy DefaultWorkspaceStrategy = WorkspaceStrategy.HardLink;

    /// <summary>
    /// Guidance message appended to errors when zero-copy hard links or symlinks cannot be created.
    /// </summary>
    public const string ZeroCopyElevationGuidance =
        "To use zero-copy workspaces without copying game files, ensure GenHub has permission to create links (on Windows, enable Developer Mode or run as Administrator).";

    /// <summary>
    /// Filename of the EA logo video.
    /// </summary>
    public const string EaLogoBik = "EA_LOGO.BIK";

    /// <summary>
    /// Filename of the 640x480 EA logo video.
    /// </summary>
    public const string EaLogo640Bik = "EA_LOGO640.BIK";

    /// <summary>
    /// Filename of the launch receipt metadata file.
    /// </summary>
    public const string LaunchReceiptFile = "launch.receipt.json";

    /// <summary>
    /// Filename of the release crash info text file.
    /// </summary>
    public const string ReleaseCrashInfoFile = "ReleaseCrashInfo.txt";

    /// <summary>
    /// Prefix for GenHub runtime workspace artifacts.
    /// </summary>
    public const string RuntimeArtifactPrefix = ".gh";

    /// <summary>
    /// File extension for log files.
    /// </summary>
    public const string LogFileExtension = ".log";

    /// <summary>
    /// File extension for temporary files.
    /// </summary>
    public const string TmpFileExtension = ".tmp";

    /// <summary>
    /// Delta reason for optional files skipped by configuration.
    /// </summary>
    public const string OptionalFileSkippedReason = "Optional file skipped or removed by configuration";
}
