using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Telemetry;

/// <summary>
/// Represents a structured crash or unhandled exception report.
/// </summary>
public sealed class CrashReport
{
    /// <summary>
    /// Gets the exception type name.
    /// </summary>
    public string ExceptionType { get; init; } = string.Empty;

    /// <summary>
    /// Gets the sanitized exception message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Gets the sanitized stack trace.
    /// </summary>
    public string StackTrace { get; init; } = string.Empty;

    /// <summary>
    /// Gets the timestamp when the crash occurred (UTC).
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the breadcrumb trail preceding the crash.
    /// </summary>
    public IReadOnlyList<Breadcrumb> Breadcrumbs { get; init; } = [];

    /// <summary>
    /// Gets additional structured metadata properties.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Properties { get; init; } = new Dictionary<string, object?>();

    /// <summary>
    /// Gets a value indicating whether the crash was fatal to the application process.
    /// </summary>
    public bool IsFatal { get; init; }
}
