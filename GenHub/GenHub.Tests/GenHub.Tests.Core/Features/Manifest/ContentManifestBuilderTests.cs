using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Manifest;
using Microsoft.Extensions.Logging;
using Moq;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Manifest;

/// <summary>
/// Unit tests for the <see cref="ContentManifestBuilder"/> class.
/// </summary>
public class ContentManifestBuilderTests
{
    /// <summary>
    /// Mock logger for the content manifest builder.
    /// </summary>
    private readonly Mock<ILogger<ContentManifestBuilder>> _loggerMock;

    /// <summary>
    /// Mock for the file hash provider used in the builder.
    /// </summary>
    private readonly Mock<IFileHashProvider> _hashProviderMock;

    /// <summary>
    /// Mock for the manifest ID service used in the builder.
    /// </summary>
    private readonly Mock<IManifestIdService> _manifestIdServiceMock;

    /// <summary>
    /// Mock for the download service used in the builder.
    /// </summary>
    private readonly Mock<IDownloadService> _downloadServiceMock;

    /// <summary>
    /// Mock for the configuration provider service used in the builder.
    /// </summary>
    private readonly Mock<IConfigurationProviderService> _configProviderServiceMock;

    /// <summary>
    /// The content manifest builder under test.
    /// </summary>
    private readonly ContentManifestBuilder _builder;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentManifestBuilderTests"/> class.
    /// </summary>
    public ContentManifestBuilderTests()
    {
        _loggerMock = new Mock<ILogger<ContentManifestBuilder>>();
        _hashProviderMock = new Mock<IFileHashProvider>();
        _manifestIdServiceMock = new Mock<IManifestIdService>();
        _downloadServiceMock = new Mock<IDownloadService>();
        _configProviderServiceMock = new Mock<IConfigurationProviderService>();

        // Set up mock to return success for ValidateAndCreateManifestId
        _manifestIdServiceMock.Setup(x => x.ValidateAndCreateManifestId(It.IsAny<string>()))
            .Returns((string id) => OperationResult<ManifestId>.CreateSuccess(ManifestId.Create(id)));

        _manifestIdServiceMock.Setup(x => x.GenerateGameInstallationId(It.IsAny<GameInstallation>(), It.IsAny<GameType>(), It.IsAny<int>()))
            .Returns((GameInstallation gi, GameType gt, int v) =>
                OperationResult<ManifestId>.CreateSuccess(ManifestId.Create("game-installation-id")));

        // Set up mock to return success for GeneratePublisherContentId
        _manifestIdServiceMock.Setup(x => x.GeneratePublisherContentId(It.IsAny<string>(), It.IsAny<ContentType>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns((string p, ContentType ct, string c, int v) =>
            {
                var generated = ManifestIdGenerator.GeneratePublisherContentId(p, ct, c, v);
                return OperationResult<ManifestId>.CreateSuccess(ManifestId.Create(generated));
            });

        _builder = new ContentManifestBuilder(
            _loggerMock.Object,
            _hashProviderMock.Object,
            _manifestIdServiceMock.Object,
            _downloadServiceMock.Object,
            _configProviderServiceMock.Object);
    }

    /// <summary>
    /// Tests that WithBasicInfo sets properties correctly.
    /// </summary>
    [Fact]
    public void WithBasicInfo_SetsPropertiesCorrectly()
    {
        // Act
        var result = _builder
            .WithBasicInfo("Test Publisher", "Test Name", "1")
            .Build();

        // Assert
        Assert.Equal("1.1.testpublisher.mod.testname", result.Id);
        Assert.Equal("Test Name", result.Name);
        Assert.Equal("1", result.Version);
    }

    /// <summary>
    /// Tests that WithContentType sets properties correctly.
    /// </summary>
    [Fact]
    public void WithContentType_SetsPropertiesCorrectly()
    {
        // Act
        var result = _builder
            .WithBasicInfo("Test Publisher", "Test Name", "1")
            .WithContentType(ContentType.Mod, GameType.Generals)
            .Build();

        // Assert
        Assert.Equal(ContentType.Mod, result.ContentType);
        Assert.Equal(GameType.Generals, result.TargetGame);
    }

    /// <summary>
    /// Tests that WithPublisher sets publisher information correctly.
    /// </summary>
    [Fact]
    public void WithPublisher_SetsPublisherInfo()
    {
        // Act
        var result = _builder
            .WithBasicInfo("Test Publisher", "Test Name", "1")
            .WithPublisher("Test Publisher", "https://test.com", "https://support.test.com", "support@test.com")
            .Build();

        // Assert
        Assert.NotNull(result.Publisher);
        Assert.Equal("Test Publisher", result.Publisher.Name);
        Assert.Equal("https://test.com", result.Publisher.Website);
        Assert.Equal("https://support.test.com", result.Publisher.SupportUrl);
        Assert.Equal("support@test.com", result.Publisher.ContactEmail);
    }

    /// <summary>
    /// Tests that AddDependency adds a dependency correctly.
    /// </summary>
    [Fact]
    public void AddDependency_AddsDependencyCorrectly()
    {
        // Act
        var result = _builder
            .WithBasicInfo("Test Publisher", "Test Name", "1")
            .AddDependency(
                id: ManifestId.Create("1.0.genhub.mod.dependency"),
                name: "Dependency Name",
                dependencyType: ContentType.GameInstallation,
                installBehavior: DependencyInstallBehavior.AutoInstall,
                minVersion: "1.0",
                maxVersion: "2.0",
                compatibleVersions: ["1.1", "1.2"],
                isExclusive: true,
                conflictsWith: [ManifestId.Create("1.0.genhub.mod.conflict")])
            .Build();

        // Assert
        Assert.Single(result.Dependencies);
        var dependency = result.Dependencies[0];
        Assert.Equal(ManifestId.Create("1.0.genhub.mod.dependency"), dependency.Id);
        Assert.Equal("Dependency Name", dependency.Name);
        Assert.Equal(ContentType.GameInstallation, dependency.DependencyType);
        Assert.Equal("1.0", dependency.MinVersion);
        Assert.Equal("2.0", dependency.MaxVersion);
        Assert.Equal(["1.1", "1.2"], dependency.CompatibleVersions);
        Assert.True(dependency.IsExclusive);
        Assert.Equal([ManifestId.Create("1.0.genhub.mod.conflict")], dependency.ConflictsWith);
        Assert.Equal(DependencyInstallBehavior.AutoInstall, dependency.InstallBehavior);
    }

    /// <summary>
    /// Tests that AddRequiredDirectories adds directories correctly.
    /// </summary>
    [Fact]
    public void AddRequiredDirectories_AddsDirectoriesCorrectly()
    {
        // Act
        var result = _builder
            .WithBasicInfo("Test Publisher", "Test Name", "1")
            .AddRequiredDirectories(DirectoryNames.Data, "Maps", "Models")
            .Build();

        // Assert
        Assert.Equal(3, result.RequiredDirectories.Count);
        Assert.Contains(DirectoryNames.Data, result.RequiredDirectories);
        Assert.Contains("Maps", result.RequiredDirectories);
        Assert.Contains("Models", result.RequiredDirectories);
    }

    /// <summary>
    /// Tests that WithInstallationInstructions sets workspace strategy.
    /// </summary>
    [Fact]
    public void WithInstallationInstructions_SetsWorkspaceStrategy()
    {
        // Act
        var result = _builder
            .WithBasicInfo("Test Publisher", "Test Name", "1")
            .WithInstallationInstructions(WorkspaceStrategy.FullCopy)
            .Build();

        // Assert
        Assert.NotNull(result.InstallationInstructions);
        Assert.Equal(WorkspaceStrategy.FullCopy, result.InstallationInstructions.WorkspaceStrategy);
    }

    /// <summary>
    /// Tests that Build returns a valid manifest with minimal configuration.
    /// </summary>
    [Fact]
    public void Build_ReturnsValidManifest_WithMinimalConfiguration()
    {
        // Act
        var result = _builder
            .WithBasicInfo("Minimal Publisher", "Minimal Manifest", "1")
            .Build();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1.1.minimalpublisher.mod.minimalmanifest", result.Id);
        Assert.Equal("Minimal Manifest", result.Name);
        Assert.Equal("1", result.Version);
        Assert.NotNull(result.Dependencies);
        Assert.NotNull(result.Files);
        Assert.NotNull(result.RequiredDirectories);
    }

    /// <summary>
    /// Tests that AddFilesFromDirectoryAsync sets the correct InstallTarget based on file extensions.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AddFilesFromDirectoryAsync_SetsCorrectInstallTargets()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHubTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var mapsDir = Path.Combine(tempDir, "Maps");
        Directory.CreateDirectory(mapsDir);
        var replaysDir = Path.Combine(tempDir, "Replays");
        Directory.CreateDirectory(replaysDir);
        var screenshotsDir = Path.Combine(tempDir, "Screenshots");
        Directory.CreateDirectory(screenshotsDir);

        try
        {
            // Create test files
            File.WriteAllText(Path.Combine(tempDir, "readme.txt"), "readme"); // Workspace
            File.WriteAllText(Path.Combine(mapsDir, "map1.map"), "map data"); // UserMapsDirectory
            File.WriteAllText(Path.Combine(mapsDir, "map_preview.tga"), "tga data"); // UserMapsDirectory (due to Maps folder)
            File.WriteAllText(Path.Combine(replaysDir, "replay1.rep"), "replay data"); // UserReplaysDirectory
            File.WriteAllText(Path.Combine(screenshotsDir, "shot1.bmp"), "bmp data"); // UserScreenshotsDirectory
            File.WriteAllText(Path.Combine(tempDir, "other.bmp"), "bmp data"); // Workspace (not in Screenshots folder)

            // Setup hash mock
            _hashProviderMock.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("test-hash");

            // Act
            var result = await _builder
                .WithBasicInfo("Test Publisher", "Test Content", "1")
                .AddFilesFromDirectoryAsync(tempDir) as ContentManifestBuilder;

            var manifest = result!.Build();

            // Assert
            var textFile = manifest.Files.Single(f => f.RelativePath == "readme.txt");
            Assert.Equal(ContentInstallTarget.Workspace, textFile.InstallTarget);

            var mapFile = manifest.Files.Single(f => f.RelativePath == Path.Combine("Maps", "map1.map"));
            Assert.Equal(ContentInstallTarget.UserMapsDirectory, mapFile.InstallTarget);

            var mapTgaFile = manifest.Files.Single(f => f.RelativePath == Path.Combine("Maps", "map_preview.tga"));
            Assert.Equal(ContentInstallTarget.UserMapsDirectory, mapTgaFile.InstallTarget);

            var replayFile = manifest.Files.Single(f => f.RelativePath == Path.Combine("Replays", "replay1.rep"));
            Assert.Equal(ContentInstallTarget.UserReplaysDirectory, replayFile.InstallTarget);

            var screenshotFile = manifest.Files.Single(f => f.RelativePath == Path.Combine("Screenshots", "shot1.bmp"));
            Assert.Equal(ContentInstallTarget.UserScreenshotsDirectory, screenshotFile.InstallTarget);

            var otherBmpFile = manifest.Files.Single(f => f.RelativePath == "other.bmp");
            Assert.Equal(ContentInstallTarget.Workspace, otherBmpFile.InstallTarget);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
