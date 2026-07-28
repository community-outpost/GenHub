using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Manifest;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Features.Manifest;

/// <summary>
/// Unit tests for the <see cref="ManifestDiscoveryService"/> class.
/// </summary>
public class ManifestDiscoveryServiceTests : IDisposable
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
    /// Temporary directory used for filesystem discovery tests.
    /// </summary>
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestDiscoveryServiceTests"/> class.
    /// </summary>
    public ManifestDiscoveryServiceTests()
    {
        _loggerMock = new Mock<ILogger<ManifestDiscoveryService>>();
        _cacheMock = new Mock<IManifestCache>();
        _discoveryService = new ManifestDiscoveryService(_loggerMock.Object, _cacheMock.Object);
        _tempDirectory = Directory.CreateTempSubdirectory("GenHub.ManifestDiscoveryTests.").FullName;
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
            ["1.0.steam.gameinstallation.generals"] = new() { Id = "1.0.steam.gameinstallation.generals", ContentType = ContentType.GameInstallation },
            ["1.0.genhub.mod.mod1content"] = new() { Id = "1.0.genhub.mod.mod1content", ContentType = ContentType.Mod },
            ["1.0.eaapp.gameinstallation.generals"] = new() { Id = "1.0.eaapp.gameinstallation.generals", ContentType = ContentType.GameInstallation },
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
            ["1.0.steam.gameinstallation.generals"] = new() { Id = "1.0.steam.gameinstallation.generals", TargetGame = GameType.Generals },
            ["1.0.eaapp.gameinstallation.zerohour"] = new() { Id = "1.0.eaapp.gameinstallation.zerohour", TargetGame = GameType.ZeroHour },
            ["1.0.retail.gameinstallation.generals"] = new() { Id = "1.0.retail.gameinstallation.generals", TargetGame = GameType.Generals },
        };

        // Act
        var generalsCompatible = ManifestDiscoveryService.GetCompatibleManifests(manifests, GameType.Generals);
        var zeroHourCompatible = ManifestDiscoveryService.GetCompatibleManifests(manifests, GameType.ZeroHour);

        // Assert
        Assert.Equal(2, generalsCompatible.Count());
        Assert.Single(zeroHourCompatible);
    }

    /// <summary>
    /// Tests that manifest discovery finds JSON manifests in nested directories and ignores non-JSON files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverManifestsAsync_DiscoversNestedJsonManifest_AndIgnoresNonJsonFile()
    {
        // Arrange
        const string nestedManifestId = "1.0.genhub.mod.nested";
        const string ignoredManifestId = "1.0.genhub.mod.ignored";
        var nestedDirectory = Path.Combine(_tempDirectory, "content", "manifests");
        Directory.CreateDirectory(nestedDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(nestedDirectory, "nested.json"),
            SerializeManifest(nestedManifestId));
        await File.WriteAllTextAsync(
            Path.Combine(_tempDirectory, "ignored.txt"),
            SerializeManifest(ignoredManifestId));

        // Act
        var manifests = await _discoveryService.DiscoverManifestsAsync([_tempDirectory]);

        // Assert
        Assert.Single(manifests);
        Assert.Contains(nestedManifestId, manifests);
        Assert.DoesNotContain(ignoredManifestId, manifests);
    }

    /// <summary>
    /// Tests that a malformed JSON file does not prevent other nested manifests from being discovered.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverManifestsAsync_WithMalformedJson_ContinuesDiscoveringNestedManifest()
    {
        // Arrange
        const string nestedManifestId = "1.0.genhub.mod.valid";
        var nestedDirectory = Path.Combine(_tempDirectory, "content", "manifests");
        Directory.CreateDirectory(nestedDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(nestedDirectory, "valid.json"),
            SerializeManifest(nestedManifestId));
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "malformed.json"), "{ invalid json");

        // Act
        var manifests = await _discoveryService.DiscoverManifestsAsync([_tempDirectory]);

        // Assert
        var manifest = Assert.Single(manifests);
        Assert.Equal(nestedManifestId, manifest.Key);
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
            Id = ManifestId.Create("1.0.genhub.mod.content"),
            Dependencies = new List<ContentDependency>
            {
                new() { Id = ManifestId.Create("1.0.genhub.mod.missing"), InstallBehavior = DependencyInstallBehavior.RequireExisting },
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
            Id = ManifestId.Create("1.0.genhub.mod.content"),
            Dependencies = new List<ContentDependency>
            {
                new() { Id = ManifestId.Create("1.0.genhub.mod.dep1"), InstallBehavior = DependencyInstallBehavior.RequireExisting },
                new() { Id = ManifestId.Create("1.0.genhub.mod.dep2"), InstallBehavior = DependencyInstallBehavior.Suggest },
            },
        };
        var availableManifests = new Dictionary<string, ContentManifest>
        {
            ["1.0.genhub.mod.dep1"] = new() { Id = ManifestId.Create("1.0.genhub.mod.dep1"), Version = "1.0" },
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
            Id = ManifestId.Create("1.0.genhub.mod.content"),
            Dependencies = new List<ContentDependency>(),
        };
        var availableManifests = new Dictionary<string, ContentManifest>();

        // Act
        var result = _discoveryService.ValidateDependencies(manifest, availableManifests);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Deletes temporary files created by filesystem discovery tests.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup should not fail an otherwise successful test.
        }
    }

    private static string SerializeManifest(string id)
    {
        return JsonSerializer.Serialize(new ContentManifest { Id = ManifestId.Create(id) });
    }
}
