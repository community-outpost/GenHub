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
    public async Task CopyProfileCommand_CallsCopyAction()
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

        var vm = new GameProfileItemViewModel("test-profile-id", mockProfile.Object, "icon.png", "cover.jpg");
        vm.CopyProfileAction = _ => Task.CompletedTask;

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
    public async Task CopyProfileCommand_Execute_WhenActionIsNull_DoesNotThrow()
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
}