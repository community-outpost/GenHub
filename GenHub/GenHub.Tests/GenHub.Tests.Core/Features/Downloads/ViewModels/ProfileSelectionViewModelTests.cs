using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Downloads.ViewModels;

/// <summary>
/// Unit tests for <see cref="ProfileSelectionViewModel"/>.
/// </summary>
public sealed class ProfileSelectionViewModelTests
{
    /// <summary>
    /// Verifies that loading profiles populates compatible and other profile collections based on target game.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadProfilesAsync_PopulatesCompatibleAndOtherProfiles_BasedOnTargetGameAsync()
    {
        // Arrange
        var profileManagerMock = new Mock<IGameProfileManager>();
        var profileContentMock = new Mock<IProfileContentService>();
        var manifestPoolMock = new Mock<IContentManifestPool>();
        var notificationMock = new Mock<INotificationService>();

        var zhProfile = new GameProfile
        {
            Id = "zh-profile-1",
            Name = "Zero Hour Profile",
            GameClient = new GameClient { GameType = GameType.ZeroHour },
        };

        var genProfile = new GameProfile
        {
            Id = "gen-profile-1",
            Name = "Generals Profile",
            GameClient = new GameClient { GameType = GameType.Generals },
        };

        profileManagerMock
            .Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([zhProfile, genProfile]));

        manifestPoolMock
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var vm = new ProfileSelectionViewModel(
            NullLogger<ProfileSelectionViewModel>.Instance,
            profileManagerMock.Object,
            profileContentMock.Object,
            manifestPoolMock.Object,
            notificationMock.Object);

        // Act: Initialize for ZeroHour content
        await vm.LoadProfilesAsync(GameType.ZeroHour, "1.0.test.manifest", "Test Content");

        // Assert
        Assert.Single(vm.CompatibleProfiles);
        Assert.Equal("zh-profile-1", vm.CompatibleProfiles[0].Profile.Id);
        Assert.Single(vm.OtherProfiles);
        Assert.Equal("gen-profile-1", vm.OtherProfiles[0].Profile.Id);
        Assert.True(vm.HasCompatibleProfiles);
        Assert.True(vm.HasOtherProfiles);
        Assert.True(vm.HasAnyProfiles);
    }

    /// <summary>
    /// Verifies that selecting a profile with a single manifest calls the single manifest overload and closes the dialog.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SelectProfileCommand_SingleManifest_CallsSingleOverloadAndClosesAsync()
    {
        // Arrange
        var profileManagerMock = new Mock<IGameProfileManager>();
        var profileContentMock = new Mock<IProfileContentService>();
        var manifestPoolMock = new Mock<IContentManifestPool>();
        var notificationMock = new Mock<INotificationService>();

        var zhProfile = new GameProfile
        {
            Id = "zh-profile-1",
            Name = "Zero Hour Profile",
            GameClient = new GameClient { GameType = GameType.ZeroHour },
        };

        profileManagerMock
            .Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([zhProfile]));

        manifestPoolMock
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        profileContentMock
            .Setup(x => x.AddContentToProfileAsync("zh-profile-1", "1.0.test.manifest", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AddToProfileResult.CreateSuccess("1.0.test.manifest", "Test Content"));

        var vm = new ProfileSelectionViewModel(
            NullLogger<ProfileSelectionViewModel>.Instance,
            profileManagerMock.Object,
            profileContentMock.Object,
            manifestPoolMock.Object,
            notificationMock.Object);

        var closeRequested = false;
        vm.RequestClose += (_, _) => closeRequested = true;

        await vm.LoadProfilesAsync(GameType.ZeroHour, "1.0.test.manifest", "Test Content");

        // Act
        await vm.SelectProfileCommand.ExecuteAsync(vm.CompatibleProfiles[0]);

        // Assert
        Assert.True(vm.WasSuccessful);
        Assert.True(closeRequested);
        Assert.Equal("Zero Hour Profile", vm.SelectedProfileName);
        profileContentMock.Verify(
            x => x.AddContentToProfileAsync("zh-profile-1", "1.0.test.manifest", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that selecting a profile with bundle manifests calls the list overload and closes the dialog.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SelectProfileCommand_BundleManifests_CallsListOverloadAndClosesAsync()
    {
        // Arrange
        var profileManagerMock = new Mock<IGameProfileManager>();
        var profileContentMock = new Mock<IProfileContentService>();
        var manifestPoolMock = new Mock<IContentManifestPool>();
        var notificationMock = new Mock<INotificationService>();

        var zhProfile = new GameProfile
        {
            Id = "zh-profile-1",
            Name = "Zero Hour Profile",
            GameClient = new GameClient { GameType = GameType.ZeroHour },
        };

        profileManagerMock
            .Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([zhProfile]));

        manifestPoolMock
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var bundleIds = new List<string> { "bundle.part.1", "bundle.part.2" };

        profileContentMock
            .Setup(x => x.AddContentToProfileAsync("zh-profile-1", It.Is<IReadOnlyList<string>>(l => l.Count == 3), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AddToProfileResult.CreateSuccess("bundle.primary", "Test Bundle"));

        var vm = new ProfileSelectionViewModel(
            NullLogger<ProfileSelectionViewModel>.Instance,
            profileManagerMock.Object,
            profileContentMock.Object,
            manifestPoolMock.Object,
            notificationMock.Object);

        var closeRequested = false;
        vm.RequestClose += (_, _) => closeRequested = true;

        await vm.LoadProfilesAsync(GameType.ZeroHour, "bundle.primary", "Test Bundle", additionalManifestIds: bundleIds);

        // Act
        await vm.SelectProfileCommand.ExecuteAsync(vm.CompatibleProfiles[0]);

        // Assert
        Assert.True(vm.WasSuccessful);
        Assert.True(closeRequested);
        profileContentMock.Verify(
            x => x.AddContentToProfileAsync("zh-profile-1", It.Is<IReadOnlyList<string>>(l => l.Count == 3), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a failure to add content to profile sets the error message and keeps the dialog open.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SelectProfileCommand_Failure_SetsErrorMessageAndDoesNotCloseAsync()
    {
        // Arrange
        var profileManagerMock = new Mock<IGameProfileManager>();
        var profileContentMock = new Mock<IProfileContentService>();
        var manifestPoolMock = new Mock<IContentManifestPool>();
        var notificationMock = new Mock<INotificationService>();

        var zhProfile = new GameProfile
        {
            Id = "zh-profile-1",
            Name = "Zero Hour Profile",
            GameClient = new GameClient { GameType = GameType.ZeroHour },
        };

        profileManagerMock
            .Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([zhProfile]));

        manifestPoolMock
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        profileContentMock
            .Setup(x => x.AddContentToProfileAsync("zh-profile-1", "1.0.test.manifest", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AddToProfileResult.CreateFailure("Incompatible version conflict"));

        var vm = new ProfileSelectionViewModel(
            NullLogger<ProfileSelectionViewModel>.Instance,
            profileManagerMock.Object,
            profileContentMock.Object,
            manifestPoolMock.Object,
            notificationMock.Object);

        var closeRequested = false;
        vm.RequestClose += (_, _) => closeRequested = true;

        await vm.LoadProfilesAsync(GameType.ZeroHour, "1.0.test.manifest", "Test Content");

        // Act
        await vm.SelectProfileCommand.ExecuteAsync(vm.CompatibleProfiles[0]);

        // Assert
        Assert.False(vm.WasSuccessful);
        Assert.False(closeRequested);
        Assert.Equal("Incompatible version conflict", vm.ErrorMessage);
    }

    /// <summary>
    /// Verifies that CancelCommand sets WasCancelled to true, WasSuccessful to false, and requests dialog close.
    /// </summary>
    [Fact]
    public void CancelCommand_SetsWasCancelledTrue_AndRequestsClose()
    {
        var vm = new ProfileSelectionViewModel(
            NullLogger<ProfileSelectionViewModel>.Instance,
            new Mock<IGameProfileManager>().Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IContentManifestPool>().Object,
            new Mock<INotificationService>().Object);

        var closeRequested = false;
        vm.RequestClose += (_, _) => closeRequested = true;

        vm.CancelCommand.Execute(null);

        Assert.True(vm.WasCancelled);
        Assert.False(vm.WasSuccessful);
        Assert.Null(vm.ErrorMessage);
        Assert.True(closeRequested);
    }

    /// <summary>
    /// Verifies that Dispose can be called multiple times safely without throwing.
    /// </summary>
    [Fact]
    public void Dispose_CanBeCalledMultipleTimesSafely()
    {
        var vm = new ProfileSelectionViewModel(
            NullLogger<ProfileSelectionViewModel>.Instance,
            new Mock<IGameProfileManager>().Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IContentManifestPool>().Object,
            new Mock<INotificationService>().Object);

        vm.Dispose();
        vm.Dispose();
    }

    /// <summary>
    /// Verifies that AddToProfileTooltip and CreateProfileTooltip include the content name.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task TooltipProperties_ReflectContentNameAsync()
    {
        var profileManagerMock = new Mock<IGameProfileManager>();
        profileManagerMock
            .Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

        var manifestPoolMock = new Mock<IContentManifestPool>();
        manifestPoolMock
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var vm = new ProfileSelectionViewModel(
            NullLogger<ProfileSelectionViewModel>.Instance,
            profileManagerMock.Object,
            new Mock<IProfileContentService>().Object,
            manifestPoolMock.Object,
            new Mock<INotificationService>().Object);

        Assert.Equal("Add this content to this profile", vm.AddToProfileTooltip);
        Assert.Equal("Create a new profile with this content", vm.CreateProfileTooltip);

        await vm.LoadProfilesAsync(GameType.ZeroHour, "manifest-1", "ShockWave Mod");

        Assert.Equal("Add ShockWave Mod to this profile", vm.AddToProfileTooltip);
        Assert.Equal("Create a new profile with ShockWave Mod", vm.CreateProfileTooltip);
    }
}
