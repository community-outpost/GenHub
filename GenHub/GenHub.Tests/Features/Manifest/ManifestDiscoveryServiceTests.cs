using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Manifest;
using GenHub.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Features.Manifest;
public partial class ManifestDiscoveryServiceTests(Mock<ILogger<ManifestDiscoveryService>> loggerMock, Mock<IManifestCache> cacheMock)
{
    private readonly ManifestDiscoveryService _discoveryService = new(loggerMock.Object, cacheMock.Object);

    private readonly Mock<ILogger<ManifestDiscoveryService>> _loggerMock;
    private readonly Mock<IManifestCache> _cacheMock;
    private readonly ManifestDiscoveryService _discoveryService;

    [Fact]
    public void GetManifestsByType_FiltersCorrectly()
    {
        // Arrange
        var manifests = new Dictionary<string, GameManifest>
        {
            ["base1"] = new() { Id = "base1", ContentType = ContentType.BaseGame },
            ["mod1"] = new() { Id = "mod1", ContentType = ContentType.Mod },
            ["base2"] = new() { Id = "base2", ContentType = ContentType.BaseGame }
        };

        // Act
        var baseGames = ManifestDiscoveryService.GetManifestsByType(manifests, ContentType.BaseGame);
        var mods = ManifestDiscoveryService.GetManifestsByType(manifests, ContentType.Mod);

        // Assert
        Assert.Equal(2, baseGames.Count());
        Assert.Single(mods);
    }

    [Fact]
    public void GetCompatibleManifests_FiltersCorrectly()
    {
        // Arrange
        var manifests = new Dictionary<string, GameManifest>
        {
            ["generals1"] = new() { Id = "generals1", TargetGame = GameType.Generals },
            ["zerohour1"] = new() { Id = "zerohour1", TargetGame = GameType.ZeroHour },
            ["generals2"] = new() { Id = "generals2", TargetGame = GameType.Generals }
        };

        // Act
        var generalsCompatible = ManifestDiscoveryService.GetCompatibleManifests(manifests, GameType.Generals);
        var zeroHourCompatible = ManifestDiscoveryService.GetCompatibleManifests(manifests, GameType.ZeroHour);

        // Assert
        Assert.Equal(2, generalsCompatible.Count());
        Assert.Single(zeroHourCompatible);
    }

    [Fact]
    public void ValidateDependencies_ReturnsFalse_WhenRequiredDependencyMissing()
    {
        // Arrange
        var manifest = new GameManifest 
        { 
            Id = "test",
            Dependencies = new List<ContentDependency>
            {
                new() { Id = "missing-dep", InstallBehavior = DependencyInstallBehavior.RequireExisting }
            }
        };
        var availableManifests = new Dictionary<string, GameManifest>();

        // Act
        var result = _discoveryService.ValidateDependencies(manifest, availableManifests);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ValidateDependencies_ReturnsTrue_WhenAllRequiredDependenciesPresent()
    {
        // Arrange
        var manifest = new GameManifest 
        { 
            Id = "test",
            Dependencies = new List<ContentDependency>
            {
                new() { Id = "dep1", InstallBehavior = DependencyInstallBehavior.RequireExisting },
                new() { Id = "dep2", InstallBehavior = DependencyInstallBehavior.Suggest }
            }
        };
        var availableManifests = new Dictionary<string, GameManifest>
        {
            ["dep1"] = new() { Id = "dep1", Version = "1.0" }
        };

        // Act
        var result = _discoveryService.ValidateDependencies(manifest, availableManifests);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateDependencies_ReturnsTrue_WhenNoDependencies()
    {
        // Arrange
        var manifest = new GameManifest 
        { 
            Id = "test",
            Dependencies = new List<ContentDependency>()
        };
        var availableManifests = new Dictionary<string, GameManifest>();

        // Act
        var result = _discoveryService.ValidateDependencies(manifest, availableManifests);

        // Assert
        Assert.True(result);
    }
}
