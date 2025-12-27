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
}
