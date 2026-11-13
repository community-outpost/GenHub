namespace GenHub.Core.Models.Storage;

/// <summary>
/// Types of validation issues that can occur in CAS.
/// </summary>
public enum CasValidationIssueType
{
    /// <summary>
    /// The computed hash doesn't match the expected hash.
    /// </summary>
    HashMismatch,

    /// <summary>
    /// The object file is corrupted or unreadable.
    /// </summary>
    CorruptedObject,

    /// <summary>
    /// The object file is missing from the filesystem.
    /// </summary>
    MissingObject,

    /// <summary>
    /// Critical validation error that requires immediate attention.
    /// </summary>
    Critical,

    /// <summary>
    /// General validation warning.
    /// </summary>
    Warning,
}
