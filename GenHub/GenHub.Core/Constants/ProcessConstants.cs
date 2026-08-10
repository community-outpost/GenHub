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
    /// Minimum time in milliseconds a Windows launcher stub's child is given to appear,
    /// measured from launch, before it is searched for.
    /// </summary>
    /// <remarks>
    /// Formerly the fixed delay before the single exited-yet check. Exit detection now
    /// waits on the process itself (see <see cref="PostSpawnExitDetectionWindowMs"/>),
    /// which can observe a stub exiting well before 500 ms; this floor preserves the time
    /// the fixed delay always gave the spawned game process to register.
    /// </remarks>
    public const int LauncherDetectionDelayMs = 500;

    /// <summary>
    /// Bounded window in milliseconds during which a just-started game process is watched
    /// for an early exit before the launch is reported successful.
    /// </summary>
    /// <remarks>
    /// Sized from measurement rather than guessed. The native Zero Hour client aborting
    /// initialisation in an empty workspace exits 1 after roughly 0.8–0.9 s once warm
    /// (macOS, Apple Silicon), so three seconds is ~3x the observed abort, absorbing slow
    /// disks and emulation. The very first run of a freshly copied binary can take 3–5 s
    /// because macOS validates the new inode before execution; an abort that slow falls
    /// outside the window and is reported through the process-exited event instead of the
    /// launch result.
    /// </remarks>
    public const int PostSpawnExitDetectionWindowMs = 3000;

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
}