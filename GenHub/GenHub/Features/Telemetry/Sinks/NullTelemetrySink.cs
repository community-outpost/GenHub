using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Telemetry;

namespace GenHub.Features.Telemetry.Sinks;

/// <summary>
/// A no-op telemetry sink for testing or when telemetry sinks are unconfigured.
/// </summary>
public sealed class NullTelemetrySink : ITelemetrySink
{
    /// <inheritdoc/>
    public string Name => "Null";

    /// <inheritdoc/>
    public bool CanHandle(TelemetryEvent telemetryEvent) => false;

    /// <inheritdoc/>
    public Task<OperationResult<bool>> EmitAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> FlushAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }
}
