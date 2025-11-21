namespace GenHub.Core.Models.Validation;

/// <summary>
/// Represents a single issue found during the validation of a game installation or version.
/// </summary>
public class ValidationIssue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationIssue"/> class.
    /// </summary>
    public ValidationIssue()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationIssue"/> class with a message and severity.
    /// </summary>
    /// <param name="message">The message describing the issue.</param>
    /// <param name="severity">The severity of the issue.</param>
    public ValidationIssue(string message, ValidationSeverity severity)
    {
        Message = message;
        Severity = severity;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationIssue"/> class with all properties.
    /// </summary>
    /// <param name="message">The message describing the issue.</param>
    /// <param name="severity">The severity of the issue.</param>
    /// <param name="path">The relative path to the file or directory that has an issue.</param>
    /// <param name="expected">The expected value (e.g., hash, file size).</param>
    /// <param name="actual">The actual value found.</param>
    /// <param name="details">Additional context or details about the issue.</param>
    public ValidationIssue(string message, ValidationSeverity severity, string path = "", string? expected = null, string? actual = null, string? details = null)
    {
        Message = message;
        Severity = severity;
        Path = path;
        Expected = expected;
        Actual = actual;
        Details = details;
    }

    /// <summary>
    /// Gets or sets the type of the validation issue.
    /// </summary>
    public ValidationIssueType IssueType { get; set; }

    /// <summary>
    /// Gets or sets the relative path to the file or directory that has an issue.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a human-readable message describing the issue.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the expected value (e.g., hash, file size).
    /// </summary>
    public string? Expected { get; set; }

    /// <summary>
    /// Gets or sets the actual value found.
    /// </summary>
    public string? Actual { get; set; }

    /// <summary>
    /// Gets or sets the severity level of the issue.
    /// </summary>
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Error;

    /// <summary>
    /// Gets or sets additional context or details about the issue.
    /// </summary>
    public string? Details { get; set; }
}