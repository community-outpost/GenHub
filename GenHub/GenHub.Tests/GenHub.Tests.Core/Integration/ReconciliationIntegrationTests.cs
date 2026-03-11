using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Models.Storage;
using GenHub.Features.Content.Services.CommunityOutpost;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Features.Content.Services.SuperHackers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Integration;

/// <summary>
/// Integration tests for content reconciliation across different providers.
/// </summary>
public class ReconciliationIntegrationTests : IDisposable
{
    private readonly HttpClient _sharedHttpClient;
    private readonly Mock<IContentManifestPool> _manifestPoolMock;
    private readonly Mock<IContentOrchestrator> _orchestratorMock;
    private readonly Mock<IContentReconciliationService> _reconciliationServiceMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IDialogService> _dialogServiceMock;
    private readonly Mock<IUserSettingsService> _userSettingsServiceMock;
    private readonly Mock<IGameProfileManager> _profileManagerMock;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReconciliationIntegrationTests"/> class.
    /// </summary>
    public ReconciliationIntegrationTests()
    {
        _sharedHttpClient = new HttpClient();
        _manifestPoolMock = new Mock<IContentManifestPool>();
        _orchestratorMock = new Mock<IContentOrchestrator>();
        _reconciliationServiceMock = new Mock<IContentReconciliationService>();
        _notificationServiceMock = new Mock<INotificationService>();
        _dialogServiceMock = new Mock<IDialogService>();
        _userSettingsServiceMock = new Mock<IUserSettingsService>();
        _profileManagerMock = new Mock<IGameProfileManager>();

        // Default settings
        var settings = new UserSettings
        {
            PreferredUpdateStrategy = UpdateStrategy.ReplaceCurrent,
            ExplicitlySetProperties = [],
            CasConfiguration = new CasConfiguration(),
        };
        settings.SetAutoUpdatePreference(GeneralsOnlineConstants.PublisherType, true);
        settings.SetAutoUpdatePreference(CommunityOutpostConstants.PublisherType, true);
        settings.SetAutoUpdatePreference(PublisherTypeConstants.TheSuperHackers, true);

        _userSettingsServiceMock.Setup(x => x.Get()).Returns(settings);

        _manifestPoolMock.Setup(x => x.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _manifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _reconciliationServiceMock.Setup(x => x.OrchestrateBulkUpdateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(1, 0)));

        _reconciliationServiceMock.Setup(x => x.OrchestrateBulkRemovalAsync(It.IsAny<IEnumerable<ManifestId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(0, 0)));

        _reconciliationServiceMock.Setup(x => x.ScheduleGarbageCollectionAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());
    }

    /// <summary>
    /// Disposes resources used by the test class.
    /// </summary>
    public void Dispose()
    {
        _sharedHttpClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that a Community Outpost profile is updated when a new version is available.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CommunityOutpost_UpdateAvailable_ShouldUpdateProfile()
    {
        // Arrange
        var oldManifestId = "1.10.communityoutpost.patch.communitypatch";

        var profile = new GameProfile
        {
            Id = "profile1",
            Name = "My Profile",
            EnabledContentIds = [oldManifestId],
        };

        // Setup profile manager to return the test profile
        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));

        var updateServiceMock = new Mock<ICommunityOutpostUpdateService>();

        updateServiceMock.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable("1.11", "1.10"));

        var oldManifest = new ContentManifest
        {
            Id = new ManifestId(oldManifestId),
            ContentType = ContentType.Patch,
            Version = "1.10",
            Publisher = new PublisherInfo { PublisherType = CommunityOutpostConstants.PublisherType },
        };
        var newManifest = new ContentManifest
        {
            Id = new ManifestId("1.11.communityoutpost.patch.communitypatch"),
            ContentType = ContentType.Patch,
            Version = "1.11",
            Publisher = new PublisherInfo { PublisherType = CommunityOutpostConstants.PublisherType },
        };

        _manifestPoolMock.SetupSequence(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldManifest]))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldManifest, newManifest]));

        _orchestratorMock.Setup(x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
            [
                new ContentSearchResult { Id = "1.11", Version = "1.11", ProviderName = "1.11" },
            ]));

        _orchestratorMock.Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(newManifest));

        var reconciler = new CommunityOutpostProfileReconciler(
            NullLogger<CommunityOutpostProfileReconciler>.Instance,
            updateServiceMock.Object,
            _manifestPoolMock.Object,
            _orchestratorMock.Object,
            _reconciliationServiceMock.Object,
            _notificationServiceMock.Object,
            _dialogServiceMock.Object,
            _userSettingsServiceMock.Object,
            _profileManagerMock.Object);

        // Act
        var result = await reconciler.CheckAndReconcileIfNeededAsync(profile.Id);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        result.Data.Should().BeTrue("Reconciler should report true when update was applied");

        // Verify that the bulk update was orchestrated
        _reconciliationServiceMock.Verify(
            x => x.OrchestrateBulkUpdateAsync(
                It.Is<IReadOnlyDictionary<string, string>>(d => d.ContainsKey(oldManifestId) && d[oldManifestId] == newManifest.Id.Value),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that Generals Online updates enforce map pack dependencies.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GeneralsOnline_UpdateWithMapPack_ShouldEnforceDependency()
    {
        // Arrange
        var updateServiceMock = new Mock<IGeneralsOnlineUpdateService>();

        updateServiceMock.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable("10.0", "9.0"));

        var oldGameClient = CreateManifest("1.9.generalsonline.gameclient.30hz", "9.0", ContentType.GameClient, PublisherTypeConstants.GeneralsOnline, GameType.ZeroHour);

        var newGameClient = CreateManifest("1.10.generalsonline.gameclient.30hz", "10.0", ContentType.GameClient, PublisherTypeConstants.GeneralsOnline, GameType.ZeroHour);
        var newMapPack = CreateManifest("1.10.generalsonline.mappack.mappack", "10.0", ContentType.MapPack, PublisherTypeConstants.GeneralsOnline, GameType.ZeroHour);

        _manifestPoolMock.SetupSequence(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldGameClient]))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldGameClient, newGameClient, newMapPack]))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldGameClient, newGameClient, newMapPack]));

        _manifestPoolMock.Setup(x => x.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId id, CancellationToken ct) =>
            {
                ContentManifest? manifest = null;
                if (id.Value == newGameClient.Id.Value)
                {
                    manifest = newGameClient;
                }
                else if (id.Value == newMapPack.Id.Value)
                {
                    manifest = newMapPack;
                }

                return manifest != null
                    ? OperationResult<ContentManifest?>.CreateSuccess(manifest)
                    : OperationResult<ContentManifest?>.CreateFailure("Manifest not found");
            });

        _orchestratorMock.Setup(x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentSearchQuery q, CancellationToken t) =>
            {
                if (q.ContentType == ContentType.GameClient)
                {
                    return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([new ContentSearchResult { Id = newGameClient.Id.Value, Version = newGameClient.Version }]);
                }

                if (q.ContentType == ContentType.MapPack)
                {
                    return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([new ContentSearchResult { Id = newMapPack.Id.Value, Version = newMapPack.Version }]);
                }

                return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure("Not found");
            });

        _orchestratorMock.Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentSearchResult r, IProgress<ContentAcquisitionProgress> p, CancellationToken c) =>
            {
                var contentTypeName = r.Id == newMapPack.Id.Value ? ContentType.MapPack : ContentType.GameClient;
                return OperationResult<ContentManifest>.CreateSuccess(
                    new ContentManifest
                    {
                        Id = ManifestId.Create(r.Id),
                        Name = r.Id,
                        Version = r.Version,
                        ContentType = contentTypeName,
                        TargetGame = GameType.ZeroHour,
                        Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GeneralsOnline },
                    });
            });

        var profile = new GameProfile
        {
            Id = "go-profile",
            Name = "GO Profile",
            GameClient = new GameClient
            {
                Id = oldGameClient.Id.Value,
                Name = "Old GO Client",
                Version = oldGameClient.Version ?? string.Empty,
                GameType = GameType.ZeroHour,
                PublisherType = PublisherTypeConstants.GeneralsOnline,
            },
            EnabledContentIds = [],
        };

        _reconciliationServiceMock.Setup(x => x.OrchestrateBulkUpdateAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(1, 0)))
            .Callback((IReadOnlyDictionary<string, string> mapping, bool delete, CancellationToken ct) =>
            {
                if (mapping.TryGetValue(oldGameClient.Id.Value, out var newId))
                {
                    profile.GameClient.Id = newId;
                }
            });

        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));

        _profileManagerMock.Setup(x => x.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        var reconciler = new GeneralsOnlineProfileReconciler(
            NullLogger<GeneralsOnlineProfileReconciler>.Instance,
            updateServiceMock.Object,
            _manifestPoolMock.Object,
            _orchestratorMock.Object,
            _reconciliationServiceMock.Object,
            _notificationServiceMock.Object,
            _dialogServiceMock.Object,
            _userSettingsServiceMock.Object,
            _profileManagerMock.Object);

        // Act
        var result = await reconciler.CheckAndReconcileIfNeededAsync(profile.Id);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);

        _profileManagerMock.Verify(
            x => x.UpdateProfileAsync(
                profile.Id,
                It.Is<UpdateProfileRequest>(r => r != null && r.EnabledContentIds != null && r.EnabledContentIds.Contains(newMapPack.Id.Value)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that updating a specific variant (e.g. Generals) doesn't switch to ZeroHour or vice versa.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SuperHackers_UpdateVariants_ShouldPreserveGameType()
    {
        // Arrange
        var updateServiceMock = new Mock<ISuperHackersUpdateService>();
        var settings = _userSettingsServiceMock.Object.Get();
        settings.PreferredUpdateStrategy = UpdateStrategy.CreateNewProfile;

        updateServiceMock.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(ContentUpdateCheckResult.CreateUpdateAvailable("20260127", "20250101"));

        var oldGeneralsParams = CreateManifest("1.20250101.thesuperhackers.gameclient.generals", "20250101", ContentType.GameClient, PublisherTypeConstants.TheSuperHackers, GameType.Generals);
        var newGeneralsParams = CreateManifest("1.20260127.thesuperhackers.gameclient.generals", "20260127", ContentType.GameClient, PublisherTypeConstants.TheSuperHackers, GameType.Generals);
        var newZeroHourParams = CreateManifest("1.20260127.thesuperhackers.gameclient.zerohour", "20260127", ContentType.GameClient, PublisherTypeConstants.TheSuperHackers, GameType.ZeroHour);

        _manifestPoolMock.SetupSequence(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldGeneralsParams]))
             .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldGeneralsParams, newGeneralsParams, newZeroHourParams]))
             .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([oldGeneralsParams, newGeneralsParams, newZeroHourParams]));

        _orchestratorMock.Setup(x => x.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([
                new ContentSearchResult { Id = newGeneralsParams.Id.Value },
                new ContentSearchResult { Id = newZeroHourParams.Id.Value },
            ]));

        _orchestratorMock.Setup(x => x.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentSearchResult r, IProgress<ContentAcquisitionProgress> p, CancellationToken c) =>
                OperationResult<ContentManifest>.CreateSuccess(
                    new ContentManifest
                    {
                        Id = ManifestId.Create(r.Id),
                        Version = r.Version,
                        ContentType = ContentType.GameClient,
                        TargetGame = GameType.Generals,
                        Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.TheSuperHackers },
                    }));

        var profile = new GameProfile
        {
            Id = "sh-profile",
            Name = "SH Generals",
            GameClient = new GameClient { Id = oldGeneralsParams.Id.Value, Name = "Old SH Client", PublisherType = PublisherTypeConstants.TheSuperHackers },
            EnabledContentIds = [],
        };

        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));

        _profileManagerMock.Setup(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        var reconciler = new SuperHackersProfileReconciler(
            NullLogger<SuperHackersProfileReconciler>.Instance,
            updateServiceMock.Object,
            _manifestPoolMock.Object,
            _orchestratorMock.Object,
            _reconciliationServiceMock.Object,
            _notificationServiceMock.Object,
            _dialogServiceMock.Object,
            _userSettingsServiceMock.Object,
            _profileManagerMock.Object);

        // Act
        var result = await reconciler.CheckAndReconcileIfNeededAsync(profile.Id);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);

        // Verify that the profile was updated with the generals GameClient ID (not zerohour)
        _profileManagerMock.Verify(
            x => x.CreateProfileAsync(
                It.Is<CreateProfileRequest>(req => req.GameClient != null && req.GameClient.Id == newGeneralsParams.Id.Value),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Should preserve the generals GameClient ID variant");
    }

    private static ContentManifest CreateManifest(string id, string version, ContentType type, string publisher, GameType targetGame)
    {
        return new ContentManifest
        {
            Id = ManifestId.Create(id),
            Name = id,
            Version = version,
            ContentType = type,
            TargetGame = targetGame,
            Publisher = new PublisherInfo { PublisherType = publisher },
        };
    }
}
