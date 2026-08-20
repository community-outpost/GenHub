using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Telemetry;

namespace GenHub.Core.Interfaces.Telemetry;

/// <summary>
/// Core contract for recording and dispatching structured telemetry events, crashes, and diagnostics.
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Gets the current active telemetry consent level.
    /// </summary>
    TelemetryLevel CurrentLevel { get; }

    /// <summary>
    /// Checks if the specified telemetry level is permitted under current user settings.
    /// </summary>
    /// <param name="level">The telemetry level to check.</param>
    /// <returns><c>true</c> if permitted; otherwise, <c>false</c>.</returns>
    bool IsEnabled(TelemetryLevel level);

    /// <summary>
    /// Tracks an anonymous structured telemetry event.
    /// </summary>
    /// <param name="eventName">The unique event name.</param>
    /// <param name="properties">Optional structured properties.</param>
    /// <param name="level">Minimum required telemetry level (defaults to AnonymousMetrics).</param>
    void TrackEvent(string eventName, IReadOnlyDictionary<string, object?>? properties = null, TelemetryLevel level = TelemetryLevel.AnonymousMetrics);

    /// <summary>
    /// Tracks an exception or crash diagnostics with sanitized stack trace and breadcrumbs.
    /// </summary>
    /// <param name="exception">The exception to track.</param>
    /// <param name="context">Optional context or subsystem name.</param>
    /// <param name="properties">Optional metadata properties.</param>
    /// <param name="isFatal">Whether the exception caused a fatal crash.</param>
    void TrackException(Exception exception, string? context = null, IReadOnlyDictionary<string, object?>? properties = null, bool isFatal = false);

    /// <summary>
    /// Adds a breadcrumb record to the in-memory circular buffer for crash investigation.
    /// </summary>
    /// <param name="message">The breadcrumb message.</param>
    /// <param name="category">The category (e.g. "ui", "game", "download").</param>
    /// <param name="data">Optional structured data.</param>
    void AddBreadcrumb(string message, string? category = null, IReadOnlyDictionary<string, object?>? data = null);

    /// <summary>
    /// Gets the recent breadcrumb history from the circular buffer.
    /// </summary>
    /// <returns>A snapshot of recent breadcrumbs.</returns>
    IReadOnlyList<Breadcrumb> GetRecentBreadcrumbs();

    /// <summary>
    /// Asynchronously flushes all queued telemetry events to registered sinks.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result indicating whether flush succeeded.</returns>
    Task<OperationResult<bool>> FlushAsync(CancellationToken cancellationToken = default);
}
