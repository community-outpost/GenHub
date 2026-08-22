using System;
using System.Collections.Generic;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Telemetry;

/// <summary>
/// Represents an immutable structured telemetry event.
/// </summary>
public sealed class TelemetryEvent
{
    /// <summary>
    /// Gets the unique event name identifier.
    /// </summary>
    public string EventName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the timestamp when the event was recorded (UTC).
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the minimum telemetry consent level required for this event.
    /// </summary>
    public TelemetryLevel Level { get; init; } = TelemetryLevel.AnonymousMetrics;

    /// <summary>
    /// Gets the anonymous installation identifier.
    /// </summary>
    public string? InstallationId { get; init; }

    /// <summary>
    /// Gets the session identifier if applicable.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the application version.
    /// </summary>
    public string AppVersion { get; init; } = string.Empty;

    /// <summary>
    /// Gets the operating system platform description.
    /// </summary>
    public string Platform { get; init; } = string.Empty;

    /// <summary>
    /// Gets the custom properties dictionary for the event.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Properties { get; init; } = new Dictionary<string, object?>();
}
