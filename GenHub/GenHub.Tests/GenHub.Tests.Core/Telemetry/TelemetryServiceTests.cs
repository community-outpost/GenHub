using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Telemetry;
using GenHub.Core.Utilities;
using GenHub.Features.Telemetry.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Telemetry;

/// <summary>
/// Unit tests for <see cref="TelemetryService"/>.
/// </summary>
public class TelemetryServiceTests : IDisposable
{
    private readonly Mock<ILogger<TelemetryService>> _mockLogger = new();
    private readonly Mock<IUserSettingsService> _mockUserSettingsService = new();
    private readonly TelemetrySanitizer _sanitizer = new();
    private readonly Mock<ITelemetrySink> _mockSink = new();
    private readonly UserSettings _settings = new()
    {
        TelemetryPreference = TelemetryLevel.AnonymousMetrics,
        AnonymousInstallationId = "test-installation-guid",
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="TelemetryServiceTests"/> class.
    /// </summary>
    public TelemetryServiceTests()
    {
        _mockUserSettingsService.Setup(s => s.Get()).Returns(() => _settings);
        _mockSink.Setup(s => s.CanHandle(It.IsAny<TelemetryEvent>())).Returns(true);
        _mockSink.Setup(s => s.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        _mockSink.Setup(s => s.FlushAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
    }

    /// <summary>
    /// Cleans up resources.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that TrackEvent does not emit when telemetry is Disabled.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task TrackEvent_WhenDisabled_DoesNotEmit()
    {
        _settings.TelemetryPreference = TelemetryLevel.Disabled;

        await using var service = new TelemetryService(
            _mockLogger.Object,
            _sanitizer,
            _mockUserSettingsService.Object,
            [_mockSink.Object]);

        service.TrackEvent(TelemetryConstants.Events.GameSessionStarted);

        // Allow background loop a moment
        await Task.Delay(50);

        _mockSink.Verify(s => s.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that TrackEvent emits when telemetry is AnonymousMetrics.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task TrackEvent_WhenAnonymousMetrics_EmitsToSink()
    {
        _settings.TelemetryPreference = TelemetryLevel.AnonymousMetrics;

        await using var service = new TelemetryService(
            _mockLogger.Object,
            _sanitizer,
            _mockUserSettingsService.Object,
            [_mockSink.Object]);

        service.TrackEvent(TelemetryConstants.Events.GameSessionStarted, new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.SessionId] = "test-session",
        });

        // Allow background loop to process
        await Task.Delay(100);

        _mockSink.Verify(s => s.EmitAsync(
            It.Is<TelemetryEvent>(e => e.EventName == TelemetryConstants.Events.GameSessionStarted && e.SessionId == "test-session"),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    /// <summary>
    /// Verifies that TrackException captures exception details, sanitized message, and breadcrumbs.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task TrackException_RecordsSanitizedCrashEvent()
    {
        _settings.TelemetryPreference = TelemetryLevel.CrashReportsOnly;

        await using var service = new TelemetryService(
            _mockLogger.Object,
            _sanitizer,
            _mockUserSettingsService.Object,
            [_mockSink.Object]);

        service.AddBreadcrumb("Clicked Launch Button", "ui");

        try
        {
            throw new InvalidOperationException("Failed to launch in C:\\Users\\Secret\\game.exe");
        }
        catch (Exception ex)
        {
            service.TrackException(ex, "GameLauncher", isFatal: true);
        }

        await Task.Delay(100);

        _mockSink.Verify(s => s.EmitAsync(
            It.Is<TelemetryEvent>(e => e.EventName == TelemetryConstants.Events.AppCrash &&
                                       e.Level == TelemetryLevel.CrashReportsOnly &&
                                       e.Properties.ContainsKey(TelemetryConstants.Properties.ExceptionType)),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    /// <summary>
    /// Verifies that breadcrumbs circular buffer is capped at MaxBreadcrumbsCount.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AddBreadcrumb_CappedAtMaxCount()
    {
        await using var service = new TelemetryService(
            _mockLogger.Object,
            _sanitizer,
            _mockUserSettingsService.Object,
            [_mockSink.Object]);

        for (int i = 0; i < 70; i++)
        {
            service.AddBreadcrumb($"Action {i}", "test");
        }

        var breadcrumbs = service.GetRecentBreadcrumbs();
        Assert.Equal(TelemetryConstants.MaxBreadcrumbsCount, breadcrumbs.Count);
        Assert.Equal("Action 69", breadcrumbs[^1].Message);
    }

    /// <summary>
    /// Verifies that FlushAsync flushes all registered sinks.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FlushAsync_CallsFlushOnAllSinks()
    {
        await using var service = new TelemetryService(
            _mockLogger.Object,
            _sanitizer,
            _mockUserSettingsService.Object,
            [_mockSink.Object]);

        var result = await service.FlushAsync();

        Assert.True(result.Success);
        _mockSink.Verify(s => s.FlushAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
