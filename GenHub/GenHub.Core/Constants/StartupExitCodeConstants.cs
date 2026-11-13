namespace GenHub.Core.Constants;

/// <summary>
/// Windows NTSTATUS exit codes that a game client reports when it fails to start, and the
/// resource keys that explain them to the user.
/// </summary>
public static class StartupExitCodeConstants
{
    /// <summary>STATUS_ACCESS_VIOLATION (0xC0000005): the process read or wrote memory it does not own.</summary>
    public const int StatusAccessViolation = unchecked((int)0xC0000005);

    /// <summary>STATUS_INVALID_IMAGE_FORMAT (0xC000007B): an image is not valid for this system, often a 32-bit and 64-bit mismatch.</summary>
    public const int StatusInvalidImageFormat = unchecked((int)0xC000007B);

    /// <summary>STATUS_DLL_NOT_FOUND (0xC0000135): the loader could not find a DLL the executable imports.</summary>
    public const int StatusDllNotFound = unchecked((int)0xC0000135);

    /// <summary>STATUS_DLL_INIT_FAILED (0xC0000142): a DLL initialization routine failed.</summary>
    public const int StatusDllInitFailed = unchecked((int)0xC0000142);

    /// <summary>STATUS_STACK_BUFFER_OVERRUN (0xC0000409): a stack buffer overrun or fail-fast request ended the process.</summary>
    public const int StatusStackBufferOverrun = unchecked((int)0xC0000409);

    /// <summary>Format that renders an exit code as its eight-digit hexadecimal NTSTATUS value.</summary>
    public const string HexFormat = "X8";

    /// <summary>Resource key for an immediate exit with a known code. Arguments: decimal code, hexadecimal code, explanation.</summary>
    public const string ImmediateExitExplainedKey = "Launch.ExitCode.ImmediateExitExplained";

    /// <summary>Resource key for a launcher exit. Arguments: exit code, expected client name, explanation.</summary>
    public const string LauncherExitExplainedKey = "Launch.ExitCode.LauncherExitExplained";

    /// <summary>Resource key explaining <see cref="StatusAccessViolation"/>.</summary>
    public const string AccessViolationKey = "Launch.ExitCode.AccessViolation";

    /// <summary>Resource key explaining <see cref="StatusInvalidImageFormat"/>.</summary>
    public const string InvalidImageFormatKey = "Launch.ExitCode.InvalidImageFormat";

    /// <summary>Resource key explaining <see cref="StatusDllNotFound"/>.</summary>
    public const string DllNotFoundKey = "Launch.ExitCode.DllNotFound";

    /// <summary>Resource key explaining <see cref="StatusDllInitFailed"/>.</summary>
    public const string DllInitFailedKey = "Launch.ExitCode.DllInitFailed";

    /// <summary>Resource key explaining <see cref="StatusStackBufferOverrun"/>.</summary>
    public const string StackBufferOverrunKey = "Launch.ExitCode.StackBufferOverrun";
}
