using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Models.Validation;

namespace GenHub.Core.Models.Results;

/// <summary>
/// Encapsulates the result of a validation operation for a game version or installation.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationResult"/> class.
    /// </summary>
    /// <param name="validatedTargetId">The unique ID of the validated target (e.g., GameVersion ID or GameInstallation ID).</param>
    /// <param name="issues">The list of issues found.</param>
    public ValidationResult(string validatedTargetId, List<ValidationIssue> issues)
    {
        ValidatedTargetId = validatedTargetId ?? throw new System.ArgumentNullException(nameof(validatedTargetId));
        Issues = issues ?? throw new System.ArgumentNullException(nameof(issues));
    }

    /// <summary>
    /// Gets the unique ID of the target that was validated (e.g., a GameVersion ID or a GameInstallation ID).
    /// </summary>
    public string ValidatedTargetId { get; }

    /// <summary>
    /// Gets the list of all issues found during validation.
    /// </summary>
    public IReadOnlyList<ValidationIssue> Issues { get; }

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
