using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Telemetry;

namespace GenHub.Core.Interfaces.Telemetry;

/// <summary>
/// Defines a pluggable destination sink for telemetry events.
/// </summary>
public interface ITelemetrySink
{
    /// <summary>
    /// Gets the unique name identifier of the sink.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Determines if this sink handles the given telemetry event.
    /// </summary>
    /// <param name="telemetryEvent">The telemetry event.</param>
    /// <returns><c>true</c> if handled; otherwise, <c>false</c>.</returns>
    bool CanHandle(TelemetryEvent telemetryEvent);

    /// <summary>
    /// Emits a single telemetry event to the sink.
    /// </summary>
    /// <param name="telemetryEvent">The telemetry event to emit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> EmitAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes any pending buffered events to the remote endpoint.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> FlushAsync(CancellationToken cancellationToken = default);
}
