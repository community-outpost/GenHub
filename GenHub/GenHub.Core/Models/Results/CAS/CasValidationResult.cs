namespace GenHub.Core.Models.Results.CAS;

using System.Linq;
using GenHub.Core.Models.Storage;

/// <summary>Result of CAS integrity validation operations.</summary>
public class CasValidationResult : ResultBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CasValidationResult"/> class.
    /// </summary>
    /// <param name="issues">The validation issues found.</param>
    /// <param name="objectsValidated">The total number of objects validated.</param>
    /// <param name="elapsed">The time taken for validation.</param>
    public CasValidationResult(
        IList<CasValidationIssue> issues,
        int objectsValidated,
        TimeSpan elapsed = default)
        : base(
            !issues.Any(i => i.IssueType == CasValidationIssueType.HashMismatch ||
                             i.IssueType == CasValidationIssueType.CorruptedObject ||
                             i.IssueType == CasValidationIssueType.MissingObject),
            issues.Where(i => i.IssueType == CasValidationIssueType.HashMismatch ||
                              i.IssueType == CasValidationIssueType.CorruptedObject ||
                              i.IssueType == CasValidationIssueType.MissingObject)
                  .Select(i => $"{i.IssueType}: {i.Details}")
                  .ToList(),
            elapsed)
    {
        Issues = issues ?? new List<CasValidationIssue>();
        ObjectsValidated = objectsValidated;
    }

    /// <summary>Initializes a new instance of the <see cref="CasValidationResult"/> class.</summary>
    public CasValidationResult()
        : this(new List<CasValidationIssue>(), 0)
    {
    }

    /// <summary>Gets or sets the validation issues found.</summary>
    public IList<CasValidationIssue> Issues { get; set; } = new List<CasValidationIssue>();

    /// <summary>Gets or sets the total number of objects validated.</summary>
    public int ObjectsValidated { get; set; }

    /// <summary>Gets the number of objects that failed validation.</summary>
    public int ObjectsWithIssues => Issues.Count;
}
