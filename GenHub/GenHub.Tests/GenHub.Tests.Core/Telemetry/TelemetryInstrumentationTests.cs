using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Telemetry;

/// <summary>
/// Unit tests verifying telemetry event instrumentation across GenHub core workflows.
/// </summary>
public class TelemetryInstrumentationTests
{
    private readonly Mock<ILogger<ActionSetOrchestrator>> _orchestratorLoggerMock = new();
    private readonly Mock<ITelemetryService> _telemetryServiceMock = new();

    /// <summary>
    /// Verifies that ActionSetOrchestrator tracks GenPatcherFixApplied on success.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task ActionSetOrchestrator_WhenFixSucceeds_TracksGenPatcherFixAppliedSuccess()
    {
        var fix = new Mock<IActionSet>();
        fix.SetupGet(f => f.Id).Returns("GenPatcher.CameraFix");
        fix.SetupGet(f => f.Title).Returns("Camera Height Fix");
        fix.SetupGet(f => f.IsCrucialFix).Returns(false);
        fix.Setup(f => f.IsApplicableAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fix.Setup(f => f.IsAppliedAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        fix.Setup(f => f.ApplyAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActionSetResult(true));

        var orchestrator = new ActionSetOrchestrator([fix.Object], [], _orchestratorLoggerMock.Object, _telemetryServiceMock.Object);
        var installation = new GameInstallation("C:\\TestPath", GameInstallationType.Steam)
        {
            HasZeroHour = true,
        };

        var result = await orchestrator.ApplyActionSetsAsync(installation, [fix.Object]);

        Assert.True(result.Success);
        _telemetryServiceMock.Verify(
            t => t.TrackEvent(
                TelemetryConstants.Events.GenPatcherFixApplied,
                It.Is<IReadOnlyDictionary<string, object?>?>(p =>
                    p != null &&
                    (string?)p[TelemetryConstants.Properties.FixId] == "GenPatcher.CameraFix" &&
                    (string?)p[TelemetryConstants.Properties.FixName] == "Camera Height Fix" &&
                    (string?)p[TelemetryConstants.Properties.GameType] == "ZeroHour" &&
                    (bool?)p[TelemetryConstants.Properties.IsCrucial] == false &&
                    (bool?)p[TelemetryConstants.Properties.Success] == true),
                It.IsAny<TelemetryLevel>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that ActionSetOrchestrator tracks GenPatcherFixApplied on failure.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task ActionSetOrchestrator_WhenFixFails_TracksGenPatcherFixAppliedFailure()
    {
        var fix = new Mock<IActionSet>();
        fix.SetupGet(f => f.Id).Returns("GenPatcher.DirectX8Fix");
        fix.SetupGet(f => f.Title).Returns("DirectX 8 Compatibility Fix");
        fix.SetupGet(f => f.IsCrucialFix).Returns(true);
        fix.Setup(f => f.IsApplicableAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fix.Setup(f => f.IsAppliedAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        fix.Setup(f => f.ApplyAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActionSetResult(false, "DirectX dll missing"));

        var orchestrator = new ActionSetOrchestrator([fix.Object], [], _orchestratorLoggerMock.Object, _telemetryServiceMock.Object);
        var installation = new GameInstallation("C:\\TestPath", GameInstallationType.Steam)
        {
            HasGenerals = true,
        };

        var result = await orchestrator.ApplyActionSetsAsync(installation, [fix.Object]);

        Assert.False(result.Success);
        _telemetryServiceMock.Verify(
            t => t.TrackEvent(
                TelemetryConstants.Events.GenPatcherFixApplied,
                It.Is<IReadOnlyDictionary<string, object?>?>(p =>
                    p != null &&
                    (string?)p[TelemetryConstants.Properties.FixId] == "GenPatcher.DirectX8Fix" &&
                    (string?)p[TelemetryConstants.Properties.FixName] == "DirectX 8 Compatibility Fix" &&
                    (string?)p[TelemetryConstants.Properties.GameType] == "Generals" &&
                    (bool?)p[TelemetryConstants.Properties.IsCrucial] == true &&
                    (bool?)p[TelemetryConstants.Properties.Success] == false &&
                    (string?)p[TelemetryConstants.Properties.ErrorMessage] == "DirectX dll missing"),
                It.IsAny<TelemetryLevel>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that ActionSetOrchestrator tracks GenPatcherFixApplied when an exception is thrown.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task ActionSetOrchestrator_WhenFixThrows_TracksGenPatcherFixAppliedFailure()
    {
        var fix = new Mock<IActionSet>();
        fix.SetupGet(f => f.Id).Returns("GenPatcher.CrashFix");
        fix.SetupGet(f => f.Title).Returns("Crash Fix");
        fix.SetupGet(f => f.IsCrucialFix).Returns(false);
        fix.Setup(f => f.IsApplicableAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fix.Setup(f => f.IsAppliedAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        fix.Setup(f => f.ApplyAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Registry error"));

        var orchestrator = new ActionSetOrchestrator([fix.Object], [], _orchestratorLoggerMock.Object, _telemetryServiceMock.Object);
        var installation = new GameInstallation("C:\\TestPath", GameInstallationType.Retail)
        {
            HasZeroHour = true,
        };

        var result = await orchestrator.ApplyActionSetsAsync(installation, [fix.Object]);

        Assert.False(result.Success);
        _telemetryServiceMock.Verify(
            t => t.TrackEvent(
                TelemetryConstants.Events.GenPatcherFixApplied,
                It.Is<IReadOnlyDictionary<string, object?>?>(p =>
                    p != null &&
                    (string?)p[TelemetryConstants.Properties.FixId] == "GenPatcher.CrashFix" &&
                    (bool?)p[TelemetryConstants.Properties.Success] == false &&
                    (string?)p[TelemetryConstants.Properties.ErrorMessage] == "Registry error"),
                It.IsAny<TelemetryLevel>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that content update applied telemetry payload contains expected keys and values.
    /// </summary>
    [Fact]
    public void ContentUpdateApplied_Event_ContainsRequiredTelemetryProperties()
    {
        var properties = new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.PublisherId] = "GeneralsOnline",
            [TelemetryConstants.Properties.Strategy] = "GeneralsOnlineContentStrategy",
            [TelemetryConstants.Properties.FromVersion] = "1.0.0",
            [TelemetryConstants.Properties.ToVersion] = "1.1.0",
            [TelemetryConstants.Properties.Success] = true,
            [TelemetryConstants.Properties.ProfilesUpdated] = 2,
        };

        _telemetryServiceMock.Object.TrackEvent(TelemetryConstants.Events.ContentUpdateApplied, properties, TelemetryLevel.AnonymousMetrics);

        _telemetryServiceMock.Verify(
            t => t.TrackEvent(
                TelemetryConstants.Events.ContentUpdateApplied,
                It.Is<IReadOnlyDictionary<string, object?>?>(p =>
                    p != null &&
                    (string?)p[TelemetryConstants.Properties.PublisherId] == "GeneralsOnline" &&
                    (string?)p[TelemetryConstants.Properties.Strategy] == "GeneralsOnlineContentStrategy" &&
                    (string?)p[TelemetryConstants.Properties.FromVersion] == "1.0.0" &&
                    (string?)p[TelemetryConstants.Properties.ToVersion] == "1.1.0" &&
                    (bool?)p[TelemetryConstants.Properties.Success] == true &&
                    (int?)p[TelemetryConstants.Properties.ProfilesUpdated] == 2),
                It.IsAny<TelemetryLevel>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that content update failed telemetry payload contains error properties.
    /// </summary>
    [Fact]
    public void ContentUpdateFailed_Event_ContainsRequiredTelemetryProperties()
    {
        var properties = new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.PublisherId] = "TheSuperHackers",
            [TelemetryConstants.Properties.Strategy] = "SuperHackersContentStrategy",
            [TelemetryConstants.Properties.Success] = false,
            [TelemetryConstants.Properties.ErrorMessage] = "Network failure downloading manifest",
        };

        _telemetryServiceMock.Object.TrackEvent(TelemetryConstants.Events.ContentUpdateFailed, properties, TelemetryLevel.AnonymousMetrics);

        _telemetryServiceMock.Verify(
            t => t.TrackEvent(
                TelemetryConstants.Events.ContentUpdateFailed,
                It.Is<IReadOnlyDictionary<string, object?>?>(p =>
                    p != null &&
                    (string?)p[TelemetryConstants.Properties.PublisherId] == "TheSuperHackers" &&
                    (string?)p[TelemetryConstants.Properties.Strategy] == "SuperHackersContentStrategy" &&
                    (bool?)p[TelemetryConstants.Properties.Success] == false &&
                    (string?)p[TelemetryConstants.Properties.ErrorMessage] == "Network failure downloading manifest"),
                It.IsAny<TelemetryLevel>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that content download completed telemetry payload contains download metrics.
    /// </summary>
    [Fact]
    public void ContentDownloadCompleted_Event_ContainsRequiredTelemetryProperties()
    {
        var properties = new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.ContentName] = "GeneralsOnline-Package.zip",
            [TelemetryConstants.Properties.FileName] = "GeneralsOnline-Package.zip",
            [TelemetryConstants.Properties.PublisherId] = "GeneralsOnline",
            [TelemetryConstants.Properties.ContentType] = "Package",
            [TelemetryConstants.Properties.FileSizeBytes] = 10485760L,
            [TelemetryConstants.Properties.DurationSeconds] = 1.25,
            [TelemetryConstants.Properties.SpeedMbps] = 8.38,
            [TelemetryConstants.Properties.Success] = true,
        };

        _telemetryServiceMock.Object.TrackEvent(TelemetryConstants.Events.ContentDownloadCompleted, properties, TelemetryLevel.AnonymousMetrics);

        _telemetryServiceMock.Verify(
            t => t.TrackEvent(
                TelemetryConstants.Events.ContentDownloadCompleted,
                It.Is<IReadOnlyDictionary<string, object?>?>(p =>
                    p != null &&
                    (string?)p[TelemetryConstants.Properties.ContentName] == "GeneralsOnline-Package.zip" &&
                    (string?)p[TelemetryConstants.Properties.PublisherId] == "GeneralsOnline" &&
                    (long?)p[TelemetryConstants.Properties.FileSizeBytes] == 10485760L &&
                    (bool?)p[TelemetryConstants.Properties.Success] == true),
                It.IsAny<TelemetryLevel>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that content download failed telemetry payload contains failure reasons.
    /// </summary>
    [Fact]
    public void ContentDownloadFailed_Event_ContainsRequiredTelemetryProperties()
    {
        var properties = new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.ContentName] = "GeneralsOnline-Package.zip",
            [TelemetryConstants.Properties.FileName] = "GeneralsOnline-Package.zip",
            [TelemetryConstants.Properties.PublisherId] = "GeneralsOnline",
            [TelemetryConstants.Properties.ContentType] = "Package",
            [TelemetryConstants.Properties.Success] = false,
            [TelemetryConstants.Properties.ErrorMessage] = "HTTP 404 Not Found",
        };

        _telemetryServiceMock.Object.TrackEvent(TelemetryConstants.Events.ContentDownloadFailed, properties, TelemetryLevel.AnonymousMetrics);

        _telemetryServiceMock.Verify(
            t => t.TrackEvent(
                TelemetryConstants.Events.ContentDownloadFailed,
                It.Is<IReadOnlyDictionary<string, object?>?>(p =>
                    p != null &&
                    (string?)p[TelemetryConstants.Properties.ContentName] == "GeneralsOnline-Package.zip" &&
                    (string?)p[TelemetryConstants.Properties.PublisherId] == "GeneralsOnline" &&
                    (bool?)p[TelemetryConstants.Properties.Success] == false &&
                    (string?)p[TelemetryConstants.Properties.ErrorMessage] == "HTTP 404 Not Found"),
                It.IsAny<TelemetryLevel>()),
            Times.Once);
    }
}
