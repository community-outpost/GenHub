using GenHub.Core.Constants;
using Xunit;

namespace GenHub.Tests.Core.Telemetry;

/// <summary>
/// Unit tests for <see cref="TelemetryConstants"/>.
/// </summary>
public class TelemetryConstantsTests
{
    /// <summary>
    /// Verifies core telemetry constants have expected default values.
    /// </summary>
    [Fact]
    public void Constants_HaveExpectedDefaults()
    {
        Assert.Equal("GenHub", TelemetryConstants.AppName);
        Assert.Equal(30, TelemetryConstants.DefaultFlushIntervalSeconds);
        Assert.Equal(500, TelemetryConstants.MaxQueueCapacity);
        Assert.Equal(5, TelemetryConstants.SessionHeartbeatIntervalMinutes);
        Assert.Equal(50, TelemetryConstants.MaxBreadcrumbsCount);
        Assert.StartsWith("https://", TelemetryConstants.DefaultSentryDsn);
        Assert.StartsWith("phc_", TelemetryConstants.DefaultPostHogApiKey);
        Assert.Equal("https://us.i.posthog.com", TelemetryConstants.DefaultPostHogHost);
        Assert.Equal("https://us.i.posthog.com/capture/", TelemetryConstants.DefaultPostHogCaptureEndpoint);
        Assert.Equal("567732", TelemetryConstants.DefaultPostHogProjectId);
    }

    /// <summary>
    /// Verifies event name constants are non-empty and distinct.
    /// </summary>
    [Fact]
    public void EventNames_AreDistinctAndNonEmpty()
    {
        var events = new[]
        {
            TelemetryConstants.Events.GameSessionStarted,
            TelemetryConstants.Events.GameSessionHeartbeat,
            TelemetryConstants.Events.GameSessionEnded,
            TelemetryConstants.Events.ContentDownloadCompleted,
            TelemetryConstants.Events.AppUpdateChecked,
            TelemetryConstants.Events.AppUpdateApplied,
            TelemetryConstants.Events.CasReconcileCompleted,
            TelemetryConstants.Events.AppCrash,
        };

        foreach (var ev in events)
        {
            Assert.False(string.IsNullOrWhiteSpace(ev));
        }

        Assert.Equal(events.Length, events.Distinct().Count());
    }

    /// <summary>
    /// Verifies property key constants are non-empty and distinct.
    /// </summary>
    [Fact]
    public void PropertyKeys_AreDistinctAndNonEmpty()
    {
        var properties = new[]
        {
            TelemetryConstants.Properties.SessionId,
            TelemetryConstants.Properties.GameType,
            TelemetryConstants.Properties.ProfileId,
            TelemetryConstants.Properties.ProfileName,
            TelemetryConstants.Properties.DurationSeconds,
            TelemetryConstants.Properties.ExitCode,
            TelemetryConstants.Properties.Platform,
            TelemetryConstants.Properties.Runner,
            TelemetryConstants.Properties.Resolution,
            TelemetryConstants.Properties.ManifestId,
            TelemetryConstants.Properties.ContentType,
            TelemetryConstants.Properties.SizeMb,
            TelemetryConstants.Properties.SpeedMbps,
            TelemetryConstants.Properties.SourceProvider,
            TelemetryConstants.Properties.RetryCount,
            TelemetryConstants.Properties.FromVersion,
            TelemetryConstants.Properties.ToVersion,
            TelemetryConstants.Properties.Channel,
            TelemetryConstants.Properties.RestartDurationMs,
            TelemetryConstants.Properties.CacheHitRate,
            TelemetryConstants.Properties.FileCount,
            TelemetryConstants.Properties.BytesReconciled,
            TelemetryConstants.Properties.ExceptionType,
            TelemetryConstants.Properties.ExceptionMessage,
            TelemetryConstants.Properties.StackTrace,
            TelemetryConstants.Properties.IsFatal,
            TelemetryConstants.Properties.Context,
            TelemetryConstants.Properties.InstallationId,
            TelemetryConstants.Properties.AppVersion,
            TelemetryConstants.Properties.ExecutablePath,
        };

        foreach (var prop in properties)
        {
            Assert.False(string.IsNullOrWhiteSpace(prop));
        }

        Assert.Equal(properties.Length, properties.Distinct().Count());
    }
}
