using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Telemetry;
using GenHub.Core.Utilities;
using GenHub.Features.Telemetry.Services;
using GenHub.Tests.Core.Collections;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Telemetry;

/// <summary>
/// Unit tests for <see cref="TelemetryService"/>.
/// </summary>
[Collection(TelemetryEnvironmentCollection.Name)]
public class TelemetryServiceTests : IDisposable
{
    private static readonly object EnvironmentLock = new();
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
    /// Verifies that a null logger fails fast at construction.
    /// </summary>
    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TelemetryService(
            null!,
            _sanitizer,
            _mockUserSettingsService.Object,
            [_mockSink.Object]));
    }

    /// <summary>
    /// Verifies that a null sanitizer fails fast at construction.
    /// </summary>
    [Fact]
    public void Constructor_WithNullSanitizer_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TelemetryService(
            _mockLogger.Object,
            null!,
            _mockUserSettingsService.Object,
            [_mockSink.Object]));
    }

    /// <summary>
    /// Verifies that a null user settings service fails fast at construction.
    /// </summary>
    [Fact]
    public void Constructor_WithNullUserSettingsService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TelemetryService(
            _mockLogger.Object,
            _sanitizer,
            null!,
            [_mockSink.Object]));
    }

    /// <summary>
    /// Verifies that TrackEvent does not emit when telemetry is Disabled.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task TrackEvent_WhenDisabled_DoesNotEmitAsync()
    {
        _settings.TelemetryPreference = TelemetryLevel.Disabled;

        await using var service = new TelemetryService(
            _mockLogger.Object,
            _sanitizer,
            _mockUserSettingsService.Object,
            [_mockSink.Object]);

        service.TrackEvent(TelemetryConstants.Events.GameSessionStarted);

        await service.FlushAsync();

        _mockSink.Verify(s => s.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that TrackEvent emits when telemetry is AnonymousMetrics.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task TrackEvent_WhenAnonymousMetrics_EmitsToSinkAsync()
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

        await service.FlushAsync();

        _mockSink.Verify(
            s => s.EmitAsync(
                It.Is<TelemetryEvent>(e => e.EventName == TelemetryConstants.Events.GameSessionStarted && e.SessionId == "test-session"),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Verifies that TrackException captures exception details, sanitized message, and breadcrumbs.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task TrackException_RecordsSanitizedCrashEventAsync()
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

        await service.FlushAsync();

        _mockSink.Verify(
            s => s.EmitAsync(
                It.Is<TelemetryEvent>(e => e.EventName == TelemetryConstants.Events.AppCrash &&
                                           e.Level == TelemetryLevel.CrashReportsOnly &&
                                           e.Properties.ContainsKey(TelemetryConstants.Properties.ExceptionType)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Verifies that TrackException emits a slim anonymous-level crash summary without
    /// exception messages, stack traces, or breadcrumbs when AnonymousMetrics is enabled.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task TrackException_WhenAnonymousMetrics_EmitsCrashSummaryWithoutSensitiveDetailsAsync()
    {
        _settings.TelemetryPreference = TelemetryLevel.AnonymousMetrics;
        var captured = new List<TelemetryEvent>();
        _mockSink.Setup(s => s.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
            .Callback<TelemetryEvent, CancellationToken>((e, _) => captured.Add(e))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        await using var service = new TelemetryService(
            _mockLogger.Object,
            _sanitizer,
            _mockUserSettingsService.Object,
            [_mockSink.Object]);

        service.AddBreadcrumb("Clicked Launch Button", "ui");

        try
        {
            throw new InvalidOperationException("Failed to launch");
        }
        catch (Exception ex)
        {
            service.TrackException(ex, "GameLauncher", isFatal: true);
        }

        await service.FlushAsync();

        Assert.Equal(2, captured.Count);
        var summary = captured.FirstOrDefault(e => e.Level == TelemetryLevel.AnonymousMetrics);
        Assert.NotNull(summary);
        Assert.Equal(TelemetryConstants.Events.AppCrash, summary.EventName);
        Assert.False(summary.Properties.ContainsKey(TelemetryConstants.Properties.ExceptionMessage));
        Assert.False(summary.Properties.ContainsKey(TelemetryConstants.Properties.StackTrace));
        Assert.False(summary.Properties.ContainsKey("breadcrumbs"));
        Assert.True(summary.Properties[TelemetryConstants.Properties.IsFatal] is true);
    }

    /// <summary>
    /// Verifies that TrackException does not emit the anonymous crash summary when only crash reports are enabled.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task TrackException_WhenCrashReportsOnly_DoesNotEmitAnonymousSummaryAsync()
    {
        _settings.TelemetryPreference = TelemetryLevel.CrashReportsOnly;

        await using var service = new TelemetryService(
            _mockLogger.Object,
            _sanitizer,
            _mockUserSettingsService.Object,
            [_mockSink.Object]);

        try
        {
            throw new InvalidOperationException("Failed to launch");
        }
        catch (Exception ex)
        {
            service.TrackException(ex, "GameLauncher", isFatal: true);
        }

        await service.FlushAsync();

        _mockSink.Verify(
            s => s.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that breadcrumbs circular buffer is capped at MaxBreadcrumbsCount.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AddBreadcrumb_CappedAtMaxCountAsync()
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
    public async Task FlushAsync_CallsFlushOnAllSinksAsync()
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

    /// <summary>
    /// Verifies that synchronous Dispose signals shutdown without waiting for in-flight sink work.
    /// </summary>
    [Fact]
    public void Dispose_WhenSinkWorkIsInFlight_ReturnsWithoutBlocking()
    {
        _settings.TelemetryPreference = TelemetryLevel.AnonymousMetrics;

        using var enteredEmit = new ManualResetEventSlim(false);
        var releaseEmit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingSink = new Mock<ITelemetrySink>();
        blockingSink.Setup(s => s.CanHandle(It.IsAny<TelemetryEvent>())).Returns(true);
        blockingSink
            .Setup(s => s.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                enteredEmit.Set();
                await releaseEmit.Task;
                return OperationResult<bool>.CreateSuccess(true);
            });
        blockingSink
            .Setup(s => s.FlushAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var service = new TelemetryService(
            _mockLogger.Object,
            _sanitizer,
            _mockUserSettingsService.Object,
            [blockingSink.Object]);

        try
        {
            service.TrackEvent(TelemetryConstants.Events.GameSessionStarted);

            Assert.True(enteredEmit.Wait(TimeSpan.FromSeconds(10)));

            var stopwatch = Stopwatch.StartNew();
            service.Dispose();
            stopwatch.Stop();

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(TelemetryConstants.FlushTimeoutSeconds),
                $"Dispose blocked for {stopwatch.Elapsed}, expected less than the flush timeout.");
        }
        finally
        {
            releaseEmit.TrySetResult(true);
            service.Dispose();
        }
    }

    /// <summary>
    /// Verifies that DO_NOT_TRACK environment variable disables telemetry collection across casing and conventions.
    /// </summary>
    /// <param name="optOutValue">The truthy opt-out string value.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("1")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("YES")]
    [InlineData("on")]
    public async Task CurrentLevel_WhenDoNotTrackEnvironmentVariableIsSet_ReturnsDisabledAsync(string optOutValue)
    {
        lock (EnvironmentLock)
        {
            var original = Environment.GetEnvironmentVariable(TelemetryConstants.EnvironmentVariables.DoNotTrack);
            Environment.SetEnvironmentVariable(TelemetryConstants.EnvironmentVariables.DoNotTrack, optOutValue);
            try
            {
                using var service = new TelemetryService(
                    _mockLogger.Object,
                    _sanitizer,
                    _mockUserSettingsService.Object,
                    [_mockSink.Object]);

                Assert.Equal(TelemetryLevel.Disabled, service.CurrentLevel);
                Assert.False(service.IsEnabled(TelemetryLevel.AnonymousMetrics));
            }
            finally
            {
                Environment.SetEnvironmentVariable(TelemetryConstants.EnvironmentVariables.DoNotTrack, original);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Verifies that GENHUB_TELEMETRY_OPTOUT environment variable disables telemetry collection across casing and conventions.
    /// </summary>
    /// <param name="optOutValue">The truthy opt-out string value.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("1")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("YES")]
    [InlineData("on")]
    public async Task CurrentLevel_WhenGenHubOptOutEnvironmentVariableIsSet_ReturnsDisabledAsync(string optOutValue)
    {
        lock (EnvironmentLock)
        {
            var original = Environment.GetEnvironmentVariable(TelemetryConstants.EnvironmentVariables.GenHubTelemetryOptOut);
            Environment.SetEnvironmentVariable(TelemetryConstants.EnvironmentVariables.GenHubTelemetryOptOut, optOutValue);
            try
            {
                using var service = new TelemetryService(
                    _mockLogger.Object,
                    _sanitizer,
                    _mockUserSettingsService.Object,
                    [_mockSink.Object]);

                Assert.Equal(TelemetryLevel.Disabled, service.CurrentLevel);
                Assert.False(service.IsEnabled(TelemetryLevel.AnonymousMetrics));
            }
            finally
            {
                Environment.SetEnvironmentVariable(TelemetryConstants.EnvironmentVariables.GenHubTelemetryOptOut, original);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Verifies that falsy opt-out values leave telemetry enabled at the user preference.
    /// </summary>
    /// <param name="variable">The environment variable holding the falsy value.</param>
    /// <param name="falsyValue">The falsy value that must not opt out.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(TelemetryConstants.EnvironmentVariables.GenHubTelemetryOptOut, "0")]
    [InlineData(TelemetryConstants.EnvironmentVariables.GenHubTelemetryOptOut, "false")]
    [InlineData(TelemetryConstants.EnvironmentVariables.GenHubTelemetryOptOut, "no")]
    [InlineData(TelemetryConstants.EnvironmentVariables.GenHubTelemetryOptOut, "off")]
    [InlineData(TelemetryConstants.EnvironmentVariables.DoNotTrack, "0")]
    [InlineData(TelemetryConstants.EnvironmentVariables.DoNotTrack, "false")]
    [InlineData(TelemetryConstants.EnvironmentVariables.DoNotTrack, "no")]
    [InlineData(TelemetryConstants.EnvironmentVariables.DoNotTrack, "off")]
    public async Task CurrentLevel_WhenOptOutVariableIsFalsy_KeepsUserPreferenceAsync(string variable, string falsyValue)
    {
        var otherVariable = string.Equals(variable, TelemetryConstants.EnvironmentVariables.DoNotTrack, StringComparison.Ordinal)
            ? TelemetryConstants.EnvironmentVariables.GenHubTelemetryOptOut
            : TelemetryConstants.EnvironmentVariables.DoNotTrack;

        lock (EnvironmentLock)
        {
            var originalTarget = Environment.GetEnvironmentVariable(variable);
            var originalOther = Environment.GetEnvironmentVariable(otherVariable);
            Environment.SetEnvironmentVariable(variable, falsyValue);
            Environment.SetEnvironmentVariable(otherVariable, null);
            try
            {
                using var service = new TelemetryService(
                    _mockLogger.Object,
                    _sanitizer,
                    _mockUserSettingsService.Object,
                    [_mockSink.Object]);

                Assert.Equal(TelemetryLevel.AnonymousMetrics, service.CurrentLevel);
                Assert.True(service.IsEnabled(TelemetryLevel.AnonymousMetrics));
            }
            finally
            {
                Environment.SetEnvironmentVariable(variable, originalTarget);
                Environment.SetEnvironmentVariable(otherVariable, originalOther);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Verifies that DO_NOT_TRACK is honored even if GENHUB_TELEMETRY_OPTOUT is set to a non-optout value like 0.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CurrentLevel_WhenDoNotTrackIsSetAndGenHubOptOutIsZero_ReturnsDisabledAsync()
    {
        lock (EnvironmentLock)
        {
            var originalDnt = Environment.GetEnvironmentVariable(TelemetryConstants.EnvironmentVariables.DoNotTrack);
            var originalOptOut = Environment.GetEnvironmentVariable(TelemetryConstants.EnvironmentVariables.GenHubTelemetryOptOut);
            Environment.SetEnvironmentVariable(TelemetryConstants.EnvironmentVariables.DoNotTrack, "1");
            Environment.SetEnvironmentVariable(TelemetryConstants.EnvironmentVariables.GenHubTelemetryOptOut, "0");
            try
            {
                using var service = new TelemetryService(
                    _mockLogger.Object,
                    _sanitizer,
                    _mockUserSettingsService.Object,
                    [_mockSink.Object]);

                Assert.Equal(TelemetryLevel.Disabled, service.CurrentLevel);
            }
            finally
            {
                Environment.SetEnvironmentVariable(TelemetryConstants.EnvironmentVariables.DoNotTrack, originalDnt);
                Environment.SetEnvironmentVariable(TelemetryConstants.EnvironmentVariables.GenHubTelemetryOptOut, originalOptOut);
            }
        }

        await Task.CompletedTask;
    }
}
