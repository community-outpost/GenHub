using System.Collections.ObjectModel;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using CoreContentDisplayItem = GenHub.Core.Models.Content.ContentDisplayItem;
using ViewModelContentDisplayItem = GenHub.Features.GameProfiles.ViewModels.ContentDisplayItem;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Tests for dependency validation logic in GameProfileSettingsViewModel.
/// </summary>
public class GameProfileSettingsViewModelDependencyTests
{
    private readonly Mock<IGameProfileManager> _mockGameProfileManager;
    private readonly Mock<IGameSettingsService> _mockGameSettingsService;
    private readonly Mock<IConfigurationProviderService> _mockConfigProvider;
    private readonly Mock<IProfileContentLoader> _mockContentLoader;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<IContentManifestPool> _mockManifestPool;
    private readonly GameProfileSettingsViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileSettingsViewModelDependencyTests"/> class.
    /// </summary>
    public GameProfileSettingsViewModelDependencyTests()
    {
        _mockGameProfileManager = new Mock<IGameProfileManager>();
        _mockGameSettingsService = new Mock<IGameSettingsService>();
        _mockConfigProvider = new Mock<IConfigurationProviderService>();
        _mockContentLoader = new Mock<IProfileContentLoader>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockManifestPool = new Mock<IContentManifestPool>();

        _viewModel = new GameProfileSettingsViewModel(
            _mockGameProfileManager.Object,
            _mockGameSettingsService.Object,
            _mockConfigProvider.Object,
            _mockContentLoader.Object,
            null, // ProfileResourceService
            _mockNotificationService.Object,
            _mockManifestPool.Object,
            null, // IContentStorageService
            null, // ILocalContentService
            NullLogger<GameProfileSettingsViewModel>.Instance,
            NullLogger<GameSettingsViewModel>.Instance);

        // Setup default behavior for content loader
        _mockContentLoader.Setup(x => x.LoadAvailableGameInstallationsAsync())
            .ReturnsAsync([]);
        _mockContentLoader.Setup(x => x.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);

        // Setup default manifest pool behavior - return failure for any unmatched manifest
        _mockManifestPool.Setup(x => x.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId id, CancellationToken ct) => OperationResult<ContentManifest?>.CreateFailure($"Manifest {id} not found"));
    }

    /// <summary>
    /// Verifies that saving a profile fails when a mod requires a specific GameInstallation ID but a different one is enabled.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Save_Fails_WhenGameInstallationIdMismatch()
    {
        // Arrange
        var modManifestId = new ManifestId("1.0.0.mod.example");
        var installationIdA = new ManifestId("1.0.0.gameinstallation.install-a");
        var installationIdB = new ManifestId("1.0.0.gameinstallation.install-b");

        // Setup mod manifest that requires installation A
        var modManifest = new ContentManifest
        {
            Id = modManifestId,
            Name = "Example Mod",
            ContentType = ContentType.Mod,
            Dependencies =
            [
                new()
                {
                    Id = installationIdA,
                    DependencyType = ContentType.GameInstallation,
                },
            ],
        };

        // Setup manifest pool to return specific manifests based on ID
        _mockManifestPool.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == "1.0.0.mod.example"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(modManifest));
        _mockManifestPool.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == "1.0.0.gameinstallation.install-a"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(new ContentManifest { Id = installationIdA, Name = "Installation A", ContentType = ContentType.GameInstallation }));
        _mockManifestPool.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == "1.0.0.gameinstallation.install-b"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(new ContentManifest { Id = installationIdB, Name = "Installation B", ContentType = ContentType.GameInstallation }));

        _viewModel.Name = "Test Profile";

        var installationItemB = new ViewModelContentDisplayItem
        {
            ManifestId = installationIdB,
            DisplayName = "Installation B",
            ContentType = ContentType.GameInstallation,
            SourceId = "source_b",
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.Steam,
        };
        _viewModel.AvailableGameInstallations = [installationItemB];
        _viewModel.SelectedGameInstallation = installationItemB;

        var modDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = modManifestId,
            DisplayName = "Example Mod",
            ContentType = ContentType.Mod,
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.Unknown,
            IsEnabled = true,
        };
        _viewModel.EnabledContent.Add(modDisplayItem);

        // Add Installation B to EnabledContent (wrong installation, but needed to pass initial validation)
        var installDisplayItemB = new ViewModelContentDisplayItem
        {
            ManifestId = installationIdB,
            DisplayName = "Installation B",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = true,
        };
        _viewModel.EnabledContent.Add(installDisplayItemB);

        // Act
        await _viewModel.SaveCommand.ExecuteAsync(null);

        // Assert
        Assert.Matches("Error: Missing required dependencies", _viewModel.StatusMessage);
        _mockGameProfileManager.Verify(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that saving a profile succeeds when a mod requires a specific GameInstallation ID and that ID is enabled.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Save_Succeeds_WhenGameInstallationIdMatches()
    {
        // Arrange
        var modManifestId = new ManifestId("1.0.0.mod.example");
        var installationIdA = new ManifestId("1.0.0.gameinstallation.install-a");

        var modManifest = new ContentManifest
        {
            Id = modManifestId,
            Name = "Example Mod",
            ContentType = ContentType.Mod,
            Dependencies =
            [
                new()
                {
                    Id = installationIdA,
                    DependencyType = ContentType.GameInstallation,
                },
            ],
        };

        // Setup manifest pool to return specific manifests
        _mockManifestPool.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == "1.0.0.mod.example"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(modManifest));
        _mockManifestPool.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == "1.0.0.gameinstallation.install-a"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(new ContentManifest { Id = installationIdA, Name = "Installation A", ContentType = ContentType.GameInstallation }));

        _viewModel.Name = "Test Profile";

        var installationItemA = new ViewModelContentDisplayItem
        {
            ManifestId = installationIdA,
            DisplayName = "Installation A",
            ContentType = ContentType.GameInstallation,
            SourceId = "source_a",
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.Steam,
        };
        _viewModel.AvailableGameInstallations = [installationItemA];
        _viewModel.SelectedGameInstallation = installationItemA;

        var modDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = modManifestId,
            DisplayName = "Example Mod",
            ContentType = ContentType.Mod,
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.Unknown,
            IsEnabled = true,
        };
        _viewModel.EnabledContent.Add(modDisplayItem);

        var installDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = installationIdA,
            DisplayName = "Installation A",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = true,
        };
        _viewModel.EnabledContent.Add(installDisplayItem);

        _mockGameProfileManager.Setup(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile()));

        // Act
        await _viewModel.SaveCommand.ExecuteAsync(null);

        // Assert
        _mockGameProfileManager.Verify(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that saving a profile succeeds when a mod has an optional dependency that is missing.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Save_Succeeds_WhenOptionalDependencyIsMissing()
    {
        // Arrange
        var modManifestId = new ManifestId("1.0.0.mod.example");
        var optionalDepId = new ManifestId("1.0.0.addon.optional");
        var installationId = new ManifestId("1.0.0.gameinstallation.example");

        var modManifest = new ContentManifest
        {
            Id = modManifestId,
            Name = "Example Mod",
            ContentType = ContentType.Mod,
            Dependencies =
            [
                new()
                {
                    Id = optionalDepId,
                    Name = "Optional Addon",
                    DependencyType = ContentType.Addon,
                    IsOptional = true,
                },
                new()
                {
                    Id = installationId,
                    DependencyType = ContentType.GameInstallation,
                    IsOptional = false,
                },
            ],
        };

        // Setup manifest pool
        _mockManifestPool.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == modManifestId.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(modManifest));

        // Mock the installation manifest too
        _mockManifestPool.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == installationId.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(new ContentManifest { Id = installationId, Name = "Installation", ContentType = ContentType.GameInstallation }));

        _viewModel.Name = "Test Profile";

        var installationItem = new ViewModelContentDisplayItem
        {
            ManifestId = installationId,
            DisplayName = "Installation",
            ContentType = ContentType.GameInstallation,
            SourceId = "source_a",
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.Steam,
        };
        _viewModel.AvailableGameInstallations = [installationItem];
        _viewModel.SelectedGameInstallation = installationItem;

        var modDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = modManifestId,
            DisplayName = "Example Mod",
            ContentType = ContentType.Mod,
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.Unknown,
            IsEnabled = true,
        };
        _viewModel.EnabledContent.Add(modDisplayItem);

        var installDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = installationId,
            DisplayName = "Installation",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = true,
        };
        _viewModel.EnabledContent.Add(installDisplayItem);

        _mockGameProfileManager.Setup(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile()));

        // Act
        await _viewModel.SaveCommand.ExecuteAsync(null);

        // Assert
        _mockGameProfileManager.Verify(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotMatch("Error: Missing required dependencies", _viewModel.StatusMessage);
    }

    /// <summary>
    /// Verifies that enabling content requiring a different Game Installation automatically switches the selected installation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task EnableContent_AutoSwitches_GameInstallation_When_Dependency_Requires_Different_Type()
    {
        // Arrange
        var modManifestId = new ManifestId("1.0.0.mod.generalsonline");
        var zeroHourInstallId = new ManifestId("1.0.0.gameinstallation.zerohour");
        var generalsInstallId = new ManifestId("1.0.0.gameinstallation.generals");

        var modManifest = new ContentManifest
        {
            Id = modManifestId,
            Name = "Generals Online",
            ContentType = ContentType.GameClient, // Treating as GameClient for this test as per requirement
            Dependencies =
            [
                new()
                {
                    Id = zeroHourInstallId, // Specifically requires Zero Hour
                    DependencyType = ContentType.GameInstallation,
                    CompatibleGameTypes = [GameType.ZeroHour],
                },
            ],
        };

        // Setup manifest pool
        _mockManifestPool.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == modManifestId.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(modManifest));

        // Available installations
        var generalsInstall = new ViewModelContentDisplayItem
        {
            ManifestId = generalsInstallId,
            DisplayName = "Generals",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.Steam, // Added required property
            IsEnabled = true, // Initially selected/enabled
        };

        var zeroHourInstall = new ViewModelContentDisplayItem
        {
            ManifestId = zeroHourInstallId,
            DisplayName = "Zero Hour",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam, // Added required property
            IsEnabled = false,
        };

        _viewModel.AvailableGameInstallations = [generalsInstall, zeroHourInstall];
        _viewModel.SelectedGameInstallation = generalsInstall;
        _viewModel.EnabledContent.Add(generalsInstall); // Simulate initial state

        var modDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = modManifestId,
            DisplayName = "Generals Online",
            ContentType = ContentType.GameClient,
            GameType = GameType.ZeroHour, // Added required property (assuming match)
            InstallationType = GameInstallationType.Unknown, // Added required property
            IsEnabled = false,
        };
        _viewModel.AvailableContent.Add(modDisplayItem);

        // Act
        // We use the command directly or the method if public. EnableContent is private but called via RelayCommand.
        _viewModel.EnableContentCommand.Execute(modDisplayItem);

        // Wait for async background operation
        await Task.Delay(50);

        // Assert
        Assert.Equal(zeroHourInstall, _viewModel.SelectedGameInstallation);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == zeroHourInstallId.Value);
        Assert.DoesNotContain(_viewModel.EnabledContent, c => c.ManifestId.Value == generalsInstallId.Value);
        Assert.True(zeroHourInstall.IsEnabled);
    }

    /// <summary>
    /// Verifies that enabling a standard GameClient (no persistent manifest) automatically switches the installation
    /// by creating a synthetic manifest dependency on its SourceId.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task EnableContent_AutoSwitches_Installation_For_Standard_GameClient_Missing_Manifest()
    {
        // Arrange
        var standardClientId = new ManifestId("1.04.eaapp.gameclient.zerohour");
        var zeroHourInstallId = new ManifestId("1.04.eaapp.gameinstallation.zerohour");
        var generalsInstallId = new ManifestId("1.08.eaapp.gameinstallation.generals");

        // NOTE: We do NOT setup the manifest pool for standardClientId.
        // It should default to Failure (as set in constructor) or we enforce it here:
        _mockManifestPool.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == standardClientId.Value), It.IsAny<CancellationToken>()))
             .ReturnsAsync(OperationResult<ContentManifest?>.CreateFailure("Not found"));

        // Available installations
        var generalsInstall = new ViewModelContentDisplayItem
        {
            ManifestId = generalsInstallId,
            DisplayName = "Generals 1.08",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.EaApp,
            IsEnabled = true,
        };

        var zeroHourInstall = new ViewModelContentDisplayItem
        {
            ManifestId = zeroHourInstallId,
            DisplayName = "Zero Hour 1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
            IsEnabled = false,
        };

        _viewModel.AvailableGameInstallations = [generalsInstall, zeroHourInstall];
        _viewModel.SelectedGameInstallation = generalsInstall;
        _viewModel.EnabledContent.Add(generalsInstall);

        // Standard Game Client Item (e.g. detected from runtime)
        var clientDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = standardClientId,
            DisplayName = "Zero Hour 1.04 Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,

            // CRITICAL: SourceId must point to the installation
            SourceId = zeroHourInstallId.Value,
            IsEnabled = false,
        };
        _viewModel.AvailableContent.Add(clientDisplayItem);

        // Act
        _viewModel.EnableContentCommand.Execute(clientDisplayItem);

        // Wait for async background operation
        await Task.Delay(50);

        // Assert
        Assert.Equal(zeroHourInstall, _viewModel.SelectedGameInstallation);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == zeroHourInstallId.Value);
        Assert.DoesNotContain(_viewModel.EnabledContent, c => c.ManifestId.Value == generalsInstallId.Value);
        Assert.True(zeroHourInstall.IsEnabled);
    }

    /// <summary>
    /// Verifies that enabling content with a strictly required dependent content (e.g. MapPack) automatically enables it if found.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task EnableContent_AutoEnables_DependentContent()
    {
        // Arrange
        var clientManifestId = new ManifestId("1.0.0.gameclient.generalsonline");
        var mapPackId = new ManifestId("1.0.0.mappack.quickmatch");

        var clientManifest = new ContentManifest
        {
            Id = clientManifestId,
            Name = "Generals Online Client",
            ContentType = ContentType.GameClient,
            Dependencies =
            [
                new()
                {
                    Id = mapPackId,
                    DependencyType = ContentType.MapPack,
                    IsOptional = false,
                    Name = "QuickMatch MapPack",
                },
            ],
        };

        // Setup manifest pool
        _mockManifestPool.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == clientManifestId.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(clientManifest));

        // Setup mocked content loader response for the specific dependency lookup
        var mapPackCoreItem = new CoreContentDisplayItem
        {
            Id = mapPackId.Value,
            ManifestId = mapPackId.Value,
            DisplayName = "QuickMatch MapPack",
            ContentType = ContentType.MapPack,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Unknown,
        };

        _mockContentLoader.Setup(x => x.LoadAvailableContentAsync(
                ContentType.MapPack,
                It.IsAny<ObservableCollection<CoreContentDisplayItem>>(),
                It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([mapPackCoreItem]);

        var clientDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = clientManifestId,
            DisplayName = "Generals Online Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.ZeroHour, // Added required property
            InstallationType = GameInstallationType.Unknown, // Added required property
            IsEnabled = false,
        };
        _viewModel.AvailableContent.Add(clientDisplayItem);

        // Act
        _viewModel.EnableContentCommand.Execute(clientDisplayItem);

        // Assert
        // Need to wait slightly because ResolveDependenciesAsync is fire-and-forget void async
        await Task.Delay(50);

        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == mapPackId.Value);
        Assert.True(_viewModel.EnabledContent.First(c => c.ManifestId.Value == mapPackId.Value).IsEnabled);
    }
}
