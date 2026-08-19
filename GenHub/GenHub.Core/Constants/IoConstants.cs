namespace GenHub.Core.Constants;

/// <summary>
/// Constants for input/output operations.
/// </summary>
public static class IoConstants
{
    /// <summary>
    /// Default buffer size for file operations (4KB).
    /// </summary>
    public const int DefaultFileBufferSize = 4096;

    /// <summary>
    /// How many times a path may be re-resolved while following symbolic links whose targets are
    /// themselves reached through links. Bounds the walk on a filesystem that contains a cycle.
    /// </summary>
    public const int MaxSymbolicLinkResolutionDepth = 8;
}