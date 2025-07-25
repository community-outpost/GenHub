namespace GenHub.Core.Models.Validation;

/// <summary>
/// Defines the type of issue found during validation.
/// </summary>
public enum ValidationIssueType
{
    /// <summary>
    /// A required file is missing.
    /// </summary>
    MissingFile,

    /// <summary>
    /// A file's content (hash) does not match the expected value, indicating corruption or modification.
    /// </summary>
    CorruptedFile,

    /// <summary>
    /// A file's size does not match the expected value.
    /// </summary>
    MismatchedFileSize,

    /// <summary>
    /// A known addon or third-party utility was detected.
    /// </summary>
    AddonDetected,

    /// <summary>
    /// An unexpected file was found that is not part of the standard installation or a known addon.
    /// </summary>
    UnexpectedFile,

    /// <summary>
    /// A required directory is missing.
    /// </summary>
    DirectoryMissing,

    /// <summary>
    /// A file or directory is not accessible due to permission issues.
    /// </summary>
    AccessDenied,

    /// <summary>
    /// There is insufficient disk space to complete the operation.
    /// </summary>
    InsufficientSpace,
}
