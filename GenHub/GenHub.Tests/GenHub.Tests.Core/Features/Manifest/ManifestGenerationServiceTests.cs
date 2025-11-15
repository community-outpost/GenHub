using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Manifest;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using GameInstallationType = GenHub.Core.Models.Enums.GameInstallationType;
using GameType = GenHub.Core.Models.Enums.GameType;

namespace GenHub.Tests.Core.Features.Manifest;

/// <summary>
/// Unit tests for <see cref="ManifestGenerationService"/> executable inclusion.
/// </summary>
public class ManifestGenerationServiceTests : IDisposable
{
    private readonly Mock<IFileHashProvider> _hashProviderMock;
    private readonly Mock<IManifestIdService> _manifestIdServiceMock;
    private readonly ManifestGenerationService _service;
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestGenerationServiceTests"/> class.
    /// </summary>
    public ManifestGenerationServiceTests()
    {
        _hashProviderMock = new Mock<IFileHashProvider>();
        _manifestIdServiceMock = new Mock<IManifestIdService>();

        // Setup hash provider to return deterministic hashes
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), default))
            .ReturnsAsync((string path, System.Threading.CancellationToken ct) => $"hash_{Path.GetFileName(path)}");

        // Setup manifest ID service to return properly formatted IDs
        // Format: version.userversion.publisher.contenttype.contentname
        // Publisher names need to be normalized (lowercase, no spaces)
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
            _manifestIdServiceMock.Object);

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
        var clientPath = Path.Combine(_tempDirectory, "TestClient");
        Directory.CreateDirectory(clientPath);
        var executablePath = Path.Combine(clientPath, "generals.exe");
        await File.WriteAllTextAsync(executablePath, "dummy exe content");

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
        var clientPath = Path.Combine(_tempDirectory, "TestClient");
        Directory.CreateDirectory(clientPath);
        var executablePath = Path.Combine(clientPath, "game.dat");
        var testContent = "This is test content for size calculation";
        await File.WriteAllTextAsync(executablePath, testContent);
        var expectedSize = new FileInfo(executablePath).Length;

        // Act
        var builder = await _service.CreateGameClientManifestAsync(
            clientPath, GameType.ZeroHour, "TestClient", "1.0", executablePath);
        var manifest = builder.Build();

        // Assert
        var executableFile = manifest.Files.FirstOrDefault(f => f.RelativePath.EndsWith("game.dat"));
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
        var clientPath = Path.Combine(_tempDirectory, "TestClient");
        Directory.CreateDirectory(clientPath);
        var executablePath = Path.Combine(clientPath, "generals.exe");
        await File.WriteAllTextAsync(executablePath, "dummy exe");

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
        var clientPath = Path.Combine(_tempDirectory, "TestClient");
        Directory.CreateDirectory(clientPath);
        var executablePath = Path.Combine(clientPath, "generals.exe");
        await File.WriteAllTextAsync(executablePath, "dummy exe");

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
        var clientPath = Path.Combine(_tempDirectory, "TestClient");
        Directory.CreateDirectory(clientPath);
        var executablePath = Path.Combine(clientPath, "generals.exe");
        await File.WriteAllTextAsync(executablePath, "dummy exe");

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
    /// Cleans up temporary test files.
    /// </summary>
    public void Dispose()
    {
        FileOperationsService.DeleteDirectoryIfExists(_tempDirectory);
    }
}
