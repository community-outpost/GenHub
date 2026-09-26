using GenHub.Core.Constants;
using System.Linq;
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
        Assert.Equal("https://us.i.posthog.com/i/v0/e/", TelemetryConstants.DefaultPostHogCaptureEndpoint);
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
            TelemetryConstants.Events.ProfileLaunched,
            TelemetryConstants.Events.ProfileLaunchedFromShortcut,
            TelemetryConstants.Events.ProfilePinned,
            TelemetryConstants.Events.ProfileShared,
            TelemetryConstants.Events.ProfileImported,
            TelemetryConstants.Events.ContentDownloadCompleted,
            TelemetryConstants.Events.ContentDownloadFailed,
            TelemetryConstants.Events.ContentUpdateApplied,
            TelemetryConstants.Events.ContentUpdateFailed,
            TelemetryConstants.Events.AppUpdateChecked,
            TelemetryConstants.Events.AppUpdateDownloaded,
            TelemetryConstants.Events.AppUpdateApplied,
            TelemetryConstants.Events.UploadThingUploadCompleted,
            TelemetryConstants.Events.UploadThingUploadFailed,
            TelemetryConstants.Events.GenPatcherFixApplied,
            TelemetryConstants.Events.ModProjectCreated,
            TelemetryConstants.Events.ModBuilt,
            TelemetryConstants.Events.CasReconcileCompleted,
            TelemetryConstants.Events.CasGarbageCollected,
            TelemetryConstants.Events.WorkspacePrepared,
            TelemetryConstants.Events.GameInstallationsDetected,
            TelemetryConstants.Events.ReplayExportedZip,
            TelemetryConstants.Events.ReplayCheckpointMinted,
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
            TelemetryConstants.Properties.LaunchSource,
            TelemetryConstants.Properties.ShortcutType,
            TelemetryConstants.Properties.ShareFormat,
            TelemetryConstants.Properties.ImportSource,
            TelemetryConstants.Properties.FixId,
            TelemetryConstants.Properties.FixName,
            TelemetryConstants.Properties.IsCrucial,
            TelemetryConstants.Properties.Success,
            TelemetryConstants.Properties.ErrorMessage,
            TelemetryConstants.Properties.ProjectName,
            TelemetryConstants.Properties.BuildSteps,
            TelemetryConstants.Properties.ToolSource,
            TelemetryConstants.Properties.FileName,
            TelemetryConstants.Properties.FileSizeBytes,
            TelemetryConstants.Properties.DurationSeconds,
            TelemetryConstants.Properties.ExitCode,
            TelemetryConstants.Properties.Platform,
            TelemetryConstants.Properties.Runner,
            TelemetryConstants.Properties.Resolution,
            TelemetryConstants.Properties.ManifestId,
            TelemetryConstants.Properties.ContentType,
            TelemetryConstants.Properties.ContentId,
            TelemetryConstants.Properties.ContentName,
            TelemetryConstants.Properties.PublisherId,
            TelemetryConstants.Properties.Strategy,
            TelemetryConstants.Properties.ProfilesUpdated,
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
            TelemetryConstants.Properties.FullDisplayVersion,
            TelemetryConstants.Properties.GitShortHash,
            TelemetryConstants.Properties.BuildChannel,
            TelemetryConstants.Properties.PullRequestNumber,
            TelemetryConstants.Properties.ExecutablePath,
            TelemetryConstants.Properties.ObjectsScanned,
            TelemetryConstants.Properties.ObjectsReferenced,
            TelemetryConstants.Properties.ObjectsDeleted,
            TelemetryConstants.Properties.BytesFreed,
            TelemetryConstants.Properties.WorkspaceId,
            TelemetryConstants.Properties.ManifestCount,
            TelemetryConstants.Properties.IsReused,
            TelemetryConstants.Properties.InstallationCount,
            TelemetryConstants.Properties.HasSteam,
            TelemetryConstants.Properties.HasEaApp,
            TelemetryConstants.Properties.HasTheFirstDecade,
            TelemetryConstants.Properties.HasGenerals,
            TelemetryConstants.Properties.HasZeroHour,
            TelemetryConstants.Properties.ReplayCount,
            TelemetryConstants.Properties.TargetFrame,
        };

        foreach (var prop in properties)
        {
            Assert.False(string.IsNullOrWhiteSpace(prop));
        }

        Assert.Equal(properties.Length, properties.Distinct().Count());
    }
}
