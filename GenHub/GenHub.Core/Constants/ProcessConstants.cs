#pragma warning disable SA1310 // Field names should not contain underscore

namespace GenHub.Core.Constants;

/// <summary>
/// Process and system constants.
/// </summary>
public static class ProcessConstants
{
    // Exit codes

    /// <summary>
    /// Standard exit code indicating successful execution.
    /// </summary>
    public const int ExitCodeSuccess = 0;

    /// <summary>
    /// Standard exit code indicating general error.
    /// </summary>
    public const int ExitCodeGeneralError = 1;

    /// <summary>
    /// Exit code indicating invalid arguments.
    /// </summary>
    public const int ExitCodeInvalidArguments = 2;

    /// <summary>
    /// Exit code indicating file not found.
    /// </summary>
    public const int ExitCodeFileNotFound = 3;

    /// <summary>
    /// Exit code indicating access denied.
    /// </summary>
    public const int ExitCodeAccessDenied = 5;

    // Windows API constants

    /// <summary>
    /// Windows API constant for restoring a minimized window.
    /// </summary>
    public const int SW_RESTORE = 9;

    /// <summary>
    /// Windows API constant for showing a window in its current state.
    /// </summary>
    public const int SW_SHOW = 5;

    /// <summary>
    /// Windows API constant for minimizing a window.
    /// </summary>
    public const int SW_MINIMIZE = 6;

    /// <summary>
    /// Windows API constant for maximizing a window.
    /// </summary>
    public const int SW_MAXIMIZE = 3;

    // Process discovery and timing constants

    /// <summary>
    /// Delay in milliseconds to wait before checking if a process has exited (launcher detection).
    /// </summary>
    public const int LauncherDetectionDelayMs = 500;

    /// <summary>
    /// Interval in milliseconds for process cleanup / reconciliation background task.
    /// </summary>
    public const int ProcessCleanupIntervalMs = 300_000; // 5 minutes

    /// <summary>
    /// Maximum number of attempts to discover a Steam-launched process.
    /// </summary>
    public const int SteamProcessDiscoveryMaxAttempts = 240;

    /// <summary>
    /// Delay in milliseconds between Steam process discovery attempts.
    /// </summary>
    public const int SteamProcessDiscoveryDelayMs = 500;

    /// <summary>
    /// Threshold in seconds to consider a process exit as "early" or "immediate".
    /// </summary>
    public const double EarlyExitThresholdSeconds = 10.0;

    /// <summary>
    /// How many characters of a process name a Unix kernel keeps. Linux stores it in a
    /// TASK_COMM_LEN buffer and macOS in a MAXCOMLEN one, both of which leave room for fifteen
    /// characters and a terminator, and the truncated value is what process enumeration matches on.
    /// </summary>
    public const int UnixProcessNameMaxLength = 15;

    /// <summary>
    /// How long to wait for a launcher's expected child process to appear. Measured spawn latency
    /// for the Easy Anti-Cheat bootstrapper is well under two seconds. Adoption dates a candidate
    /// against the launcher's own start time rather than <see cref="EarlyExitThresholdSeconds"/>,
    /// so this may be raised as far as a slow bootstrapper needs.
    /// </summary>
    public const int SpawnedChildDiscoveryTimeoutMs = 10_000;

    /// <summary>
    /// Interval in milliseconds between polls for a launcher's expected child process.
    /// </summary>
    public const int SpawnedChildPollIntervalMs = 100;

    /// <summary>
    /// How long to wait for an abandoned launcher to exit after it is killed. The launcher is
    /// already being torn down on a cancelled launch, so this only bounds the cleanup.
    /// </summary>
    public const int AbandonedLauncherKillWaitMs = 2_000;

    /// <summary>
    /// How long to keep polling for the expected child after the launcher itself exits cleanly.
    /// Covers the race between the child being spawned and becoming enumerable, without waiting
    /// out <see cref="SpawnedChildDiscoveryTimeoutMs"/> once the launcher is known to be gone.
    /// </summary>
    public const int LauncherExitGracePeriodMs = 1_000;
}