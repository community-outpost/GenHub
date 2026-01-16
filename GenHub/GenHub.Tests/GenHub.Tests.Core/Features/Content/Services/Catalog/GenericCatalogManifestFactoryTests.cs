using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit.Abstractions;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.Catalog;

/// <summary>
/// Unit tests for <see cref="GenericCatalogManifestFactory"/>.
/// </summary>
public class GenericCatalogManifestFactoryTests
{
    private readonly Mock<ILogger<GenericCatalogManifestFactory>> _loggerMock;
    private readonly GenericCatalogManifestFactory _factory;
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenericCatalogManifestFactoryTests"/> class.
    /// </summary>
    /// <param name="output">The test output helper.</param>
    public GenericCatalogManifestFactoryTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerMock = new Mock<ILogger<GenericCatalogManifestFactory>>();
        _factory = new GenericCatalogManifestFactory(_loggerMock.Object);
    }

    /// <summary>
    /// Verifies that CanHandle returns true for catalog-based publishers.
    /// </summary>
    [Fact]
    public void CanHandle_CatalogPublisher_ReturnsTrue()
    {
        // Arrange
        var manifest = new ContentManifest
        {
            Id = "1.0.mypublisher.mod.test-mod",
            Name = "Test Mod",
            Publisher = new PublisherInfo { PublisherType = "mypublisher" },
        };

        // Act
        var result = _factory.CanHandle(manifest);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Verifies that CanHandle returns false for built-in publishers.
    /// </summary>
    /// <param name="publisherType">Type of the publisher.</param>
    [Theory]
    [InlineData("Steam")]
    [InlineData("steam")]
    [InlineData("EA")]
    [InlineData("Origin")]
    [InlineData("ea")]
    [InlineData("Ultimate")]
    [InlineData("TheSuperHackers")]
    [InlineData("thesuperhackers")]
    [InlineData("GeneralsOnline")]
    [InlineData("CommunityOutpost")]
    public void CanHandle_BuiltinPublisher_ReturnsFalse(string publisherType)
    {
        // Arrange
        var manifest = new ContentManifest
        {
            Id = $"1.0.{publisherType}.mod.test",
            Name = "Test Mod",
            Publisher = new PublisherInfo { PublisherType = publisherType },
        };

        // Act
        var result = _factory.CanHandle(manifest);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Verifies that CreateManifestsFromExtractedContentAsync computes SHA256 hashes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_ComputesSha256Hashes()
    {
        // Arrange
        var tempDir = CreateTestDirectoryWithFiles();

        var originalManifest = new ContentManifest
        {
            Id = "1.0.mypublisher.mod.test-mod",
            Name = "Test Mod",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                PublisherType = "mypublisher",
                Name = "My Publisher",
            },
            Metadata = new ContentMetadata
            {
                Description = "Test Description",
                Tags = ["test", "mod"],
            },
            Files = [],
            Dependencies = [],
            InstallationInstructions = new InstallationInstructions(),
        };

        // Act
        var result = await _factory.CreateManifestsFromExtractedContentAsync(originalManifest, tempDir);

        // Assert
        Assert.Single(result);
        var enrichedManifest = result.First();

        // Verify files were added with hashes
        Assert.NotEmpty(enrichedManifest.Files);
        Assert.All(enrichedManifest.Files, f =>
        {
            Assert.NotNull(f.Hash);
            Assert.Equal(64, f.Hash.Length); // SHA256 is 64 hex chars
            Assert.True(f.Size > 0);
            Assert.Equal(ContentSourceType.ContentAddressable, f.SourceType);
        });

        // Cleanup
        try
        {
            Directory.Delete(tempDir, true);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Verifies that CreateManifestsFromExtractedContentAsync sets WorkspaceStrategy correctly.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <param name="expectedStrategy">The expected workspace strategy.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(ContentType.Mod, WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(ContentType.Addon, WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(ContentType.Map, WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(ContentType.MapPack, WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(ContentType.LanguagePack, WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(ContentType.GameClient, WorkspaceStrategy.FullCopy)]
    public async Task CreateManifestsFromExtractedContentAsync_SetsWorkspaceStrategy_Correctly(
        ContentType contentType,
        WorkspaceStrategy expectedStrategy)
    {
        // Arrange
        var tempDir = CreateTestDirectoryWithFiles();

        var originalManifest = new ContentManifest
        {
            Id = "1.0.mypublisher.mod.test-mod",
            Name = "Test Mod",
            ContentType = contentType,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                PublisherType = "mypublisher",
                Name = "My Publisher",
            },
            Metadata = new ContentMetadata
            {
                Description = "Test Description",
            },
            Files = [],
            InstallationInstructions = new InstallationInstructions(),
        };

        // Act
        var result = await _factory.CreateManifestsFromExtractedContentAsync(originalManifest, tempDir);
        var enrichedManifest = result.First();

        // Assert
        Assert.Equal(expectedStrategy, enrichedManifest.InstallationInstructions.WorkspaceStrategy);

        // Cleanup
        try
        {
            Directory.Delete(tempDir, true);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Verifies that CreateManifestsFromExtractedContentAsync throws for null manifest.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_NullManifest_ThrowsArgumentNullException()
    {
        // Arrange
        var tempDir = CreateTestDirectoryWithFiles();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _factory.CreateManifestsFromExtractedContentAsync(null!, tempDir);
        });

        // Cleanup
        try
        {
            Directory.Delete(tempDir, true);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Verifies that CreateManifestsFromExtractedContentAsync throws for non-existent directory.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_NonExistentDirectory_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var manifest = new ContentManifest
        {
            Id = "1.0.mypublisher.mod.test-mod",
            Name = "Test Mod",
            Publisher = new PublisherInfo { PublisherType = "mypublisher" },
            Files = [],
            InstallationInstructions = new InstallationInstructions(),
        };
        var nonExistentDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        // Act & Assert
        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
        {
            await _factory.CreateManifestsFromExtractedContentAsync(manifest, nonExistentDir);
        });
    }

    /// <summary>
    /// Verifies that CreateManifestsFromExtractedContentAsync preserves metadata.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_PreservesMetadata()
    {
        // Arrange
        var tempDir = CreateTestDirectoryWithFiles();

        var originalManifest = new ContentManifest
        {
            Id = "1.0.mypublisher.mod.test-mod",
            Name = "Test Mod",
            ContentType = ContentType.Mod,
            TargetGame = GameType.Generals,
            Version = "1.0.0",
            Publisher = new PublisherInfo
            {
                PublisherType = "mypublisher",
                Name = "My Publisher",
                Website = "https://example.com",
                SupportUrl = "https://example.com/support",
                ContactEmail = "contact@example.com",
            },
            Metadata = new ContentMetadata
            {
                Description = "Test Description",
                Tags = ["test", "mod", "gameplay"],
                IconUrl = "https://example.com/icon.png",
                ScreenshotUrls = ["https://example.com/screen1.jpg"],
            },
            Dependencies =
            [
                new() { Id = "1.0.other.mod.dep", Name = "Dependency" },
            ],
            Files = [],
            InstallationInstructions = new InstallationInstructions(),
        };

        // Act
        var result = await _factory.CreateManifestsFromExtractedContentAsync(originalManifest, tempDir);
        var enrichedManifest = result.First();

        // Assert
        Assert.Equal("1.0.mypublisher.mod.test-mod", enrichedManifest.Id.Value);
        Assert.Equal("Test Mod", enrichedManifest.Name);
        Assert.Equal(ContentType.Mod, enrichedManifest.ContentType);
        Assert.Equal(GameType.Generals, enrichedManifest.TargetGame);
        Assert.Equal("1.0.0", enrichedManifest.Version);
        Assert.Equal("My Publisher", enrichedManifest.Publisher.Name);
        Assert.Equal("https://example.com", enrichedManifest.Publisher.Website);
        Assert.Equal("Test Description", enrichedManifest.Metadata.Description);
        Assert.Equal(3, enrichedManifest.Metadata.Tags.Count);
        Assert.Single(enrichedManifest.Dependencies);

        // Cleanup
        try
        {
            Directory.Delete(tempDir, true);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Verifies that GetManifestDirectory returns the extracted directory.
    /// </summary>
    [Fact]
    public void GetManifestDirectory_ReturnsExtractedDirectory()
    {
        // Arrange
        var manifest = new ContentManifest
        {
            Id = "1.0.mypublisher.mod.test-mod",
            Name = "Test Mod",
            Publisher = new PublisherInfo { PublisherType = "mypublisher" },
        };
        var extractedDir = "/path/to/extracted";

        // Act
        var result = _factory.GetManifestDirectory(manifest, extractedDir);

        // Assert
        Assert.Equal(extractedDir, result);
    }

    /// <summary>
    /// Verifies that PublisherId matches expected format.
    /// </summary>
    [Fact]
    public void PublisherId_MatchesExpectedFormat()
    {
        // Act
        var publisherId = _factory.PublisherId;

        // Assert
        Assert.Equal("catalog.generic", publisherId);
    }

    /// <summary>
    /// Verifies that executable files are correctly identified.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_IdentifiesExecutableFiles()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        // Create executable files
        File.WriteAllBytes(Path.Combine(tempDir, "program.exe"), "MZ"u8.ToArray()); // MZ header
        File.WriteAllBytes(Path.Combine(tempDir, "library.dll"), "MZ"u8.ToArray());
        File.WriteAllText(Path.Combine(tempDir, "readme.txt"), "Readme");

        var originalManifest = new ContentManifest
        {
            Id = "1.0.mypublisher.mod.test-mod",
            Name = "Test Mod",
            Publisher = new PublisherInfo { PublisherType = "mypublisher" },
            Files = [],
            InstallationInstructions = new InstallationInstructions(),
        };

        // Act
        var result = await _factory.CreateManifestsFromExtractedContentAsync(originalManifest, tempDir);
        var enrichedManifest = result.First();

        // Assert
        var exeFile = enrichedManifest.Files.FirstOrDefault(f => f.RelativePath == "program.exe");
        var dllFile = enrichedManifest.Files.FirstOrDefault(f => f.RelativePath == "library.dll");
        var txtFile = enrichedManifest.Files.FirstOrDefault(f => f.RelativePath == "readme.txt");

        Assert.NotNull(exeFile);
        Assert.True(exeFile.IsExecutable);

        Assert.NotNull(dllFile);
        Assert.True(dllFile.IsExecutable);

        Assert.NotNull(txtFile);
        Assert.False(txtFile.IsExecutable);

        // Cleanup
        try
        {
            Directory.Delete(tempDir, true);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Verifies that file paths use forward slashes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_UsesForwardSlashesInPaths()
    {
        // Arrange
        var tempDir = CreateTestDirectoryWithFiles();

        var originalManifest = new ContentManifest
        {
            Id = "1.0.mypublisher.mod.test-mod",
            Name = "Test Mod",
            Publisher = new PublisherInfo { PublisherType = "mypublisher" },
            Files = [],
            InstallationInstructions = new InstallationInstructions(),
        };

        // Act
        var result = await _factory.CreateManifestsFromExtractedContentAsync(originalManifest, tempDir);
        var enrichedManifest = result.First();

        // Assert
        Assert.All(enrichedManifest.Files, f =>
        {
            Assert.DoesNotContain("\\", f.RelativePath);
            Assert.Contains("/", f.RelativePath);
        });

        // Cleanup
        try
        {
            Directory.Delete(tempDir, true);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Creates a temporary test directory with sample files.
    /// </summary>
    /// <returns>The path to the created test directory.</returns>
    private static string CreateTestDirectoryWithFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        // Create some test files
        File.WriteAllText(Path.Combine(tempDir, "test.txt"), "Test content");
        File.WriteAllBytes(Path.Combine(tempDir, "test.bin"), [0x00, 0x01, 0x02]);

        // Create a subdirectory with files
        var subDir = Path.Combine(tempDir, "subdir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "nested.txt"), "Nested content");

        return tempDir;
    }
}
