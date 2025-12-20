using System;

namespace GenHub.Infrastructure.Exceptions;

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
