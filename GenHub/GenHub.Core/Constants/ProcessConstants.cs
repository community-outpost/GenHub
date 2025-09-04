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
#pragma warning disable SA1310 // Field names should not contain underscore

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

    // Process priority constants

    /// <summary>
    /// Process priority class for real-time priority.
    /// </summary>
    public const int REALTIME_PRIORITY_CLASS = 0x00000100;

    /// <summary>
    /// Process priority class for high priority.
    /// </summary>
    public const int HIGH_PRIORITY_CLASS = 0x00000080;

    /// <summary>
    /// Process priority class for above normal priority.
    /// </summary>
    public const int ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000;

    /// <summary>
    /// Process priority class for normal priority.
    /// </summary>
    public const int NORMAL_PRIORITY_CLASS = 0x00000020;

    /// <summary>
    /// Process priority class for below normal priority.
    /// </summary>
    public const int BELOW_NORMAL_PRIORITY_CLASS = 0x00004000;

    /// <summary>
    /// Process priority class for idle priority.
    /// </summary>
    public const int IDLE_PRIORITY_CLASS = 0x00000040;
#pragma warning restore SA1310 // Field names should not contain underscore
}
