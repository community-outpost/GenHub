namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines user consent levels for telemetry data collection and dispatch.
/// </summary>
public enum TelemetryLevel
{
    /// <summary>
    /// Telemetry is completely disabled. No network transmission or external sinks.
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// Sends only unhandled exceptions and fatal crash diagnostics to crash reporting sinks.
    /// </summary>
    CrashReportsOnly = 1,

    /// <summary>
    /// Sends anonymous usage metrics, game session durations, download counts, and update adoption metrics.
    /// </summary>
    AnonymousMetrics = 2,
}
