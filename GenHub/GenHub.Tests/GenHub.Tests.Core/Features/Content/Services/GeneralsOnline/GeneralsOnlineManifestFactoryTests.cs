using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.GeneralsOnline;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    /// Verifies that <see cref="GeneralsOnlineManifestFactory.CreateManifests"/> throws <see cref="ArgumentException"/>
    /// when the QFE value in the version string exceeds 9, preventing ambiguous legacy manifest IDs.
    /// </summary>
    [Fact]
    public void CreateManifests_WithQfeExceedingNine_ThrowsArgumentException()
    {
        // Arrange
        var release = new GeneralsOnlineRelease
        {
            Version = "101525_QFE10",
            ReleaseDate = DateTime.UtcNow,
            PortableUrl = "https://example.com/GeneralsOnline_portable_101525_QFE10.zip",
            PortableSize = 1048576,
            Changelog = "https://example.com/changelog",
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => _factory.CreateManifests(release));
        Assert.Contains("exceeding 9", ex.Message);
    }

    /// <summary>
    /// Verifies that <see cref="GeneralsOnlineManifestFactory.CreateManifests"/> generates 4 manifests:
    /// 60Hz GameClient, Test Environment GameClient, QuickMatch MapPack, and GeneralsOnlineGameData data patch.
    /// </summary>
    [Fact]
    public void CreateManifests_GeneratesFourManifests_IncludingTestEnvironmentAndGameDataPatch()
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
        Assert.Equal(4, manifests.Count);

        var gameClient60Hz = manifests.FirstOrDefault(m => m.ContentType == ContentType.GameClient && m.Id.Value.EndsWith(GeneralsOnlineConstants.Variant60HzSuffix));
        var gameClientTest = manifests.FirstOrDefault(m => m.ContentType == ContentType.GameClient && m.Id.Value.EndsWith(GeneralsOnlineConstants.VariantTestEnvironmentSuffix));
        var mapPack = manifests.FirstOrDefault(m => m.ContentType == ContentType.MapPack);
        var gameDataPatch = manifests.FirstOrDefault(m => m.ContentType == ContentType.Patch);

        Assert.NotNull(gameClient60Hz);
        Assert.NotNull(gameClientTest);
        Assert.NotNull(mapPack);
        Assert.NotNull(gameDataPatch);

        // Verify 60Hz GameClient manifest
        Assert.Contains(GeneralsOnlineConstants.Variant60HzSuffix, gameClient60Hz.Id.Value);
        Assert.Equal(GameType.ZeroHour, gameClient60Hz.TargetGame);
        Assert.Equal(GameClientConstants.GeneralsOnline60HzDisplayName, gameClient60Hz.Name);

        // Verify Test Environment GameClient manifest
        Assert.Contains(GeneralsOnlineConstants.VariantTestEnvironmentSuffix, gameClientTest.Id.Value);
        Assert.Equal(GameType.ZeroHour, gameClientTest.TargetGame);
        Assert.Equal(GameClientConstants.GeneralsOnlineTestEnvironmentDisplayName, gameClientTest.Name);
        Assert.Contains(GeneralsOnlineVariantTags.TagTestEnvironment, gameClientTest.Metadata?.Tags ?? []);

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

        // Verify OriginalContentId provenance mapping
        Assert.Equal($"{GeneralsOnlineConstants.ContentIdPrefix}101525_QFE5", gameClient60Hz.OriginalContentId);
        Assert.Equal($"{GeneralsOnlineConstants.ContentIdPrefix}101525_QFE5_MapPack", mapPack.OriginalContentId);
        Assert.Equal($"{GeneralsOnlineConstants.ContentIdPrefix}101525_QFE5_Patch", gameDataPatch.OriginalContentId);
    }

    /// <summary>
    /// Verifies that all variants from the same release share a single versioned variant group id
    /// and family name so they collapse into a single content card with variants.
    /// </summary>
    [Fact]
    public void CreateManifests_SameRelease_SharesVersionVariantGroup()
    {
        // Arrange
        var release = new GeneralsOnlineRelease
        {
            Version = "032926_QFE1",
            ReleaseDate = DateTime.UtcNow,
            PortableUrl = "https://example.com/GeneralsOnline_portable_032926_QFE1.zip",
            PortableSize = 1048576,
            Changelog = "https://example.com/changelog",
        };

        // Act
        var manifests = _factory.CreateManifests(release);

        // Assert
        Assert.Equal(4, manifests.Count);
        Assert.All(manifests, m =>
        {
            Assert.Equal("generalsonline-032926_qfe1", m.Metadata?.VariantGroupId);
            Assert.Equal("Generals Online 032926_QFE1", m.Metadata?.VariantFamilyName);
            Assert.Equal(GeneralsOnlineConstants.CoverSource, m.Metadata?.CoverUrl);
        });
    }

    /// <summary>
    /// Verifies that different QFE builds of one date produce distinct variant groups.
    /// </summary>
    [Fact]
    public void CreateManifests_DifferentQfe_ProducesDistinctVariantGroups()
    {
        // Arrange
        GeneralsOnlineRelease CreateRelease(string version) => new()
        {
            Version = version,
            ReleaseDate = DateTime.UtcNow,
            PortableUrl = $"https://example.com/GeneralsOnline_portable_{version}.zip",
            PortableSize = 1048576,
            Changelog = "https://example.com/changelog",
        };

        // Act
        var first = _factory.CreateManifests(CreateRelease("032926_QFE1"));
        var second = _factory.CreateManifests(CreateRelease("032926_QFE2"));

        // Assert
        Assert.NotEqual(
            first[0].Metadata?.VariantGroupId,
            second[0].Metadata?.VariantGroupId);
    }

    /// <summary>
    /// Verifies that the GameData patch depends only on Zero Hour (decoupled so other game clients can use it),
    /// while the GameClients (60Hz and Test Environment) have an auto-install optional dependency on the GameData patch.
    /// </summary>
    [Fact]
    public void Dependencies_GameDataPatch_DependsOnlyOnZeroHour_WhileGameClientsHaveAutoInstallOptionalGameData()
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
        var gameClient60Hz = manifests.First(m => m.ContentType == ContentType.GameClient && m.Id.Value.EndsWith(GeneralsOnlineConstants.Variant60HzSuffix));
        var gameClientTest = manifests.First(m => m.ContentType == ContentType.GameClient && m.Id.Value.EndsWith(GeneralsOnlineConstants.VariantTestEnvironmentSuffix));
        var gameDataPatch = manifests.First(m => m.ContentType == ContentType.Patch);

        // Assert - GameData patch depends only on Zero Hour, NOT on GameClient
        var zhDepInPatch = Assert.Single(gameDataPatch.Dependencies);
        Assert.Equal(ContentType.GameInstallation, zhDepInPatch.DependencyType);
        Assert.DoesNotContain(gameDataPatch.Dependencies, d => d.DependencyType == ContentType.GameClient);

        // Assert - 60Hz GameClient has optional auto-install GameData patch dependency
        var gameDataDepIn60Hz = gameClient60Hz.Dependencies.FirstOrDefault(d => d.DependencyType == ContentType.Patch);
        Assert.NotNull(gameDataDepIn60Hz);
        Assert.Equal(gameDataPatch.Id.Value, gameDataDepIn60Hz.Id.Value);
        Assert.True(gameDataDepIn60Hz.IsOptional);
        Assert.Equal(DependencyInstallBehavior.AutoInstall, gameDataDepIn60Hz.InstallBehavior);

        // Assert - Test Environment GameClient also has optional auto-install GameData patch dependency
        var gameDataDepInTest = gameClientTest.Dependencies.FirstOrDefault(d => d.DependencyType == ContentType.Patch);
        Assert.NotNull(gameDataDepInTest);
        Assert.Equal(gameDataPatch.Id.Value, gameDataDepInTest.Id.Value);
        Assert.True(gameDataDepInTest.IsOptional);
        Assert.Equal(DependencyInstallBehavior.AutoInstall, gameDataDepInTest.InstallBehavior);
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
    public async Task CreateManifestsFromExtractedContentAsync_SeparatesFilesCorrectlyAsync()
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
        var bigPath = Path.Combine(gameDataDir, "500_900_CommunityPatch_CoreINI.big");
        File.WriteAllText(bigPath, "fake big content");

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
        var result = await _factory.CreateManifestsFromExtractedContentAsync(originalManifest, _tempDir, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var manifests = result.Data!;
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
        Assert.Single(gameDataPatch.Files);
        Assert.All(gameDataPatch.Files, f =>
        {
            Assert.Equal(ContentInstallTarget.UserDataDirectory, f.InstallTarget);
            Assert.False(f.IsExecutable);
            Assert.StartsWith(GeneralsOnlineConstants.GameDataSubdirectory, f.RelativePath, StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(f.Hash);
        });
        Assert.Contains(gameDataPatch.Files, f => f.RelativePath.EndsWith("500_900_CommunityPatch_CoreINI.big", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that directories with names starting with "Maps" or "GeneralsOnlineGameData" (e.g. Maps_backup, GeneralsOnlineGameData_backup)
    /// are not misclassified as Maps or GeneralsOnlineGameData.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_SiblingDirectories_AreNotMisclassifiedAsync()
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
        var result = await _factory.CreateManifestsFromExtractedContentAsync(originalManifest, _tempDir, CancellationToken.None);

        // Assert - MapPack and GameData patch are omitted because they have 0 files
        Assert.True(result.Success);
        var manifests = result.Data!;
        Assert.Single(manifests);
        var gameClient = manifests.Single();
        Assert.Equal(ContentType.GameClient, gameClient.ContentType);
        Assert.DoesNotContain(manifests, m => m.ContentType == ContentType.MapPack);
        Assert.DoesNotContain(manifests, m => m.ContentType == ContentType.Patch);

        // Assert - GameClient must contain the sibling files as workspace files
        Assert.Contains(gameClient.Files, f => f.RelativePath.Contains("Maps_backup"));
        Assert.Contains(gameClient.Files, f => f.RelativePath.Contains("GeneralsOnlineGameData_backup"));
    }

    /// <summary>
    /// Verifies that <see cref="GeneralsOnlineManifestFactory.CreateManifestsFromExtractedContentAsync"/> throws
    /// <see cref="OperationCanceledException"/> when passed a cancelled token.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_PreCancelledToken_ThrowsOperationCanceledExceptionAsync()
    {
        // Arrange
        var originalManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.1015255.generalsonline.gameclient.60hz"),
            Name = GameClientConstants.GeneralsOnline60HzDisplayName,
            Version = "101525_QFE5",
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GeneralsOnline },
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _factory.CreateManifestsFromExtractedContentAsync(originalManifest, _tempDir, cts.Token));
    }

    /// <summary>
    /// Verifies that GameData patch metadata tags do not contain duplicate tags.
    /// </summary>
    [Fact]
    public void CreateManifests_GameDataPatchTags_HasNoDuplicateTags()
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
        var gameDataPatch = manifests.First(m => m.ContentType == ContentType.Patch);

        // Assert
        var tags = gameDataPatch.Metadata?.Tags ?? [];
        var distinctTags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(distinctTags.Count, tags.Count);
        Assert.Contains("gamedata", tags);
        Assert.Contains("patch", tags);
        Assert.Contains("generalsonline", tags);
    }

    /// <summary>
    /// Verifies that <see cref="GeneralsOnlineManifestFactory.CreateManifestsFromExtractedContentAsync"/> returns
    /// a failure result when the GameClient manifest has zero files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_EmptyGameClient_ReturnsFailureAsync()
    {
        // Arrange: Only create map files, no GameClient files
        var mapsDir = Path.Combine(_tempDir, GeneralsOnlineConstants.MapsSubdirectory, "TestMap");
        Directory.CreateDirectory(mapsDir);
        File.WriteAllText(Path.Combine(mapsDir, "TestMap.map"), "fake map");

        var originalManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.1015255.generalsonline.gameclient.60hz"),
            Name = GameClientConstants.GeneralsOnline60HzDisplayName,
            Version = "101525_QFE5",
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GeneralsOnline },
        };

        // Act
        var result = await _factory.CreateManifestsFromExtractedContentAsync(originalManifest, _tempDir, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("has no files in extract path", result.FirstError);
    }

    /// <summary>
    /// Verifies <see cref="GeneralsOnlineDependencyBuilder"/> directly returns the expected GameData dependencies.
    /// </summary>
    [Fact]
    public void DependencyBuilder_GetDependenciesForGameData_ReturnsExpectedDependencies()
    {
        // Act
        var dependencies = GeneralsOnlineDependencyBuilder.GetDependenciesForGameData(1015255);

        // Assert - decoupled from 60Hz GameClient, only requires Zero Hour installation
        var zhDep = Assert.Single(dependencies);
        Assert.Equal(ContentType.GameInstallation, zhDep.DependencyType);
        Assert.DoesNotContain(dependencies, d => d.DependencyType == ContentType.GameClient);

        var builder = new GeneralsOnlineDependencyBuilder();
        var patchManifest = new ContentManifest
        {
            Version = "101525_QFE5",
            ContentType = ContentType.Patch,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GeneralsOnline },
        };
        var resolvedDeps = builder.GetDependencies(patchManifest);
        var resolvedZhDep = Assert.Single(resolvedDeps);
        Assert.Equal(ContentType.GameInstallation, resolvedZhDep.DependencyType);
        Assert.DoesNotContain(resolvedDeps, d => d.DependencyType == ContentType.GameClient);
    }

    /// <summary>
    /// Verifies that CreateManifests propagates Sha256 to file hash and installation instructions download hash.
    /// </summary>
    [Fact]
    public void CreateManifests_WithSha256_SetsFileHashAndDownloadHash()
    {
        // Arrange
        const string expectedHash = "abc123hash";
        var release = new GeneralsOnlineRelease
        {
            Version = "101525_QFE5",
            ReleaseDate = DateTime.UtcNow,
            PortableUrl = "https://example.com/GeneralsOnline_portable_101525_QFE5.zip",
            PortableSize = 1048576,
            Sha256 = expectedHash,
            Changelog = "https://example.com/changelog",
        };

        // Act
        var manifests = _factory.CreateManifests(release);

        // Assert
        var gameClient = manifests.FirstOrDefault(m => m.ContentType == ContentType.GameClient);
        Assert.NotNull(gameClient);
        Assert.Equal(expectedHash, gameClient.InstallationInstructions?.DownloadHash);
        var zipFile = Assert.Single(gameClient.Files);
        Assert.Equal(expectedHash, zipFile.Hash);
    }

    /// <summary>
    /// Verifies that CreateManifests generates manifest IDs matching the frozen legacy encoding
    /// for tagged and digit-bearing versions (e.g. EAC and X86 builds).
    /// </summary>
    /// <param name="version">The Generals Online release version string to parse.</param>
    /// <param name="expectedClientId">The expected legacy manifest ID.</param>
    [Theory]
    [InlineData("042826_QFE3_EAC", "1.428263.generalsonline.gameclient.60hz")]
    [InlineData("011526_QFE1_EAC_X86", "1.11526186.generalsonline.gameclient.60hz")]
    public void CreateManifests_WithTaggedVersion_GeneratesExpectedManifestIdComponent(string version, string expectedClientId)
    {
        // Arrange
        var release = new GeneralsOnlineRelease
        {
            Version = version,
            ReleaseDate = DateTime.UtcNow,
            PortableUrl = "https://example.com/test.zip",
        };

        // Act
        var manifests = _factory.CreateManifests(release);
        var gameClient = manifests.FirstOrDefault(m => m.ContentType == ContentType.GameClient);

        // Assert
        Assert.NotNull(gameClient);
        Assert.Equal(expectedClientId, gameClient.Id.Value);
    }

    /// <summary>
    /// Verifies that when all four variants are present in extracted content (including the test environment executable),
    /// all 4 manifests are produced with correct executables.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_WithAllFourVariants_ProducesFourManifestsAsync()
    {
        // Arrange
        var exe60Path = Path.Combine(_tempDir, GameClientConstants.GeneralsOnline60HzExecutable);
        var exeTestPath = Path.Combine(_tempDir, GameClientConstants.GeneralsOnlineDefaultExecutable);
        var dllPath = Path.Combine(_tempDir, "GameNetworkingSockets.dll");
        File.WriteAllText(exe60Path, "fake 60hz exe content");
        File.WriteAllText(exeTestPath, "fake test exe content");
        File.WriteAllText(dllPath, "fake dll content");

        var mapsDir = Path.Combine(_tempDir, GeneralsOnlineConstants.MapsSubdirectory, "Tournament Desert");
        Directory.CreateDirectory(mapsDir);
        var mapFilePath = Path.Combine(mapsDir, "Tournament Desert.map");
        File.WriteAllText(mapFilePath, "fake map content");

        var gameDataDir = Path.Combine(_tempDir, GeneralsOnlineConstants.GameDataSubdirectory);
        Directory.CreateDirectory(gameDataDir);
        var bigPath = Path.Combine(gameDataDir, "500_900_CommunityPatch_CoreINI.big");
        File.WriteAllText(bigPath, "fake big content");

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
        var result = await _factory.CreateManifestsFromExtractedContentAsync(originalManifest, _tempDir, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var manifests = result.Data!;
        Assert.Equal(4, manifests.Count);

        var gameClient60Hz = manifests.FirstOrDefault(m => m.ContentType == ContentType.GameClient && m.Id.Value.EndsWith(GeneralsOnlineConstants.Variant60HzSuffix));
        var gameClientTest = manifests.FirstOrDefault(m => m.ContentType == ContentType.GameClient && m.Id.Value.EndsWith(GeneralsOnlineConstants.VariantTestEnvironmentSuffix));
        var mapPack = manifests.FirstOrDefault(m => m.ContentType == ContentType.MapPack);
        var gameDataPatch = manifests.FirstOrDefault(m => m.ContentType == ContentType.Patch);

        Assert.NotNull(gameClient60Hz);
        Assert.NotNull(gameClientTest);
        Assert.NotNull(mapPack);
        Assert.NotNull(gameDataPatch);

        // Test environment must mark generalsonlinezh.exe as executable
        var testExe = Assert.Single(gameClientTest.Files, f => f.IsExecutable);
        Assert.Equal(GameClientConstants.GeneralsOnlineDefaultExecutable, testExe.RelativePath, ignoreCase: true);

        // 60Hz client must mark 60Hz executable as executable
        var sixtyExe = Assert.Single(gameClient60Hz.Files, f => f.IsExecutable);
        Assert.Equal(GameClientConstants.GeneralsOnline60HzExecutable, sixtyExe.RelativePath, ignoreCase: true);

        // Test environment must NOT include 60Hz binary
        Assert.DoesNotContain(gameClientTest.Files, f => f.RelativePath.Equals(GameClientConstants.GeneralsOnline60HzExecutable, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that older archives missing MapPack or GameData directories gracefully omit those manifests
    /// and drop dependencies to them from the game clients.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_OlderArchive_WithoutMapPackOrGameData_ReconcilesDependenciesGracefullyAsync()
    {
        // Arrange: Only provide 60Hz game client files (no maps, no gamedata, no test exe)
        var exe60Path = Path.Combine(_tempDir, GameClientConstants.GeneralsOnline60HzExecutable);
        File.WriteAllText(exe60Path, "fake 60hz exe content");

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
        var result = await _factory.CreateManifestsFromExtractedContentAsync(originalManifest, _tempDir, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var manifests = result.Data!;
        var gameClient = Assert.Single(manifests);
        Assert.Equal(ContentType.GameClient, gameClient.ContentType);

        // Missing MapPack and GameData dependencies should be reconciled (dropped)
        Assert.DoesNotContain(gameClient.Dependencies, d => d.DependencyType == ContentType.MapPack);
        Assert.DoesNotContain(gameClient.Dependencies, d => d.DependencyType == ContentType.Patch);
    }

    /// <summary>
    /// Verifies that when both 60Hz and Test Environment variants are extracted, files are cleanly partitioned:
    /// Test Environment excludes 60Hz binary and EAC files, while 60Hz excludes dedicated Test Environment binary.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_PartitionsVariantsAndExcludesIrrelevantFilesAsync()
    {
        // Arrange: Create simulated extracted directory structure containing EAC + Test Environment
        var exe60Path = Path.Combine(_tempDir, GameClientConstants.GeneralsOnline60HzExecutable);
        var exeTestPath = Path.Combine(_tempDir, GameClientConstants.GeneralsOnlineTestEnvironmentExecutable);
        var launcherPath = Path.Combine(_tempDir, GameClientConstants.GeneralsOnlineDefaultExecutable);
        var eacLauncherPath = Path.Combine(_tempDir, GameClientConstants.GeneralsOnlineEacLauncherExecutable);
        var eacSetupPath = Path.Combine(_tempDir, GameClientConstants.GeneralsOnlineEacSetupExecutable);
        var eossdkPath = Path.Combine(_tempDir, "EOSSDK-Win32-Shipping.dll");
        var eacDir = Path.Combine(_tempDir, "EasyAntiCheat");
        Directory.CreateDirectory(eacDir);
        var eacSettingsPath = Path.Combine(eacDir, "Settings.json");
        var sharedDllPath = Path.Combine(_tempDir, "xaudio2_9redist.dll");

        File.WriteAllText(exe60Path, "fake 60hz exe content");
        File.WriteAllText(exeTestPath, "fake test env exe content");
        File.WriteAllText(launcherPath, "fake launcher exe content");
        File.WriteAllText(eacLauncherPath, "fake eac launcher content");
        File.WriteAllText(eacSetupPath, "fake eac setup content");
        File.WriteAllText(eossdkPath, "fake eos sdk content");
        File.WriteAllText(eacSettingsPath, "fake eac settings content");
        File.WriteAllText(sharedDllPath, "fake shared dll content");

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
        var result = await _factory.CreateManifestsFromExtractedContentAsync(originalManifest, _tempDir, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var manifests = result.Data!;
        var gameClient60Hz = manifests.FirstOrDefault(m => m.ContentType == ContentType.GameClient && m.Id.Value.EndsWith(GeneralsOnlineConstants.Variant60HzSuffix));
        var gameClientTest = manifests.FirstOrDefault(m => m.ContentType == ContentType.GameClient && m.Id.Value.EndsWith(GeneralsOnlineConstants.VariantTestEnvironmentSuffix));

        Assert.NotNull(gameClient60Hz);
        Assert.NotNull(gameClientTest);

        // 60Hz client checks
        Assert.Contains(gameClient60Hz.Files, f => f.RelativePath.Equals(GameClientConstants.GeneralsOnline60HzExecutable, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(gameClient60Hz.Files, f => f.RelativePath.Equals(GameClientConstants.GeneralsOnlineEacLauncherExecutable, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(gameClient60Hz.Files, f => f.RelativePath.Equals("EOSSDK-Win32-Shipping.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(gameClient60Hz.Files, f => f.RelativePath.Contains("EasyAntiCheat", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(gameClient60Hz.Files, f => f.RelativePath.Equals(GameClientConstants.GeneralsOnlineTestEnvironmentExecutable, StringComparison.OrdinalIgnoreCase));

        // Test Environment checks
        var testExe = Assert.Single(gameClientTest.Files, f => f.IsExecutable);
        Assert.Equal(GameClientConstants.GeneralsOnlineTestEnvironmentExecutable, testExe.RelativePath, ignoreCase: true);
        Assert.Contains(gameClientTest.Files, f => f.RelativePath.Equals("xaudio2_9redist.dll", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(gameClientTest.Files, f => f.RelativePath.Equals(GameClientConstants.GeneralsOnline60HzExecutable, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(gameClientTest.Files, f => f.RelativePath.Equals(GameClientConstants.GeneralsOnlineEacLauncherExecutable, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(gameClientTest.Files, f => f.RelativePath.Equals(GameClientConstants.GeneralsOnlineEacSetupExecutable, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(gameClientTest.Files, f => f.RelativePath.Equals("EOSSDK-Win32-Shipping.dll", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(gameClientTest.Files, f => f.RelativePath.Contains("EasyAntiCheat", StringComparison.OrdinalIgnoreCase));
    }
}
