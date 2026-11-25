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
        Assert.Equal(20, TelemetryConstants.MaxTelemetryContentIds);
        Assert.StartsWith("https://", TelemetryConstants.DefaultSentryDsn);
        Assert.StartsWith("phc_", TelemetryConstants.DefaultPostHogApiKey);
        Assert.Equal("https://us.i.posthog.com", TelemetryConstants.DefaultPostHogHost);
        Assert.Equal("https://us.i.posthog.com/i/v0/e/", TelemetryConstants.DefaultPostHogCaptureEndpoint);
        Assert.Equal("/i/v0/e/", TelemetryConstants.DefaultPostHogCapturePath);
        Assert.Equal("PR-", TelemetryConstants.PullRequestChannelPrefix);
        Assert.Equal("567732", TelemetryConstants.DefaultPostHogProjectId);
        Assert.Equal(2, TelemetryConstants.FlushTimeoutSeconds);
        Assert.Equal("Release", TelemetryConstants.ReleaseChannel);
        Assert.Equal("Native", TelemetryConstants.Runners.Native);
        Assert.Equal("Wine", TelemetryConstants.Runners.Wine);
        Assert.Equal("Proton-", TelemetryConstants.Runners.ProtonPrefix);
        Assert.Equal("Linux-Runner", TelemetryConstants.Runners.Linux);
        Assert.Equal("macOS-Runner", TelemetryConstants.Runners.MacOS);
        Assert.Equal("Package", TelemetryConstants.ContentTypes.Package);
        Assert.Equal("desktop", TelemetryConstants.ShortcutTypes.Desktop);
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
            TelemetryConstants.Events.ProfileLaunchFailed,
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
            TelemetryConstants.Events.WndEditorOpened,
            TelemetryConstants.Events.WndDocumentOpened,
            TelemetryConstants.Events.WndDocumentSaved,
            TelemetryConstants.Events.WndDocumentValidated,
            TelemetryConstants.Events.WndTexturesImported,
            TelemetryConstants.Events.AppCrash,
            TelemetryConstants.Events.AppStarted,
            TelemetryConstants.Events.PublisherSubscribed,
            TelemetryConstants.Events.PublisherUnsubscribed,
            TelemetryConstants.Events.PublisherStudioOpened,
            TelemetryConstants.Events.PublisherStudioProjectCreated,
            TelemetryConstants.Events.PublisherStudioDefinitionExported,
            TelemetryConstants.Events.PublisherStudioPublished,
        };

        foreach (var ev in events)
        {
            Assert.False(string.IsNullOrWhiteSpace(ev));
        }

        Assert.Equal(events.Length, events.Distinct().Count());
    }

    /// <summary>
    /// Verifies property name constants are non-empty and distinct.
    /// </summary>
    [Fact]
    public void PropertyNames_AreDistinctAndNonEmpty()
    {
        var properties = new[]
        {
            TelemetryConstants.Properties.SessionId,
            TelemetryConstants.Properties.GameType,
            TelemetryConstants.Properties.ProfileId,
            TelemetryConstants.Properties.ProfileName,
            TelemetryConstants.Properties.GameClientId,
            TelemetryConstants.Properties.GameClientName,
            TelemetryConstants.Properties.GameClientVersion,
            TelemetryConstants.Properties.GameClientPublisher,
            TelemetryConstants.Properties.TimeToLaunchMs,
            TelemetryConstants.Properties.LaunchSource,
            TelemetryConstants.Properties.ShortcutType,
            TelemetryConstants.Properties.ShareFormat,
            TelemetryConstants.Properties.ImportSource,
            TelemetryConstants.Properties.FixId,
            TelemetryConstants.Properties.FixName,
            TelemetryConstants.Properties.IsCrucial,
            TelemetryConstants.Properties.Success,
            TelemetryConstants.Properties.ErrorMessage,
            TelemetryConstants.Properties.ErrorCategory,
            TelemetryConstants.Properties.ProjectName,
            TelemetryConstants.Properties.BuildSteps,
            TelemetryConstants.Properties.ToolSource,
            TelemetryConstants.Properties.FileName,
            TelemetryConstants.Properties.FileSizeBytes,
            TelemetryConstants.Properties.DurationSeconds,
            TelemetryConstants.Properties.ExitCode,
            TelemetryConstants.Properties.WasGraceful,
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
            TelemetryConstants.Properties.HasEAApp,
            TelemetryConstants.Properties.HasTheFirstDecade,
            TelemetryConstants.Properties.HasGenerals,
            TelemetryConstants.Properties.HasZeroHour,
            TelemetryConstants.Properties.ReplayCount,
            TelemetryConstants.Properties.TargetFrame,
            TelemetryConstants.Properties.WindowCount,
            TelemetryConstants.Properties.HasLinkedAssets,
            TelemetryConstants.Properties.IsValid,
            TelemetryConstants.Properties.TextureCount,
            TelemetryConstants.Properties.FilePath,
            TelemetryConstants.Properties.PublisherName,
            TelemetryConstants.Properties.Author,
            TelemetryConstants.Properties.CatalogUrl,
            TelemetryConstants.Properties.DefinitionUrl,
            TelemetryConstants.Properties.CatalogCount,
            TelemetryConstants.Properties.ProviderType,
            TelemetryConstants.Properties.Publisher,
            TelemetryConstants.Properties.Content,
            TelemetryConstants.Properties.Package,
            TelemetryConstants.Properties.GameClient,
            TelemetryConstants.Properties.ContentIds,
            TelemetryConstants.Properties.ContentCount,
            TelemetryConstants.Properties.DurationHours,
            TelemetryConstants.Properties.Crashed,
            TelemetryConstants.Properties.IsCrash,
        };

        foreach (var prop in properties)
        {
            Assert.False(string.IsNullOrWhiteSpace(prop));
        }

        Assert.Equal(properties.Length, properties.Distinct().Count());
    }

    /// <summary>
    /// Verifies opt-out environment variables and truthy values.
    /// </summary>
    [Fact]
    public void EnvironmentVariables_ContainExpectedKeys()
    {
        Assert.Equal("GENHUB_TELEMETRY_OPTOUT", TelemetryConstants.EnvironmentVariables.GenHubTelemetryOptOut);
        Assert.Equal("DO_NOT_TRACK", TelemetryConstants.EnvironmentVariables.DoNotTrack);
        Assert.Contains("1", TelemetryConstants.OptOutTruthyValues);
        Assert.Contains("true", TelemetryConstants.OptOutTruthyValues);
        Assert.Contains("yes", TelemetryConstants.OptOutTruthyValues);
        Assert.Contains("on", TelemetryConstants.OptOutTruthyValues);
    }
}
