using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Verifies that <see cref="ProfileContentService"/> reports whether an add-to-profile
/// operation landed in an active game session. Live synchronization itself is owned by the
/// profile manager's single live-sync pipeline; the service only surfaces the outcome.
/// </summary>
public sealed class ProfileContentServiceLiveTests
{
    /// <summary>
    /// Verifies that adding a map to a running profile reports it as applied live.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task AddContentToProfileAsync_WhenProfileIsRunning_ReportsAppliedLiveAsync()
    {
        // Arrange
        var fixture = new LiveFixture(running: true);

        // Act
        var result = await fixture.Service.AddContentToProfileAsync(fixture.Profile.Id, LiveFixture.MapId);

        // Assert
        Assert.True(result.Success, result.FirstError);
        Assert.True(result.WasAppliedLive);
        Assert.NotNull(fixture.CapturedRequest?.EnabledContentIds);
        Assert.Contains(LiveFixture.MapId, fixture.CapturedRequest.EnabledContentIds, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that adding a map to an idle profile does not report it as applied live.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task AddContentToProfileAsync_WhenProfileIsIdle_ReportsNotAppliedLiveAsync()
    {
        // Arrange
        var fixture = new LiveFixture(running: false);

        // Act
        var result = await fixture.Service.AddContentToProfileAsync(fixture.Profile.Id, LiveFixture.MapId);

        // Assert
        Assert.True(result.Success, result.FirstError);
        Assert.False(result.WasAppliedLive);
        Assert.NotNull(fixture.CapturedRequest?.EnabledContentIds);
        Assert.Contains(LiveFixture.MapId, fixture.CapturedRequest.EnabledContentIds, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that a missing launch registry degrades to not applied live without failing the add.
    /// </summary>
    /// <returns>A task representing the test operation.</returns>
    [Fact]
    public async Task AddContentToProfileAsync_WhenLaunchRegistryIsMissing_ReportsNotAppliedLiveAsync()
    {
        // Arrange
        var fixture = new LiveFixture(running: true, includeLaunchRegistry: false);

        // Act
        var result = await fixture.Service.AddContentToProfileAsync(fixture.Profile.Id, LiveFixture.MapId);

        // Assert
        Assert.True(result.Success, result.FirstError);
        Assert.False(result.WasAppliedLive);
    }

    private sealed class LiveFixture
    {
        internal const string InstallationId = "1.104.steam.gameinstallation.zerohour";
        internal const string ClientId = "1.0.communityoutpost.gameclient.communitypatch";
        internal const string MapId = "1.0.0.map.desert";

        internal LiveFixture(bool running, bool includeLaunchRegistry = true)
        {
            Profile = new GameProfile
            {
                Id = "profile-id",
                Name = "Community Patch",
                GameClient = new GameClient { Id = ClientId, GameType = GameType.ZeroHour },
                EnabledContentIds = [InstallationId, ClientId],
            };

            var manifests = new Dictionary<string, ContentManifest>(StringComparer.OrdinalIgnoreCase)
            {
                [InstallationId] = CreateManifest(InstallationId, "Zero Hour Installation", ContentType.GameInstallation),
                [ClientId] = CreateManifest(ClientId, "Community Patch", ContentType.GameClient),
                [MapId] = CreateManifest(MapId, "Tournament Desert", ContentType.Map),
            };

            var profileManager = new Mock<IGameProfileManager>();
            var manifestPool = new Mock<IContentManifestPool>();
            var installationService = new Mock<IGameInstallationService>();
            var contentOrchestrator = new Mock<IContentOrchestrator>();
            var notifications = new Mock<INotificationService>();

            profileManager
                .Setup(manager => manager.GetProfileAsync(Profile.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(Profile));
            profileManager
                .Setup(manager => manager.UpdateProfileAsync(Profile.Id, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, UpdateProfileRequest request, CancellationToken _) =>
                {
                    CapturedRequest = request;
                    return ProfileOperationResult<GameProfile>.CreateSuccess(Profile);
                });
            manifestPool
                .Setup(pool => pool.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ManifestId manifestId, CancellationToken _) =>
                    manifests.TryGetValue(manifestId.Value, out var manifest)
                        ? OperationResult<ContentManifest?>.CreateSuccess(manifest)
                        : OperationResult<ContentManifest?>.CreateSuccess(null));
            manifestPool
                .Setup(pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifests.Values));

            Mock<ILaunchRegistry>? launchRegistry = null;
            if (includeLaunchRegistry)
            {
                launchRegistry = new Mock<ILaunchRegistry>();
                launchRegistry.Setup(l => l.GetAllActiveLaunchesAsync())
                    .ReturnsAsync(running ? [CreateActiveLaunch(Profile.Id)] : []);
            }

            Service = new ProfileContentService(
                profileManager.Object,
                manifestPool.Object,
                new ProfileContentResolutionServices(
                    new DependencyResolver(
                        manifestPool.Object,
                        NullLogger<DependencyResolver>.Instance),
                    installationService.Object),
                contentOrchestrator.Object,
                notifications.Object,
                NullLogger<ProfileContentService>.Instance,
                null,
                launchRegistry?.Object);
        }

        internal GameProfile Profile { get; }

        internal ProfileContentService Service { get; }

        internal UpdateProfileRequest? CapturedRequest { get; private set; }

        private static ContentManifest CreateManifest(string id, string name, ContentType contentType)
        {
            return new ContentManifest
            {
                Id = ManifestId.Create(id),
                Name = name,
                ContentType = contentType,
                TargetGame = GameType.ZeroHour,
            };
        }

        private static GameLaunchInfo CreateActiveLaunch(string profileId) => new()
        {
            LaunchId = "launch-1",
            ProfileId = profileId,
            WorkspaceId = "ws-1",
            ProcessInfo = new GameProcessInfo
            {
                ProcessId = 1234,
                ProcessName = "generals.exe",
                StartTime = DateTime.UtcNow,
            },
        };
    }
}
