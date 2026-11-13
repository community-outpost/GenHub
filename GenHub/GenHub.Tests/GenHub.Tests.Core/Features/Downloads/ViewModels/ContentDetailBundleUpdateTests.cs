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
using System;
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

    /// <summary>
    /// Verifies that a failed profile update skips workspace cleanup and the
    /// replacement broadcast, leaving the old workspace intact, and raises a warning toast.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ApplyBundleComponentUpdateStrategy_UpdateFails_SkipsCleanupAndBroadcastAsync()
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
            .Setup(m => m.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("update failed"));
        var workspaceMock = new Mock<IWorkspaceManager>();
        var notificationMock = new Mock<INotificationService>();

        var viewModel = CreateViewModel(
            profileManagerMock.Object,
            workspaceMock.Object,
            notificationService: notificationMock.Object);
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

        workspaceMock.Verify(
            m => m.CleanupWorkspaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        notificationMock.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        Assert.Null(received);
    }

    /// <summary>
    /// Verifies that an unexpected exception during profile update is caught,
    /// skips cleanup and broadcast, and raises a warning toast.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ApplyBundleComponentUpdateStrategy_StrategyThrows_LogsAndRaisesWarningToastAsync()
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
            .ThrowsAsync(new InvalidOperationException("unexpected error"));
        var workspaceMock = new Mock<IWorkspaceManager>();
        var notificationMock = new Mock<INotificationService>();

        var viewModel = CreateViewModel(
            profileManagerMock.Object,
            workspaceMock.Object,
            notificationService: notificationMock.Object);
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

        workspaceMock.Verify(
            m => m.CleanupWorkspaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        notificationMock.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        Assert.Null(received);
    }

    /// <summary>
    /// Verifies that cancellation during old-manifest removal propagates instead
    /// of being swallowed by the removal error handler.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ApplyBundleComponentUpdateStrategy_RemovalCanceled_RethrowsAsync()
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
            .Setup(m => m.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        var manifestPoolMock = new Mock<IContentManifestPool>();
        manifestPoolMock
            .Setup(m => m.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var workspaceMock = new Mock<IWorkspaceManager>();
        workspaceMock
            .Setup(m => m.CleanupWorkspaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var viewModel = CreateViewModel(
            profileManagerMock.Object,
            workspaceMock.Object,
            manifestPoolMock.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => viewModel.ApplyBundleComponentUpdateStrategyAsync(
            new ContentSearchResult { Id = "content-1", Name = "Content" },
            "content-1",
            OldManifestId,
            CreateManifest(),
            new UpdateDialogResult { Strategy = UpdateStrategy.ReplaceCurrent, DeleteOldVersions = true },
            CancellationToken.None));
    }

    /// <summary>
    /// Verifies that a failed profile scrub surfaces a warning while the manifest
    /// deletion still stands and the downloaded state is still reported.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ApplyBundleComponentUpdateStrategy_ScrubFails_WarnsButKeepsDeletionAsync()
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
            .Setup(m => m.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        profileManagerMock
            .Setup(m => m.ScrubDeletedManifestReferencesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ProfileScrubResult>.CreateFailure("Profile store unavailable"));
        var manifestPoolMock = new Mock<IContentManifestPool>();
        manifestPoolMock
            .Setup(m => m.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        var workspaceMock = new Mock<IWorkspaceManager>();
        workspaceMock
            .Setup(m => m.CleanupWorkspaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        var notificationMock = new Mock<INotificationService>();
        var contentStateMock = new Mock<IContentStateService>();

        var viewModel = CreateViewModel(
            profileManagerMock.Object,
            workspaceMock.Object,
            manifestPoolMock.Object,
            notificationMock.Object,
            contentStateMock.Object);

        await viewModel.ApplyBundleComponentUpdateStrategyAsync(
            new ContentSearchResult { Id = "content-1", Name = "Content" },
            "content-1",
            OldManifestId,
            CreateManifest(),
            new UpdateDialogResult { Strategy = UpdateStrategy.ReplaceCurrent, DeleteOldVersions = true },
            CancellationToken.None);

        manifestPoolMock.Verify(
            m => m.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
        notificationMock.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        contentStateMock.Verify(
            s => s.NotifyStateChanged("content-1", ContentState.Downloaded, NewManifestId, null),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a partially failed profile scrub surfaces a warning naming
    /// the affected profiles.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ApplyBundleComponentUpdateStrategy_ScrubPartial_WarnsWithProfileNamesAsync()
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
            .Setup(m => m.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        profileManagerMock
            .Setup(m => m.ScrubDeletedManifestReferencesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ProfileScrubResult>.CreateSuccess(new ProfileScrubResult(0, 0, ["Stale Profile"])));
        var manifestPoolMock = new Mock<IContentManifestPool>();
        manifestPoolMock
            .Setup(m => m.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        var workspaceMock = new Mock<IWorkspaceManager>();
        workspaceMock
            .Setup(m => m.CleanupWorkspaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        var notificationMock = new Mock<INotificationService>();

        var viewModel = CreateViewModel(
            profileManagerMock.Object,
            workspaceMock.Object,
            manifestPoolMock.Object,
            notificationMock.Object);

        await viewModel.ApplyBundleComponentUpdateStrategyAsync(
            new ContentSearchResult { Id = "content-1", Name = "Content" },
            "content-1",
            OldManifestId,
            CreateManifest(),
            new UpdateDialogResult { Strategy = UpdateStrategy.ReplaceCurrent, DeleteOldVersions = true },
            CancellationToken.None);

        notificationMock.Verify(
            n => n.ShowWarning(
                It.IsAny<string>(),
                It.Is<string>(message => message.Contains("Stale Profile", StringComparison.Ordinal)),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when one profile update fails during a bundle update, earlier
    /// replaced profiles are rolled back to their original state.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ApplyBundleComponentUpdateStrategy_ProfileUpdateFails_RollsBackEarlierReplacedProfilesAsync()
    {
        var profile1 = new GameProfile
        {
            Id = "profile-1",
            Name = "Profile 1",
            EnabledContentIds = [OldManifestId],
            ActiveWorkspaceId = "workspace-1",
        };
        var profile2 = new GameProfile
        {
            Id = "profile-2",
            Name = "Profile 2",
            EnabledContentIds = [OldManifestId],
            ActiveWorkspaceId = "workspace-2",
        };

        var profileManagerMock = new Mock<IGameProfileManager>();
        profileManagerMock
            .Setup(m => m.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile1, profile2]));

        profileManagerMock
            .Setup(m => m.UpdateProfileAsync("profile-1", It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile1));

        profileManagerMock
            .Setup(m => m.UpdateProfileAsync("profile-2", It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("Database error"));

        var workspaceMock = new Mock<IWorkspaceManager>();
        workspaceMock
            .Setup(m => m.CleanupWorkspaceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var notificationMock = new Mock<INotificationService>();
        var viewModel = CreateViewModel(
            profileManagerMock.Object,
            workspaceMock.Object,
            notificationService: notificationMock.Object);

        await viewModel.ApplyBundleComponentUpdateStrategyAsync(
            new ContentSearchResult { Id = "content-1", Name = "Content" },
            "content-1",
            OldManifestId,
            CreateManifest(),
            new UpdateDialogResult { Strategy = UpdateStrategy.ReplaceCurrent, DeleteOldVersions = true },
            CancellationToken.None);

        // Profile 1 was updated to new manifest, then rolled back to original
        profileManagerMock.Verify(m => m.UpdateProfileAsync("profile-1", It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        notificationMock.Verify(n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
    }

    private static ContentDetailViewModel CreateViewModel(
        IGameProfileManager profileManager,
        IWorkspaceManager workspaceManager,
        IContentManifestPool? manifestPool = null,
        INotificationService? notificationService = null,
        IContentStateService? contentStateService = null)
    {
        return new ContentDetailViewModel(
            new ContentSearchResult { Id = "content-1", Name = "Content" },
            [],
            Mock.Of<IProfileContentService>(),
            profileManager,
            notificationService ?? Mock.Of<INotificationService>(),
            Mock.Of<ITabProviderRegistry>(),
            contentStateService ?? Mock.Of<IContentStateService>(),
            Mock.Of<IContentDownloadCoordinator>(),
            manifestPool ?? Mock.Of<IContentManifestPool>(),
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
