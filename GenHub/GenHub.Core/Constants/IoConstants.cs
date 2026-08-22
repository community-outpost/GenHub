namespace GenHub.Core.Constants;

/// <summary>
/// Constants for input/output operations.
/// </summary>
public static class IoConstants
{
    /// <summary>
    /// Default buffer size for file operations (64KB).
    /// </summary>
    public const int DefaultFileBufferSize = 65536;

    /// <summary>
    /// How many times a path may be re-resolved while following symbolic links whose targets are
    /// themselves reached through links. Bounds the walk on a filesystem that contains a cycle.
    /// </summary>
    public const int MaxSymbolicLinkResolutionDepth = 8;

    /// <summary>
    /// Suffix that marks a staging file written beside its final location so the existing file is
    /// only replaced once the write has completed. The name it is appended to is random rather than
    /// the destination name, which keeps a staged write from outgrowing the Windows path limit.
    /// </summary>
    public const string StagingFileSuffix = ".genhub-staging";
}