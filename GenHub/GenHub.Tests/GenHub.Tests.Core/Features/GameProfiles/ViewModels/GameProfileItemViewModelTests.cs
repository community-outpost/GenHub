using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Features.GameProfiles.ViewModels;
using Moq;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Tests for <see cref="GameProfileItemViewModel"/>.
/// </summary>
public class GameProfileItemViewModelTests
{
    /// <summary>
    /// Verifies construction of <see cref="GameProfileItemViewModel"/>.
    /// </summary>
    [Fact]
    public void CanConstruct()
    {
        var mockProfile = new Mock<IGameProfile>();
        mockProfile.SetupGet(p => p.Version).Returns("1.0");
        mockProfile.SetupGet(p => p.ExecutablePath).Returns("C:/fake/path.exe");
        var vm = new GameProfileItemViewModel("test-profile-id", mockProfile.Object, "icon.png", "cover.jpg");
        Assert.NotNull(vm);
        Assert.Equal("test-profile-id", vm.ProfileId);
    }

    /// <summary>
    /// Verifies that version display is suppressed for local content even if GameClient has a version.
    /// </summary>
    [Fact]
    public void Construction_WithLocalContent_SuppressVersionDisplay()
    {
        // Arrange
        var gameClient = new GenHub.Core.Models.GameClients.GameClient
        {
            Id = "schema.1.local.map.some-map", // local publisher in ID
            Version = "1.0", // Has a version that should be suppressed
            Name = "Local Map",
        };

        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-profile-local",
            Name = "Test Local Profile",
            GameClient = gameClient,
        };

        // Act
        var vm = new GameProfileItemViewModel("test-profile-local", profile, null!, null!);

        // Assert
        Assert.Equal("Local", vm.Publisher); // Extracted from "local" segment
        Assert.Empty(vm.GameVersion ?? string.Empty); // Suppressed
    }

    /// <summary>
    /// Verifies that the copy profile command calls the copy action when executed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CopyProfileCommand_CallsCopyActionAsync()
    {
        // Arrange
        var mockProfile = new Mock<IGameProfile>();
        mockProfile.SetupGet(p => p.Version).Returns("1.0");
        mockProfile.SetupGet(p => p.ExecutablePath).Returns("C:/fake/path.exe");

        var vm = new GameProfileItemViewModel("test-profile-id", mockProfile.Object, "icon.png", "cover.jpg");

        GameProfileItemViewModel? passedVm = null;
        vm.CopyProfileAction = viewModel =>
        {
            passedVm = viewModel;
            return Task.CompletedTask;
        };

        // Act
        await vm.CopyProfileCommand.ExecuteAsync(null);

        // Assert
        Assert.NotNull(passedVm);
        Assert.Same(vm, passedVm);
    }

    /// <summary>
    /// Verifies that the copy profile command can be executed when copy action is set.
    /// </summary>
    [Fact]
    public void CopyProfileCommand_CanExecute_WhenActionIsSet()
    {
        // Arrange
        var mockProfile = new Mock<IGameProfile>();
        mockProfile.SetupGet(p => p.Version).Returns("1.0");
        mockProfile.SetupGet(p => p.ExecutablePath).Returns("C:/fake/path.exe");

        var vm = new GameProfileItemViewModel("test-profile-id", mockProfile.Object, "icon.png", "cover.jpg")
        {
            CopyProfileAction = _ => Task.CompletedTask,
        };

        // Act & Assert
        Assert.True(vm.CopyProfileCommand.CanExecute(null));
    }

    /// <summary>
    /// Verifies that the copy profile command can be executed even when copy action is null.
    /// </summary>
    [Fact]
    public void CopyProfileCommand_CanExecute_WhenActionIsNull()
    {
        // Arrange
        var mockProfile = new Mock<IGameProfile>();
        mockProfile.SetupGet(p => p.Version).Returns("1.0");
        mockProfile.SetupGet(p => p.ExecutablePath).Returns("C:/fake/path.exe");

        var vm = new GameProfileItemViewModel("test-profile-id", mockProfile.Object, "icon.png", "cover.jpg");

        // Don't set CopyProfileAction

        // Act & Assert
        Assert.True(vm.CopyProfileCommand.CanExecute(null));
    }

    /// <summary>
    /// Verifies that the copy profile command execution is safe when copy action is null.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CopyProfileCommand_Execute_WhenActionIsNull_DoesNotThrowAsync()
    {
        // Arrange
        var mockProfile = new Mock<IGameProfile>();
        mockProfile.SetupGet(p => p.Version).Returns("1.0");
        mockProfile.SetupGet(p => p.ExecutablePath).Returns("C:/fake/path.exe");

        var vm = new GameProfileItemViewModel("test-profile-id", mockProfile.Object, "icon.png", "cover.jpg");

        // Don't set CopyProfileAction (null)

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() => vm.CopyProfileCommand.ExecuteAsync(null));
        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies that constructing with a publisher game client properly sets version and publisher badges.
    /// </summary>
    [Fact]
    public void Construction_WithPublisherGameClient_SetsVersionAndPublisherBadges()
    {
        // Arrange
        var gameClient = new GenHub.Core.Models.GameClients.GameClient
        {
            Id = "1.104.generalsonline.gameclient.zerohour",
            Name = "Generals Online",
            Version = "000104",
            PublisherType = "GeneralsOnline",
        };

        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-profile-go",
            Name = "Test GO Profile",
            GameClient = gameClient,
        };

        // Act
        var vm = new GameProfileItemViewModel("test-profile-go", profile, null!, null!);

        // Assert
        Assert.Equal("Generals Online", vm.Publisher);
        Assert.Equal("000104", vm.GameVersion);
        Assert.Contains("Generals Online", vm.Description);
    }

    /// <summary>
    /// Verifies that release-date versions like 20260821 are not divided into v202608.21.
    /// </summary>
    [Fact]
    public void Construction_WithDateBasedGameClient_PreservesFullDateVersion()
    {
        // Arrange
        var gameClient = new GenHub.Core.Models.GameClients.GameClient
        {
            Id = "1.20260821.thesuperhackers.gameclient.zerohour",
            Name = "The Super Hackers",
            Version = "20260821",
            PublisherType = "thesuperhackers",
        };

        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-profile-tsh",
            Name = "Test TSH Profile",
            GameClient = gameClient,
        };

        // Act
        var vm = new GameProfileItemViewModel("test-profile-tsh", profile, null!, null!);

        // Assert
        Assert.Equal("The Super Hackers", vm.Publisher);
        Assert.Equal("20260821", vm.GameVersion);
    }

    /// <summary>
    /// Verifies that enabling a patch manifest overrides the version and publisher badges.
    /// </summary>
    [Fact]
    public void Construction_WithEnabledCommunityPatch_SetsVersionAndPublisherBadges()
    {
        // Arrange
        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-profile-patch",
            Name = "Test Patch Profile",
            EnabledContentIds = ["1.106.communityoutpost.patch.zerohour"],
        };

        // Act
        var vm = new GameProfileItemViewModel("test-profile-patch", profile, null!, null!);

        // Assert
        Assert.Equal("Community Outpost", vm.Publisher);
        Assert.Equal("v1.06", vm.GameVersion);
        Assert.Contains("v1.06", vm.Description);
    }

    /// <summary>
    /// Verifies that calling UpdateFromProfile updates version and publisher badges when the client changes.
    /// </summary>
    [Fact]
    public void UpdateFromProfile_WithChangedGameClient_UpdatesBadges()
    {
        // Arrange
        var initialProfile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-profile-updating",
            Name = "Initial Profile",
        };

        var vm = new GameProfileItemViewModel("test-profile-updating", initialProfile, null!, null!);
        Assert.Empty(vm.GameVersion ?? string.Empty);

        var updatedClient = new GenHub.Core.Models.GameClients.GameClient
        {
            Id = "1.104.generalsonline.gameclient.zerohour",
            Name = "Generals Online",
            Version = "000104",
            PublisherType = "GeneralsOnline",
        };

        var updatedProfile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-profile-updating",
            Name = "Updated Profile",
            GameClient = updatedClient,
        };

        // Act
        vm.UpdateFromProfile(updatedProfile);

        // Assert
        Assert.Equal("Generals Online", vm.Publisher);
        Assert.Equal("000104", vm.GameVersion);
        Assert.Contains("Generals Online", vm.Description);
    }
}
