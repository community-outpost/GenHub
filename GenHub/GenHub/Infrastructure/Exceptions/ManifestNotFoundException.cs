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
