namespace GenHub.Windows.Constants;

/// <summary>
/// Windows Win32 error codes for file system operations.
/// </summary>
internal static class Win32ErrorCodes
{
    /// <summary>
    /// The request is not supported (file system may not support hard links).
    /// </summary>
    public const int ErrorInvalidFunction = 1;

    /// <summary>
    /// The file does not exist.
    /// </summary>
    public const int ErrorFileNotFound = 2;

    /// <summary>
    /// The path does not exist.
    /// </summary>
    public const int ErrorPathNotFound = 3;

    /// <summary>
    /// Access denied (check file permissions).
    /// </summary>
    public const int ErrorAccessDenied = 5;

    /// <summary>
    /// Cannot create hard link across different volumes/drives.
    /// </summary>
    public const int ErrorNotSameDevice = 17;

    /// <summary>
    /// The file is in use by another process.
    /// </summary>
    public const int ErrorSharingViolation = 32;

    /// <summary>
    /// The destination file already exists.
    /// </summary>
    public const int ErrorFileExists = 80;

    /// <summary>
    /// The destination file already exists.
    /// </summary>
    public const int ErrorAlreadyExists = 183;

    /// <summary>
    /// Maximum number of hard links (1023) exceeded for this file.
    /// </summary>
    public const int ErrorTooManyLinks = 1142;

    /// <summary>
    /// Gets a human-readable error message for a Win32 error code.
    /// </summary>
    /// <param name="errorCode">The Win32 error code.</param>
    /// <returns>A descriptive error message.</returns>
    public static string GetErrorMessage(int errorCode)
    {
        return errorCode switch
        {
            ErrorInvalidFunction => "The request is not supported (file system may not support hard links)",
            ErrorFileNotFound => "The target file does not exist",
            ErrorPathNotFound => "The target path does not exist",
            ErrorAccessDenied => "Access denied (check file permissions)",
            ErrorNotSameDevice => "Cannot create hard link across different volumes/drives",
            ErrorSharingViolation => "The file is in use by another process",
            ErrorFileExists => "The destination file already exists",
            ErrorAlreadyExists => "The destination file already exists",
            ErrorTooManyLinks => "Maximum number of hard links (1023) exceeded for this file",
            _ => $"Win32 error code {errorCode}"
        };
    }
}
