using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Manifest;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using ContentType = GenHub.Core.Models.Enums.ContentType;
using GameType = GenHub.Core.Models.Enums.GameType;

namespace GenHub.Tests.Core.Features.Manifest;

/// <summary>
/// Unit tests for <see cref="ManifestGenerationService"/> executable inclusion.
/// </summary>
public class ManifestGenerationServiceTests : IDisposable
{
    private readonly Mock<IFileHashProvider> _hashProviderMock;
    private readonly Mock<IManifestIdService> _manifestIdServiceMock;
    private readonly Mock<IDownloadService> _downloadServiceMock;
    private readonly Mock<IConfigurationProviderService> _configProviderServiceMock;
    private readonly ManifestGenerationService _service;
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestGenerationServiceTests"/> class.
    /// </summary>
    public ManifestGenerationServiceTests()
    {
        _hashProviderMock = new Mock<IFileHashProvider>();
        _manifestIdServiceMock = new Mock<IManifestIdService>();
        _downloadServiceMock = new Mock<IDownloadService>();
        _configProviderServiceMock = new Mock<IConfigurationProviderService>();

        // Setup hash provider to return deterministic hashes
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), default))
            .ReturnsAsync((string path, System.Threading.CancellationToken ct) => $"hash_{Path.GetFileName(path)}");

        // Setup manifest ID service to return properly formatted IDs
        // Format: version.userversion.publisher.contenttype.contentname
        // Publisher names need to be normalized (lowercase, no spaces)
        _manifestIdServiceMock.Setup(x => x.GenerateGameInstallationId(It.IsAny<GameInstallation>(), It.IsAny<GameType>(), It.IsAny<string?>()))
            .Returns((GameInstallation inst, GameType gt, string? v) => OperationResult<ManifestId>.CreateSuccess(ManifestId.Create("1.0.ea.gameinstallation.generals")));

        _manifestIdServiceMock.Setup(x => x.GeneratePublisherContentId(
                It.IsAny<string>(), It.IsAny<ContentType>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns((string p, ContentType ct, string c, int v) =>
            {
                var normalizedPublisher = p.ToLowerInvariant().Replace(" ", string.Empty);
                var normalizedContent = c.ToLowerInvariant().Replace(" ", string.Empty);
                var contentTypeString = ct.ToString().ToLowerInvariant();
                return OperationResult<ManifestId>.CreateSuccess(
                    ManifestId.Create($"1.{v}.{normalizedPublisher}.{contentTypeString}.{normalizedContent}"));
            });

        _service = new ManifestGenerationService(
            NullLogger<ManifestGenerationService>.Instance,
            _hashProviderMock.Object,
            _manifestIdServiceMock.Object,
            _downloadServiceMock.Object,
            _configProviderServiceMock.Object);

        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Tests that CreateGameClientManifestAsync includes the executable in the manifest.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameClientManifestAsync_IncludesExecutableWithHash()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "generals.exe"), "dummy exe content");
        var (clientPath, executablePath) = await PrepareDummyExeAsync();

        // Act
        var builder = await _service.CreateGameClientManifestAsync(
            clientPath, GameType.Generals, "TestClient", "1.0", executablePath);
        var manifest = builder.Build();

        // Assert
        Assert.NotNull(manifest);
        var executableFile = manifest.Files.FirstOrDefault(f => f.RelativePath.EndsWith("generals.exe"));
        Assert.NotNull(executableFile);
        Assert.Equal("hash_generals.exe", executableFile.Hash);
        Assert.Equal(GenHub.Core.Models.Enums.ContentSourceType.GameInstallation, executableFile.SourceType);
    }

    /// <summary>
    /// Tests that CreateGameClientManifestAsync includes executable with correct size.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameClientManifestAsync_IncludesExecutableWithCorrectSize()
    {
        // Arrange
        var (clientPath, executablePath) = await PrepareDummyExeAsync();
        var testContent = "This is test content for size calculation";
        await File.WriteAllTextAsync(executablePath, testContent);
        var expectedSize = new FileInfo(executablePath).Length;

        // Act
        var builder = await _service.CreateGameClientManifestAsync(
            clientPath, GameType.ZeroHour, "TestClient", "1.0", executablePath);
        var manifest = builder.Build();

        // Assert
        var executableFile = manifest.Files.FirstOrDefault(f => f.RelativePath.EndsWith("generals.exe"));
        Assert.NotNull(executableFile);
        Assert.Equal(expectedSize, executableFile.Size);
    }

    /// <summary>
    /// Tests that CreateGameClientManifestAsync throws when executable is missing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameClientManifestAsync_ThrowsWhenExecutableMissing()
    {
        // Arrange
        var clientPath = Path.Combine(_tempDirectory, "TestClient");
        Directory.CreateDirectory(clientPath);
        var executablePath = Path.Combine(clientPath, "nonexistent.exe");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _service.CreateGameClientManifestAsync(
                clientPath, GameType.Generals, "TestClient", "1.0", executablePath));
    }

    /// <summary>
    /// Tests that CreateGameClientManifestAsync includes required DLLs when present.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameClientManifestAsync_IncludesRequiredDllsWhenPresent()
    {
        // Arrange
        var (clientPath, executablePath) = await PrepareDummyExeAsync();

        // Create required DLLs
        await File.WriteAllTextAsync(Path.Combine(clientPath, "steam_api.dll"), "dll content");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "binkw32.dll"), "dll content");

        // Act
        var builder = await _service.CreateGameClientManifestAsync(
            clientPath, GameType.Generals, "TestClient", "1.0", executablePath);
        var manifest = builder.Build();

        // Assert
        Assert.Contains(manifest.Files, f => f.RelativePath.EndsWith("steam_api.dll"));
        Assert.Contains(manifest.Files, f => f.RelativePath.EndsWith("binkw32.dll"));
    }

    /// <summary>
    /// Tests that CreateGameClientManifestAsync includes config files when present.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameClientManifestAsync_IncludesConfigFilesWhenPresent()
    {
        // Arrange
        var (clientPath, executablePath) = await PrepareDummyExeAsync();

        // Create config files
        await File.WriteAllTextAsync(Path.Combine(clientPath, "options.ini"), "config content");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "skirmish.ini"), "config content");

        // Act
        var builder = await _service.CreateGameClientManifestAsync(
            clientPath, GameType.Generals, "TestClient", "1.0", executablePath);
        var manifest = builder.Build();

        // Assert
        Assert.Contains(manifest.Files, f => f.RelativePath.EndsWith("options.ini"));
        Assert.Contains(manifest.Files, f => f.RelativePath.EndsWith("skirmish.ini"));
    }

    /// <summary>
    /// Tests that manifest Files section contains multiple items.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameClientManifestAsync_ManifestContainsMultipleFiles()
    {
        // Arrange
        var (clientPath, executablePath) = await PrepareDummyExeAsync();

        // Create DLLs and config files
        await File.WriteAllTextAsync(Path.Combine(clientPath, "steam_api.dll"), "dll");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "binkw32.dll"), "dll");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "options.ini"), "config");

        // Act
        var builder = await _service.CreateGameClientManifestAsync(
            clientPath, GameType.Generals, "TestClient", "1.0", executablePath);
        var manifest = builder.Build();

        // Assert - At minimum: exe + 2 DLLs + 1 config = 4 files
        Assert.True(manifest.Files.Count >= 4, $"Expected at least 4 files, got {manifest.Files.Count}");
    }

    /// <summary>
    /// Tests that CreateGameClientManifestAsync includes all DLLs and Generals.dat for EA App clients.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameClientManifestAsync_IncludesAllDllsAndGeneralsDatForEaApp()
    {
        // Arrange
        var clientPath = Path.Combine(_tempDirectory, "EaAppClient");
        Directory.CreateDirectory(clientPath);
        var executablePath = Path.Combine(clientPath, "game.dat");
        await File.WriteAllTextAsync(executablePath, "dummy game.dat");

        // Create various DLLs, some in RequiredDlls, some auxiliary
        await File.WriteAllTextAsync(Path.Combine(clientPath, "binkw32.dll"), "dll");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "P2XDLL.DLL"), "ea wrapper");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "patchw32.dll"), "patch dll");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "custom_wrapper.dll"), "custom dll");

        // Create Generals.dat
        await File.WriteAllTextAsync(Path.Combine(clientPath, "Generals.dat"), "data file");

        // Act
        // Use "ea" in the client name to trigger EA App logic
        var builder = await _service.CreateGameClientManifestAsync(
            clientPath, GameType.ZeroHour, "EA App Zero Hour", "1.04", executablePath);
        var manifest = builder.Build();

        // Assert
        Assert.Contains(manifest.Files, f => f.RelativePath == "game.dat" && f.IsExecutable);
        Assert.Contains(manifest.Files, f => f.RelativePath == "binkw32.dll");
        Assert.Contains(manifest.Files, f => f.RelativePath == "P2XDLL.DLL");
        Assert.Contains(manifest.Files, f => f.RelativePath == "patchw32.dll");
        Assert.Contains(manifest.Files, f => f.RelativePath == "custom_wrapper.dll");
        Assert.Contains(manifest.Files, f => f.RelativePath == "Generals.dat");

        // Also verify required DLLs from GameClientConstants are included
        Assert.Contains(manifest.Files, f => f.RelativePath == "binkw32.dll");
    }

    /// <summary>
    /// Tests that CreateGameInstallationManifestAsync uses CSV-based generation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameInstallationManifestAsync_UsesCsvWhenAvailable()
    {
        // Arrange
        var installationPath = Path.Combine(_tempDirectory, "GeneralsInstall");
        Directory.CreateDirectory(installationPath);

        // Create some files that are in the generals.csv
        await File.WriteAllTextAsync(Path.Combine(installationPath, "generals.exe"), "dummy");
        await File.WriteAllTextAsync(Path.Combine(installationPath, "AudioEnglish.big"), "dummy");

        // Act
        var builder = await _service.CreateGameInstallationManifestAsync(
            installationPath, GameType.Generals, GameInstallationType.Steam, "1.08");
        var manifest = builder.Build();

        // Assert
        Assert.NotNull(manifest);
        Assert.Contains(manifest.Files, f => f.RelativePath == "generals.exe");
        Assert.Contains(manifest.Files, f => f.RelativePath == "AudioEnglish.big");
    }

    /// <summary>
    /// Cleans up temporary test files.
    /// </summary>
    public void Dispose()
    {
        FileOperationsService.DeleteDirectoryIfExists(_tempDirectory);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Prepares a dummy executable file for testing.
    /// </summary>
    /// <returns>A tuple containing the client path and executable path.</returns>
    private async Task<(string ClientPath, string ExecutablePath)> PrepareDummyExeAsync()
    {
        var clientPath = Path.Combine(_tempDirectory, "TestClient");
        Directory.CreateDirectory(clientPath);
        var executablePath = Path.Combine(clientPath, "generals.exe");
        await File.WriteAllTextAsync(executablePath, "dummy exe");
        return (clientPath, executablePath);
    }
}
