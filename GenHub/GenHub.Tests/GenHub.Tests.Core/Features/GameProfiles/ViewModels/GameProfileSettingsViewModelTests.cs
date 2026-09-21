using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameProfiles;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using CoreContentDisplayItem = GenHub.Core.Models.Content.ContentDisplayItem;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

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
    public async Task InitializeForNewProfileAsync_WithRequiredServices_SetsDefaultsAndLoadsContentAsync()
    {
        // Arrange
        var mockGameSettingsService = new Mock<IGameSettingsService>();
        var mockContentLoader = new Mock<IProfileContentLoader>();
        var mockConfigProvider = new Mock<IConfigurationProviderService>();

        var availableInstallations = new ObservableCollection<CoreContentDisplayItem>
       {
           new()
           {
               Id = "1.108.steam.gameinstallation.generals",
               ManifestId = "1.108.steam.gameinstallation.generals",
               DisplayName = "Command & Conquer: Generals",
               ContentType = GenHub.Core.Models.Enums.ContentType.GameInstallation,
               GameType = GenHub.Core.Models.Enums.GameType.Generals,
               InstallationType = GenHub.Core.Models.Enums.GameInstallationType.Steam,
           },
           new()
           {
               Id = "1.108.steam.gameinstallation.zh",
               ManifestId = "1.108.steam.gameinstallation.zh",
               DisplayName = "Zero Hour",
               ContentType = GenHub.Core.Models.Enums.ContentType.GameInstallation,
               GameType = GenHub.Core.Models.Enums.GameType.ZeroHour,
               InstallationType = GenHub.Core.Models.Enums.GameInstallationType.Steam,
           },
       };

        mockContentLoader
            .Setup(x => x.LoadAvailableGameInstallationsAsync())
            .ReturnsAsync(availableInstallations);

        mockContentLoader
            .Setup(x => x.LoadAvailableContentAsync(
                It.IsAny<GenHub.Core.Models.Enums.ContentType>(),
                It.IsAny<ObservableCollection<CoreContentDisplayItem>>(),
                It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync([]);

        mockConfigProvider
            .Setup(x => x.GetDefaultWorkspaceStrategy())
            .Returns(WorkspaceStrategy.HardLink);

        var nullLogger = NullLogger<GameProfileSettingsViewModel>.Instance;
        var gameSettingsLogger = NullLogger<GameSettingsViewModel>.Instance;

        var vm = new GameProfileSettingsViewModel(
            null!,
            mockGameSettingsService.Object,
            mockConfigProvider.Object,
            mockContentLoader.Object,
            null, // ProfileResourceService
            null, // INotificationService
            null, // IContentManifestPool
            null, // IContentStorageService
            null, // ILocalContentService
            null, // IGenLauncherNormalizationService
            null, // IDialogService
            nullLogger,
            gameSettingsLogger);

        // Act
        await vm.InitializeForNewProfileAsync();

        // Assert
        Assert.Equal("New Profile", vm.Name);
        Assert.Equal(string.Empty, vm.Description);
        Assert.Equal("#1976D2", vm.ColorValue);
        Assert.Equal(WorkspaceStrategy.HardLink, vm.SelectedWorkspaceStrategy);
        Assert.NotEmpty(vm.AvailableGameInstallations);
        Assert.Equal(2, vm.AvailableGameInstallations.Count);
    }

    /// <summary>
    /// Verifies that initializing for an existing profile without a GameProfileManager sets error state.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InitializeForProfileAsync_WithoutProfileManager_SetsLoadingErrorAsync()
    {
        // Arrange
        var mockGameSettingsService = new Mock<IGameSettingsService>();
        var nullLogger = NullLogger<GameProfileSettingsViewModel>.Instance;
        var gameSettingsLogger = NullLogger<GameSettingsViewModel>.Instance;

        var vm = new GameProfileSettingsViewModel(
            null!,
            mockGameSettingsService.Object,
            null,
            null,
            null, // ProfileResourceService
            null, // INotificationService
            null, // IContentManifestPool
            null, // IContentStorageService
            null, // ILocalContentService
            null, // IGenLauncherNormalizationService
            null, // IDialogService
            nullLogger,
            gameSettingsLogger);

        // Act
        await vm.InitializeForProfileAsync("test-profile-id");

        // Assert
        Assert.True(vm.LoadingError);
        Assert.Equal("Error loading profile", vm.StatusMessage);
    }

    /// <summary>
    /// Verifies that receiving a <see cref="ManifestReplacedMessage"/> updates enabled content without duplication.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReceiveManifestReplacedMessage_UpdatesEnabledContent_WithoutDuplicationAsync()
    {
        // Arrange
        var mockGameSettingsService = new Mock<IGameSettingsService>();
        var mockContentLoader = new Mock<IProfileContentLoader>();
        var mockManifestPool = new Mock<IContentManifestPool>();

        var oldId = "1.0.test.mod.modv1";
        var newId = "1.0.test.mod.modv2";

        var oldItem = new GenHub.Features.GameProfiles.ViewModels.ContentDisplayItem
        {
            ManifestId = GenHub.Core.Models.Manifest.ManifestId.Create(oldId),
            DisplayName = "My Mod v1",
            IsEnabled = true,
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            GameType = GenHub.Core.Models.Enums.GameType.Generals,
            InstallationType = GenHub.Core.Models.Enums.GameInstallationType.Steam,
        };

        var newManifest = new ContentManifest
        {
            Id = GenHub.Core.Models.Manifest.ManifestId.Create(newId),
            Name = "My Mod v2",
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            Version = "2.0",
        };

        mockManifestPool
            .Setup(x => x.GetManifestAsync(It.Is<GenHub.Core.Models.Manifest.ManifestId>(id => id.Value == newId), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(newManifest));

        mockContentLoader
            .Setup(x => x.CreateManifestDisplayItem(
                It.Is<ContentManifest>(m => m.Id.Value == newId),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>()))
            .Returns(new CoreContentDisplayItem
            {
                Id = newId,
                ManifestId = newId,
                DisplayName = "My Mod v2",
                Version = "2.0",
                ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
                GameType = GenHub.Core.Models.Enums.GameType.Generals,
                InstallationType = GenHub.Core.Models.Enums.GameInstallationType.Steam,
            });

        var logger = NullLogger<GameProfileSettingsViewModel>.Instance;
        var vm = new GameProfileSettingsViewModel(
            null, // gameProfileManager
            mockGameSettingsService.Object,
            null, // configurationProvider
            mockContentLoader.Object,
            null, // ProfileResourceService
            null, // INotificationService
            mockManifestPool.Object,
            null, // IContentStorageService
            null, // ILocalContentService
            null, // IGenLauncherNormalizationService
            null, // IDialogService
            logger,
            NullLogger<GameSettingsViewModel>.Instance);

        // Directly populate the EnabledContent collection to simulate state
        vm.EnabledContent.Add(oldItem);

        // Act - call handler directly to avoid Dispatcher issues in test
        // WeakReferenceMessenger.Default.Send(new ManifestReplacedMessage(oldId, newId));
        await vm.HandleManifestReplacementAsync(oldId, newId);

        // Assert
        // 1. Old item should be gone from EnabledContent
        Assert.DoesNotContain(vm.EnabledContent, c => c.ManifestId.Value == oldId);

        // 2. New item should be present in EnabledContent
        Assert.Contains(vm.EnabledContent, c => c.ManifestId.Value == newId);

        // 3. New item should be enabled
        var item = vm.EnabledContent.FirstOrDefault(c => c.ManifestId.Value == newId);
        Assert.NotNull(item);
        Assert.True(item.IsEnabled);
    }

    /// <summary>
    /// Verifies that saving a new profile with a fallback GameClient item preserves date-based versions without decimal conversion.
    /// </summary>
    /// <param name="manifestId">The manifest ID of the game client item.</param>
    /// <param name="expectedVersion">The expected version string on the created GameClient.</param>
    /// <param name="expectedPublisher">The expected publisher string on the created GameClient.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("1.20260821.thesuperhackers.gameclient.zerohour", "20260821", "thesuperhackers")]
    [InlineData("1.104.steam.gameclient.zerohour", "1.04", "steam")]
    [InlineData("1.104.generalsonline.gameclient.zerohour", "000104", "generalsonline")]
    public async Task SaveCommand_ForNewProfile_WithFallbackGameClientItem_ResolvesExpectedVersionAsync(
        string manifestId,
        string expectedVersion,
        string expectedPublisher)
    {
        // Arrange
        var mockProfileManager = new Mock<IGameProfileManager>();
        var mockGameSettingsService = new Mock<IGameSettingsService>();
        var mockContentLoader = new Mock<IProfileContentLoader>();
        var mockConfigProvider = new Mock<IConfigurationProviderService>();

        mockProfileManager
            .Setup(m => m.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "new-profile" }));

        var vm = new GameProfileSettingsViewModel(
            mockProfileManager.Object,
            mockGameSettingsService.Object,
            mockConfigProvider.Object,
            mockContentLoader.Object,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            NullLogger<GameProfileSettingsViewModel>.Instance,
            NullLogger<GameSettingsViewModel>.Instance);

        vm.Name = "Test Profile";
        vm.SelectedGameInstallation = new GenHub.Features.GameProfiles.ViewModels.ContentDisplayItem
        {
            Id = "1.108.steam.gameinstallation.zh",
            ManifestId = GenHub.Core.Models.Manifest.ManifestId.Create("1.108.steam.gameinstallation.zh"),
            DisplayName = "Zero Hour",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            SourceId = "steam-zh",
            GameClient = new GameClient
            {
                Id = "client-zh",
                Name = "Zero Hour Client",
                GameType = GameType.ZeroHour,
                ExecutablePath = "generals.exe",
                WorkingDirectory = "C:\\Games\\ZH",
            },
        };

        var clientItem = new GenHub.Features.GameProfiles.ViewModels.ContentDisplayItem
        {
            ManifestId = GenHub.Core.Models.Manifest.ManifestId.Create(manifestId),
            DisplayName = "Test Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = true,
        };
        vm.EnabledContent.Add(clientItem);

        // Act
        await vm.SaveCommand.ExecuteAsync(null);

        // Assert
        mockProfileManager.Verify(
            m => m.CreateProfileAsync(
                It.Is<CreateProfileRequest>(r =>
                    r.GameClient != null &&
                    r.GameClient.Version == expectedVersion &&
                    r.GameClient.PublisherType == expectedPublisher &&
                    r.GameClient.ExecutablePath == "generals.exe" &&
                    r.GameClient.WorkingDirectory == "C:\\Games\\ZH"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that changing the game type filter while initializing does not trigger content reload.
    /// </summary>
    [Fact]
    public void OnGameTypeFilterChanged_WhenIsInitializing_DoesNotTriggerContentReload()
    {
        // Arrange
        var mockGameSettingsService = new Mock<IGameSettingsService>();
        var mockContentLoader = new Mock<IProfileContentLoader>();
        var mockManifestPool = new Mock<IContentManifestPool>();

        var vm = new GameProfileSettingsViewModel(
            null,
            mockGameSettingsService.Object,
            null,
            mockContentLoader.Object,
            null,
            null,
            mockManifestPool.Object,
            null,
            null,
            null,
            null,
            NullLogger<GameProfileSettingsViewModel>.Instance,
            NullLogger<GameSettingsViewModel>.Instance);

        vm.IsInitializing = true;

        // Act
        vm.GameTypeFilter = GameType.ZeroHour;

        // Assert - verify content loader was never called while IsInitializing is true
        mockContentLoader.Verify(
            x => x.LoadAvailableContentAsync(
                It.IsAny<GenHub.Core.Models.Enums.ContentType>(),
                It.IsAny<ObservableCollection<CoreContentDisplayItem>>(),
                It.IsAny<List<string>>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that changing the game type filter when not initializing triggers reload and populates matching content.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task OnGameTypeFilterChanged_WhenNotInitializing_LoadsFilteredContentAsync()
    {
        // Arrange
        var mockGameSettingsService = new Mock<IGameSettingsService>();
        var mockContentLoader = new Mock<IProfileContentLoader>();
        var mockManifestPool = new Mock<IContentManifestPool>();

        var zhItem = new CoreContentDisplayItem
        {
            Id = "1.0.moddb.mod.zhmod",
            ManifestId = "1.0.moddb.mod.zhmod",
            DisplayName = "ZH Mod",
            GameType = GameType.ZeroHour,
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
        };

        var genItem = new CoreContentDisplayItem
        {
            Id = "1.0.moddb.mod.genmod",
            ManifestId = "1.0.moddb.mod.genmod",
            DisplayName = "Generals Mod",
            GameType = GameType.Generals,
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
        };

        mockContentLoader
            .Setup(x => x.LoadAvailableContentAsync(
                It.IsAny<GenHub.Core.Models.Enums.ContentType>(),
                It.IsAny<ObservableCollection<CoreContentDisplayItem>>(),
                It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([zhItem, genItem]);

        var vm = new GameProfileSettingsViewModel(
            null,
            mockGameSettingsService.Object,
            null,
            mockContentLoader.Object,
            null,
            null,
            mockManifestPool.Object,
            null,
            null,
            null,
            null,
            NullLogger<GameProfileSettingsViewModel>.Instance,
            NullLogger<GameSettingsViewModel>.Instance);

        vm.IsInitializing = false;

        // Act
        vm.GameTypeFilter = GameType.ZeroHour;
        await vm.RefreshFiltersAndContentAsync();

        // Assert
        Assert.Single(vm.AvailableContent);
        Assert.Equal("ZH Mod", vm.AvailableContent[0].DisplayName);
    }
}
