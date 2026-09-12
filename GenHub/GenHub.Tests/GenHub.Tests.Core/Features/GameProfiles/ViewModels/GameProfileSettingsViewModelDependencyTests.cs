using System.Collections.ObjectModel;
using GenHub.Core.Constants;
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
            null, // IGenLauncherNormalizationService
            null, // IDialogService
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
    public async Task Save_Fails_WhenGameInstallationIdMismatchAsync()
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
    public async Task Save_Succeeds_WhenGameInstallationIdMatchesAsync()
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
    public async Task Save_Succeeds_WhenOptionalDependencyIsMissingAsync()
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
    public async Task EnableContent_AutoSwitches_GameInstallation_When_Dependency_Requires_Different_TypeAsync()
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
    public async Task EnableContent_AutoSwitches_Installation_For_Standard_GameClient_Missing_ManifestAsync()
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
    public async Task EnableContent_AutoEnables_DependentContentAsync()
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

    /// <summary>
    /// Verifies that enabling content requiring a GameInstallation does not auto switch when a compatible installation is already selected.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task EnableContent_DoesNotAutoSwitch_WhenMatchingGameInstallationAlreadySelectedAsync()
    {
        // Arrange
        var mapPackManifestId = new ManifestId("1.813262.generalsonline.mappack.quickmatchmaps");
        var zeroHourInstallId = new ManifestId("1.104.steam.gameinstallation.zerohour");

        var mapPackManifest = new ContentManifest
        {
            Id = mapPackManifestId,
            Name = "GeneralsOnline QuickMatch Maps",
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new()
                {
                    DependencyType = ContentType.GameInstallation,
                    CompatibleGameTypes = [GameType.ZeroHour],
                },
            ],
        };

        _mockManifestPool.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == mapPackManifestId.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(mapPackManifest));

        var zeroHourInstall = new ViewModelContentDisplayItem
        {
            ManifestId = zeroHourInstallId,
            DisplayName = "Zero Hour v1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = true,
        };

        _viewModel.AvailableGameInstallations = [zeroHourInstall];
        _viewModel.SelectedGameInstallation = zeroHourInstall;
        _viewModel.EnabledContent.Add(zeroHourInstall);

        var mapPackItem = new ViewModelContentDisplayItem
        {
            ManifestId = mapPackManifestId,
            DisplayName = "GeneralsOnline QuickMatch Maps",
            ContentType = ContentType.MapPack,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = false,
        };
        _viewModel.AvailableContent.Add(mapPackItem);

        // Act
        await _viewModel.EnableContentCommand.ExecuteAsync(mapPackItem);

        // Assert
        Assert.Equal(zeroHourInstall, _viewModel.SelectedGameInstallation);
        Assert.Single(_viewModel.EnabledContent, c => c.ContentType == ContentType.GameInstallation);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == mapPackManifestId.Value);
    }

    /// <summary>
    /// Verifies that setting SelectedGameInstallation automatically adds it to EnabledContent and removes previous installations.
    /// </summary>
    [Fact]
    public void SelectedGameInstallation_AutoAddsToEnabledContent_AndReplacesPreviousInstallation()
    {
        var install1 = new ViewModelContentDisplayItem
        {
            ManifestId = new ManifestId("1.0.0.gameinstallation.steam-zh"),
            DisplayName = "Zero Hour Steam",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
        };
        var install2 = new ViewModelContentDisplayItem
        {
            ManifestId = new ManifestId("1.0.0.gameinstallation.ea-zh"),
            DisplayName = "Zero Hour EA",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
        };

        _viewModel.AvailableGameInstallations = [install1, install2];

        // Select first
        _viewModel.SelectedGameInstallation = install1;
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == install1.ManifestId.Value);
        Assert.Single(_viewModel.EnabledContent, c => c.ContentType == ContentType.GameInstallation);

        // Select second - replaces first
        _viewModel.SelectedGameInstallation = install2;
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == install2.ManifestId.Value);
        Assert.DoesNotContain(_viewModel.EnabledContent, c => c.ManifestId.Value == install1.ManifestId.Value);
        Assert.Single(_viewModel.EnabledContent, c => c.ContentType == ContentType.GameInstallation);

        // Clear selection
        _viewModel.SelectedGameInstallation = null;
        Assert.DoesNotContain(_viewModel.EnabledContent, c => c.ContentType == ContentType.GameInstallation);
    }

    /// <summary>
    /// Verifies that enabling content requiring a GameInstallation automatically selects and enables the compatible installation when none was selected.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task EnableContent_GameClient_AutoSelectsCompatibleGameInstallation_WhenNoneSelectedAsync()
    {
        var clientManifestId = new ManifestId("1.0.0.gameclient.zerohour");
        var zhInstallId = new ManifestId("1.0.0.gameinstallation.steam-zh");

        var clientManifest = new ContentManifest
        {
            Id = clientManifestId,
            Name = "Zero Hour Client",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new()
                {
                    DependencyType = ContentType.GameInstallation,
                    CompatibleGameTypes = [GameType.ZeroHour],
                },
            ],
        };

        _mockManifestPool.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == clientManifestId.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(clientManifest));

        var zhInstall = new ViewModelContentDisplayItem
        {
            ManifestId = zhInstallId,
            DisplayName = "Zero Hour Steam",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = false,
        };

        _viewModel.AvailableGameInstallations = [zhInstall];
        _viewModel.SelectedGameInstallation = null;

        var clientItem = new ViewModelContentDisplayItem
        {
            ManifestId = clientManifestId,
            DisplayName = "Zero Hour Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = false,
        };
        _viewModel.AvailableContent.Add(clientItem);

        // Act
        await _viewModel.EnableContentCommand.ExecuteAsync(clientItem);

        // Assert
        Assert.NotNull(_viewModel.SelectedGameInstallation);
        Assert.Equal(zhInstallId.Value, _viewModel.SelectedGameInstallation.ManifestId.Value);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == zhInstallId.Value);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == clientManifestId.Value);
    }

    /// <summary>
    /// Verifies that disabling the last GameClient or Mod auto-disables the SelectedGameInstallation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DisableContent_WhenLastClientOrModRemoved_AutoDisablesSelectedGameInstallationAsync()
    {
        var zhInstall = new ViewModelContentDisplayItem
        {
            ManifestId = new ManifestId("1.0.0.gameinstallation.steam-zh"),
            DisplayName = "Zero Hour Steam",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = true,
        };

        var clientItem = new ViewModelContentDisplayItem
        {
            ManifestId = new ManifestId("1.0.0.gameclient.zerohour"),
            DisplayName = "Zero Hour Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = true,
        };

        _viewModel.AvailableGameInstallations = [zhInstall];
        _viewModel.SelectedGameInstallation = zhInstall;
        _viewModel.EnabledContent.Add(clientItem);

        // Act - disable the only client
        await _viewModel.DisableContentCommand.ExecuteAsync(clientItem);

        // Assert
        Assert.Null(_viewModel.SelectedGameInstallation);
        Assert.DoesNotContain(_viewModel.EnabledContent, c => c.ContentType == ContentType.GameInstallation);
    }

    /// <summary>
    /// Verifies that disabling non-client/non-mod content like Addon or Map does not clear SelectedGameInstallation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DisableContent_WhenRemovingAddonOrMap_DoesNotAutoDisableSelectedGameInstallationAsync()
    {
        var zhInstall = new ViewModelContentDisplayItem
        {
            ManifestId = new ManifestId("1.0.0.gameinstallation.steam-zh"),
            DisplayName = "Zero Hour Steam",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = true,
        };

        var addonItem = new ViewModelContentDisplayItem
        {
            ManifestId = new ManifestId("1.0.0.addon.test"),
            DisplayName = "Test Addon",
            ContentType = ContentType.Addon,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = true,
        };

        _viewModel.AvailableGameInstallations = [zhInstall];
        _viewModel.SelectedGameInstallation = zhInstall;
        _viewModel.EnabledContent.Add(addonItem);

        // Act - disable the addon
        await _viewModel.DisableContentCommand.ExecuteAsync(addonItem);

        // Assert - installation remains selected
        Assert.NotNull(_viewModel.SelectedGameInstallation);
        Assert.Equal(zhInstall.ManifestId.Value, _viewModel.SelectedGameInstallation.ManifestId.Value);
    }

    /// <summary>
    /// Verifies that clearing EnabledContent resets SelectedGameInstallation via collection changed reset action.
    /// </summary>
    [Fact]
    public void EnabledContent_WhenCleared_ResetsSelectedGameInstallation()
    {
        var zhInstall = new ViewModelContentDisplayItem
        {
            ManifestId = new ManifestId("1.0.0.gameinstallation.steam-zh"),
            DisplayName = "Zero Hour Steam",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = true,
        };

        _viewModel.AvailableGameInstallations = [zhInstall];
        _viewModel.SelectedGameInstallation = zhInstall;

        Assert.NotNull(_viewModel.SelectedGameInstallation);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == zhInstall.ManifestId.Value);

        // Act - clear EnabledContent raising NotifyCollectionChangedAction.Reset
        _viewModel.EnabledContent.Clear();

        // Assert - SelectedGameInstallation is cleared
        Assert.Null(_viewModel.SelectedGameInstallation);
    }

    /// <summary>
    /// Verifies that saving a standalone profile (e.g. Executable or ModdingTool) succeeds without SelectedGameInstallation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Save_WhenStandaloneProfileWithoutGameInstallation_SucceedsAsync()
    {
        // Arrange
        var toolManifestId = new ManifestId("1.0.0.moddingtool.worldbuilder");
        var toolManifest = new ContentManifest
        {
            Id = toolManifestId,
            Name = "World Builder",
            ContentType = ContentType.ModdingTool,
        };

        _mockManifestPool.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == toolManifestId.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(toolManifest));

        _viewModel.Name = "Standalone Tool Profile";
        _viewModel.SelectedGameInstallation = null;

        var toolDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = toolManifestId,
            DisplayName = "World Builder",
            ContentType = ContentType.ModdingTool,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Unknown,
            IsEnabled = true,
        };
        _viewModel.EnabledContent.Add(toolDisplayItem);

        _mockGameProfileManager.Setup(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile()));

        // Act
        await _viewModel.SaveCommand.ExecuteAsync(null);

        // Assert
        _mockGameProfileManager.Verify(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that changing SelectedGameInstallation synchronizes IsEnabled flags and replaces the item in EnabledContent.
    /// </summary>
    [Fact]
    public void SelectedGameInstallation_WhenChanged_SynchronizesIsEnabledAndReplacesInEnabledContent()
    {
        // Arrange
        var install1 = new ViewModelContentDisplayItem
        {
            ManifestId = new ManifestId("install-1"),
            DisplayName = "Zero Hour Install 1",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
        };
        var install2 = new ViewModelContentDisplayItem
        {
            ManifestId = new ManifestId("install-2"),
            DisplayName = "Zero Hour Install 2",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
        };

        _viewModel.AvailableGameInstallations.Add(install1);
        _viewModel.AvailableGameInstallations.Add(install2);

        // Act 1: select install1
        _viewModel.SelectedGameInstallation = install1;

        // Assert 1
        Assert.True(install1.IsEnabled);
        Assert.False(install2.IsEnabled);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == install1.ManifestId.Value);

        // Act 2: switch to install2
        _viewModel.SelectedGameInstallation = install2;

        // Assert 2
        Assert.False(install1.IsEnabled);
        Assert.True(install2.IsEnabled);
        Assert.DoesNotContain(_viewModel.EnabledContent, c => c.ManifestId.Value == install1.ManifestId.Value);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == install2.ManifestId.Value);
    }

    /// <summary>
    /// Verifies that adding a duplicate ManifestId to EnabledContent deduplicates the collection.
    /// </summary>
    [Fact]
    public void EnabledContent_WhenDuplicateItemAdded_DeduplicatesManifestId()
    {
        // Arrange
        var item1 = new ViewModelContentDisplayItem
        {
            ManifestId = new ManifestId("dup-id"),
            DisplayName = "Mod Item 1",
            ContentType = ContentType.Mod,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Unknown,
        };
        var item2 = new ViewModelContentDisplayItem
        {
            ManifestId = new ManifestId("dup-id"),
            DisplayName = "Mod Item 2",
            ContentType = ContentType.Mod,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Unknown,
        };

        // Act
        _viewModel.EnabledContent.Add(item1);
        _viewModel.EnabledContent.Add(item2);

        // Assert
        Assert.Equal(1, _viewModel.EnabledContent.Count(x => x.ManifestId.Value == "dup-id"));
    }

    /// <summary>
    /// Verifies that setting SelectedGameInstallation when the profile is a standalone tool profile
    /// does not inject the GameInstallation into EnabledContent.
    /// </summary>
    [Fact]
    public void SelectedGameInstallation_WhenStandaloneToolProfile_DoesNotAddInstallationToEnabledContent()
    {
        // Arrange
        var toolDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = new ManifestId("1.0.0.moddingtool.worldbuilder"),
            DisplayName = "World Builder",
            ContentType = ContentType.ModdingTool,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Unknown,
            IsEnabled = true,
        };
        _viewModel.EnabledContent.Add(toolDisplayItem);

        var install1 = new ViewModelContentDisplayItem
        {
            ManifestId = new ManifestId("install-1"),
            DisplayName = "Zero Hour Install",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
        };
        _viewModel.AvailableGameInstallations.Add(install1);

        // Act
        _viewModel.SelectedGameInstallation = install1;

        // Assert - tool remains the only enabled content; installation was not injected
        Assert.DoesNotContain(_viewModel.EnabledContent, c => c.ContentType == ContentType.GameInstallation);
        Assert.Single(_viewModel.EnabledContent);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == toolDisplayItem.ManifestId.Value);
    }

    /// <summary>
    /// Verifies that executing EnableContentCommand with a GameInstallation on a standalone tool profile
    /// is rejected and does not add the installation to EnabledContent.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task EnableContentCommand_WhenStandaloneToolProfile_RejectsGameInstallationAsync()
    {
        // Arrange
        var toolDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = new ManifestId("1.0.0.moddingtool.worldbuilder"),
            DisplayName = "World Builder",
            ContentType = ContentType.ModdingTool,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Unknown,
            IsEnabled = true,
        };
        _viewModel.EnabledContent.Add(toolDisplayItem);

        var installItem = new ViewModelContentDisplayItem
        {
            ManifestId = new ManifestId("1.0.0.gameinstallation.steam-zh"),
            DisplayName = "Zero Hour Install",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = false,
        };
        _viewModel.AvailableContent.Add(installItem);

        // Act
        await _viewModel.EnableContentCommand.ExecuteAsync(installItem);

        // Assert
        Assert.DoesNotContain(_viewModel.EnabledContent, c => c.ContentType == ContentType.GameInstallation);
        Assert.Equal("Standalone tool profiles do not require or support game installations", _viewModel.StatusMessage);
    }

    /// <summary>
    /// Verifies that disabling a GameInstallation is blocked when a dependent GameClient is active.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DisableContent_BlocksGameInstallationRemoval_WhenDependentGameClientIsActiveAsync()
    {
        // Arrange
        var standardClientId = new ManifestId("1.04.eaapp.gameclient.zerohour");
        var zeroHourInstallId = new ManifestId("1.04.eaapp.gameinstallation.zerohour");

        var zeroHourInstall = new ViewModelContentDisplayItem
        {
            ManifestId = zeroHourInstallId,
            DisplayName = "Zero Hour 1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
            IsEnabled = true,
        };

        var clientDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = standardClientId,
            DisplayName = "Zero Hour 1.04 Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
            SourceId = zeroHourInstallId.Value,
            IsEnabled = true,
        };

        _viewModel.AvailableGameInstallations = [zeroHourInstall];
        _viewModel.SelectedGameInstallation = zeroHourInstall;
        _viewModel.EnabledContent.Add(zeroHourInstall);
        _viewModel.EnabledContent.Add(clientDisplayItem);

        // Act
        await _viewModel.DisableContentCommand.ExecuteAsync(zeroHourInstall);

        // Assert
        Assert.Equal(zeroHourInstall, _viewModel.SelectedGameInstallation);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == zeroHourInstallId.Value);
        Assert.True(zeroHourInstall.IsEnabled);
        Assert.Contains("Cannot remove", _viewModel.StatusMessage);
        _mockNotificationService.Verify(
            x => x.ShowWarning(ProfileValidationConstants.CannotRemoveInstallationTitle, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that disabling a GameInstallation is allowed after the dependent GameClient is removed first.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DisableContent_AllowsGameInstallationRemoval_AfterDependentGameClientIsRemovedAsync()
    {
        // Arrange
        var standardClientId = new ManifestId("1.04.eaapp.gameclient.zerohour");
        var zeroHourInstallId = new ManifestId("1.04.eaapp.gameinstallation.zerohour");

        var zeroHourInstall = new ViewModelContentDisplayItem
        {
            ManifestId = zeroHourInstallId,
            DisplayName = "Zero Hour 1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
            IsEnabled = true,
        };

        var clientDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = standardClientId,
            DisplayName = "Zero Hour 1.04 Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
            SourceId = zeroHourInstallId.Value,
            IsEnabled = true,
        };

        var modDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = new ManifestId("1.0.0.mod.test"),
            DisplayName = "Test Mod",
            ContentType = ContentType.Mod,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Unknown,
            IsEnabled = true,
        };

        _viewModel.AvailableGameInstallations = [zeroHourInstall];
        _viewModel.SelectedGameInstallation = zeroHourInstall;
        _viewModel.EnabledContent.Add(zeroHourInstall);
        _viewModel.EnabledContent.Add(clientDisplayItem);
        _viewModel.EnabledContent.Add(modDisplayItem);

        // Act 1: Disable client first
        await _viewModel.DisableContentCommand.ExecuteAsync(clientDisplayItem);
        Assert.DoesNotContain(_viewModel.EnabledContent, c => c.ManifestId.Value == standardClientId.Value);

        // Act 2: Disable installation
        await _viewModel.DisableContentCommand.ExecuteAsync(zeroHourInstall);

        // Assert
        Assert.Null(_viewModel.SelectedGameInstallation);
        Assert.DoesNotContain(_viewModel.EnabledContent, c => c.ManifestId.Value == zeroHourInstallId.Value);
        Assert.False(zeroHourInstall.IsEnabled);
        Assert.Equal($"Disabled {zeroHourInstall.DisplayName}", _viewModel.StatusMessage);
    }

    /// <summary>
    /// Verifies that saving fails with error message and notification when SelectedGameInstallation is null.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Save_FailsWithNotification_WhenSelectedGameInstallationIsNullAsync()
    {
        // Arrange
        _viewModel.Name = "Test Profile";
        _viewModel.SelectedGameInstallation = null;

        // Act
        await _viewModel.SaveCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("Please select a game installation", _viewModel.StatusMessage);
        _mockNotificationService.Verify(
            x => x.ShowError(ProfileValidationConstants.MissingGameInstallationTitle, ProfileValidationConstants.SelectGameInstallationBeforeSaving, It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        _mockGameProfileManager.Verify(
            x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that disabling a GameInstallation is blocked when a publisher-agnostic GameClient
    /// (e.g. StrictPublisher = false) depends on the installation's GameType.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DisableContent_BlocksGameInstallationRemoval_WhenPublisherAgnosticClientDependsOnInstallationAsync()
    {
        // Arrange
        var agnosticClientId = new ManifestId("1.104.genhub.gameclient.zerohour");
        var zeroHourInstallId = new ManifestId("1.04.eaapp.gameinstallation.zerohour");

        var zeroHourInstall = new ViewModelContentDisplayItem
        {
            ManifestId = zeroHourInstallId,
            DisplayName = "Zero Hour 1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
            IsEnabled = true,
        };

        var clientManifest = new ContentManifest
        {
            Id = agnosticClientId,
            Name = "SuperHackers ZH Client",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = new ManifestId("1.104.genhub.gameinstallation.zerohour"),
                    DependencyType = ContentType.GameInstallation,
                    StrictPublisher = false,
                    CompatibleGameTypes = [GameType.ZeroHour],
                    IsOptional = false,
                },
            ],
        };

        var clientDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = agnosticClientId,
            DisplayName = "SuperHackers ZH Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Unknown,
            Manifest = clientManifest,
            IsEnabled = true,
        };

        _viewModel.AvailableGameInstallations = [zeroHourInstall];
        _viewModel.SelectedGameInstallation = zeroHourInstall;
        _viewModel.EnabledContent.Add(zeroHourInstall);
        _viewModel.EnabledContent.Add(clientDisplayItem);

        // Act
        await _viewModel.DisableContentCommand.ExecuteAsync(zeroHourInstall);

        // Assert
        Assert.Equal(zeroHourInstall, _viewModel.SelectedGameInstallation);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == zeroHourInstallId.Value);
        Assert.True(zeroHourInstall.IsEnabled);
        Assert.Contains("Cannot remove", _viewModel.StatusMessage);
        _mockNotificationService.Verify(
            x => x.ShowWarning(ProfileValidationConstants.CannotRemoveInstallationTitle, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that deleting a GameInstallation is blocked when an active GameClient depends on it.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteContent_BlocksGameInstallationDeletion_WhenDependentGameClientIsActiveAsync()
    {
        // Arrange
        var standardClientId = new ManifestId("1.04.eaapp.gameclient.zerohour");
        var zeroHourInstallId = new ManifestId("1.04.eaapp.gameinstallation.zerohour");

        var zeroHourInstall = new ViewModelContentDisplayItem
        {
            ManifestId = zeroHourInstallId,
            DisplayName = "Zero Hour 1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
            IsEnabled = true,
        };

        var clientDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = standardClientId,
            DisplayName = "Zero Hour 1.04 Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
            SourceId = zeroHourInstallId.Value,
            IsEnabled = true,
        };

        _viewModel.AvailableGameInstallations = [zeroHourInstall];
        _viewModel.SelectedGameInstallation = zeroHourInstall;
        _viewModel.EnabledContent.Add(zeroHourInstall);
        _viewModel.EnabledContent.Add(clientDisplayItem);

        // Act
        await _viewModel.DeleteContentCommand.ExecuteAsync(zeroHourInstall);

        // Assert
        Assert.Contains("Cannot delete", _viewModel.StatusMessage);
        _mockNotificationService.Verify(
            x => x.ShowWarning(ProfileValidationConstants.CannotDeleteInstallationTitle, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that disabling a GameInstallation is allowed when the client only declares an optional dependency.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DisableContent_AllowsGameInstallationRemoval_WhenClientInstallationDependencyIsOptionalAsync()
    {
        // Arrange
        var clientId = new ManifestId("1.0.0.gameclient.optionalclient");
        var zeroHourInstallId = new ManifestId("1.04.eaapp.gameinstallation.zerohour");

        var zeroHourInstall = new ViewModelContentDisplayItem
        {
            ManifestId = zeroHourInstallId,
            DisplayName = "Zero Hour 1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
            IsEnabled = true,
        };

        var clientManifest = new ContentManifest
        {
            Id = clientId,
            Name = "Optional Client",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = zeroHourInstallId,
                    DependencyType = ContentType.GameInstallation,
                    IsOptional = true,
                },
            ],
        };

        var clientDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = clientId,
            DisplayName = "Optional Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Unknown,
            Manifest = clientManifest,
            IsEnabled = true,
        };

        _viewModel.AvailableGameInstallations = [zeroHourInstall];
        _viewModel.SelectedGameInstallation = zeroHourInstall;
        _viewModel.EnabledContent.Add(zeroHourInstall);
        _viewModel.EnabledContent.Add(clientDisplayItem);

        // Act
        await _viewModel.DisableContentCommand.ExecuteAsync(zeroHourInstall);

        // Assert - allowed because the dependency is optional
        Assert.Null(_viewModel.SelectedGameInstallation);
        Assert.DoesNotContain(_viewModel.EnabledContent, c => c.ManifestId.Value == zeroHourInstallId.Value);
        Assert.False(zeroHourInstall.IsEnabled);
    }

    /// <summary>
    /// Verifies that disabling a GameInstallation is blocked when a publisher-agnostic GameClient
    /// has empty CompatibleGameTypes but matching content-type and content-name segments.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DisableContent_BlocksGameInstallationRemoval_WhenPublisherAgnosticClientMatchesEmptyCompatibleGameTypesAsync()
    {
        // Arrange
        var agnosticClientId = new ManifestId("1.104.genhub.gameclient.zerohour");
        var zeroHourInstallId = new ManifestId("1.04.eaapp.gameinstallation.zerohour");

        var zeroHourInstall = new ViewModelContentDisplayItem
        {
            ManifestId = zeroHourInstallId,
            DisplayName = "Zero Hour 1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
            IsEnabled = true,
        };

        var clientManifest = new ContentManifest
        {
            Id = agnosticClientId,
            Name = "SuperHackers ZH Client",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = new ManifestId("1.104.genhub.gameinstallation.zerohour"),
                    DependencyType = ContentType.GameInstallation,
                    StrictPublisher = false,
                    CompatibleGameTypes = [],
                    IsOptional = false,
                },
            ],
        };

        var clientDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = agnosticClientId,
            DisplayName = "SuperHackers ZH Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Unknown,
            Manifest = clientManifest,
            IsEnabled = true,
        };

        _viewModel.AvailableGameInstallations = [zeroHourInstall];
        _viewModel.SelectedGameInstallation = zeroHourInstall;
        _viewModel.EnabledContent.Add(zeroHourInstall);
        _viewModel.EnabledContent.Add(clientDisplayItem);

        // Act
        await _viewModel.DisableContentCommand.ExecuteAsync(zeroHourInstall);

        // Assert - blocked because segments 3 and 4 match (gameinstallation.zerohour)
        Assert.Equal(zeroHourInstall, _viewModel.SelectedGameInstallation);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == zeroHourInstallId.Value);
        Assert.True(zeroHourInstall.IsEnabled);
        _mockNotificationService.Verify(
            x => x.ShowWarning(ProfileValidationConstants.CannotRemoveInstallationTitle, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that disabling a GameInstallation is allowed when a publisher-agnostic GameClient
    /// has empty CompatibleGameTypes and differing content-name segment.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DisableContent_AllowsGameInstallationRemoval_WhenPublisherAgnosticClientWithEmptyCompatibleGameTypesDoesNotMatchNameAsync()
    {
        // Arrange
        var agnosticClientId = new ManifestId("1.104.genhub.gameclient.generals");
        var zeroHourInstallId = new ManifestId("1.04.eaapp.gameinstallation.zerohour");

        var zeroHourInstall = new ViewModelContentDisplayItem
        {
            ManifestId = zeroHourInstallId,
            DisplayName = "Zero Hour 1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
            IsEnabled = true,
        };

        var clientManifest = new ContentManifest
        {
            Id = agnosticClientId,
            Name = "Generals Client",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.Generals,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = new ManifestId("1.104.genhub.gameinstallation.generals"),
                    DependencyType = ContentType.GameInstallation,
                    StrictPublisher = false,
                    CompatibleGameTypes = [],
                    IsOptional = false,
                },
            ],
        };

        var clientDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = agnosticClientId,
            DisplayName = "Generals Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.Unknown,
            Manifest = clientManifest,
            IsEnabled = true,
        };

        _viewModel.AvailableGameInstallations = [zeroHourInstall];
        _viewModel.SelectedGameInstallation = zeroHourInstall;
        _viewModel.EnabledContent.Add(zeroHourInstall);
        _viewModel.EnabledContent.Add(clientDisplayItem);

        // Act
        await _viewModel.DisableContentCommand.ExecuteAsync(zeroHourInstall);

        // Assert - allowed because dependency requires 'generals' installation, not 'zerohour'
        Assert.Null(_viewModel.SelectedGameInstallation);
        Assert.DoesNotContain(_viewModel.EnabledContent, c => c.ManifestId.Value == zeroHourInstallId.Value);
        Assert.False(zeroHourInstall.IsEnabled);
    }

    /// <summary>
    /// Verifies that disabling a GameInstallation is blocked by the fallback heuristic when an active GameClient
    /// has no manifest or dependencies and an empty SourceId, but matches the installation's GameType.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DisableContent_BlocksGameInstallationRemoval_WhenActiveClientHasNoDependenciesOrManifest_AndMatchesGameTypeAsync()
    {
        // Arrange
        var clientId = new ManifestId("1.104.community.gameclient.zhclient");
        var zeroHourInstallId = new ManifestId("1.04.eaapp.gameinstallation.zerohour");

        var zeroHourInstall = new ViewModelContentDisplayItem
        {
            ManifestId = zeroHourInstallId,
            DisplayName = "Zero Hour 1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
            IsEnabled = true,
        };

        var clientDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = clientId,
            DisplayName = "ZH Fallback Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Unknown,
            SourceId = string.Empty,
            Manifest = null,
            IsEnabled = true,
        };

        _viewModel.AvailableGameInstallations = [zeroHourInstall];
        _viewModel.SelectedGameInstallation = zeroHourInstall;
        _viewModel.EnabledContent.Add(zeroHourInstall);
        _viewModel.EnabledContent.Add(clientDisplayItem);

        // Act
        await _viewModel.DisableContentCommand.ExecuteAsync(zeroHourInstall);

        // Assert - blocked by fallback heuristic (same GameType)
        Assert.Equal(zeroHourInstall, _viewModel.SelectedGameInstallation);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == zeroHourInstallId.Value);
        Assert.True(zeroHourInstall.IsEnabled);
        _mockNotificationService.Verify(
            x => x.ShowWarning(ProfileValidationConstants.CannotRemoveInstallationTitle, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that disabling a GameInstallation is blocked by the fallback heuristic when an active GameClient
    /// has a different GameType, but the installation matches SelectedGameInstallation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DisableContent_BlocksGameInstallationRemoval_WhenActiveClientHasNoDependenciesOrManifest_AndMatchesSelectedGameInstallationAsync()
    {
        // Arrange
        var clientId = new ManifestId("1.104.community.gameclient.otherclient");
        var zeroHourInstallId = new ManifestId("1.04.eaapp.gameinstallation.zerohour");

        var zeroHourInstall = new ViewModelContentDisplayItem
        {
            ManifestId = zeroHourInstallId,
            DisplayName = "Zero Hour 1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
            IsEnabled = true,
        };

        var clientDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = clientId,
            DisplayName = "Other Fallback Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.Unknown,
            SourceId = string.Empty,
            Manifest = null,
            IsEnabled = true,
        };

        _viewModel.AvailableGameInstallations = [zeroHourInstall];
        _viewModel.SelectedGameInstallation = zeroHourInstall;
        _viewModel.EnabledContent.Add(zeroHourInstall);
        _viewModel.EnabledContent.Add(clientDisplayItem);

        // Act
        await _viewModel.DisableContentCommand.ExecuteAsync(zeroHourInstall);

        // Assert - blocked because zeroHourInstall matches SelectedGameInstallation
        Assert.Equal(zeroHourInstall, _viewModel.SelectedGameInstallation);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == zeroHourInstallId.Value);
        Assert.True(zeroHourInstall.IsEnabled);
        _mockNotificationService.Verify(
            x => x.ShowWarning(ProfileValidationConstants.CannotRemoveInstallationTitle, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that deleting a GameInstallation is allowed by the fallback heuristic when an active GameClient
    /// has no manifest or dependencies and an empty SourceId, but differs in GameType and is not the SelectedGameInstallation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteContent_AllowsGameInstallationDeletion_WhenActiveClientHasNoDependenciesOrManifest_AndDiffersInGameTypeAsync()
    {
        // Arrange
        var clientId = new ManifestId("1.104.community.gameclient.generalsclient");
        var zeroHourInstallId = new ManifestId("1.04.eaapp.gameinstallation.zerohour");
        var generalsInstallId = new ManifestId("1.04.eaapp.gameinstallation.generals");

        var zeroHourInstall = new ViewModelContentDisplayItem
        {
            ManifestId = zeroHourInstallId,
            DisplayName = "Zero Hour 1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
            IsEnabled = false,
        };

        var generalsInstall = new ViewModelContentDisplayItem
        {
            ManifestId = generalsInstallId,
            DisplayName = "Generals 1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.EaApp,
            IsEnabled = true,
        };

        var clientDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = clientId,
            DisplayName = "Generals Fallback Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.Unknown,
            SourceId = string.Empty,
            Manifest = null,
            IsEnabled = true,
        };

        _viewModel.AvailableGameInstallations = [zeroHourInstall, generalsInstall];
        _viewModel.SelectedGameInstallation = generalsInstall;
        _viewModel.EnabledContent.Add(generalsInstall);
        _viewModel.EnabledContent.Add(clientDisplayItem);

        // Act
        await _viewModel.DeleteContentCommand.ExecuteAsync(zeroHourInstall);

        // Assert - deletion is not blocked by the Generals client
        _mockNotificationService.Verify(
            x => x.ShowWarning(ProfileValidationConstants.CannotDeleteInstallationTitle, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that disabling a GameInstallation is blocked when an active GameClient has an unconstrained default dependency
    /// (CompatibleGameTypes is empty) and the installation is the SelectedGameInstallation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DisableContent_BlocksGameInstallationRemoval_WhenActiveClientHasUnconstrainedDefaultDependency_AndMatchesSelectedGameInstallationAsync()
    {
        // Arrange
        var clientId = new ManifestId("1.104.community.gameclient.unconstrainedclient");
        var zeroHourInstallId = new ManifestId("1.04.eaapp.gameinstallation.zerohour");

        var zeroHourInstall = new ViewModelContentDisplayItem
        {
            ManifestId = zeroHourInstallId,
            DisplayName = "Zero Hour 1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
            IsEnabled = true,
        };

        var clientManifest = new ContentManifest
        {
            Id = clientId,
            Name = "Unconstrained Client",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(ManifestConstants.DefaultContentDependencyId),
                    DependencyType = ContentType.GameInstallation,
                    CompatibleGameTypes = [],
                    IsOptional = false,
                },
            ],
        };

        var clientDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = clientId,
            DisplayName = "Unconstrained Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Unknown,
            Manifest = clientManifest,
            IsEnabled = true,
        };

        _viewModel.AvailableGameInstallations = [zeroHourInstall];
        _viewModel.SelectedGameInstallation = zeroHourInstall;
        _viewModel.EnabledContent.Add(zeroHourInstall);
        _viewModel.EnabledContent.Add(clientDisplayItem);

        // Act
        await _viewModel.DisableContentCommand.ExecuteAsync(zeroHourInstall);

        // Assert - blocked because zeroHourInstall matches SelectedGameInstallation
        Assert.Equal(zeroHourInstall, _viewModel.SelectedGameInstallation);
        Assert.Contains(_viewModel.EnabledContent, c => c.ManifestId.Value == zeroHourInstallId.Value);
        Assert.True(zeroHourInstall.IsEnabled);
        _mockNotificationService.Verify(
            x => x.ShowWarning(ProfileValidationConstants.CannotRemoveInstallationTitle, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that disabling a GameInstallation is allowed when an active GameClient has an unconstrained default dependency
    /// (CompatibleGameTypes is empty) but the installation being removed is not the SelectedGameInstallation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DisableContent_AllowsGameInstallationRemoval_WhenActiveClientHasUnconstrainedDefaultDependency_AndDiffersFromSelectedGameInstallationAsync()
    {
        // Arrange
        var clientId = new ManifestId("1.104.community.gameclient.unconstrainedclient");
        var zeroHourInstallId = new ManifestId("1.04.eaapp.gameinstallation.zerohour");
        var generalsInstallId = new ManifestId("1.04.eaapp.gameinstallation.generals");

        var zeroHourInstall = new ViewModelContentDisplayItem
        {
            ManifestId = zeroHourInstallId,
            DisplayName = "Zero Hour 1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.EaApp,
            IsEnabled = false,
        };

        var generalsInstall = new ViewModelContentDisplayItem
        {
            ManifestId = generalsInstallId,
            DisplayName = "Generals 1.04",
            ContentType = ContentType.GameInstallation,
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.EaApp,
            IsEnabled = true,
        };

        var clientManifest = new ContentManifest
        {
            Id = clientId,
            Name = "Unconstrained Client",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.Generals,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(ManifestConstants.DefaultContentDependencyId),
                    DependencyType = ContentType.GameInstallation,
                    CompatibleGameTypes = [],
                    IsOptional = false,
                },
            ],
        };

        var clientDisplayItem = new ViewModelContentDisplayItem
        {
            ManifestId = clientId,
            DisplayName = "Unconstrained Client",
            ContentType = ContentType.GameClient,
            GameType = GameType.Generals,
            InstallationType = GameInstallationType.Unknown,
            Manifest = clientManifest,
            IsEnabled = true,
        };

        _viewModel.AvailableGameInstallations = [zeroHourInstall, generalsInstall];
        _viewModel.SelectedGameInstallation = generalsInstall;
        _viewModel.EnabledContent.Add(generalsInstall);
        _viewModel.EnabledContent.Add(clientDisplayItem);

        // Act - disable zeroHourInstall (not SelectedGameInstallation)
        await _viewModel.DisableContentCommand.ExecuteAsync(zeroHourInstall);

        // Assert - allowed because generalsInstall is the SelectedGameInstallation satisfying the default dependency
        Assert.Equal(generalsInstall, _viewModel.SelectedGameInstallation);
        Assert.DoesNotContain(_viewModel.EnabledContent, c => c.ManifestId.Value == zeroHourInstallId.Value);
    }
}
