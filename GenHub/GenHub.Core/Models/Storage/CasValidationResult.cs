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
                                            i.IssueType == CasValidationIssueType.CorruptedObject ||
                                            i.IssueType == CasValidationIssueType.MissingObject);

    /// <summary>
    /// Gets or sets the total number of objects validated.
    /// </summary>
    public int ObjectsValidated { get; set; }

    /// <summary>
    /// Gets the number of objects that failed validation.
    /// </summary>
    public int ObjectsWithIssues => Issues.Count;
}
