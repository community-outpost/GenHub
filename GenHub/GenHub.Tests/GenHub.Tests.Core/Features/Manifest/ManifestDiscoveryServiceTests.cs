using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Manifest;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Features.Manifest;

/// <summary>
/// Unit tests for the <see cref="ManifestDiscoveryService"/> class.
/// </summary>
public class ManifestDiscoveryServiceTests
{
    /// <summary>
    /// Mock logger for the manifest discovery service.
    /// </summary>
    private readonly Mock<ILogger<ManifestDiscoveryService>> _loggerMock;

    /// <summary>
    /// Mock manifest cache.
    /// </summary>
    private readonly Mock<IManifestCache> _cacheMock;

    /// <summary>
    /// The manifest discovery service under test.
    /// </summary>
    private readonly ManifestDiscoveryService _discoveryService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestDiscoveryServiceTests"/> class.
    /// </summary>
    public ManifestDiscoveryServiceTests()
    {
        _loggerMock = new Mock<ILogger<ManifestDiscoveryService>>();
        _cacheMock = new Mock<IManifestCache>();
        _discoveryService = new ManifestDiscoveryService(_loggerMock.Object, _cacheMock.Object);
    }

    /// <summary>
    /// Tests that GetManifestsByType filters manifests correctly by content type.
    /// </summary>
    [Fact]
    public void GetManifestsByType_FiltersCorrectly()
    {
        // Arrange
        var manifests = new Dictionary<string, ContentManifest>
        {
            ["base1"] = new() { Id = "base1", ContentType = ContentType.GameInstallation },
            ["mod1"] = new() { Id = "mod1", ContentType = ContentType.Mod },
            ["base2"] = new() { Id = "base2", ContentType = ContentType.GameInstallation },
        };

        // Act
        var baseGames = ManifestDiscoveryService.GetManifestsByType(manifests, ContentType.GameInstallation);
        var mods = ManifestDiscoveryService.GetManifestsByType(manifests, ContentType.Mod);

        // Assert
        Assert.Equal(2, baseGames.Count());
        Assert.Single(mods);
    }

    /// <summary>
    /// Tests that GetCompatibleManifests filters manifests correctly by game type.
    /// </summary>
    [Fact]
    public void GetCompatibleManifests_FiltersCorrectly()
    {
        // Arrange
        var manifests = new Dictionary<string, ContentManifest>
        {
            ["generals1"] = new() { Id = "generals1", TargetGame = GameType.Generals },
            ["zerohour1"] = new() { Id = "zerohour1", TargetGame = GameType.ZeroHour },
            ["generals2"] = new() { Id = "generals2", TargetGame = GameType.Generals },
        };

        // Act
        var generalsCompatible = ManifestDiscoveryService.GetCompatibleManifests(manifests, GameType.Generals);
        var zeroHourCompatible = ManifestDiscoveryService.GetCompatibleManifests(manifests, GameType.ZeroHour);

        // Assert
        Assert.Equal(2, generalsCompatible.Count());
        Assert.Single(zeroHourCompatible);
    }

    /// <summary>
    /// Tests that ValidateDependencies returns false when a required dependency is missing.
    /// </summary>
    [Fact]
    public void ValidateDependencies_ReturnsFalse_WhenRequiredDependencyMissing()
    {
        // Arrange
        var manifest = new ContentManifest
        {
            Id = "test",
            Dependencies = new List<ContentDependency>
            {
                new() { Id = "missing-dep", InstallBehavior = DependencyInstallBehavior.RequireExisting },
            },
        };
        var availableManifests = new Dictionary<string, ContentManifest>();

        // Act
        var result = _discoveryService.ValidateDependencies(manifest, availableManifests);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Tests that ValidateDependencies returns true when all required dependencies are present.
    /// </summary>
    [Fact]
    public void ValidateDependencies_ReturnsTrue_WhenAllRequiredDependenciesPresent()
    {
        // Arrange
        var manifest = new ContentManifest
        {
            Id = "test",
            Dependencies = new List<ContentDependency>
            {
                new() { Id = "dep1", InstallBehavior = DependencyInstallBehavior.RequireExisting },
                new() { Id = "dep2", InstallBehavior = DependencyInstallBehavior.Suggest },
            },
        };
        var availableManifests = new Dictionary<string, ContentManifest>
        {
            ["dep1"] = new() { Id = "dep1", Version = "1.0" },
        };

        // Act
        var result = _discoveryService.ValidateDependencies(manifest, availableManifests);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Tests that ValidateDependencies returns true when manifest has no dependencies.
    /// </summary>
    [Fact]
    public void ValidateDependencies_ReturnsTrue_WhenNoDependencies()
    {
        // Arrange
        var manifest = new ContentManifest
        {
            Id = "test",
            Dependencies = new List<ContentDependency>(),
        };
        var availableManifests = new Dictionary<string, ContentManifest>();

        // Act
        var result = _discoveryService.ValidateDependencies(manifest, availableManifests);

        // Assert
        Assert.True(result);
    }
}
