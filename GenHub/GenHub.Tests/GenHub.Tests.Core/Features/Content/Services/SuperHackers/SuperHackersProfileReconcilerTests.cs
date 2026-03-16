using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.SuperHackers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Content.Services.SuperHackers;

/// <summary>
/// Tests for <see cref="SuperHackersProfileReconciler"/>.
/// </summary>
public class SuperHackersProfileReconcilerTests
{
    private readonly Mock<ISuperHackersUpdateService> _updateServiceMock;
    private readonly Mock<IContentManifestPool> _manifestPoolMock;
    private readonly Mock<IContentOrchestrator> _contentOrchestratorMock;
    private readonly Mock<IContentReconciliationService> _reconciliationServiceMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IDialogService> _dialogServiceMock;
    private readonly Mock<IUserSettingsService> _userSettingsServiceMock;
    private readonly Mock<IGameProfileManager> _profileManagerMock;

    private readonly SuperHackersProfileReconciler _reconciler;

    /// <summary>
    /// Initializes a new instance of the <see cref="SuperHackersProfileReconcilerTests"/> class.
    /// </summary>
    public SuperHackersProfileReconcilerTests()
    {
        _updateServiceMock = new Mock<ISuperHackersUpdateService>();
        _manifestPoolMock = new Mock<IContentManifestPool>();
        _contentOrchestratorMock = new Mock<IContentOrchestrator>();
        _reconciliationServiceMock = new Mock<IContentReconciliationService>();
        _notificationServiceMock = new Mock<INotificationService>();
        _dialogServiceMock = new Mock<IDialogService>();
        _userSettingsServiceMock = new Mock<IUserSettingsService>();
        _profileManagerMock = new Mock<IGameProfileManager>();

        _reconciliationServiceMock
            .Setup(x => x.OrchestrateBulkUpdateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(0, 0)));

        _reconciliationServiceMock
            .Setup(x => x.ScheduleGarbageCollectionAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());

        _profileManagerMock
            .Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([])));

        _reconciler = new SuperHackersProfileReconciler(
            NullLogger<SuperHackersProfileReconciler>.Instance,
            _updateServiceMock.Object,
            _manifestPoolMock.Object,
            _contentOrchestratorMock.Object,
            _reconciliationServiceMock.Object,
            _notificationServiceMock.Object,
            _dialogServiceMock.Object,
            _userSettingsServiceMock.Object,
            _profileManagerMock.Object);
    }

    /// <summary>
    /// Returns false (no update performed) when no update is available.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_NoUpdateAvailable_ReturnsFalse()
    {
        _updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateNoUpdateAvailable("1.0.0"));

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1");

        Assert.True(result.Success);
        Assert.False(result.Data);
    }

    /// <summary>
    /// Returns failure when the update check itself fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_UpdateCheckFails_ReturnsFailure()
    {
        _updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("network error"));

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1");

        Assert.False(result.Success);
    }

    /// <summary>
    /// Returns false without running reconciliation when the user has skipped the update version.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_VersionSkipped_ReturnsFalse()
    {
        const string latestVersion = "2.0.0";

        _updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "1.0.0"));

        var settings = new UserSettings();
        settings.SkipVersion(PublisherTypeConstants.TheSuperHackers, latestVersion);

        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1");

        Assert.True(result.Success);
        Assert.False(result.Data);
        _contentOrchestratorMock.Verify(
            x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Returns false (no update performed) when the user dismisses the update dialog without accepting.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_UserSkipsDialog_ReturnsFalse()
    {
        _updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable("2.0.0", "1.0.0"));

        var settings = new UserSettings();
        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        _dialogServiceMock
            .Setup(x => x.ShowUpdateOptionDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new UpdateDialogResult { Action = "Skip" });

        _userSettingsServiceMock
            .Setup(x => x.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()))
            .ReturnsAsync(true);

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1");

        Assert.True(result.Success);
        Assert.False(result.Data);
        _contentOrchestratorMock.Verify(
            x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Returns failure when content acquisition fails after the user accepts the update.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckAndReconcileIfNeededAsync_AcquireFails_ReturnsFailure()
    {
        const string latestVersion = "2.0.0";

        _updateServiceMock
            .Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable(latestVersion, "1.0.0"));

        var settings = new UserSettings();
        settings.SetAutoUpdatePreference(PublisherTypeConstants.TheSuperHackers, true);
        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        _manifestPoolMock
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        _contentOrchestratorMock
            .Setup(x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new ContentSearchResult { Name = "SuperHackers", Version = latestVersion },
            ]));

        _contentOrchestratorMock
            .Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateFailure("download timed out"));

        var result = await _reconciler.CheckAndReconcileIfNeededAsync("profile1");

        Assert.False(result.Success);
        Assert.Contains("download timed out", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }
}
