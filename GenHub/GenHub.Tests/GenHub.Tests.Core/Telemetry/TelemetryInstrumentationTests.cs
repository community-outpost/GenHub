using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
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
    /// Verifies that content update applied telemetry is dispatched through reconciler production code.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ContentUpdateApplied_Event_ContainsRequiredTelemetryPropertiesAsync()
    {
        const string latestVersion = "2.0.0";
        var updateServiceMock = new Mock<ICommunityOutpostUpdateService>();
        updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "1.0.0"));

        var settings = new UserSettings();
        settings.SetAutoUpdatePreference(CommunityOutpostConstants.PublisherType, true);
        var userSettingsServiceMock = new Mock<IUserSettingsService>();
        userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        var oldManifest = new ContentManifest
        {
            Id = "1.100.communityoutpost.patch.communitypatch",
            Name = "Community Patch",
            Version = "1.0.0",
            Publisher = new PublisherInfo
            {
                PublisherType = CommunityOutpostConstants.PublisherType,
                Name = "Community Outpost",
            },
        };

        var newManifest = new ContentManifest
        {
            Id = "1.200.communityoutpost.patch.communitypatch",
            Name = "Community Patch",
            Version = latestVersion,
            Publisher = new PublisherInfo
            {
                PublisherType = CommunityOutpostConstants.PublisherType,
                Name = "Community Outpost",
            },
        };

        var poolCallCount = 0;
        var manifestPoolMock = new Mock<IContentManifestPool>();
        manifestPoolMock
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                poolCallCount++;
                return OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(
                    poolCallCount == 1 ? [oldManifest] : [oldManifest, newManifest]);
            });

        var contentOrchestratorMock = new Mock<IContentOrchestrator>();
        contentOrchestratorMock
            .Setup(x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new ContentSearchResult { Name = "Community Patch", Version = latestVersion },
            ]));
        contentOrchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(newManifest));

        var profileManagerMock = new Mock<IGameProfileManager>();
        profileManagerMock
            .Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([])));

        var reconciliationServiceMock = new Mock<IContentReconciliationService>();
        reconciliationServiceMock
            .Setup(x => x.OrchestrateBulkUpdateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(1, 0)));

        var reconciler = new CommunityOutpostProfileReconciler(
            NullLogger<CommunityOutpostProfileReconciler>.Instance,
            updateServiceMock.Object,
            manifestPoolMock.Object,
            contentOrchestratorMock.Object,
            reconciliationServiceMock.Object,
            Mock.Of<INotificationService>(),
            Mock.Of<IDialogService>(),
            userSettingsServiceMock.Object,
            profileManagerMock.Object,
            _telemetryServiceMock.Object);

        var result = await reconciler.CheckAndReconcileIfNeededAsync("profile1");
        Assert.True(result.Success, result.FirstError);

        _telemetryServiceMock.Verify(
            t => t.TrackEvent(
                TelemetryConstants.Events.ContentUpdateApplied,
                It.Is<IReadOnlyDictionary<string, object?>?>(p =>
                    p != null &&
                    (string?)p[TelemetryConstants.Properties.PublisherId] == CommunityOutpostConstants.PublisherType &&
                    (string?)p[TelemetryConstants.Properties.FromVersion] == "1.0.0" &&
                    (string?)p[TelemetryConstants.Properties.ToVersion] == latestVersion &&
                    (bool?)p[TelemetryConstants.Properties.Success] == true),
                It.IsAny<TelemetryLevel>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that content update failed telemetry is dispatched through reconciler production code.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ContentUpdateFailed_Event_ContainsRequiredTelemetryPropertiesAsync()
    {
        const string latestVersion = "2.0.0";
        var updateServiceMock = new Mock<ICommunityOutpostUpdateService>();
        updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "1.0.0"));

        var settings = new UserSettings();
        settings.SetAutoUpdatePreference(CommunityOutpostConstants.PublisherType, true);
        var userSettingsServiceMock = new Mock<IUserSettingsService>();
        userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        var manifestPoolMock = new Mock<IContentManifestPool>();
        manifestPoolMock
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var contentOrchestratorMock = new Mock<IContentOrchestrator>();
        contentOrchestratorMock
            .Setup(x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new ContentSearchResult { Name = "Community Patch", Version = latestVersion },
            ]));

        contentOrchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateFailure("server unavailable"));

        var reconciler = new CommunityOutpostProfileReconciler(
            NullLogger<CommunityOutpostProfileReconciler>.Instance,
            updateServiceMock.Object,
            manifestPoolMock.Object,
            contentOrchestratorMock.Object,
            Mock.Of<IContentReconciliationService>(),
            Mock.Of<INotificationService>(),
            Mock.Of<IDialogService>(),
            userSettingsServiceMock.Object,
            Mock.Of<IGameProfileManager>(),
            _telemetryServiceMock.Object);

        await reconciler.CheckAndReconcileIfNeededAsync("profile1");

        _telemetryServiceMock.Verify(
            t => t.TrackEvent(
                TelemetryConstants.Events.ContentUpdateFailed,
                It.Is<IReadOnlyDictionary<string, object?>?>(p =>
                    p != null &&
                    (string?)p[TelemetryConstants.Properties.PublisherId] == CommunityOutpostConstants.PublisherType &&
                    (string?)p[TelemetryConstants.Properties.FromVersion] == "1.0.0" &&
                    (string?)p[TelemetryConstants.Properties.ToVersion] == latestVersion &&
                    ((string?)p[TelemetryConstants.Properties.ErrorMessage])!.Contains("server unavailable")),
                It.IsAny<TelemetryLevel>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that content download completed telemetry payload contains download metrics when driven through DownloadService.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ContentDownloadCompleted_Event_ContainsRequiredTelemetryPropertiesAsync()
    {
        var fileContent = new byte[] { 1, 2, 3, 4, 5 };
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fileContent),
            });

        var loggerMock = new Mock<ILogger<DownloadService>>();
        var httpClient = new HttpClient(handler.Object);
        var hashProvider = new Sha256HashProvider();
        var downloadService = new DownloadService(loggerMock.Object, httpClient, hashProvider, telemetryService: _telemetryServiceMock.Object);

        var tempFile = Path.GetTempFileName();
        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/file.bin"),
                DestinationPath = tempFile,
                OverwriteExisting = true,
            };

            var result = await downloadService.DownloadFileAsync(config);

            Assert.True(result.Success);
            _telemetryServiceMock.Verify(
                t => t.TrackEvent(
                    TelemetryConstants.Events.ContentDownloadCompleted,
                    It.Is<IReadOnlyDictionary<string, object?>?>(p =>
                        p != null &&
                        (string?)p[TelemetryConstants.Properties.FileName] == Path.GetFileName(tempFile) &&
                        p.ContainsKey(TelemetryConstants.Properties.SizeMb) &&
                        p.ContainsKey(TelemetryConstants.Properties.DurationSeconds)),
                    It.IsAny<TelemetryLevel>()),
                Times.Once);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that content download failed telemetry payload contains failure reasons when driven through DownloadService.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ContentDownloadFailed_Event_ContainsRequiredTelemetryPropertiesAsync()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        var loggerMock = new Mock<ILogger<DownloadService>>();
        var httpClient = new HttpClient(handler.Object);
        var hashProvider = new Sha256HashProvider();
        var downloadService = new DownloadService(loggerMock.Object, httpClient, hashProvider, telemetryService: _telemetryServiceMock.Object);

        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var config = new DownloadConfiguration
            {
                Url = new Uri("http://test/nonexistent.bin"),
                DestinationPath = tempFile,
                OverwriteExisting = true,
                MaxRetryAttempts = 1,
            };

            var result = await downloadService.DownloadFileAsync(config);

            Assert.False(result.Success);
            _telemetryServiceMock.Verify(
                t => t.TrackEvent(
                    TelemetryConstants.Events.ContentDownloadFailed,
                    It.Is<IReadOnlyDictionary<string, object?>?>(p =>
                        p != null &&
                        (string?)p[TelemetryConstants.Properties.FileName] == Path.GetFileName(tempFile) &&
                        p.ContainsKey(TelemetryConstants.Properties.ErrorMessage)),
                    It.IsAny<TelemetryLevel>()),
                Times.Once);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
