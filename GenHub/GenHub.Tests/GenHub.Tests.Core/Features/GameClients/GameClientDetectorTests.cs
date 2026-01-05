using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
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
    private static readonly IReadOnlyList<string> PossibleExecutableNames = [GameClientConstants.GeneralsExecutable, GameClientConstants.GeneralsOnline30HzExecutable, GameClientConstants.GeneralsOnline60HzExecutable];
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
            .Returns(PossibleExecutableNames);

        // Setup hash registry to return version from hash
        _hashRegistryMock.Setup(x => x.GetVersionFromHash(GameClientHashRegistry.Generals108HashPublic, GameType.Generals))
            .Returns("1.08");
        _hashRegistryMock.Setup(x => x.GetVersionFromHash(GameClientHashRegistry.ZeroHour105HashPublic, GameType.ZeroHour))
            .Returns("1.05");
        _hashRegistryMock.Setup(x => x.GetVersionFromHash(It.IsNotIn(GameClientHashRegistry.Generals108HashPublic, GameClientHashRegistry.ZeroHour105HashPublic), It.IsAny<GameType>()))
            .Returns(GameClientConstants.UnknownVersion);

        _detector = new GameClientDetector(
            _manifestGenerationServiceMock.Object,
            _contentManifestPoolMock.Object,
            _hashProviderMock.Object,
            _hashRegistryMock.Object,
            [],
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

        List<GameInstallation> installations = [installation];

        // Setup hash provider to return known Generals hash
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(executablePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameClientHashRegistry.Generals108HashPublic);

        // Setup manifest generation
        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var manifest = new ContentManifest { Id = ManifestId.Create("1.108.steam.gameclient.generals") };
        manifestBuilderMock.Setup(x => x.Build()).Returns(manifest);

        _manifestGenerationServiceMock.Setup(x => x.CreateGameClientManifestAsync(
                generalsPath, GameType.Generals, It.IsAny<string>(), It.IsAny<string>(), executablePath, It.IsAny<PublisherInfo?>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _detector.DetectGameClientsFromInstallationsAsync(installations);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Items);
        var client = result.Items[0];
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

        List<GameInstallation> installations = [installation];

        // Setup hash provider to return known Zero Hour hash
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(executablePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameClientHashRegistry.ZeroHour105HashPublic);

        // Setup manifest generation
        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var manifest = new ContentManifest { Id = ManifestId.Create("1.105.steam.gameclient.zerohour") };
        manifestBuilderMock.Setup(x => x.Build()).Returns(manifest);

        _manifestGenerationServiceMock.Setup(x => x.CreateGameClientManifestAsync(
                It.IsAny<string>(), It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PublisherInfo?>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _detector.DetectGameClientsFromInstallationsAsync(installations);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Items);
        var client = result.Items[0];
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
                It.IsAny<string>(), It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PublisherInfo?>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _detector.ScanDirectoryForGameClientsAsync(_tempDirectory);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Items);
        var client = result.Items[0];
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
        Assert.Contains("Directory does not exist", result.Errors[0]);
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
                It.IsAny<string>(), It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PublisherInfo?>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _detector.ScanDirectoryForGameClientsAsync(_tempDirectory);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Items);
        var client = result.Items[0];
        Assert.Equal(GameType.Generals, client.GameType); // Default assumption
        Assert.Equal(GameClientConstants.UnknownVersion, client.Version);
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
        // Arrange - Create identifier for GeneralsOnline 30Hz
        var generalsOnlineIdentifierMock = new Mock<IGameClientIdentifier>();
        generalsOnlineIdentifierMock.Setup(x => x.PublisherId).Returns(PublisherTypeConstants.GeneralsOnline);
        generalsOnlineIdentifierMock.Setup(x => x.CanIdentify(It.Is<string>(p => p.Contains(GameClientConstants.GeneralsOnline30HzExecutable)))).Returns(true);
        generalsOnlineIdentifierMock.Setup(x => x.CanIdentify(It.Is<string>(p => !p.Contains(GameClientConstants.GeneralsOnline30HzExecutable)))).Returns(false);
        generalsOnlineIdentifierMock.Setup(x => x.Identify(It.IsAny<string>())).Returns(new GameClientIdentification(
            PublisherTypeConstants.GeneralsOnline,
            "30Hz",
            "GeneralsOnline 30Hz",
            GameType.Generals,
            GameClientConstants.UnknownVersion));

        // Create detector with the identifier
        var detectorWith30HzIdentifier = new GameClientDetector(
            _manifestGenerationServiceMock.Object,
            _contentManifestPoolMock.Object,
            _hashProviderMock.Object,
            _hashRegistryMock.Object,
            [generalsOnlineIdentifierMock.Object],
            NullLogger<GameClientDetector>.Instance);

        var generalsPath = Path.Combine(_tempDirectory, "Generals");
        Directory.CreateDirectory(generalsPath);

        var generalsOnlineExePath = Path.Combine(generalsPath, GameClientConstants.GeneralsOnline30HzExecutable);
        await File.WriteAllTextAsync(generalsOnlineExePath, "dummy content");

        // Also create standard executable for the installation client
        var standardExePath = Path.Combine(generalsPath, GameClientConstants.GeneralsExecutable);
        await File.WriteAllTextAsync(standardExePath, "dummy content");

        var installation = new GameInstallation("C:\\TestInstall", GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = generalsPath,
        };

        List<GameInstallation> installations = [installation];

        // Setup hash provider
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(standardExePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameClientHashRegistry.Generals108HashPublic);
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(generalsOnlineExePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync("any_hash"); // GeneralsOnline doesn't use hash

        // Setup manifest generation
        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var generalsOnlineManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.generalsonline.gameclient.generals-generalsonline-30hz"),
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GeneralsOnline },
        };
        manifestBuilderMock.Setup(x => x.Build()).Returns(generalsOnlineManifest);

        var standardGeneralsManifestBuilder = new Mock<IContentManifestBuilder>();
        var standardGeneralsManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.108.steam.gameclient.generals"),
        };
        standardGeneralsManifestBuilder.Setup(x => x.Build()).Returns(standardGeneralsManifest);

        _manifestGenerationServiceMock.Setup(
                x => x.CreateGameClientManifestAsync(
                    generalsPath,
                    GameType.Generals,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    standardExePath,
                    It.IsAny<PublisherInfo?>()))
            .ReturnsAsync(standardGeneralsManifestBuilder.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await detectorWith30HzIdentifier.DetectGameClientsFromInstallationsAsync(installations);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Items.Count);

        var generalsOnlineClient = result.Items.FirstOrDefault(c => c.Name.Contains("GeneralsOnline"));
        Assert.NotNull(generalsOnlineClient);
        Assert.Equal(GameType.Generals, generalsOnlineClient.GameType);
        Assert.Equal(GameClientConstants.UnknownVersion, generalsOnlineClient.Version); // GeneralsOnline clients auto-update

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
        // Arrange - Create identifier for GeneralsOnline 60Hz
        var generalsOnlineIdentifierMock = new Mock<IGameClientIdentifier>();
        generalsOnlineIdentifierMock.Setup(x => x.PublisherId).Returns(PublisherTypeConstants.GeneralsOnline);
        generalsOnlineIdentifierMock.Setup(x => x.CanIdentify(It.Is<string>(p => p.Contains("generalsonlinezh_60.exe")))).Returns(true);
        generalsOnlineIdentifierMock.Setup(x => x.CanIdentify(It.Is<string>(p => !p.Contains("generalsonlinezh_60.exe")))).Returns(false);
        generalsOnlineIdentifierMock.Setup(x => x.Identify(It.IsAny<string>())).Returns(new GameClientIdentification(
            PublisherTypeConstants.GeneralsOnline,
            "60Hz",
            "GeneralsOnline 60Hz",
            GameType.ZeroHour,
            GameClientConstants.UnknownVersion));

        // Create detector with the identifier
        var detectorWith60HzIdentifier = new GameClientDetector(
            _manifestGenerationServiceMock.Object,
            _contentManifestPoolMock.Object,
            _hashProviderMock.Object,
            _hashRegistryMock.Object,
            [generalsOnlineIdentifierMock.Object],
            NullLogger<GameClientDetector>.Instance);

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

        List<GameInstallation> installations = [installation];

        // Setup hash provider
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(standardExePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameClientHashRegistry.ZeroHour105HashPublic);

        // Setup manifest generation
        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var generalsOnlineManifest = new ContentManifest { Id = ManifestId.Create("1.0.generalsonline.gameclient.zerohour-generalsonline-60hz"), Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GeneralsOnline } };
        manifestBuilderMock.Setup(x => x.Build()).Returns(generalsOnlineManifest);

        _manifestGenerationServiceMock.Setup(
                x => x.CreateGameClientManifestAsync(
                    zeroHourPath,
                    GameType.ZeroHour,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    standardExePath,
                    It.IsAny<PublisherInfo?>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await detectorWith60HzIdentifier.DetectGameClientsFromInstallationsAsync(installations);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Items.Count); // GeneralsOnline 60Hz + standard Zero Hour client

        var generalsOnlineClient = result.Items.FirstOrDefault(c => c.Name.Contains("GeneralsOnline"));
        Assert.NotNull(generalsOnlineClient);
        Assert.Equal(GameType.ZeroHour, generalsOnlineClient.GameType);
        Assert.Equal(GameClientConstants.UnknownVersion, generalsOnlineClient.Version); // GeneralsOnline clients auto-update

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
        // Arrange - Create identifiers for both 30Hz and 60Hz
        var identifier30HzMock = new Mock<IGameClientIdentifier>();
        identifier30HzMock.Setup(x => x.PublisherId).Returns(PublisherTypeConstants.GeneralsOnline);
        identifier30HzMock.Setup(x => x.CanIdentify(It.Is<string>(p => p.Contains(GameClientConstants.GeneralsOnline30HzExecutable)))).Returns(true);
        identifier30HzMock.Setup(x => x.CanIdentify(It.Is<string>(p => !p.Contains(GameClientConstants.GeneralsOnline30HzExecutable)))).Returns(false);
        identifier30HzMock.Setup(x => x.Identify(It.IsAny<string>())).Returns(new GameClientIdentification(
            PublisherTypeConstants.GeneralsOnline,
            "30Hz",
            "GeneralsOnline 30Hz",
            GameType.Generals,
            GameClientConstants.UnknownVersion));

        var identifier60HzMock = new Mock<IGameClientIdentifier>();
        identifier60HzMock.Setup(x => x.PublisherId).Returns(PublisherTypeConstants.GeneralsOnline);
        identifier60HzMock.Setup(x => x.CanIdentify(It.Is<string>(p => p.Contains(GameClientConstants.GeneralsOnline60HzExecutable)))).Returns(true);
        identifier60HzMock.Setup(x => x.CanIdentify(It.Is<string>(p => !p.Contains(GameClientConstants.GeneralsOnline60HzExecutable)))).Returns(false);
        identifier60HzMock.Setup(x => x.Identify(It.IsAny<string>())).Returns(new GameClientIdentification(
            PublisherTypeConstants.GeneralsOnline,
            "60Hz",
            "GeneralsOnline 60Hz",
            GameType.Generals,
            GameClientConstants.UnknownVersion));

        // Create detector with both identifiers
        var detectorWithMultipleIdentifiers = new GameClientDetector(
            _manifestGenerationServiceMock.Object,
            _contentManifestPoolMock.Object,
            _hashProviderMock.Object,
            _hashRegistryMock.Object,
            [identifier30HzMock.Object, identifier60HzMock.Object],
            NullLogger<GameClientDetector>.Instance);

        var generalsPath = Path.Combine(_tempDirectory, "GeneralsMultiple");
        Directory.CreateDirectory(generalsPath);

        var generalsonline30HzPath = Path.Combine(generalsPath, GameClientConstants.GeneralsOnline30HzExecutable);
        var generalsonline60HzPath = Path.Combine(generalsPath, GameClientConstants.GeneralsOnline60HzExecutable);
        var standardExePath = Path.Combine(generalsPath, GameClientConstants.GeneralsExecutable);

        await File.WriteAllTextAsync(generalsonline30HzPath, "dummy");
        await File.WriteAllTextAsync(generalsonline60HzPath, "dummy");
        await File.WriteAllTextAsync(standardExePath, "dummy");

        var installation = new GameInstallation("C:\\TestInstall", GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = generalsPath,
        };

        List<GameInstallation> installations = [installation];

        // Setup hash provider
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(standardExePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameClientHashRegistry.Generals108HashPublic);

        // Setup manifest generation for all variants
        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var manifest = new ContentManifest { Id = ManifestId.Create("1.108.steam.gameclient.generalsonline"), Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GeneralsOnline } };
        manifestBuilderMock.Setup(x => x.Build()).Returns(manifest);

        _manifestGenerationServiceMock.Setup(
                x => x.CreateGameClientManifestAsync(
                    generalsPath,
                    GameType.Generals,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<PublisherInfo?>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await detectorWithMultipleIdentifiers.DetectGameClientsFromInstallationsAsync(installations);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.Items.Count); // 2 GeneralsOnline variants (30Hz, 60Hz) + 1 standard client
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

        List<GameInstallation> installations = [installation];

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
                    It.IsAny<string>(),
                    It.IsAny<PublisherInfo?>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _detector.DetectGameClientsFromInstallationsAsync(installations);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Items); // Only standard client, no GeneralsOnline
        Assert.DoesNotContain(result.Items, c => c.Name.Contains("GeneralsOnline"));

        // Verify CreateGeneralsOnlineClientManifestAsync was NOT called (no GeneralsOnline files)
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }

        GC.SuppressFinalize(this);
    }
}