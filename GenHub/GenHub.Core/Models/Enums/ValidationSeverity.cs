namespace GenHub.Core.Models.Validation;

/// <summary>
/// Defines the severity level of a validation issue.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>Informational message that doesn't affect validation.</summary>
    Info,

    /// <summary>Warning that should be noted but doesn't prevent validation.</summary>
    Warning,

    /// <summary>Error that prevents the target from being considered valid.</summary>
    Error,

    /// <summary>Critical error that indicates severe corruption or security issues.</summary>
    Critical,
}
