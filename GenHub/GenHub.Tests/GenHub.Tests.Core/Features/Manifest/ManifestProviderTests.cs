using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Manifest;
using GenHub.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

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
    private readonly Mock<IManifestCache> _cacheMock;

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
        _cacheMock = new Mock<IManifestCache>();
        _manifestProvider = new ManifestProvider(_loggerMock.Object, _cacheMock.Object);
    }

    /// <summary>
    /// Tests that GetManifestAsync returns manifest from cache when available for GameVersion.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetManifestAsync_WithGameVersion_ReturnsFromCache_WhenAvailable()
    {
        // Arrange
        var gameVersion = new GameVersion
        {
            Id = "test-version",
            Name = "Test Version",
        };
        var expectedManifest = new GameManifest
        {
            Id = "test-version",
            Name = "Test Manifest",
        };

        _cacheMock.Setup(x => x.GetManifest("test-version"))
                  .Returns(expectedManifest);

        // Act
        var result = await _manifestProvider.GetManifestAsync(gameVersion);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-version", result.Id);
        _cacheMock.Verify(x => x.GetManifest("test-version"), Times.Once);
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
            installationType: GameInstallationType.Origin,
            logger: null)
        {
            HasGenerals = true,
            HasZeroHour = false,
        };
        var expectedManifest = new GameManifest
        {
            Id = "Origin.Generals",
            Name = "Test Manifest",
        };

        _cacheMock.Setup(x => x.GetManifest("Origin.Generals"))
                  .Returns(expectedManifest);

        // Act
        var result = await _manifestProvider.GetManifestAsync(installation);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Origin.Generals", result.Id);
        _cacheMock.Verify(x => x.GetManifest("Origin.Generals"), Times.Once);
    }

    /// <summary>
    /// Tests that GetManifestAsync uses ZeroHour ID when ZeroHour is available.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetManifestAsync_WithZeroHourInstallation_UsesZeroHourId()
    {
        // Arrange
        var installation = new GameInstallation(
            installationPath: @"C:\TestPath",
            installationType: GameInstallationType.Steam,
            logger: null)
        {
            HasGenerals = true,
            HasZeroHour = true,
        };
        var expectedManifest = new GameManifest
        {
            Id = "Steam.ZeroHour",
            Name = "Test Manifest",
        };

        _cacheMock.Setup(x => x.GetManifest("Steam.ZeroHour"))
                  .Returns(expectedManifest);

        // Act
        var result = await _manifestProvider.GetManifestAsync(installation);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Steam.ZeroHour", result.Id);
    }

    /// <summary>
    /// Tests that GetManifestAsync returns null when manifest is not found in cache or resources.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetManifestAsync_ReturnsNull_WhenManifestNotFoundInCacheAndResources()
    {
        // Arrange
        var gameVersion = new GameVersion { Id = "non-existent" };
        _cacheMock.Setup(x => x.GetManifest(It.IsAny<string>())).Returns((GameManifest?)null);

        // Act
        var result = await _manifestProvider.GetManifestAsync(gameVersion);

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
        var gameVersion = new GameVersion
        {
            Id = "expected-id",
            Name = "Test Version",
        };

        // Simulate manifest returned with mismatched Id
        var mismatchedManifest = new GameManifest
        {
            Id = "wrong-id",
            Name = "Test Manifest",
        };
        _cacheMock.Setup(x => x.GetManifest("expected-id"))
                  .Returns(mismatchedManifest);

        // Act & Assert
        await Assert.ThrowsAsync<ManifestValidationException>(
            () => _manifestProvider.GetManifestAsync(gameVersion));
    }

    /// <summary>
    /// Tests that ValidateManifestSecurity throws security exception for path traversal.
    /// </summary>
    [Fact]
    public void ValidateManifestSecurity_ThrowsSecurityException_ForPathTraversal()
    {
        // Arrange
        var manifest = new GameManifest
        {
            Id = "test-manifest",
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
