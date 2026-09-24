using System.Collections.Generic;

namespace GenHub.Core.Interfaces.Telemetry;

/// <summary>
/// Sanitizes sensitive user data, personal paths, usernames, IP addresses, and tokens from telemetry payloads.
/// </summary>
public interface ITelemetrySanitizer
{
    /// <summary>
    /// Sanitizes an input string by removing sensitive usernames, home folders, and personal paths.
    /// </summary>
    /// <param name="input">The input string to sanitize.</param>
    /// <returns>The sanitized string with sensitive data masked.</returns>
    string SanitizeString(string? input);

    /// <summary>
    /// Sanitizes an exception stack trace.
    /// </summary>
    /// <param name="stackTrace">The raw stack trace string.</param>
    /// <returns>The sanitized stack trace.</returns>
    string SanitizeStackTrace(string? stackTrace);

    /// <summary>
    /// Recursively sanitizes a dictionary of properties.
    /// </summary>
    /// <param name="properties">The raw properties dictionary.</param>
    /// <returns>A sanitized dictionary.</returns>
    IReadOnlyDictionary<string, object?> SanitizeProperties(IReadOnlyDictionary<string, object?>? properties);
}
