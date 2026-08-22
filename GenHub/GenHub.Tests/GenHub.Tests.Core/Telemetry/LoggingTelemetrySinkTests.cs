using System.Collections.Generic;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Telemetry;
using GenHub.Features.Telemetry.Sinks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Telemetry;

/// <summary>
/// Unit tests for <see cref="LoggingTelemetrySink"/>.
/// </summary>
public class LoggingTelemetrySinkTests
{
    private readonly Mock<ILogger<LoggingTelemetrySink>> _loggerMock = new();
    private readonly LoggingTelemetrySink _sink;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingTelemetrySinkTests"/> class.
    /// </summary>
    public LoggingTelemetrySinkTests()
    {
        _sink = new LoggingTelemetrySink(_loggerMock.Object);
    }

    /// <summary>
    /// Verifies sink metadata and CanHandle predicate.
    /// </summary>
    [Fact]
    public void SinkProperties_AreValid()
    {
        Assert.Equal("Logging", _sink.Name);

        var ev = new TelemetryEvent
        {
            EventName = TelemetryConstants.Events.GameSessionStarted,
            Level = TelemetryLevel.AnonymousMetrics,
        };

        Assert.True(_sink.CanHandle(ev));
    }

    /// <summary>
    /// Verifies EmitAsync succeeds for normal event.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task EmitAsync_StandardEvent_ReturnsSuccessAsync()
    {
        var ev = new TelemetryEvent
        {
            EventName = TelemetryConstants.Events.GameSessionStarted,
            SessionId = "1234",
            Level = TelemetryLevel.AnonymousMetrics,
            Properties = new Dictionary<string, object?> { ["game"] = "Generals" },
        };

        var result = await _sink.EmitAsync(ev);
        Assert.True(result.Success);
    }

    /// <summary>
    /// Verifies EmitAsync succeeds for crash event.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task EmitAsync_CrashEvent_ReturnsSuccessAsync()
    {
        var ev = new TelemetryEvent
        {
            EventName = TelemetryConstants.Events.AppCrash,
            Level = TelemetryLevel.CrashReportsOnly,
            Properties = new Dictionary<string, object?> { ["error"] = "Fatal error" },
        };

        var result = await _sink.EmitAsync(ev);
        Assert.True(result.Success);
    }
}
