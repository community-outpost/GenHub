using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Manifest;
using GenHub.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Reflection;

namespace GenHub.Tests.Core.Features.Manifest;

/// <summary>
/// Unit tests for the <see cref="ManifestProvider"/> class.
/// </summary>
public class ManifestProviderTests
{
    /// <summary>
    /// Mock logger for the manifest provider.
    /// </summary>
    private readonly Mock<ILogger<ManifestProvider>> _loggerMock;

    /// <summary>
    /// Mock manifest cache.
    /// </summary>
    private readonly Mock<IContentManifestPool> _poolMock;

    /// <summary>
    /// Mock manifest ID service.
    /// </summary>
    private readonly Mock<IManifestIdService> _manifestIdServiceMock;

    /// <summary>
    /// Mock content manifest builder.
    /// </summary>
    private readonly Mock<IContentManifestBuilder> _manifestBuilderMock;

    /// <summary>
    /// The manifest provider under test.
    /// </summary>
    private readonly ManifestProvider _manifestProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestProviderTests"/> class.
    /// </summary>
    public ManifestProviderTests()
    {
        _loggerMock = new Mock<ILogger<ManifestProvider>>();
        _poolMock = new Mock<IContentManifestPool>();
        _manifestIdServiceMock = new Mock<IManifestIdService>();
        _manifestBuilderMock = new Mock<IContentManifestBuilder>();
        _manifestProvider = new ManifestProvider(_loggerMock.Object, _poolMock.Object, _manifestIdServiceMock.Object, _manifestBuilderMock.Object);
    }

    /// <summary>
    /// Tests that GetManifestAsync returns manifest from cache when available for GameClient.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetManifestAsync_WithGameClient_ReturnsFromCache_WhenAvailable()
    {
        // Arrange
        var gameClient = new GameClient
        {
            Id = "1.0.genhub.mod.version",
            Name = "Test Version",
        };
        var expectedManifest = new ContentManifest
        {
            Id = "1.0.genhub.mod.version",
            Name = "Test Manifest",
        };

        _poolMock.Setup(x => x.GetManifestAsync(ManifestId.Create("1.0.genhub.mod.version"), default))
                  .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(expectedManifest));

        // Act
        var result = await _manifestProvider.GetManifestAsync(gameClient);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1.0.genhub.mod.version", result.Id);
        _poolMock.Verify(x => x.GetManifestAsync(ManifestId.Create("1.0.genhub.mod.version"), default), Times.Once);
    }

    /// <summary>
    /// Tests that GetManifestAsync builds correct manifest ID for game installation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetManifestAsync_WithGameInstallation_BuildsCorrectManifestId()
    {
        // Arrange
        var installation = new GameInstallation(
            installationPath: @"C:\TestPath",
            installationType: GameInstallationType.EaApp,
            logger: null);
        installation.SetPaths(@"C:\TestPath\Command and Conquer Generals", null);

        // Use the manifest version constant to generate the expected ID
        // "1.08" version becomes "108" in the manifest ID for schema compliance
        var expectedManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.108.eaapp.gameinstallation.generals"),
            Name = "Test Manifest",
        };

        _poolMock.Setup(x => x.GetManifestAsync(ManifestId.Create("1.108.eaapp.gameinstallation.generals"), default))
                  .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(expectedManifest));

        // Act
        var result = await _manifestProvider.GetManifestAsync(installation);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1.108.eaapp.gameinstallation.generals", result.Id);
        _poolMock.Verify(x => x.GetManifestAsync(ManifestId.Create("1.108.eaapp.gameinstallation.generals"), default), Times.Once);
    }

    /// <summary>
    /// Tests that GetManifestAsync uses Zero Hour ID for Zero Hour installations.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetManifestAsync_WithZeroHourInstallation_UsesZeroHourId()
    {
        // Arrange
        var tempZeroHourPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempZeroHourPath);
        var zeroHourExe = Path.Combine(tempZeroHourPath, "generals.exe");
        File.WriteAllText(zeroHourExe, "dummy");
        try
        {
            var installation = new GameInstallation(
                installationPath: @"C:\TestPath",
                installationType: GameInstallationType.Steam,
                logger: null);

            // SetPaths with null Generals path and only Zero Hour path to ensure Zero Hour detection
            installation.SetPaths(null, tempZeroHourPath);

            // Use the Zero Hour manifest version constant to generate the expected ID
            // "1.04" version becomes "104" in the manifest ID for schema compliance
            var expectedManifest = new ContentManifest
            {
                Id = ManifestId.Create("1.104.steam.gameinstallation.zerohour"),
                Name = "Test Manifest",
            };

            _poolMock.Setup(x => x.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(expectedManifest));

            // Act
            var result = await _manifestProvider.GetManifestAsync(installation);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("1.104.steam.gameinstallation.zerohour", result.Id);
        }
        finally
        {
            if (Directory.Exists(tempZeroHourPath))
            {
                Directory.Delete(tempZeroHourPath, true);
            }
        }
    }

    /// <summary>
    /// Tests that GetManifestAsync returns null when manifest is not found in cache or resources.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetManifestAsync_ReturnsNull_WhenManifestNotFoundInCacheAndResources()
    {
        // Arrange
        var gameClient = new GameClient { Id = "1.0.genhub.nonexistent" };
        _poolMock.Setup(x => x.GetManifestAsync(It.IsAny<ManifestId>(), default))
                  .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(null));

        // Act
        var result = await _manifestProvider.GetManifestAsync(gameClient);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that GetManifestAsync throws validation exception when manifest ID doesn't match.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetManifestAsync_ThrowsValidationException_WhenManifestIdMismatch()
    {
        // Arrange
        var gameClient = new GameClient
        {
            Id = "1.0.genhub.mod.testcontent",
            Name = "Test Version",
        };

        // Simulate manifest returned with mismatched Id
        var mismatchedManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.genhub.mod.wrongcontent"),
            Name = "Test Manifest",
        };
        _poolMock.Setup(x => x.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(mismatchedManifest));

        // Act & Assert
        await Assert.ThrowsAsync<ManifestValidationException>(
            () => _manifestProvider.GetManifestAsync(gameClient));
    }

    /// <summary>
    /// Tests that ValidateManifestSecurity throws security exception for path traversal.
    /// </summary>
    [Fact]
    public void ValidateManifestSecurity_ThrowsSecurityException_ForPathTraversal()
    {
        // Arrange
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.genhub.mod.testcontent"),
            Files =
            [
                new()
                {
                RelativePath = "../malicious/path",
                }

            ],
            RequiredDirectories = [],
        };

        // Act & Assert
        var method = typeof(ManifestProvider)
            .GetMethod("ValidateManifestSecurity", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var ex = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [manifest]));
        Assert.IsType<ManifestSecurityException>(ex.InnerException);
    }
}
