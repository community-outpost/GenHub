namespace GenHub.Core.Models.Results;

using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Models.Validation;

/// <summary>Encapsulates the result of a validation operation for a game version or installation.</summary>
public class ValidationResult(string validatedTargetId, List<ValidationIssue>? issues, TimeSpan elapsed = default)
    : ResultBase(DetermineSuccess(issues), ExtractErrorMessages(issues), elapsed)
{
    /// <summary>Gets the unique ID of the target that was validated (e.g., a GameClient ID or a GameInstallation ID).</summary>
    public string ValidatedTargetId { get; } = validatedTargetId;

    /// <summary>Gets the list of all issues found during validation.</summary>
    public IReadOnlyList<ValidationIssue> Issues { get; } = issues ?? new List<ValidationIssue>();

    /// <summary>Gets a value indicating whether the target is considered valid.</summary>
    public bool IsValid => Success;

    /// <summary>Gets the count of critical issues that prevent the target from being considered valid.</summary>
    public int CriticalIssueCount => Issues.Count(i => i.Severity == ValidationSeverity.Error || i.Severity == ValidationSeverity.Critical);

    /// <summary>Gets the count of warning issues that don't prevent validity but should be noted.</summary>
    public int WarningIssueCount => Issues.Count(i => i.Severity == ValidationSeverity.Warning);

    /// <summary>Gets the count of informational issues.</summary>
    public int InfoIssueCount => Issues.Count(i => i.Severity == ValidationSeverity.Info);

    /// <summary>Determines if validation was successful based on the presence of critical issues.</summary>
    private static bool DetermineSuccess(List<ValidationIssue>? issues)
    {
        if (issues == null || issues.Count == 0)
        {
            return true;
        }

        // Validation fails if there are any Error or Critical severity issues
        return !issues.Any(i => i.Severity == ValidationSeverity.Error || i.Severity == ValidationSeverity.Critical);
    }

    /// <summary>Extracts error messages from critical validation issues.</summary>
    private static List<string> ExtractErrorMessages(List<ValidationIssue>? issues)
    {
        if (issues == null || issues.Count == 0)
        {
            return new List<string>();
        }

        // Only include messages from Error or Critical severity issues
        return issues
            .Where(i => i.Severity == ValidationSeverity.Error || i.Severity == ValidationSeverity.Critical)
            .Select(i => i.Message)
            .ToList();
    }
}