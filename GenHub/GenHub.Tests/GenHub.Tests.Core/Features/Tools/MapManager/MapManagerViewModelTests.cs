using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.MapManager;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.MapManager;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.Tools.MapManager.ViewModels;
using GenHub.Infrastructure.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.MapManager;

/// <summary>
/// Unit tests for <see cref="MapManagerViewModel"/> MapPack and Profile integration.
/// </summary>
public sealed class MapManagerViewModelTests : IDisposable
{
    private readonly Mock<IMapDirectoryService> _mockDirectoryService;
    private readonly Mock<IMapImportService> _mockImportService;
    private readonly Mock<IMapExportService> _mockExportService;
    private readonly Mock<IMapPackService> _mockMapPackService;
    private readonly Mock<IUploadHistoryService> _mockUploadHistoryService;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<ILocalizationService> _mockLocalizationService;
    private readonly Mock<IGameProfileManager> _mockProfileManager;
    private readonly Mock<IProfileContentService> _mockProfileContentService;
    private readonly Mock<IContentManifestPool> _mockManifestPool;
    private readonly TgaImageParser _imageParser;
    private readonly string _tempDirectory;
    private readonly MapManagerViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapManagerViewModelTests"/> class.
    /// </summary>
    public MapManagerViewModelTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "GenHub_MapTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _mockDirectoryService = new Mock<IMapDirectoryService>();
        _mockImportService = new Mock<IMapImportService>();
        _mockExportService = new Mock<IMapExportService>();
        _mockMapPackService = new Mock<IMapPackService>();
        _mockUploadHistoryService = new Mock<IUploadHistoryService>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockLocalizationService = new Mock<ILocalizationService>();

        _mockProfileManager = new Mock<IGameProfileManager>();
        _mockProfileContentService = new Mock<IProfileContentService>();
        _mockManifestPool = new Mock<IContentManifestPool>();

        _mockDirectoryService
            .Setup(d => d.GetMapDirectory(It.IsAny<GameType>()))
            .Returns(_tempDirectory);

        _imageParser = new TgaImageParser(NullLogger<TgaImageParser>.Instance);

        _viewModel = new MapManagerViewModel(
            _mockDirectoryService.Object,
            _mockImportService.Object,
            _mockExportService.Object,
            _mockMapPackService.Object,
            _mockUploadHistoryService.Object,
            _mockNotificationService.Object,
            _imageParser,
            NullLogger<MapManagerViewModel>.Instance,
            localizationService: _mockLocalizationService.Object);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _viewModel.Dispose();
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort cleanup
            }
        }
    }

    /// <summary>
    /// Verifies that AddMapPackToProfileAsync does nothing when the passed mapPack is null.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AddMapPackToProfile_WhenMapPackIsNull_DoesNothingAsync()
    {
        // Act
        await _viewModel.AddMapPackToProfileCommand.ExecuteAsync(null);

        // Assert
        _mockNotificationService.Verify(
            n => n.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
        _mockNotificationService.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
        _mockNotificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that AddMapPackToProfileAsync shows an info notification when operating in demo mode.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AddMapPackToProfile_WhenDemoPath_ShowsInfoNotificationAsync()
    {
        // Arrange
        _mockDirectoryService
            .Setup(d => d.GetMapDirectory(It.IsAny<GameType>()))
            .Returns(@"C:\Users\DemoUser\" + MapManagerConstants.WindowsMockPathSegment + @"\Maps");

        var mapPack = new MapPack
        {
            Id = ManifestId.Create("1.0.local.mappack.demo"),
            Name = "Demo MapPack",
        };

        // Act
        await _viewModel.AddMapPackToProfileCommand.ExecuteAsync(mapPack);

        // Assert
        _mockNotificationService.Verify(
            n => n.ShowInfo("Add to Profile", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that AddMapPackToProfileAsync shows a warning notification when the MapPack manifest ID is missing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AddMapPackToProfile_WhenIdIsEmpty_ShowsWarningAsync()
    {
        // Arrange
        var mapPack = new MapPack
        {
            Id = default,
            Name = "Invalid MapPack",
        };

        // Act
        await _viewModel.AddMapPackToProfileCommand.ExecuteAsync(mapPack);

        // Assert
        _mockNotificationService.Verify(
            n => n.ShowWarning("Add to Profile", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that AddMapPackToProfileAsync shows an error when ProfileSelectionViewModel cannot be created.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AddMapPackToProfile_WhenFactoryReturnsNull_ShowsErrorAsync()
    {
        // Arrange
        _viewModel.ProfileSelectionViewModelFactory = () => null!;

        var mapPack = new MapPack
        {
            Id = ManifestId.Create("1.0.local.mappack.test"),
            Name = "Test MapPack",
        };

        // Act
        await _viewModel.AddMapPackToProfileCommand.ExecuteAsync(mapPack);

        // Assert
        _mockNotificationService.Verify(
            n => n.ShowError("Profile Selection", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that AddMapPackToProfileAsync configures ProfileSelectionViewModel and closes the modal on success.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AddMapPackToProfile_WhenSuccessful_ClosesMapPackPanelAsync()
    {
        // Arrange
        var zhProfile = new GameProfile
        {
            Id = "zh-profile-1",
            Name = "ZH Profile",
            GameClient = new GameClient { GameType = GameType.ZeroHour },
        };

        _mockProfileManager
            .Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([zhProfile]));

        _mockManifestPool
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var profileVm = new ProfileSelectionViewModel(
            NullLogger<ProfileSelectionViewModel>.Instance,
            _mockProfileManager.Object,
            _mockProfileContentService.Object,
            _mockManifestPool.Object,
            _mockNotificationService.Object);

        _viewModel.ProfileSelectionViewModelFactory = () => profileVm;

        var dialogShown = false;
        _viewModel.ShowProfileSelectionDialogHandler = vm =>
        {
            dialogShown = true;
            Assert.Equal("Test Pack", vm.ContentName);
            Assert.Equal("1.0.local.mappack.test", vm.ContentManifestId);
            Assert.Equal(GameType.ZeroHour, vm.TargetGame);
            vm.WasSuccessful = true;
            vm.SelectedProfileName = "ZH Profile";
            return Task.FromResult(true);
        };

        var mapPack = new MapPack
        {
            Id = ManifestId.Create("1.0.local.mappack.test"),
            Name = "Test Pack",
            TargetGame = GameType.ZeroHour,
        };

        _viewModel.IsMapPackPanelOpen = true;

        // Act
        await _viewModel.AddMapPackToProfileCommand.ExecuteAsync(mapPack);

        // Assert
        Assert.True(dialogShown);
        Assert.False(_viewModel.IsMapPackPanelOpen);
    }

    /// <summary>
    /// Verifies that CreateAndAddMapPackToProfileAsync warns when input is invalid.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateAndAddMapPackToProfile_WhenInputInvalid_ShowsWarningAsync()
    {
        // Arrange: empty name and empty maps
        _viewModel.NewMapPackName = string.Empty;

        // Act
        await _viewModel.CreateAndAddMapPackToProfileCommand.ExecuteAsync(null);

        // Assert
        _mockNotificationService.Verify(
            n => n.ShowWarning("Invalid Input", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that CreateAndAddMapPackToProfileAsync shows an error when CAS creation fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateAndAddMapPackToProfile_WhenCreationFails_ShowsErrorAsync()
    {
        // Arrange
        _viewModel.NewMapPackName = "Failed Pack";
        _viewModel.SelectedMaps.Add(new MapFile
        {
            FileName = "Map1.map",
            FullPath = Path.Combine(_tempDirectory, "Map1.map"),
            SizeBytes = 1024,
            GameType = GameType.ZeroHour,
            LastModified = DateTime.UtcNow,
        });

        _mockMapPackService
            .Setup(s => s.CreateCasMapPackAsync(
                It.IsAny<string>(),
                It.IsAny<GameType>(),
                It.IsAny<IEnumerable<MapFile>>(),
                It.IsAny<IProgress<ContentStorageProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateFailure("Storage error"));

        // Act
        await _viewModel.CreateAndAddMapPackToProfileCommand.ExecuteAsync(null);

        // Assert
        _mockNotificationService.Verify(
            n => n.ShowError("Creation Failed", "Storage error", It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that CreateAndAddMapPackToProfileAsync creates the MapPack and opens profile selection on success.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateAndAddMapPackToProfile_WhenCreationSucceeds_OpensProfileSelectionAsync()
    {
        // Arrange
        _viewModel.NewMapPackName = "New Pack";
        _viewModel.SelectedMaps.Add(new MapFile
        {
            FileName = "Map1.map",
            FullPath = Path.Combine(_tempDirectory, "Map1.map"),
            SizeBytes = 2048,
            GameType = GameType.ZeroHour,
            LastModified = DateTime.UtcNow,
        });

        var createdManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.local.mappack.new-pack"),
            Name = "New Pack",
            ContentType = GenHub.Core.Models.Enums.ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
        };

        _mockMapPackService
            .Setup(s => s.CreateCasMapPackAsync(
                It.IsAny<string>(),
                It.IsAny<GameType>(),
                It.IsAny<IEnumerable<MapFile>>(),
                It.IsAny<IProgress<ContentStorageProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(createdManifest));

        _mockMapPackService
            .Setup(s => s.GetAllMapPacksAsync())
            .ReturnsAsync(new List<MapPack>());

        var profileVm = new ProfileSelectionViewModel(
            NullLogger<ProfileSelectionViewModel>.Instance,
            _mockProfileManager.Object,
            _mockProfileContentService.Object,
            _mockManifestPool.Object,
            _mockNotificationService.Object);

        _viewModel.ProfileSelectionViewModelFactory = () => profileVm;

        var dialogShown = false;
        _viewModel.ShowProfileSelectionDialogHandler = vm =>
        {
            dialogShown = true;
            Assert.Equal("New Pack", vm.ContentName);
            Assert.Equal("1.0.local.mappack.new-pack", vm.ContentManifestId);
            return Task.FromResult(true);
        };

        // Act
        await _viewModel.CreateAndAddMapPackToProfileCommand.ExecuteAsync(null);

        // Assert
        Assert.True(dialogShown);
        Assert.Equal(string.Empty, _viewModel.NewMapPackName);
    }
}
