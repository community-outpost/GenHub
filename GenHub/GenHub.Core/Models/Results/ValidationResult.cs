using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Models.Validation;

namespace GenHub.Core.Models.Results;

/// <summary>
/// Encapsulates the result of a validation operation for a game version or installation.
/// </summary>
public class ValidationResult(string validatedTargetId, List<ValidationIssue> issues)
{
    /// <summary>
    /// Gets the unique ID of the target that was validated (e.g., a GameVersion ID or a GameInstallation ID).
    /// </summary>
    public string ValidatedTargetId { get; } = validatedTargetId;

    /// <summary>
    /// Gets the list of all issues found during validation.
    /// </summary>
    public IReadOnlyList<ValidationIssue> Issues { get; } = issues ?? new List<ValidationIssue>();

    /// <summary>
    /// Gets a value indicating whether the target is considered valid.
    /// </summary>
    public bool IsValid => !Issues.Any(i => IsCriticalIssue(i.IssueType));

    /// <summary>
    /// Gets the count of critical issues that prevent the target from being considered valid.
    /// </summary>
    public int CriticalIssueCount => Issues.Count(i => IsCriticalIssue(i.IssueType));

    /// <summary>
    /// Gets the count of warning issues that don't prevent validity but should be noted.
    /// </summary>
    public int WarningIssueCount => Issues.Count(i => !IsCriticalIssue(i.IssueType));

    private static bool IsCriticalIssue(ValidationIssueType issueType)
    {
        return issueType switch
        {
            ValidationIssueType.MissingFile => true,
            ValidationIssueType.CorruptedFile => true,
            ValidationIssueType.MismatchedFileSize => true,
            ValidationIssueType.DirectoryMissing => true,
            _ => false
        };
    }
}
