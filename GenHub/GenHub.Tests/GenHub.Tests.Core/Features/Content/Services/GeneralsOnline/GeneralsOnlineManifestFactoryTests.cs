using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.GeneralsOnline;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Unit tests for <see cref="GeneralsOnlineManifestFactory"/> and related dependency creation.
/// </summary>
public class GeneralsOnlineManifestFactoryTests : IDisposable
{
    private readonly Mock<IProviderDefinitionLoader> _providerLoaderMock;
    private readonly GeneralsOnlineManifestFactory _factory;
    private readonly string _tempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineManifestFactoryTests"/> class.
    /// </summary>
    public GeneralsOnlineManifestFactoryTests()
    {
        _providerLoaderMock = new Mock<IProviderDefinitionLoader>();
        _providerLoaderMock
            .Setup(l => l.GetProvider(PublisherTypeConstants.GeneralsOnline))
            .Returns(new ProviderDefinition
            {
                ProviderId = PublisherTypeConstants.GeneralsOnline,
                PublisherType = PublisherTypeConstants.GeneralsOnline,
                Description = "Community multiplayer for Generals Zero Hour",
                DefaultTags = ["multiplayer", "online"],
                Endpoints = new ProviderEndpoints
                {
                    WebsiteUrl = "https://example.com/go",
                },
            });

        _factory = new GeneralsOnlineManifestFactory(
            NullLogger<GeneralsOnlineManifestFactory>.Instance,
            _providerLoaderMock.Object);

        _tempDir = Path.Combine(Path.GetTempPath(), "GenHub_GOTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Cleans up temporary test directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that <see cref="GeneralsOnlineManifestFactory.CreateManifests"/> generates 3 manifests:
    /// 60Hz GameClient, QuickMatch MapPack, and GeneralsOnlineGameData data patch.
    /// </summary>
    [Fact]
    public void CreateManifests_GeneratesThreeManifests_IncludingGameDataPatch()
    {
        // Arrange
        var release = new GeneralsOnlineRelease
        {
            Version = "101525_QFE5",
            ReleaseDate = DateTime.UtcNow,
            PortableUrl = "https://example.com/GeneralsOnline_portable_101525_QFE5.zip",
            PortableSize = 1048576,
            Changelog = "https://example.com/changelog",
        };

        // Act
        var manifests = _factory.CreateManifests(release);

        // Assert
        Assert.Equal(3, manifests.Count);

        var gameClient = manifests.FirstOrDefault(m => m.ContentType == ContentType.GameClient);
        var mapPack = manifests.FirstOrDefault(m => m.ContentType == ContentType.MapPack);
        var gameDataPatch = manifests.FirstOrDefault(m => m.ContentType == ContentType.Patch);

        Assert.NotNull(gameClient);
        Assert.NotNull(mapPack);
        Assert.NotNull(gameDataPatch);

        // Verify GameClient manifest
        Assert.Contains(GeneralsOnlineConstants.Variant60HzSuffix, gameClient.Id.Value);
        Assert.Equal(GameType.ZeroHour, gameClient.TargetGame);
        Assert.Equal(GameClientConstants.GeneralsOnline60HzDisplayName, gameClient.Name);

        // Verify MapPack manifest
        Assert.Contains("quickmatchmaps", mapPack.Id.Value);
        Assert.Equal(GameType.ZeroHour, mapPack.TargetGame);
        Assert.Equal(GeneralsOnlineConstants.QuickMatchMapPackDisplayName, mapPack.Name);

        // Verify GameData Patch manifest
        Assert.Contains(GeneralsOnlineConstants.GameDataPatchSuffix, gameDataPatch.Id.Value);
        Assert.Equal(ContentType.Patch, gameDataPatch.ContentType);
        Assert.Equal(GameType.ZeroHour, gameDataPatch.TargetGame);
        Assert.Equal(GeneralsOnlineConstants.GameDataDisplayName, gameDataPatch.Name);
        Assert.Equal(GeneralsOnlineConstants.GameDataDescription, gameDataPatch.Metadata?.Description);
        Assert.Contains(GeneralsOnlineVariantTags.TagGameData, gameDataPatch.Metadata?.Tags ?? []);
    }

    /// <summary>
    /// Verifies that the GameData patch depends on the 60Hz GameClient and Zero Hour,
    /// while the 60Hz GameClient does not depend on the GameData patch (making GameData patch optional).
    /// </summary>
    [Fact]
    public void Dependencies_GameDataPatch_DependsOn60HzGameClientAndZeroHour_WhileGameClientDoesNotDependOnGameData()
    {
        // Arrange
        var release = new GeneralsOnlineRelease
        {
            Version = "101525_QFE5",
            ReleaseDate = DateTime.UtcNow,
            PortableUrl = "https://example.com/test.zip",
        };

        // Act
        var manifests = _factory.CreateManifests(release);
        var gameClient = manifests.First(m => m.ContentType == ContentType.GameClient);
        var gameDataPatch = manifests.First(m => m.ContentType == ContentType.Patch);

        // Assert - GameData patch has dependencies on Zero Hour and 60Hz GameClient
        Assert.NotEmpty(gameDataPatch.Dependencies);
        var zhDepInPatch = gameDataPatch.Dependencies.FirstOrDefault(d => d.DependencyType == ContentType.GameInstallation);
        var clientDepInPatch = gameDataPatch.Dependencies.FirstOrDefault(d => d.DependencyType == ContentType.GameClient);

        Assert.NotNull(zhDepInPatch);
        Assert.NotNull(clientDepInPatch);
        Assert.Contains(GeneralsOnlineConstants.Variant60HzSuffix, clientDepInPatch.Id.Value);
        Assert.False(clientDepInPatch.IsOptional);
        Assert.True(clientDepInPatch.StrictPublisher);
        Assert.Equal(PublisherTypeConstants.GeneralsOnline, clientDepInPatch.PublisherType);

        // Assert - GameClient dependencies do NOT include Patch dependency
        Assert.DoesNotContain(gameClient.Dependencies, d => d.DependencyType == ContentType.Patch);
        Assert.DoesNotContain(gameClient.Dependencies, d => d.Id.Value.Contains(GeneralsOnlineConstants.GameDataPatchSuffix));
    }

    /// <summary>
    /// Verifies that <see cref="GeneralsOnlineManifestFactory.CanHandle"/> returns true for GameClient, MapPack, and Patch.
    /// </summary>
    [Fact]
    public void CanHandle_WithValidManifestTypes_ReturnsTrue()
    {
        // Arrange
        var publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GeneralsOnline };

        var clientManifest = new ContentManifest { ContentType = ContentType.GameClient, Publisher = publisher };
        var mapPackManifest = new ContentManifest { ContentType = ContentType.MapPack, Publisher = publisher };
        var patchManifest = new ContentManifest { ContentType = ContentType.Patch, Publisher = publisher };
        var otherPublisherManifest = new ContentManifest { ContentType = ContentType.Patch, Publisher = new PublisherInfo { PublisherType = "other" } };
        var otherTypeManifest = new ContentManifest { ContentType = ContentType.Mod, Publisher = publisher };

        // Act & Assert
        Assert.True(_factory.CanHandle(clientManifest));
        Assert.True(_factory.CanHandle(mapPackManifest));
        Assert.True(_factory.CanHandle(patchManifest));
        Assert.False(_factory.CanHandle(otherPublisherManifest));
        Assert.False(_factory.CanHandle(otherTypeManifest));
    }

    /// <summary>
    /// Verifies that <see cref="GeneralsOnlineManifestFactory.CreateManifestsFromExtractedContentAsync"/> separates files
    /// correctly among GameClient, MapPack, and GameData Patch manifests.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test execution.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_SeparatesFilesCorrectly()
    {
        // Arrange: Create simulated extracted directory structure
        var exePath = Path.Combine(_tempDir, GameClientConstants.GeneralsOnline60HzExecutable);
        var dllPath = Path.Combine(_tempDir, "GameNetworkingSockets.dll");
        File.WriteAllText(exePath, "fake exe content");
        File.WriteAllText(dllPath, "fake dll content");

        var mapsDir = Path.Combine(_tempDir, GeneralsOnlineConstants.MapsSubdirectory, "Tournament Desert");
        Directory.CreateDirectory(mapsDir);
        var mapFilePath = Path.Combine(mapsDir, "Tournament Desert.map");
        File.WriteAllText(mapFilePath, "fake map content");

        var gameDataDir = Path.Combine(_tempDir, GeneralsOnlineConstants.GameDataSubdirectory);
        Directory.CreateDirectory(gameDataDir);
        var splashPath = Path.Combine(gameDataDir, "GOSplash.bmp");
        var mapCachePath = Path.Combine(gameDataDir, "MapCacheGO.ini");
        File.WriteAllText(splashPath, "fake splash bmp");
        File.WriteAllText(mapCachePath, "fake mapcache ini");

        var originalManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.1015255.generalsonline.gameclient.60hz"),
            Name = GameClientConstants.GeneralsOnline60HzDisplayName,
            Version = "101525_QFE5",
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GeneralsOnline },
            Metadata = new ContentMetadata { ReleaseDate = DateTime.UtcNow },
        };

        // Act
        var manifests = await _factory.CreateManifestsFromExtractedContentAsync(originalManifest, _tempDir, CancellationToken.None);

        // Assert
        Assert.Equal(3, manifests.Count);

        var gameClient = manifests.First(m => m.ContentType == ContentType.GameClient);
        var mapPack = manifests.First(m => m.ContentType == ContentType.MapPack);
        var gameDataPatch = manifests.First(m => m.ContentType == ContentType.Patch);

        // Check GameClient files
        Assert.Equal(2, gameClient.Files.Count);
        Assert.Contains(gameClient.Files, f => f.RelativePath == GameClientConstants.GeneralsOnline60HzExecutable && f.IsExecutable && f.InstallTarget == ContentInstallTarget.Workspace);
        Assert.Contains(gameClient.Files, f => f.RelativePath == "GameNetworkingSockets.dll" && !f.IsExecutable && f.InstallTarget == ContentInstallTarget.Workspace);
        Assert.DoesNotContain(gameClient.Files, f => f.RelativePath.Contains("Maps"));
        Assert.DoesNotContain(gameClient.Files, f => f.RelativePath.Contains("GeneralsOnlineGameData"));

        // Check MapPack files
        Assert.Single(mapPack.Files);
        var mapFile = mapPack.Files[0];
        Assert.Equal(ContentInstallTarget.UserMapsDirectory, mapFile.InstallTarget);
        Assert.False(mapFile.IsExecutable);
        Assert.EndsWith(".map", mapFile.RelativePath, StringComparison.OrdinalIgnoreCase);
        Assert.False(mapFile.RelativePath.StartsWith("Maps", StringComparison.OrdinalIgnoreCase));

        // Check GameData patch files
        Assert.Equal(2, gameDataPatch.Files.Count);
        Assert.All(gameDataPatch.Files, f =>
        {
            Assert.Equal(ContentInstallTarget.Workspace, f.InstallTarget);
            Assert.False(f.IsExecutable);
            Assert.StartsWith(GeneralsOnlineConstants.GameDataSubdirectory, f.RelativePath, StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(f.Hash);
        });
        Assert.Contains(gameDataPatch.Files, f => f.RelativePath.EndsWith("GOSplash.bmp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(gameDataPatch.Files, f => f.RelativePath.EndsWith("MapCacheGO.ini", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that directories with names starting with "Maps" or "GeneralsOnlineGameData" (e.g. Maps_backup, GeneralsOnlineGameData_backup)
    /// are not misclassified as Maps or GeneralsOnlineGameData.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_SiblingDirectories_AreNotMisclassified()
    {
        // Arrange
        var siblingMapDir = Path.Combine(_tempDir, "Maps_backup");
        Directory.CreateDirectory(siblingMapDir);
        File.WriteAllText(Path.Combine(siblingMapDir, "backup.map"), "fake map backup");

        var siblingGameDataDir = Path.Combine(_tempDir, "GeneralsOnlineGameData_backup");
        Directory.CreateDirectory(siblingGameDataDir);
        File.WriteAllText(Path.Combine(siblingGameDataDir, "backup.ini"), "fake ini backup");

        var originalManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.1015255.generalsonline.gameclient.60hz"),
            Name = GameClientConstants.GeneralsOnline60HzDisplayName,
            Version = "101525_QFE5",
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GeneralsOnline },
            Metadata = new ContentMetadata { ReleaseDate = DateTime.UtcNow },
        };

        // Act
        var manifests = await _factory.CreateManifestsFromExtractedContentAsync(originalManifest, _tempDir, CancellationToken.None);

        var gameClient = manifests.First(m => m.ContentType == ContentType.GameClient);
        var mapPack = manifests.First(m => m.ContentType == ContentType.MapPack);
        var gameDataPatch = manifests.First(m => m.ContentType == ContentType.Patch);

        // Assert - MapPack and GameData patch must not contain sibling files
        Assert.Empty(mapPack.Files);
        Assert.Empty(gameDataPatch.Files);

        // Assert - GameClient must contain the sibling files as workspace files
        Assert.Contains(gameClient.Files, f => f.RelativePath.Contains("Maps_backup"));
        Assert.Contains(gameClient.Files, f => f.RelativePath.Contains("GeneralsOnlineGameData_backup"));
    }

    /// <summary>
    /// Verifies <see cref="GeneralsOnlineDependencyBuilder"/> directly returns the expected GameData dependencies.
    /// </summary>
    [Fact]
    public void DependencyBuilder_GetDependenciesForGameData_ReturnsExpectedDependencies()
    {
        // Act
        var dependencies = GeneralsOnlineDependencyBuilder.GetDependenciesForGameData(1015255);

        // Assert
        Assert.Equal(2, dependencies.Count);
        Assert.Contains(dependencies, d => d.DependencyType == ContentType.GameInstallation);
        Assert.Contains(dependencies, d => d.DependencyType == ContentType.GameClient && d.Id.Value.Contains("60hz"));

        var builder = new GeneralsOnlineDependencyBuilder();
        var patchManifest = new ContentManifest
        {
            ContentType = ContentType.Patch,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GeneralsOnline },
        };
        var resolvedDeps = builder.GetDependencies(patchManifest);
        Assert.Equal(2, resolvedDeps.Count);
        Assert.Contains(resolvedDeps, d => d.DependencyType == ContentType.GameInstallation);
        Assert.Contains(resolvedDeps, d => d.DependencyType == ContentType.GameClient);
    }
}
