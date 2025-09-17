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
            ["1.0.base1.installation.game"] = new() { Id = "1.0.base1.installation.game", ContentType = ContentType.GameInstallation },
            ["1.0.mod1.publisher.content"] = new() { Id = "1.0.mod1.publisher.content", ContentType = ContentType.Mod },
            ["1.0.base2.installation.game"] = new() { Id = "1.0.base2.installation.game", ContentType = ContentType.GameInstallation },
        };

        // Act
        var gameInstallations = ManifestDiscoveryService.GetManifestsByType(manifests, ContentType.GameInstallation);
        var mods = ManifestDiscoveryService.GetManifestsByType(manifests, ContentType.Mod);

        // Assert
        Assert.Equal(2, gameInstallations.Count());
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
            ["1.0.generals1.installation.game"] = new() { Id = "1.0.generals1.installation.game", TargetGame = GameType.Generals },
            ["1.0.zerohour1.installation.game"] = new() { Id = "1.0.zerohour1.installation.game", TargetGame = GameType.ZeroHour },
            ["1.0.generals2.installation.game"] = new() { Id = "1.0.generals2.installation.game", TargetGame = GameType.Generals },
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
            Id = ManifestId.Create("1.0.test.publisher.content"),
            Dependencies = new List<ContentDependency>
            {
                new() { Id = ManifestId.Create("1.0.missing.publisher.content"), InstallBehavior = DependencyInstallBehavior.RequireExisting },
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
            Id = ManifestId.Create("1.0.test.publisher.content"),
            Dependencies = new List<ContentDependency>
            {
                new() { Id = ManifestId.Create("1.0.dep1.publisher.content"), InstallBehavior = DependencyInstallBehavior.RequireExisting },
                new() { Id = ManifestId.Create("1.0.dep2.publisher.content"), InstallBehavior = DependencyInstallBehavior.Suggest },
            },
        };
        var availableManifests = new Dictionary<string, ContentManifest>
        {
            ["1.0.dep1.publisher.content"] = new() { Id = ManifestId.Create("1.0.dep1.publisher.content"), Version = "1.0" },
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
            Id = ManifestId.Create("1.0.test.publisher.content"),
            Dependencies = new List<ContentDependency>(),
        };
        var availableManifests = new Dictionary<string, ContentManifest>();

        // Act
        var result = _discoveryService.ValidateDependencies(manifest, availableManifests);

        // Assert
        Assert.True(result);
    }
}
