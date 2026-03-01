using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Storage;
using GenHub.Features.Content.Services;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Tests for the <see cref="ContentStorageService"/>.
/// </summary>
public class ContentStorageServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _storageRoot;
    private readonly Mock<ILogger<ContentStorageService>> _loggerMock;
    private readonly Mock<ICasService> _casServiceMock;
    private readonly ContentStorageService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentStorageServiceTests"/> class.
    /// </summary>
    public ContentStorageServiceTests()
    {
        // Setup temp directories
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString());
        _storageRoot = Path.Combine(_tempRoot, "Storage");
        Directory.CreateDirectory(_storageRoot);

        // Mocks
        _loggerMock = new Mock<ILogger<ContentStorageService>>();
        _casServiceMock = new Mock<ICasService>();

        // We can't easily mock the concrete CasReferenceTracker without an interface or virtual methods,
        // so we'll construct a real one with mocked dependencies.
        var casConfig = Options.Create(new CasConfiguration { CasRootPath = _storageRoot });
        var trackerLogger = new Mock<ILogger<CasReferenceTracker>>();
        var referenceTracker = new CasReferenceTracker(casConfig, trackerLogger.Object);

        _service = new ContentStorageService(
            _storageRoot,
            _loggerMock.Object,
            _casServiceMock.Object,
            referenceTracker);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch
        {
            // Allowed to fail during cleanup
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Tests that content storage fails when a file path traverses outside the source directory.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_WithTraversingSourcePath_ShouldFail()
    {
        // Arrange
        // Source Dir: /Temp/Source
        // File SourcePath: /Temp/Other/secret.txt (Traverses out of Source)
        var sourceDir = Path.Combine(_tempRoot, "Source");
        Directory.CreateDirectory(sourceDir);

        var otherDir = Path.Combine(_tempRoot, "Other");
        Directory.CreateDirectory(otherDir);
        var secretFile = Path.Combine(otherDir, "secret.txt");
        await File.WriteAllTextAsync(secretFile, "secret");

        var manifest = new ContentManifest
        {
            Id = "1.0.publisher.gameclient.traversal",
            ContentType = ContentType.GameClient,
            Files =
            [
                new()
                {
                    RelativePath = "innocent.txt",
                    SourcePath = secretFile, // Absolute path outside sourceDir
                    SourceType = ContentSourceType.LocalFile,
                },
            ],
        };

        // Act
        var result = await _service.StoreContentAsync(manifest, sourceDir);

        // Assert
        Assert.False(result.Success, "Operation should fail due to security validation");
        Assert.Contains("traverses outside base directory", result.FirstError);
    }

    /// <summary>
    /// Tests that content storage succeeds when a file path is a valid absolute path inside the source directory.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_WithValidExternalSourcePath_ShouldSucceed()
    {
        // Arrange
        // Source Dir: /Temp/ExternalGame
        // File SourcePath: /Temp/ExternalGame/game.exe (Valid absolute path inside source)
        // This simulates the behavior of GameInstallation or Downloaded content
        var sourceDir = Path.Combine(_tempRoot, "ExternalGame");
        Directory.CreateDirectory(sourceDir);

        var gameFile = Path.Combine(sourceDir, "game.exe");
        await File.WriteAllTextAsync(gameFile, "bin");

        var manifest = new ContentManifest
        {
            Id = "1.0.publisher.gameinstallation.external",
            ContentType = ContentType.GameInstallation, // No physical storage needed, but validation still runs
            Files =
            [
                new()
                {
                    RelativePath = "game.exe",
                    SourcePath = gameFile, // Absolute path INSIDE sourceDir
                    SourceType = ContentSourceType.LocalFile,
                },
            ],
        };

        // Act
        var result = await _service.StoreContentAsync(manifest, sourceDir);

        // Assert
        Assert.True(result.Success, $"Operation failed with: {result.FirstError}");
    }
}
