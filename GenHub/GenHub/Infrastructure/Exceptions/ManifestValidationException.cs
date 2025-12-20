using System;

namespace GenHub.Infrastructure.Exceptions;

/// <summary>
/// Exception thrown when a manifest fails validation checks.
/// </summary>
public class ManifestValidationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestValidationException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ManifestValidationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestValidationException"/> class.
    /// </summary>
    /// <param name="manifestId">The ID of the manifest that failed validation.</param>
    /// <param name="validationError">The specific validation error message.</param>
    public ManifestValidationException(string manifestId, string validationError)
        : base($"Manifest '{manifestId}' validation failed: {validationError}")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestValidationException"/> class.
    /// </summary>
    /// <param name="manifestId">The ID of the manifest that failed validation.</param>
    /// <param name="validationError">The specific validation error message.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ManifestValidationException(string manifestId, string validationError, Exception innerException)
        : base($"Manifest '{manifestId}' validation failed: {validationError}", innerException)
    {
    }
}
