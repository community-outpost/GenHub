using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameClients;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.GameClients;

/// <summary>
/// Unit tests for <see cref="GameClientDetector"/>.
/// </summary>
public class GameClientDetectorTests : IDisposable
{
    private readonly Mock<IManifestGenerationService> _manifestGenerationServiceMock;
    private readonly Mock<IContentManifestPool> _contentManifestPoolMock;
    private readonly Mock<IFileHashProvider> _hashProviderMock;
    private readonly Mock<IGameClientHashRegistry> _hashRegistryMock;
    private readonly GameClientDetector _detector;
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameClientDetectorTests"/> class.
    /// </summary>
    public GameClientDetectorTests()
    {
        _manifestGenerationServiceMock = new Mock<IManifestGenerationService>();
        _contentManifestPoolMock = new Mock<IContentManifestPool>();
        _hashProviderMock = new Mock<IFileHashProvider>();
        _hashRegistryMock = new Mock<IGameClientHashRegistry>();

        // Setup hash registry to return possible executable names
        _hashRegistryMock.Setup(x => x.PossibleExecutableNames)
            .Returns(new List<string>
            {
                "generals.exe",
                "generalsonlinezh_30.exe",
                "generalsonlinezh_60.exe",
            }.AsReadOnly());

        // Setup hash registry to return version from hash
        _hashRegistryMock.Setup(x => x.GetVersionFromHash(GameClientHashRegistry.Generals108HashPublic, GameType.Generals))
            .Returns("1.08");
        _hashRegistryMock.Setup(x => x.GetVersionFromHash(GameClientHashRegistry.ZeroHour105HashPublic, GameType.ZeroHour))
            .Returns("1.05");
        _hashRegistryMock.Setup(x => x.GetVersionFromHash(It.IsNotIn(GameClientHashRegistry.Generals108HashPublic, GameClientHashRegistry.ZeroHour105HashPublic), It.IsAny<GameType>()))
            .Returns("Unknown");

        _detector = new GameClientDetector(
            _manifestGenerationServiceMock.Object,
            _contentManifestPoolMock.Object,
            _hashProviderMock.Object,
            _hashRegistryMock.Object,
            NullLogger<GameClientDetector>.Instance);
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Tests that DetectGameClientsFromInstallationsAsync correctly detects Generals clients.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectGameClientsFromInstallationsAsync_WithGeneralsInstallation_DetectsGeneralsClient()
    {
        // Arrange
        var generalsPath = Path.Combine(_tempDirectory, "Generals");
        Directory.CreateDirectory(generalsPath);
        var executablePath = Path.Combine(generalsPath, "generals.exe");
        await File.WriteAllTextAsync(executablePath, "dummy content");

        var installation = new GameInstallation("C:\\TestInstall", GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = generalsPath,
        };

        var installations = new List<GameInstallation> { installation };

        // Setup hash provider to return known Generals hash
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(executablePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameClientHashRegistry.Generals108HashPublic);

        // Setup manifest generation
        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var manifest = new ContentManifest { Id = ManifestId.Create("1.108.steam.gameclient.generals") };
        manifestBuilderMock.Setup(x => x.Build()).Returns(manifest);

        _manifestGenerationServiceMock.Setup(x => x.CreateGameClientManifestAsync(
                It.IsAny<string>(), It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _detector.DetectGameClientsFromInstallationsAsync(installations);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Items);
        var client = result.Items.First();
        Assert.Equal(GameType.Generals, client.GameType);
        Assert.Equal("1.08", client.Version);
        Assert.Equal(executablePath, client.ExecutablePath);
        Assert.Equal(generalsPath, client.WorkingDirectory);
    }

    /// <summary>
    /// Tests that DetectGameClientsFromInstallationsAsync correctly detects Zero Hour clients.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectGameClientsFromInstallationsAsync_WithZeroHourInstallation_DetectsZeroHourClient()
    {
        // Arrange
        var zeroHourPath = Path.Combine(_tempDirectory, "ZeroHour");
        Directory.CreateDirectory(zeroHourPath);
        var executablePath = Path.Combine(zeroHourPath, "generals.exe");
        await File.WriteAllTextAsync(executablePath, "dummy content");

        var installation = new GameInstallation("C:\\TestInstall", GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = zeroHourPath,
        };

        var installations = new List<GameInstallation> { installation };

        // Setup hash provider to return known Zero Hour hash
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(executablePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameClientHashRegistry.ZeroHour105HashPublic);

        // Setup manifest generation
        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var manifest = new ContentManifest { Id = ManifestId.Create("1.105.steam.gameclient.zerohour") };
        manifestBuilderMock.Setup(x => x.Build()).Returns(manifest);

        _manifestGenerationServiceMock.Setup(x => x.CreateGameClientManifestAsync(
                It.IsAny<string>(), It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _detector.DetectGameClientsFromInstallationsAsync(installations);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Items);
        var client = result.Items.First();
        Assert.Equal(GameType.ZeroHour, client.GameType);
        Assert.Equal("1.05", client.Version);
        Assert.Equal(executablePath, client.ExecutablePath);
        Assert.Equal(zeroHourPath, client.WorkingDirectory);
    }

    /// <summary>
    /// Tests that ScanDirectoryForGameClientsAsync can find game clients in directories.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanDirectoryForGameClientsAsync_WithValidExecutable_FindsGameClient()
    {
        // Arrange
        var gameDir = Path.Combine(_tempDirectory, "TestGame");
        Directory.CreateDirectory(gameDir);
        var executablePath = Path.Combine(gameDir, "generals.exe");
        await File.WriteAllTextAsync(executablePath, "dummy content");

        // Setup hash provider to return known hash
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(executablePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameClientHashRegistry.Generals108HashPublic);

        // Setup manifest generation
        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var manifest = new ContentManifest { Id = ManifestId.Create("1.108.steam.gameclient.scannedgenerals") };
        manifestBuilderMock.Setup(x => x.Build()).Returns(manifest);

        _manifestGenerationServiceMock.Setup(x => x.CreateGameClientManifestAsync(
                It.IsAny<string>(), It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _detector.ScanDirectoryForGameClientsAsync(_tempDirectory);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Items);
        var client = result.Items.First();
        Assert.Equal(GameType.Generals, client.GameType);
        Assert.Equal("1.08", client.Version);
        Assert.Equal(executablePath, client.ExecutablePath);
        Assert.Contains("Scanned Generals 1.08", client.Name);
    }

    /// <summary>
    /// Tests that ScanDirectoryForGameClientsAsync handles non-existent directories gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanDirectoryForGameClientsAsync_WithNonExistentDirectory_ReturnsFailure()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDirectory, "NonExistent");

        // Act
        var result = await _detector.ScanDirectoryForGameClientsAsync(nonExistentPath);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Directory does not exist", result.Errors.First());
    }

    /// <summary>
    /// Tests that ValidateGameClientAsync returns true for valid clients.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ValidateGameClientAsync_WithValidClient_ReturnsTrue()
    {
        // Arrange
        var executablePath = Path.Combine(_tempDirectory, "generals.exe");
        await File.WriteAllTextAsync(executablePath, "dummy content");

        var client = new GameClient
        {
            ExecutablePath = executablePath,
        };

        // Act
        var result = await _detector.ValidateGameClientAsync(client);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Tests that ValidateGameClientAsync returns false for clients with non-existent executables.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ValidateGameClientAsync_WithInvalidClient_ReturnsFalse()
    {
        // Arrange
        var client = new GameClient
        {
            ExecutablePath = Path.Combine(_tempDirectory, "nonexistent.exe"),
        };

        // Act
        var result = await _detector.ValidateGameClientAsync(client);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Tests that the detector handles unknown hashes gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanDirectoryForGameClientsAsync_WithUnknownHash_CreatesUnknownClient()
    {
        // Arrange
        var gameDir = Path.Combine(_tempDirectory, "UnknownGame");
        Directory.CreateDirectory(gameDir);
        var executablePath = Path.Combine(gameDir, "generals.exe");
        await File.WriteAllTextAsync(executablePath, "dummy content");

        // Setup hash provider to return unknown hash
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(executablePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync("unknown_hash_12345");

        // Setup manifest generation
        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var manifest = new ContentManifest { Id = ManifestId.Create("1.0.genhub.gameclient.unknownclient") };
        manifestBuilderMock.Setup(x => x.Build()).Returns(manifest);

        _manifestGenerationServiceMock.Setup(x => x.CreateGameClientManifestAsync(
                It.IsAny<string>(), It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _detector.ScanDirectoryForGameClientsAsync(_tempDirectory);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Items);
        var client = result.Items.First();
        Assert.Equal(GameType.Generals, client.GameType); // Default assumption
        Assert.Equal("Unknown", client.Version);
        Assert.Equal(executablePath, client.ExecutablePath);
        Assert.Contains("Unknown Game", client.Name);
    }

    /// <summary>
    /// Tests that DetectGameClientsFromInstallationsAsync detects GeneralsOnline 30Hz client.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectGameClientsFromInstallationsAsync_WithGeneralsOnline30HzExecutable_DetectsClient()
    {
        // Arrange
        var generalsPath = Path.Combine(_tempDirectory, "Generals");
        Directory.CreateDirectory(generalsPath);

        var generalsOnlineExePath = Path.Combine(generalsPath, "generalsonlinezh_30.exe");
        await File.WriteAllTextAsync(generalsOnlineExePath, "dummy content");

        // Also create standard executable for the installation client
        var standardExePath = Path.Combine(generalsPath, "generals.exe");
        await File.WriteAllTextAsync(standardExePath, "dummy content");

        var installation = new GameInstallation("C:\\TestInstall", GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = generalsPath,
        };

        var installations = new List<GameInstallation> { installation };

        // Setup hash provider
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(standardExePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameClientHashRegistry.Generals108HashPublic);
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(generalsOnlineExePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync("any_hash"); // GeneralsOnline doesn't use hash

        // Setup manifest generation
        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var generalsOnlineManifest = new ContentManifest { Id = ManifestId.Create("1.0.generalsonline.gameclient.generals-generalsonline-30hz") };
        manifestBuilderMock.Setup(x => x.Build()).Returns(generalsOnlineManifest);

        _manifestGenerationServiceMock.Setup(
                x => x.CreateGeneralsOnlineClientManifestAsync(
                    generalsPath,
                    GameType.Generals,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    generalsOnlineExePath))
            .ReturnsAsync(manifestBuilderMock.Object);

        _manifestGenerationServiceMock.Setup(
                x => x.CreateGameClientManifestAsync(
                    generalsPath,
                    GameType.Generals,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    standardExePath))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _detector.DetectGameClientsFromInstallationsAsync(installations);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Items.Count); // GeneralsOnline 30Hz + standard Generals client

        var generalsOnlineClient = result.Items.FirstOrDefault(c => c.Name.Contains("GeneralsOnline"));
        Assert.NotNull(generalsOnlineClient);
        Assert.Equal(GameType.Generals, generalsOnlineClient.GameType);
        Assert.Equal("Auto-Updated", generalsOnlineClient.Version); // GeneralsOnline clients auto-update
        Assert.Equal(generalsOnlineExePath, generalsOnlineClient.ExecutablePath);
        Assert.Contains("30Hz", generalsOnlineClient.Name);
    }

    /// <summary>
    /// Tests that DetectGameClientsFromInstallationsAsync detects GeneralsOnline 60Hz client.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectGameClientsFromInstallationsAsync_WithGeneralsOnline60HzExecutable_DetectsClient()
    {
        // Arrange
        var zeroHourPath = Path.Combine(_tempDirectory, "ZeroHour");
        Directory.CreateDirectory(zeroHourPath);

        var generalsOnlineExePath = Path.Combine(zeroHourPath, "generalsonlinezh_60.exe");
        await File.WriteAllTextAsync(generalsOnlineExePath, "dummy content");

        // Also create standard executable
        var standardExePath = Path.Combine(zeroHourPath, "generals.exe");
        await File.WriteAllTextAsync(standardExePath, "dummy content");

        var installation = new GameInstallation("C:\\TestInstall", GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = zeroHourPath,
        };

        var installations = new List<GameInstallation> { installation };

        // Setup hash provider
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(standardExePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameClientHashRegistry.ZeroHour105HashPublic);

        // Setup manifest generation
        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var generalsOnlineManifest = new ContentManifest { Id = ManifestId.Create("1.0.generalsonline.gameclient.zerohour-generalsonline-60hz") };
        manifestBuilderMock.Setup(x => x.Build()).Returns(generalsOnlineManifest);

        _manifestGenerationServiceMock.Setup(
                x => x.CreateGeneralsOnlineClientManifestAsync(
                    zeroHourPath,
                    GameType.ZeroHour,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    generalsOnlineExePath))
            .ReturnsAsync(manifestBuilderMock.Object);

        _manifestGenerationServiceMock.Setup(
                x => x.CreateGameClientManifestAsync(
                    zeroHourPath,
                    GameType.ZeroHour,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    standardExePath))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _detector.DetectGameClientsFromInstallationsAsync(installations);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Items.Count); // GeneralsOnline 60Hz + standard Zero Hour client

        var generalsOnlineClient = result.Items.FirstOrDefault(c => c.Name.Contains("GeneralsOnline"));
        Assert.NotNull(generalsOnlineClient);
        Assert.Equal(GameType.ZeroHour, generalsOnlineClient.GameType);
        Assert.Equal("Auto-Updated", generalsOnlineClient.Version); // GeneralsOnline clients auto-update
        Assert.Equal(generalsOnlineExePath, generalsOnlineClient.ExecutablePath);
        Assert.Contains("60Hz", generalsOnlineClient.Name);
    }

    /// <summary>
    /// Tests that DetectGameClientsFromInstallationsAsync detects multiple GeneralsOnline variants.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectGameClientsFromInstallationsAsync_WithMultipleGeneralsOnlineVariants_DetectsAllClients()
    {
        // Arrange
        var generalsPath = Path.Combine(_tempDirectory, "GeneralsMultiple");
        Directory.CreateDirectory(generalsPath);

        var generalsonline30HzPath = Path.Combine(generalsPath, "generalsonlinezh_30.exe");
        var generalsonline60HzPath = Path.Combine(generalsPath, "generalsonlinezh_60.exe");
        var standardExePath = Path.Combine(generalsPath, "generals.exe");

        await File.WriteAllTextAsync(generalsonline30HzPath, "dummy");
        await File.WriteAllTextAsync(generalsonline60HzPath, "dummy");
        await File.WriteAllTextAsync(standardExePath, "dummy");

        var installation = new GameInstallation("C:\\TestInstall", GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = generalsPath,
        };

        var installations = new List<GameInstallation> { installation };

        // Setup hash provider
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(standardExePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameClientHashRegistry.Generals108HashPublic);

        // Setup manifest generation for all variants
        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var manifest = new ContentManifest { Id = ManifestId.Create("1.108.steam.gameclient.generalsonline") };
        manifestBuilderMock.Setup(x => x.Build()).Returns(manifest);

        _manifestGenerationServiceMock.Setup(
                x => x.CreateGeneralsOnlineClientManifestAsync(
                    generalsPath,
                    GameType.Generals,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _manifestGenerationServiceMock.Setup(
                x => x.CreateGameClientManifestAsync(
                    generalsPath,
                    GameType.Generals,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    standardExePath))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _detector.DetectGameClientsFromInstallationsAsync(installations);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.Items.Count); // 2 GeneralsOnline variants (30Hz, 60Hz) + 1 standard client

        var generalsOnlineClients = result.Items.Where(c => c.Name.Contains("GeneralsOnline")).ToList();
        Assert.Equal(2, generalsOnlineClients.Count);

        Assert.Single(generalsOnlineClients, c => c.Name.Contains("30Hz"));
        Assert.Single(generalsOnlineClients, c => c.Name.Contains("60Hz"));
    }

    /// <summary>
    /// Tests that GeneralsOnline manifest is generated with correct publisher info.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectGameClientsFromInstallationsAsync_GeneralsOnlineManifestHasCorrectPublisher_VerifiesPublisherInfo()
    {
        // Arrange
        var generalsPath = Path.Combine(_tempDirectory, "GeneralsPublisher");
        Directory.CreateDirectory(generalsPath);

        var generalsOnlineExePath = Path.Combine(generalsPath, "generalsonlinezh_30.exe");
        await File.WriteAllTextAsync(generalsOnlineExePath, "dummy");

        var standardExePath = Path.Combine(generalsPath, "generals.exe");
        await File.WriteAllTextAsync(standardExePath, "dummy");

        var installation = new GameInstallation("C:\\TestInstall", GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = generalsPath,
        };

        var installations = new List<GameInstallation> { installation };

        // Setup hash provider
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(standardExePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameClientHashRegistry.Generals108HashPublic);

        // Setup manifest generation to capture the call
        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.generalsonline.gameclient.generals-generalsonline-30hz"),
            Publisher = new PublisherInfo { Name = PublisherTypeConstants.GeneralsOnline, },
        };
        manifestBuilderMock.Setup(x => x.Build()).Returns(manifest);

        _manifestGenerationServiceMock.Setup(
                x => x.CreateGeneralsOnlineClientManifestAsync(
                    generalsPath,
                    GameType.Generals,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    generalsOnlineExePath))
            .ReturnsAsync(manifestBuilderMock.Object);

        _manifestGenerationServiceMock.Setup(
                x => x.CreateGameClientManifestAsync(
                    generalsPath,
                    GameType.Generals,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    standardExePath))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _detector.DetectGameClientsFromInstallationsAsync(installations);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.Items);

        // Verify CreateGeneralsOnlineClientManifestAsync was called with correct parameters
        _manifestGenerationServiceMock.Verify(
            x => x.CreateGeneralsOnlineClientManifestAsync(
                generalsPath,
                GameType.Generals,
                It.IsAny<string>(),
                "Auto-Updated", // GeneralsOnline clients auto-update
                generalsOnlineExePath),
            Times.Once);
    }

    /// <summary>
    /// Tests that GeneralsOnline detection skips missing executable files gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectGameClientsFromInstallationsAsync_WithMissingGeneralsOnlineExecutable_SkipsAndContinues()
    {
        // Arrange - create installation with only standard executable, no GeneralsOnline
        var generalsPath = Path.Combine(_tempDirectory, "GeneralsNoGeneralsOnline");
        Directory.CreateDirectory(generalsPath);

        var standardExePath = Path.Combine(generalsPath, "generals.exe");
        await File.WriteAllTextAsync(standardExePath, "dummy");

        var installation = new GameInstallation("C:\\TestInstall", GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = generalsPath,
        };

        var installations = new List<GameInstallation> { installation };

        // Setup hash provider
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(standardExePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameClientHashRegistry.Generals108HashPublic);

        // Setup manifest generation
        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var manifest = new ContentManifest { Id = ManifestId.Create("1.108.steam.gameclient.generals") };
        manifestBuilderMock.Setup(x => x.Build()).Returns(manifest);

        _manifestGenerationServiceMock.Setup(
                x => x.CreateGameClientManifestAsync(
                    generalsPath,
                    GameType.Generals,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    standardExePath))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _detector.DetectGameClientsFromInstallationsAsync(installations);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Items); // Only standard client, no GeneralsOnline
        Assert.DoesNotContain(result.Items, c => c.Name.Contains("GeneralsOnline"));

        // Verify CreateGeneralsOnlineClientManifestAsync was NOT called (no GeneralsOnline files)
        _manifestGenerationServiceMock.Verify(
            x => x.CreateGeneralsOnlineClientManifestAsync(
                It.IsAny<string>(),
                It.IsAny<GameType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }
}
