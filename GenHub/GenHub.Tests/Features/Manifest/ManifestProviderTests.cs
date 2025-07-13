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

namespace GenHub.Tests.Features.Manifest;

public class ManifestProviderTests
{
    private readonly Mock<ILogger<ManifestProvider>> _loggerMock;
    private readonly Mock<IManifestCache> _cacheMock;
    private readonly ManifestProvider _manifestProvider;

    public ManifestProviderTests()
    {
        _loggerMock = new Mock<ILogger<ManifestProvider>>();
        _cacheMock = new Mock<IManifestCache>();
        _manifestProvider = new ManifestProvider(_loggerMock.Object, _cacheMock.Object);
    }

    [Fact]
    public async Task GetManifestAsync_WithGameVersion_ReturnsFromCache_WhenAvailable()
    {
        // Arrange
        var gameVersion = new GameVersion 
        { 
            Id = "test-version", 
            Name = "Test Version" 
        };
        var expectedManifest = new GameManifest 
        { 
            Id = "test-version", 
            Name = "Test Manifest" 
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

    [Fact]
    public async Task GetManifestAsync_WithGameInstallation_BuildsCorrectManifestId()
    {
        // Arrange
        var installation = new GameInstallation
        {
            InstallationType = GameInstallationType.Origin,
            HasGenerals = true,
            HasZeroHour = false
        };
        var expectedManifest = new GameManifest 
        { 
            Id = "Origin.Generals", 
            Name = "Test Manifest" 
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

    [Fact]
    public async Task GetManifestAsync_WithZeroHourInstallation_UsesZeroHourId()
    {
        // Arrange
        var installation = new GameInstallation
        {
            InstallationType = GameInstallationType.Steam,
            HasGenerals = true,
            HasZeroHour = true
        };
        var expectedManifest = new GameManifest 
        { 
            Id = "Steam.ZeroHour", 
            Name = "Test Manifest" 
        };
        
        _cacheMock.Setup(x => x.GetManifest("Steam.ZeroHour"))
                  .Returns(expectedManifest);

        // Act
        var result = await _manifestProvider.GetManifestAsync(installation);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Steam.ZeroHour", result.Id);
    }

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
        var result = await _manifestProvider.GetManifestAsync(gameVersion);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetManifestAsync_ThrowsValidationException_WhenManifestIdMismatch()
    {
        // Arrange
        var gameVersion = new GameVersion 
        { 
            Id = "expected-id", 
            Name = "Test Version" 
        };
        
        _cacheMock.Setup(x => x.GetManifest("expected-id"))
                  .Returns((GameManifest?)null);

        // Act & Assert
        await Assert.ThrowsAsync<ManifestValidationException>(
            () => _manifestProvider.GetManifestAsync(gameVersion));
    }

    [Fact]
    public async Task ValidateManifestSecurity_ThrowsSecurityException_ForPathTraversal()
    {
        // Arrange
        var manifest = new GameManifest
        {
            Id = "test-manifest",
            Files = new List<ManifestFile>
            {
                new() { RelativePath = "../malicious/path" }
            }
        };

        // Act & Assert
        Assert.Throws<ManifestSecurityException>(() => 
            typeof(ManifestProvider)
                .GetMethod("ValidateManifestSecurity", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.Invoke(null, new object[] { manifest }));
    }
}
