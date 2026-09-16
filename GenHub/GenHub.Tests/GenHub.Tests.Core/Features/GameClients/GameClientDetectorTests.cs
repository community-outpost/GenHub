using System.Reflection;
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
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Features.GameClients;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text;

namespace GenHub.Tests.Core.Features.GameClients;

/// <summary>
/// Unit tests for <see cref="GameClientDetector"/>.
/// </summary>
public class GameClientDetectorTests : IDisposable
{
    private static readonly IReadOnlyList<string> PossibleExecutableNames =
    [
        GameClientConstants.GeneralsExecutable,
        GameClientConstants.GeneralsOnlineEacLauncherExecutable,
        GameClientConstants.GeneralsOnline60HzExecutable,
    ];

    private static readonly byte[] MachOHeader = [0xFE, 0xED, 0xFA, 0xCE, 0x00, 0x00, 0x00, 0x00];

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

    /// <summary>Pooled publisher manifests are scoped to the requested game.</summary>
    /// <param name="gameType">The requested game or all-game sentinel.</param>
    /// <param name="expectedCount">The expected number of manifests.</param>
    /// <returns>The asynchronous operation.</returns>
    [Theory]
    [InlineData(GameType.Generals, 1)]
    [InlineData(GameType.ZeroHour, 1)]
    [InlineData(GameType.Unknown, 2)]
    public async Task GetExistingPublisherManifestsAsync_FiltersTargetGameAsync(GameType gameType, int expectedCount)
    {
        var manifests = new[] { GameType.Generals, GameType.ZeroHour }.Select(game => new ContentManifest
        {
            Id = ManifestId.Create($"1.1.generalsonline.gameclient.{game.ToString().ToLowerInvariant()}"),
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
            TargetGame = game,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GeneralsOnline },
            Files = [new ManifestFile { SourceType = ContentSourceType.ContentAddressable, Hash = "test-hash" }],
        }).ToList();
        _contentManifestPoolMock.Setup(pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifests));
        var method = typeof(GameClientDetector).GetMethod("GetExistingPublisherManifestsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = await (Task<List<ContentManifest>>)method.Invoke(
            _detector, [PublisherTypeConstants.GeneralsOnline, gameType, CancellationToken.None])!;
        Assert.Equal(expectedCount, result.Count);
        Assert.All(result, manifest => Assert.True(gameType == GameType.Unknown || manifest.TargetGame == gameType));
    }

    /// <summary>
    /// Tests that DetectGameClientsFromInstallationsAsync correctly detects Generals clients.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectGameClientsFromInstallationsAsync_WithGeneralsInstallation_DetectsGeneralsClientAsync()
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
    public async Task DetectGameClientsFromInstallationsAsync_WithZeroHourInstallation_DetectsZeroHourClientAsync()
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
    public async Task ScanDirectoryForGameClientsAsync_WithValidExecutable_FindsGameClientAsync()
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
    /// An application bundle scans as one client rooted at the bundle; the scan
    /// does not recurse inside and identify the inner binary separately.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanDirectoryForGameClientsAsync_WithAppBundle_ReportsBundleOnceAsync()
    {
        // Arrange
        var gameDir = Path.Combine(_tempDirectory, "MacGame");
        var bundleDir = Path.Combine(gameDir, "GeneralsX.app");
        var macOsDir = Path.Combine(bundleDir, "Contents", "MacOS");
        Directory.CreateDirectory(macOsDir);
        await File.WriteAllBytesAsync(Path.Combine(macOsDir, "GeneralsX"), MachOHeader);

        var identifierMock = new Mock<IGameClientIdentifier>();
        identifierMock.Setup(x => x.CanIdentify(It.IsAny<string>())).Returns(true);
        identifierMock
            .Setup(x => x.Identify(It.IsAny<string>()))
            .Returns(new GameClientIdentification(
                publisherId: "community",
                variant: "macos",
                displayName: "Community Zero Hour (macOS)",
                gameType: GameType.ZeroHour,
                localVersion: null));
        var detector = new GameClientDetector(
            _manifestGenerationServiceMock.Object,
            _contentManifestPoolMock.Object,
            _hashProviderMock.Object,
            _hashRegistryMock.Object,
            [identifierMock.Object],
            NullLogger<GameClientDetector>.Instance);

        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var manifest = new ContentManifest { Id = ManifestId.Create("1.0.test.gameclient.appbundle") };
        manifestBuilderMock.Setup(x => x.Build()).Returns(manifest);
        _manifestGenerationServiceMock.Setup(x => x.CreateGameClientManifestAsync(
                It.IsAny<string>(), It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PublisherInfo?>()))
            .ReturnsAsync(manifestBuilderMock.Object);
        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await detector.ScanDirectoryForGameClientsAsync(_tempDirectory);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Items);
        Assert.Equal(bundleDir, result.Items[0].ExecutablePath);
        Assert.Equal("1.0.test.gameclient.appbundle", result.Items[0].Id);
        _manifestGenerationServiceMock.Verify(
            x => x.CreateGameClientManifestAsync(
                It.IsAny<string>(),
                It.IsAny<GameType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                Path.Combine(macOsDir, "GeneralsX"),
                It.IsAny<PublisherInfo?>()),
            Times.Once);
        identifierMock.Verify(x => x.Identify(It.IsAny<string>()), Times.Once);
    }

    /// <summary>
    /// An arbitrarily named engine carrying Zero Hour markers is typed by sniffing even
    /// though no hash or publisher name recognizes it.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanDirectoryForGameClientsAsync_RenamedZeroHourEngine_TypesBySniffingAsync()
    {
        // Arrange
        var gameDir = Path.Combine(_tempDirectory, "Recovery");
        Directory.CreateDirectory(gameDir);
        var executablePath = Path.Combine(gameDir, "generalszh_mp-recovery.exe");
        await File.WriteAllBytesAsync(executablePath, EngineBytes(withMarkers: true));
        SetupManifestGeneration();

        // Act
        var result = await _detector.ScanDirectoryForGameClientsAsync(_tempDirectory);

        // Assert
        Assert.True(result.Success);
        var client = Assert.Single(result.Items);
        Assert.Equal(GameType.ZeroHour, client.GameType);
        Assert.Equal(executablePath, client.ExecutablePath);
        Assert.Contains("Scanned Zero Hour", client.Name);
    }

    /// <summary>
    /// Tools are excluded by file name before sniffing, even with full engine markers.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanDirectoryForGameClientsAsync_WorldBuilder_SkippedAsync()
    {
        // Arrange
        var gameDir = Path.Combine(_tempDirectory, "Tools");
        Directory.CreateDirectory(gameDir);
        await File.WriteAllBytesAsync(Path.Combine(gameDir, "WorldBuilder.exe"), EngineBytes(withMarkers: true));

        // Act
        var result = await _detector.ScanDirectoryForGameClientsAsync(_tempDirectory);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Items);
    }

    /// <summary>
    /// A broadened candidate without any evidence stays silent instead of becoming an
    /// Unknown entry.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanDirectoryForGameClientsAsync_UnevidencedExe_SkippedAsync()
    {
        // Arrange
        var gameDir = Path.Combine(_tempDirectory, "Utils");
        Directory.CreateDirectory(gameDir);
        await File.WriteAllBytesAsync(Path.Combine(gameDir, "random-tool.exe"), EngineBytes(withMarkers: false));

        // Act
        var result = await _detector.ScanDirectoryForGameClientsAsync(_tempDirectory);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Items);
    }

    /// <summary>
    /// A packed stub resolves its game type from the sibling engine while keeping the
    /// stub as the launch target.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanDirectoryForGameClientsAsync_PackedStubWithZeroHourSibling_TypesFromSiblingAsync()
    {
        // Arrange
        var gameDir = Path.Combine(_tempDirectory, "Stubbed");
        Directory.CreateDirectory(gameDir);
        var stubPath = Path.Combine(gameDir, "generals.exe");
        await File.WriteAllBytesAsync(stubPath, PackedStubBytes());
        await File.WriteAllBytesAsync(Path.Combine(gameDir, "game.dat"), EngineBytes(withMarkers: true));
        SetupManifestGeneration();

        // Act
        var result = await _detector.ScanDirectoryForGameClientsAsync(_tempDirectory);

        // Assert
        Assert.True(result.Success);
        var client = Assert.Single(result.Items);
        Assert.Equal(GameType.ZeroHour, client.GameType);
        Assert.Equal(stubPath, client.ExecutablePath);
    }

    /// <summary>
    /// An arbitrarily named engine carrying the Generals token is typed by sniffing even
    /// though no hash or publisher name recognizes it.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanDirectoryForGameClientsAsync_RenamedGeneralsEngine_TypesBySniffingAsync()
    {
        // Arrange
        var gameDir = Path.Combine(_tempDirectory, "Recovery108");
        Directory.CreateDirectory(gameDir);
        var executablePath = Path.Combine(gameDir, "generals_backup_108.exe");
        await File.WriteAllBytesAsync(executablePath, EngineBytes(withMarkers: false, withGeneralsToken: true));
        SetupManifestGeneration();

        // Act
        var result = await _detector.ScanDirectoryForGameClientsAsync(_tempDirectory);

        // Assert
        Assert.True(result.Success);
        var client = Assert.Single(result.Items);
        Assert.Equal(GameType.Generals, client.GameType);
        Assert.Equal(executablePath, client.ExecutablePath);
        Assert.Contains("Scanned Generals", client.Name);
    }

    /// <summary>
    /// A packed stub beside a Generals engine resolves to Generals while keeping the
    /// stub as the launch target.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanDirectoryForGameClientsAsync_PackedStubWithGeneralsSibling_TypesFromSiblingAsync()
    {
        // Arrange
        var gameDir = Path.Combine(_tempDirectory, "StubbedGenerals");
        Directory.CreateDirectory(gameDir);
        var stubPath = Path.Combine(gameDir, "generals.exe");
        await File.WriteAllBytesAsync(stubPath, PackedStubBytes());
        await File.WriteAllBytesAsync(Path.Combine(gameDir, "game.dat"), EngineBytes(withMarkers: false, withGeneralsToken: true));
        SetupManifestGeneration();

        // Act
        var result = await _detector.ScanDirectoryForGameClientsAsync(_tempDirectory);

        // Assert
        Assert.True(result.Success);
        var client = Assert.Single(result.Items);
        Assert.Equal(GameType.Generals, client.GameType);
        Assert.Equal(stubPath, client.ExecutablePath);
    }

    /// <summary>
    /// Tests that ScanDirectoryForGameClientsAsync handles non-existent directories gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanDirectoryForGameClientsAsync_WithNonExistentDirectory_ReturnsFailureAsync()
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
    public async Task ValidateGameClientAsync_WithValidClient_ReturnsTrueAsync()
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
    public async Task ValidateGameClientAsync_WithInvalidClient_ReturnsFalseAsync()
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
    /// Tests that ValidateGameClientAsync returns true for valid resolvable macOS app bundles.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateGameClientAsync_WithValidMacAppBundle_ReturnsTrueAsync()
    {
        // Arrange
        var gameDir = Path.Combine(_tempDirectory, "MacGameValid");
        var bundleDir = Path.Combine(gameDir, "GeneralsX.app");
        var macOsDir = Path.Combine(bundleDir, "Contents", "MacOS");
        Directory.CreateDirectory(macOsDir);
        await File.WriteAllBytesAsync(Path.Combine(macOsDir, "GeneralsX"), MachOHeader);

        var client = new GameClient
        {
            ExecutablePath = bundleDir,
        };

        // Act
        var result = await _detector.ValidateGameClientAsync(client);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Tests that ValidateGameClientAsync returns false for empty unresolvable macOS app bundles.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateGameClientAsync_WithEmptyMacAppBundle_ReturnsFalseAsync()
    {
        // Arrange
        var gameDir = Path.Combine(_tempDirectory, "MacGameEmpty");
        var bundleDir = Path.Combine(gameDir, "GeneralsEmpty.app");
        Directory.CreateDirectory(bundleDir);

        var client = new GameClient
        {
            ExecutablePath = bundleDir,
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
    public async Task ScanDirectoryForGameClientsAsync_WithUnknownHash_CreatesUnknownClientAsync()
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
    /// A GeneralsOnline directory holds both the Easy Anti-Cheat bootstrapper and the binary it
    /// wraps. A scan must report the installation once, through the bootstrapper.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanDirectoryForGameClientsAsync_WithEacLauncherBesideSixtyHertz_FindsOnlyWrapperAsync()
    {
        var gameDir = Path.Combine(_tempDirectory, "GeneralsOnline");
        Directory.CreateDirectory(gameDir);
        var wrapperPath = Path.Combine(gameDir, GameClientConstants.GeneralsOnlineEacLauncherExecutable);
        var sixtyHertzPath = Path.Combine(gameDir, GameClientConstants.GeneralsOnline60HzExecutable);
        await File.WriteAllTextAsync(wrapperPath, "dummy content");
        await File.WriteAllTextAsync(sixtyHertzPath, "dummy content");

        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("unknown_hash_12345");

        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        manifestBuilderMock.Setup(x => x.Build())
            .Returns(new ContentManifest { Id = ManifestId.Create("1.0.genhub.gameclient.unknownclient") });

        _manifestGenerationServiceMock.Setup(x => x.CreateGameClientManifestAsync(
                It.IsAny<string>(), It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PublisherInfo?>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var result = await _detector.ScanDirectoryForGameClientsAsync(_tempDirectory);

        Assert.True(result.Success);
        var only = Assert.Single(result.Items);
        Assert.Equal(wrapperPath, only.ExecutablePath);
    }

    /// <summary>
    /// The bootstrapper is absent from the retail hash registry by definition, so an unrecognized
    /// hash must fall through to the publisher identifier rather than to the generic entry. A
    /// GeneralsOnline client reported as GameType.Generals never matches the Zero Hour launch
    /// path, which is what writes settings.json.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanDirectoryForGameClientsAsync_WithEacLauncherAndUnknownHash_ClassifiesAsZeroHourGeneralsOnlineAsync()
    {
        var gameDir = Path.Combine(_tempDirectory, "GeneralsOnline");
        Directory.CreateDirectory(gameDir);
        var wrapperPath = Path.Combine(gameDir, GameClientConstants.GeneralsOnlineEacLauncherExecutable);
        await File.WriteAllTextAsync(wrapperPath, "dummy content");

        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("unknown_hash_12345");

        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        manifestBuilderMock.Setup(x => x.Build())
            .Returns(new ContentManifest { Id = ManifestId.Create("1.0.generalsonline.gameclient.generals-generalsonline-60hz") });

        _manifestGenerationServiceMock.Setup(x => x.CreateGameClientManifestAsync(
                It.IsAny<string>(), It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PublisherInfo?>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // The production identifier, so this pins real classification rather than a mock's answer.
        var detector = new GameClientDetector(
            _manifestGenerationServiceMock.Object,
            _contentManifestPoolMock.Object,
            _hashProviderMock.Object,
            _hashRegistryMock.Object,
            [new GeneralsOnlineClientIdentifier()],
            NullLogger<GameClientDetector>.Instance);

        var result = await detector.ScanDirectoryForGameClientsAsync(_tempDirectory);

        Assert.True(result.Success);
        var only = Assert.Single(result.Items);
        Assert.Equal(wrapperPath, only.ExecutablePath);
        Assert.Equal(GameType.ZeroHour, only.GameType);
        Assert.Equal(GameClientConstants.GeneralsOnline60HzDisplayName, only.Name);

        // IsPublisherClient turns on PublisherType alone. Without it the client reads as a base
        // retail install, so version resolution treats it as the base game and the launcher UI
        // never sees a publisher client.
        Assert.Equal(PublisherTypeConstants.GeneralsOnline, only.PublisherType);
        Assert.True(only.IsPublisherClient);
    }

    /// <summary>
    /// One misbehaving identifier must not take the rest down with it. The caller's handler
    /// swallows anything thrown here and returns null, so an escaping exception would drop the
    /// executable entirely rather than falling through to the identifiers after it.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanDirectoryForGameClientsAsync_WhenAnIdentifierThrows_StillTriesTheRestAsync()
    {
        var gameDir = Path.Combine(_tempDirectory, "GeneralsOnline");
        Directory.CreateDirectory(gameDir);
        var wrapperPath = Path.Combine(gameDir, GameClientConstants.GeneralsOnlineEacLauncherExecutable);
        await File.WriteAllTextAsync(wrapperPath, "dummy content");

        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("unknown_hash_12345");

        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        manifestBuilderMock.Setup(x => x.Build())
            .Returns(new ContentManifest { Id = ManifestId.Create("1.0.generalsonline.gameclient.generals-generalsonline-60hz") });

        _manifestGenerationServiceMock.Setup(x => x.CreateGameClientManifestAsync(
                It.IsAny<string>(), It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PublisherInfo?>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Throws from CanIdentify, which is the probe that runs before Identify.
        var throwingIdentifier = new Mock<IGameClientIdentifier>();
        throwingIdentifier.Setup(x => x.PublisherId).Returns("throwing");
        throwingIdentifier.Setup(x => x.CanIdentify(It.IsAny<string>())).Throws(new InvalidOperationException("boom"));

        var detector = new GameClientDetector(
            _manifestGenerationServiceMock.Object,
            _contentManifestPoolMock.Object,
            _hashProviderMock.Object,
            _hashRegistryMock.Object,
            [throwingIdentifier.Object, new GeneralsOnlineClientIdentifier()],
            NullLogger<GameClientDetector>.Instance);

        var result = await detector.ScanDirectoryForGameClientsAsync(_tempDirectory);

        Assert.True(result.Success);
        var only = Assert.Single(result.Items);
        Assert.Equal(GameType.ZeroHour, only.GameType);
        Assert.Equal(GameClientConstants.GeneralsOnline60HzDisplayName, only.Name);
    }

    /// <summary>
    /// Portables predating 060526_QFE1 ship no bootstrapper, so the wrapped binary stays the
    /// entry point rather than being filtered out with it.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanDirectoryForGameClientsAsync_WithoutEacLauncher_FindsSixtyHertzClientAsync()
    {
        var gameDir = Path.Combine(_tempDirectory, "GeneralsOnlinePreEac");
        Directory.CreateDirectory(gameDir);
        var sixtyHertzPath = Path.Combine(gameDir, GameClientConstants.GeneralsOnline60HzExecutable);
        await File.WriteAllTextAsync(sixtyHertzPath, "dummy content");

        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("unknown_hash_12345");

        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        manifestBuilderMock.Setup(x => x.Build())
            .Returns(new ContentManifest { Id = ManifestId.Create("1.0.genhub.gameclient.unknownclient") });

        _manifestGenerationServiceMock.Setup(x => x.CreateGameClientManifestAsync(
                It.IsAny<string>(), It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PublisherInfo?>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var result = await _detector.ScanDirectoryForGameClientsAsync(_tempDirectory);

        Assert.True(result.Success);
        var only = Assert.Single(result.Items);
        Assert.Equal(sixtyHertzPath, only.ExecutablePath);
    }

    /// <summary>
    /// Tests that DetectGameClientsFromInstallationsAsync detects GeneralsOnline 60Hz client.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectGameClientsFromInstallationsAsync_WithGeneralsOnline60HzExecutable_DetectsClientAsync()
    {
        // Arrange - Create identifier for GeneralsOnline 60Hz
        var generalsOnlineIdentifierMock = new Mock<IGameClientIdentifier>();
        generalsOnlineIdentifierMock.Setup(x => x.PublisherId).Returns(PublisherTypeConstants.GeneralsOnline);
        generalsOnlineIdentifierMock.Setup(x => x.CanIdentify(It.Is<string>(p => p.Contains(GameClientConstants.GeneralsOnline60HzExecutable)))).Returns(true);
        generalsOnlineIdentifierMock.Setup(x => x.CanIdentify(It.Is<string>(p => !p.Contains(GameClientConstants.GeneralsOnline60HzExecutable)))).Returns(false);
        generalsOnlineIdentifierMock.Setup(x => x.Identify(It.IsAny<string>())).Returns(new GameClientIdentification(
            PublisherTypeConstants.GeneralsOnline,
            "60Hz",
            "GeneralsOnline 60Hz",
            GameType.Generals,
            GameClientConstants.UnknownVersion));

        // Create detector with the identifier
        var detectorWith60HzIdentifier = new GameClientDetector(
            _manifestGenerationServiceMock.Object,
            _contentManifestPoolMock.Object,
            _hashProviderMock.Object,
            _hashRegistryMock.Object,
            [generalsOnlineIdentifierMock.Object],
            NullLogger<GameClientDetector>.Instance);

        var generalsPath = Path.Combine(_tempDirectory, "Generals");
        Directory.CreateDirectory(generalsPath);

        var generalsOnlineExePath = Path.Combine(generalsPath, GameClientConstants.GeneralsOnline60HzExecutable);
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
            Id = ManifestId.Create("1.0.generalsonline.gameclient.generals-generalsonline-60hz"),
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
        var result = await detectorWith60HzIdentifier.DetectGameClientsFromInstallationsAsync(installations);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Items.Count);

        var generalsOnlineClient = result.Items.FirstOrDefault(c => c.Name.Contains("GeneralsOnline"));
        Assert.NotNull(generalsOnlineClient);
        Assert.Equal(GameType.Generals, generalsOnlineClient.GameType);
        Assert.Equal(GameClientConstants.UnknownVersion, generalsOnlineClient.Version); // GeneralsOnline clients auto-update

        Assert.Equal(generalsOnlineExePath, generalsOnlineClient.ExecutablePath);

        Assert.Contains("60Hz", generalsOnlineClient.Name);
    }

    /// <summary>
    /// Since 060526_QFE1 the Easy Anti-Cheat bootstrapper ships beside the binary it wraps.
    /// Detection must yield a single client pointing at the bootstrapper, not one client per
    /// recognised executable name.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectGameClientsFromInstallationsAsync_WithEacLauncherBesideSixtyHertz_DetectsOnlyWrapperAsync()
    {
        var identifierMock = new Mock<IGameClientIdentifier>();
        identifierMock.Setup(x => x.PublisherId).Returns(PublisherTypeConstants.GeneralsOnline);
        identifierMock.Setup(x => x.CanIdentify(It.IsAny<string>())).Returns(false);

        var detector = new GameClientDetector(
            _manifestGenerationServiceMock.Object,
            _contentManifestPoolMock.Object,
            _hashProviderMock.Object,
            _hashRegistryMock.Object,
            [identifierMock.Object],
            NullLogger<GameClientDetector>.Instance);

        var zeroHourPath = Path.Combine(_tempDirectory, "ZeroHourEac");
        Directory.CreateDirectory(zeroHourPath);

        var wrapperPath = Path.Combine(zeroHourPath, GameClientConstants.GeneralsOnlineEacLauncherExecutable);
        var sixtyHertzPath = Path.Combine(zeroHourPath, GameClientConstants.GeneralsOnline60HzExecutable);
        await File.WriteAllTextAsync(wrapperPath, "dummy content");
        await File.WriteAllTextAsync(sixtyHertzPath, "dummy content");

        var installation = new GameInstallation("C:\\TestInstallEac", GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = zeroHourPath,
        };

        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("any_hash");

        _contentManifestPoolMock
            .Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var result = await detector.DetectGameClientsFromInstallationsAsync([installation]);

        Assert.True(result.Success);
        var generalsOnlineClients = result.Items
            .Where(client => client.Name.Contains("GeneralsOnline", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var only = Assert.Single(generalsOnlineClients);
        Assert.Equal(wrapperPath, only.ExecutablePath);
    }

    /// <summary>
    /// Tests that DetectGameClientsFromInstallationsAsync detects GeneralsOnline 60Hz client for Zero Hour.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectGameClientsFromInstallationsAsync_WithGeneralsOnline60HzExecutable_DetectsZeroHourClientAsync()
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
    /// Tests that DetectGameClientsFromInstallationsAsync detects GeneralsOnline 60Hz variant with standard client.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectGameClientsFromInstallationsAsync_WithGeneralsOnline60HzVariant_DetectsClientWithStandardAsync()
    {
        // Arrange - Create identifier for 60Hz
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

        // Create detector with the identifier
        var detectorWithIdentifier = new GameClientDetector(
            _manifestGenerationServiceMock.Object,
            _contentManifestPoolMock.Object,
            _hashProviderMock.Object,
            _hashRegistryMock.Object,
            [identifier60HzMock.Object],
            NullLogger<GameClientDetector>.Instance);

        var generalsPath = Path.Combine(_tempDirectory, "GeneralsMultiple");
        Directory.CreateDirectory(generalsPath);

        var generalsonline60HzPath = Path.Combine(generalsPath, GameClientConstants.GeneralsOnline60HzExecutable);
        var standardExePath = Path.Combine(generalsPath, GameClientConstants.GeneralsExecutable);

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
        var result = await detectorWithIdentifier.DetectGameClientsFromInstallationsAsync(installations);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Items.Count); // 1 GeneralsOnline variant (60Hz) + 1 standard client
    }

    /// <summary>
    /// Tests that GeneralsOnline detection skips missing executable files gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectGameClientsFromInstallationsAsync_WithMissingGeneralsOnlineExecutable_SkipsAndContinuesAsync()
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

    /// <summary>
    /// Tests that DetectGameClientsFromInstallationsAsync detects a Generals client when the
    /// executable uses alternate casing (for example <c>Generals.exe</c> on case-sensitive filesystems).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectGameClientsFromInstallationsAsync_WithCapitalizedGeneralsExecutable_DetectsGeneralsClientAsync()
    {
        // Arrange
        var generalsPath = Path.Combine(_tempDirectory, "GeneralsCased");
        Directory.CreateDirectory(generalsPath);
        var executablePath = Path.Combine(generalsPath, "Generals.exe");
        await File.WriteAllTextAsync(executablePath, "dummy content");

        var installation = new GameInstallation("C:\\TestInstall", GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = generalsPath,
        };

        List<GameInstallation> installations = [installation];

        // Setup hash provider to return known Generals hash for any resolved path
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameClientHashRegistry.Generals108HashPublic);

        // Setup manifest generation
        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var manifest = new ContentManifest { Id = ManifestId.Create("1.108.steam.gameclient.generals") };
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
        Assert.Equal(GameType.Generals, client.GameType);
        Assert.Equal("1.08", client.Version);
        Assert.True(File.Exists(client.ExecutablePath));
        Assert.Equal("Generals.exe", Path.GetFileName(client.ExecutablePath), ignoreCase: true);
        Assert.Equal(generalsPath, client.WorkingDirectory);
    }

    /// <summary>
    /// Tests that DetectGameClientsFromInstallationsAsync detects a Zero Hour client when the
    /// executable uses alternate casing (for example <c>Generals.exe</c> on case-sensitive filesystems).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectGameClientsFromInstallationsAsync_WithCapitalizedZeroHourExecutable_DetectsZeroHourClientAsync()
    {
        // Arrange
        var zeroHourPath = Path.Combine(_tempDirectory, "ZeroHourCased");
        Directory.CreateDirectory(zeroHourPath);
        var executablePath = Path.Combine(zeroHourPath, "Generals.exe");
        await File.WriteAllTextAsync(executablePath, "dummy content");

        var installation = new GameInstallation("C:\\TestInstall", GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = zeroHourPath,
        };

        List<GameInstallation> installations = [installation];

        // Setup hash provider to return known Zero Hour hash for any resolved path
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
        Assert.True(File.Exists(client.ExecutablePath));
        Assert.Equal("Generals.exe", Path.GetFileName(client.ExecutablePath), ignoreCase: true);
        Assert.Equal(zeroHourPath, client.WorkingDirectory);
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

    private static byte[] EngineBytes(bool withMarkers, bool withGeneralsToken = false)
    {
        var bytes = new byte[512];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        if (withMarkers)
        {
            var title = Encoding.ASCII.GetBytes(GameBinaryConstants.ZeroHourTitle);
            var menu = Encoding.ASCII.GetBytes(" " + GameBinaryConstants.ChallengeMenuMarker);
            Buffer.BlockCopy(title, 0, bytes, 64, title.Length);
            Buffer.BlockCopy(menu, 0, bytes, 64 + title.Length, menu.Length);
        }

        if (withGeneralsToken)
        {
            var token = Encoding.ASCII.GetBytes(" " + GameBinaryConstants.GeneralsEngineMarker);
            Buffer.BlockCopy(token, 0, bytes, 256, token.Length);
        }

        return bytes;
    }

    private static byte[] PackedStubBytes()
    {
        var file = new byte[4608];
        file[0] = (byte)'M';
        file[1] = (byte)'Z';
        ushort sectionCount = 1;
        ushort optionalHeaderSize = 224;
        ushort pe32Magic = 0x10B;
        uint textVirtualSize = 4096;
        uint textRawPointer = 512;
        BitConverter.GetBytes(64).CopyTo(file, 0x3C);
        file[64] = (byte)'P';
        file[65] = (byte)'E';
        BitConverter.GetBytes(sectionCount).CopyTo(file, 70);
        BitConverter.GetBytes(optionalHeaderSize).CopyTo(file, 84);
        BitConverter.GetBytes(pe32Magic).CopyTo(file, 88);
        Encoding.ASCII.GetBytes(".text").CopyTo(file, 312);
        BitConverter.GetBytes(textVirtualSize).CopyTo(file, 328);
        BitConverter.GetBytes(textRawPointer).CopyTo(file, 332);
        new Random(7).NextBytes(new Span<byte>(file, 512, 4096));
        return file;
    }

    private void SetupManifestGeneration()
    {
        var manifestBuilderMock = new Mock<IContentManifestBuilder>();
        var manifest = new ContentManifest { Id = ManifestId.Create("1.0.scanned.test.client") };
        manifestBuilderMock.Setup(x => x.Build()).Returns(manifest);

        _manifestGenerationServiceMock.Setup(x => x.CreateGameClientManifestAsync(
                It.IsAny<string>(), It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PublisherInfo?>()))
            .ReturnsAsync(manifestBuilderMock.Object);

        _contentManifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
    }
}
