using GenHub.Core.Interfaces.Common;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.AppUpdate.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.AppUpdate.ViewModels;

/// <summary>
/// Unit tests for <see cref="UpdateNotificationViewModel"/> with Velopack integration.
/// </summary>
public class UpdateNotificationViewModelTests
{
    /// <summary>
    /// Verifies that when no update is available, status is updated correctly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesCommand_WhenNoUpdateAvailable_UpdatesStatus()
    {
        var mockVelopack = new Mock<IVelopackUpdateManager>();
        mockVelopack.Setup(x => x.CheckForUpdatesAsync(It.IsAny<CancellationToken>()))
           .ReturnsAsync((Velopack.UpdateInfo?)null);

        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new GenHub.Core.Models.Common.UserSettings());

        var vm = new UpdateNotificationViewModel(
            mockVelopack.Object,
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object);

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.CheckForUpdatesCommand).ExecuteAsync(null);

        Assert.False(vm.IsUpdateAvailable);
        Assert.Contains("up to date", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that constructor initializes properly.
    /// </summary>
    [Fact]
    public void Constructor_InitializesSuccessfully()
    {
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new GenHub.Core.Models.Common.UserSettings());

        var vm = new UpdateNotificationViewModel(
            Mock.Of<IVelopackUpdateManager>(),
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object);

        Assert.NotNull(vm);
        Assert.False(vm.IsUpdateAvailable);
        Assert.False(vm.IsChecking);
        Assert.False(vm.IsInstalling);
    }

    /// <summary>
    /// Verifies that check button state reflects checking status.
    /// </summary>
    [Fact]
    public void IsCheckButtonEnabled_ReflectsCheckingState()
    {
        var mockUserSettings = new Mock<IUserSettingsService>();
        mockUserSettings.Setup(x => x.Get()).Returns(new GenHub.Core.Models.Common.UserSettings());

        var vm = new UpdateNotificationViewModel(
            Mock.Of<IVelopackUpdateManager>(),
            Mock.Of<ILogger<UpdateNotificationViewModel>>(),
            mockUserSettings.Object);

        Assert.True(vm.IsCheckButtonEnabled);
    }
}