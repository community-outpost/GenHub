using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Telemetry;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Telemetry.Sinks;

/// <summary>
/// Telemetry sink that outputs structured events to the application log stream.
/// </summary>
public sealed class LoggingTelemetrySink(ILogger<LoggingTelemetrySink> logger) : ITelemetrySink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    /// <inheritdoc/>
    public string Name => "Logging";

    /// <inheritdoc/>
    public bool CanHandle(TelemetryEvent telemetryEvent) => true;

    /// <inheritdoc/>
    public Task<OperationResult<bool>> EmitAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);

        try
        {
            if (telemetryEvent.EventName == TelemetryConstants.Events.AppCrash)
            {
                logger.LogWarning(
                    "[Telemetry:Crash] Event={EventName}, Platform={Platform}, AppVersion={AppVersion}, Properties={Properties}",
                    telemetryEvent.EventName,
                    telemetryEvent.Platform,
                    telemetryEvent.AppVersion,
                    JsonSerializer.Serialize(telemetryEvent.Properties, JsonOptions));
            }
            else
            {
                logger.LogDebug(
                    "[Telemetry:Event] Event={EventName}, Session={SessionId}, Level={Level}, Properties={Properties}",
                    telemetryEvent.EventName,
                    telemetryEvent.SessionId,
                    telemetryEvent.Level,
                    JsonSerializer.Serialize(telemetryEvent.Properties, JsonOptions));
            }

            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }
        catch (Exception ex)
        {
            logger.LogTrace(ex, "Failed to write telemetry event to logger");
            return Task.FromResult(OperationResult<bool>.CreateFailure(ex.Message));
        }
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> FlushAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }
}
