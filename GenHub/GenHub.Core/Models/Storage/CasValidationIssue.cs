namespace GenHub.Core.Models.Storage;

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
    public CasValidationIssueType IssueType { get; set; } = CasValidationIssueType.Warning;

    /// <summary>
    /// Gets or sets additional details about the issue.
    /// </summary>
    public string Details { get; set; } = string.Empty;
}