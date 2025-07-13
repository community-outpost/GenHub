using System;

namespace GenHub.Infrastructure.Exceptions;

/// <summary>
/// Exception thrown when a requested manifest cannot be found.
/// </summary>
public class ManifestNotFoundException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestNotFoundException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ManifestNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestNotFoundException"/> class.
    /// </summary>
    /// <param name="manifestId">The ID of the manifest that was not found.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ManifestNotFoundException(string manifestId, Exception innerException)
        : base($"Manifest '{manifestId}' not found", innerException)
    {
    }
}

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

/// <summary>
/// Exception thrown when manifest security validation fails.
/// </summary>
public class ManifestSecurityException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestSecurityException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ManifestSecurityException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestSecurityException"/> class.
    /// </summary>
    /// <param name="manifestId">The ID of the manifest that failed security validation.</param>
    /// <param name="securityIssue">The specific security issue.</param>
    public ManifestSecurityException(string manifestId, string securityIssue)
        : base($"Manifest '{manifestId}' security validation failed: {securityIssue}")
    {
    }
}
