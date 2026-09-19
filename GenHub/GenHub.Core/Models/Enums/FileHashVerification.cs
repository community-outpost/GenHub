namespace GenHub.Core.Models.Enums;

/// <summary>
/// Outcome of comparing a file's content against an expected hash.
/// </summary>
public enum FileHashVerification
{
    /// <summary>
    /// The hash could not be computed, so nothing is known about the file's content.
    /// Callers must not treat this as evidence that the file changed.
    /// </summary>
    Failed = 0,

    /// <summary>
    /// The hash was computed and matches the expected value.
    /// </summary>
    Match = 1,

    /// <summary>
    /// The hash was computed and differs from the expected value.
    /// </summary>
    Mismatch = 2,
}
