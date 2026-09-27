using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Downloads.ViewModels;

/// <summary>
/// Tests that bundle-component updates in <see cref="ContentDetailViewModel"/> match
/// the shared bulk-update path: stale workspaces are cleaned, the workspace link is
/// cleared, and open profile settings are notified of the replacement.
/// </summary>
public sealed class ContentDetailBundleUpdateTests
{
    private const string OldManifestId = "1.0.test.mod.old";
    private const string NewManifestId = "1.0.test.mod.new";

    /// <summary>
    /// Verifies that a ReplaceCurrent bundle update cleans the stale workspace,
    /// clears the profile workspace link, and broadcasts the replacement.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ApplyBundleComponentUpdateStrategy_ReplaceCurrent_CleansWorkspaceAndBroadcastsAsync()
    {
        var profile = new GameProfile
        {
            Id = "profile-1",
            Name = "Profile",
            EnabledContentIds = [OldManifestId],
            ActiveWorkspaceId = "workspace-1",
        };
        var profileManagerMock = new Mock<IGameProfileManager>();
        profileManagerMock
            .Setup(m => m.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));
        UpdateProfileRequest? capturedRequest = null;
        profileManagerMock
            .Setup(m => m.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, UpdateProfileRequest, CancellationToken>((_, request, _) => capturedRequest = request)
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        var workspaceMock = new Mock<IWorkspaceManager>();
        workspaceMock
            .Setup(m => m.CleanupWorkspaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var viewModel = CreateViewModel(profileManagerMock.Object, workspaceMock.Object);
        var recipient = new object();
        ManifestReplacedMessage? received = null;
        WeakReferenceMessenger.Default.Register<ManifestReplacedMessage>(recipient, (_, message) => received = message);

        try
        {
            await viewModel.ApplyBundleComponentUpdateStrategyAsync(
                new ContentSearchResult { Id = "content-1", Name = "Content" },
                "content-1",
                OldManifestId,
                CreateManifest(),
                new UpdateDialogResult { Strategy = UpdateStrategy.ReplaceCurrent, DeleteOldVersions = false },
                CancellationToken.None);
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<ManifestReplacedMessage>(recipient);
        }

        workspaceMock.Verify(m => m.CleanupWorkspaceAsync("workspace-1", It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(capturedRequest);
        Assert.Equal(string.Empty, capturedRequest.ActiveWorkspaceId);
        Assert.Equal([NewManifestId], capturedRequest.EnabledContentIds);
        Assert.NotNull(received);
        Assert.Equal(OldManifestId, received.OldId);
        Assert.Equal(NewManifestId, received.NewId);
    }

    /// <summary>
    /// Verifies that a CreateNewProfile bundle update leaves existing profiles and
    /// their workspaces untouched without broadcasting a replacement.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ApplyBundleComponentUpdateStrategy_CreateNewProfile_SkipsCleanupAndBroadcastAsync()
    {
        var profile = new GameProfile
        {
            Id = "profile-1",
            Name = "Profile",
            EnabledContentIds = [OldManifestId],
            ActiveWorkspaceId = "workspace-1",
        };
        var profileManagerMock = new Mock<IGameProfileManager>();
        profileManagerMock
            .Setup(m => m.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));
        profileManagerMock
            .Setup(m => m.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        var workspaceMock = new Mock<IWorkspaceManager>();

        var viewModel = CreateViewModel(profileManagerMock.Object, workspaceMock.Object);
        var recipient = new object();
        ManifestReplacedMessage? received = null;
        WeakReferenceMessenger.Default.Register<ManifestReplacedMessage>(recipient, (_, message) => received = message);

        try
        {
            await viewModel.ApplyBundleComponentUpdateStrategyAsync(
                new ContentSearchResult { Id = "content-1", Name = "Content" },
                "content-1",
                OldManifestId,
                CreateManifest(),
                new UpdateDialogResult { Strategy = UpdateStrategy.CreateNewProfile, DeleteOldVersions = false },
                CancellationToken.None);
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<ManifestReplacedMessage>(recipient);
        }

        profileManagerMock.Verify(
            m => m.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        workspaceMock.Verify(
            m => m.CleanupWorkspaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Null(received);
    }

    private static ContentDetailViewModel CreateViewModel(IGameProfileManager profileManager, IWorkspaceManager workspaceManager)
    {
        return new ContentDetailViewModel(
            new ContentSearchResult { Id = "content-1", Name = "Content" },
            [],
            Mock.Of<IProfileContentService>(),
            profileManager,
            Mock.Of<INotificationService>(),
            Mock.Of<ITabProviderRegistry>(),
            Mock.Of<IContentStateService>(),
            Mock.Of<IContentDownloadCoordinator>(),
            Mock.Of<IContentManifestPool>(),
            NullLoggerFactory.Instance,
            NullLogger<ContentDetailViewModel>.Instance,
            workspaceManager: workspaceManager);
    }

    private static ContentManifest CreateManifest()
    {
        return new ContentManifest
        {
            Id = ManifestId.Create(NewManifestId),
            Name = "Content",
            Version = "2.0.0",
            TargetGame = GameType.ZeroHour,
            ContentType = ContentType.Mod,
        };
    }
}
