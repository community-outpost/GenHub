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
#pragma warning restore SA1310 // Field names should not contain underscore
}
