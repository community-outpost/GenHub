using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Contains tests for <see cref="GameProfileSettingsViewModel"/>.
/// </summary>
public class GameProfileSettingsViewModelTests
{
    /// <summary>
    /// Verifies that the ViewModel can initialize for a new profile with required services.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InitializeForNewProfileAsync_WithRequiredServices_SetsDefaultsAndLoadsContent()
    {
        // Arrange
        var mockGameSettingsService = new Mock<IGameSettingsService>();
        var mockContentLoader = new Mock<IProfileContentLoader>();
        var mockConfigProvider = new Mock<IConfigurationProviderService>();

        var availableInstallations = new ObservableCollection<GenHub.Core.Models.Content.ContentDisplayItem>
       {
           new GenHub.Core.Models.Content.ContentDisplayItem
           {
               Id = "1.108.steam.gameinstallation.generals",
               ManifestId = "1.108.steam.gameinstallation.generals",
               DisplayName = "Command & Conquer: Generals",
               ContentType = GenHub.Core.Models.Enums.ContentType.GameInstallation,
           },
           new GenHub.Core.Models.Content.ContentDisplayItem
           {
               Id = "1.108.steam.gameinstallation.zh",
               ManifestId = "1.108.steam.gameinstallation.zh",
               DisplayName = "Zero Hour",
               ContentType = GenHub.Core.Models.Enums.ContentType.GameInstallation,
           },
       };

        mockContentLoader
            .Setup(x => x.LoadAvailableGameInstallationsAsync())
            .ReturnsAsync(availableInstallations);

        mockContentLoader
            .Setup(x => x.LoadAvailableContentAsync(
                It.IsAny<GenHub.Core.Models.Enums.ContentType>(),
                It.IsAny<ObservableCollection<GenHub.Core.Models.Content.ContentDisplayItem>>(),
                It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync(new ObservableCollection<GenHub.Core.Models.Content.ContentDisplayItem>());

        mockConfigProvider
            .Setup(x => x.GetDefaultWorkspaceStrategy())
            .Returns(WorkspaceStrategy.SymlinkOnly);

        var nullLogger1 = NullLogger<GameProfileSettingsViewModel>.Instance;
        var nullLogger2 = NullLogger<GameSettingsViewModel>.Instance;

        var vm = new GameProfileSettingsViewModel(
            null,
            mockGameSettingsService.Object,
            mockConfigProvider.Object,
            mockContentLoader.Object,
            nullLogger1,
            nullLogger2);

        // Act
        await vm.InitializeForNewProfileAsync();

        // Assert
        Assert.Equal("New Profile", vm.Name);
        Assert.Equal("A new game profile", vm.Description);
        Assert.Equal("#1976D2", vm.ColorValue);
        Assert.Equal(WorkspaceStrategy.SymlinkOnly, vm.SelectedWorkspaceStrategy);
        Assert.NotEmpty(vm.AvailableGameInstallations);
        Assert.Equal(2, vm.AvailableGameInstallations.Count);
        Assert.Equal("Command & Conquer: Generals", vm.SelectedGameInstallation?.DisplayName);
        Assert.False(vm.LoadingError);
        Assert.Contains("Found 2 installations", vm.StatusMessage);
    }

    /// <summary>
    /// Verifies that initializing for an existing profile without a GameProfileManager sets error state.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InitializeForProfileAsync_WithoutProfileManager_SetsLoadingError()
    {
        // Arrange
        var mockGameSettingsService = new Mock<IGameSettingsService>();
        var nullLogger1 = NullLogger<GameProfileSettingsViewModel>.Instance;
        var nullLogger2 = NullLogger<GameSettingsViewModel>.Instance;

        var vm = new GameProfileSettingsViewModel(
            null,
            mockGameSettingsService.Object,
            null,
            null,
            nullLogger1,
            nullLogger2);

        // Act
        await vm.InitializeForProfileAsync("test-profile-id");

        // Assert
        Assert.True(vm.LoadingError);
        Assert.Equal("Error loading profile", vm.StatusMessage);
    }
}
