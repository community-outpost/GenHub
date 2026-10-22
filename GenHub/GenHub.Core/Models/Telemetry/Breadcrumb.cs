using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Telemetry;

/// <summary>
/// Represents a breadcrumb trail record leading up to an event or crash.
/// </summary>
public sealed class Breadcrumb
{
    /// <summary>
    /// Gets the breadcrumb message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Gets the breadcrumb category.
    /// </summary>
    public string Category { get; init; } = "general";

    /// <summary>
    /// Gets the timestamp when the breadcrumb was added.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets optional structured data associated with the breadcrumb.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Data { get; init; }
}
