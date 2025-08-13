using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Storage;

/// <summary>
/// Result of CAS integrity validation operations.
/// </summary>
public class CasValidationResult
{
    /// <summary>
    /// Gets or sets the validation issues found.
    /// </summary>
    public IList<CasValidationIssue> Issues { get; set; } = new List<CasValidationIssue>();

    /// <summary>
    /// Gets a value indicating whether the validation passed (no critical issues).
    /// </summary>
    public bool IsValid => !Issues.Any(i => i.IssueType == CasValidationIssueType.HashMismatch ||
                                            i.IssueType == CasValidationIssueType.CorruptedObject);

    /// <summary>
    /// Gets or sets the total number of objects validated.
    /// </summary>
    public int ObjectsValidated { get; set; }

    /// <summary>
    /// Gets the number of objects that failed validation.
    /// </summary>
    public int ObjectsWithIssues => Issues.Count;
}

/// <summary>
/// Represents a validation issue found during CAS integrity checks.
/// </summary>
public class CasValidationIssue
{
    /// <summary>
    /// Gets or sets the path to the object with issues.
    /// </summary>
    public string ObjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the expected hash of the object.
    /// </summary>
    public string ExpectedHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the actual hash computed for the object.
    /// </summary>
    public string ActualHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of validation issue.
    /// </summary>
    public CasValidationIssueType IssueType { get; set; }

    /// <summary>
    /// Gets or sets additional details about the issue.
    /// </summary>
    public string Details { get; set; } = string.Empty;
}
