using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GenHub.Common.Services;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameClients;
using GenHub.Features.Manifest;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.GameClients;

/// <summary>
/// Integration tests for GameClient manifest generation with real file system operations.
/// </summary>
public class GameClientManifestIntegrationTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Sha256HashProvider _hashProvider;
    private readonly IManifestIdService _manifestIdService;
    private readonly ManifestGenerationService _manifestService;
    private readonly Mock<IContentManifestPool> _manifestPoolMock;
    private readonly GameClientDetector _detector;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameClientManifestIntegrationTests"/> class.
    /// </summary>
    public GameClientManifestIntegrationTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);

        _hashProvider = new Sha256HashProvider();
        _manifestIdService = new ManifestIdService();
        _manifestService = new ManifestGenerationService(
            NullLogger<ManifestGenerationService>.Instance,
            _hashProvider,
            _manifestIdService,
            new Mock<IDownloadService>().Object,
            new Mock<IConfigurationProviderService>().Object);

        _manifestPoolMock = new Mock<IContentManifestPool>();
        _manifestPoolMock.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _detector = new GameClientDetector(
            _manifestService,
            _manifestPoolMock.Object,
            _hashProvider,
            new GameClientHashRegistry(),
            [],
            NullLogger<GameClientDetector>.Instance);
    }

    /// <summary>
    /// Integration test: Generate GameClient manifest from simulated Steam Generals installation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GenerateGameClientManifest_WithSteamGeneralsInstallation_CreatesManifestWithExecutable()
    {
        var generalsPath = Path.Combine(_tempDirectory, "Steam", "Generals");
        Directory.CreateDirectory(generalsPath);

        var executablePath = Path.Combine(generalsPath, "generals.exe");
        await File.WriteAllTextAsync(executablePath, "Dummy Steam Generals executable");
        await File.WriteAllTextAsync(Path.Combine(generalsPath, "steam_api.dll"), "Steam API");
        await File.WriteAllTextAsync(Path.Combine(generalsPath, "options.ini"), "[Options]");

        var installation = new GameInstallation(generalsPath, GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = generalsPath,
        };

        var result = await _detector.DetectGameClientsFromInstallationsAsync([installation]);

        Assert.True(result.Success);
        Assert.Single(result.Items);

        var gameClient = result.Items[0];
        Assert.NotNull(gameClient);
        Assert.NotEmpty(gameClient.Id);
        Assert.Equal(GameType.Generals, gameClient.GameType);
        Assert.Equal(executablePath, gameClient.ExecutablePath);
    }

    /// <summary>
    /// Integration test: Verify executable hash is computed correctly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GenerateGameClientManifest_ExecutableHashIsComputed()
    {
        var clientPath = Path.Combine(_tempDirectory, "TestClient");
        Directory.CreateDirectory(clientPath);

        var executablePath = Path.Combine(clientPath, "game.dat");
        await File.WriteAllTextAsync(executablePath, "Test executable content");

        var expectedHash = await _hashProvider.ComputeFileHashAsync(executablePath);

        var builder = await _manifestService.CreateGameClientManifestAsync(
            clientPath, GameType.ZeroHour, "TestClient", "1.0", executablePath);
        var manifest = builder.Build();

        var executableFile = manifest.Files.FirstOrDefault(f => f.RelativePath.EndsWith("game.dat"));
        Assert.NotNull(executableFile);
        Assert.NotEmpty(executableFile.Hash);
        Assert.Equal(expectedHash, executableFile.Hash);
        Assert.True(executableFile.IsExecutable);
    }

    /// <summary>
    /// Integration test: Verify manifest includes all expected files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GenerateGameClientManifest_IncludesAllExpectedFiles()
    {
        var clientPath = Path.Combine(_tempDirectory, "FullClient");
        Directory.CreateDirectory(clientPath);

        var executablePath = Path.Combine(clientPath, "generals.exe");
        await File.WriteAllTextAsync(executablePath, "Executable");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "steam_api.dll"), "Steam");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "binkw32.dll"), "Bink");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "mss32.dll"), "Miles");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "eauninstall.dll"), "EA");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "options.ini"), "Options");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "skirmish.ini"), "Skirmish");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "network.ini"), "Network");

        var builder = await _manifestService.CreateGameClientManifestAsync(
            clientPath, GameType.Generals, "FullClient", "1.0", executablePath);
        var manifest = builder.Build();

        Assert.True(manifest.Files.Count >= 8, $"Expected at least 8 files, got {manifest.Files.Count}");
        Assert.Contains(manifest.Files, f => f.RelativePath.EndsWith("generals.exe") && !string.IsNullOrEmpty(f.Hash));
        Assert.Contains(manifest.Files, f => f.RelativePath.EndsWith("steam_api.dll"));
        Assert.Contains(manifest.Files, f => f.RelativePath.EndsWith("options.ini"));
    }

    /// <summary>
    /// Cleans up temporary test files.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }

        GC.SuppressFinalize(this);
    }
}
