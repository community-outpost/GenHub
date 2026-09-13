using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
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
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Tests that content storage fails when a file path traverses outside the source directory.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_WithTraversingSourcePath_ShouldFailAsync()
    {
        // Arrange
        // Source Dir: /Temp/SafeDir
        // Secret Dir: /Temp/SecretDir/secret.txt
        // File SourcePath: /Temp/SafeDir/../../SecretDir/secret.txt (Traverses out of Source Dir)
        var sourceDir = Path.Combine(_tempRoot, "SafeDir");
        Directory.CreateDirectory(sourceDir);

        var secretDir = Path.Combine(_tempRoot, "SecretDir");
        Directory.CreateDirectory(secretDir);
        var secretFile = Path.Combine(secretDir, "secret.txt");
        await File.WriteAllTextAsync(secretFile, "classified");

        var manifest = new ContentManifest
        {
            Id = "1.0.publisher.gameinstallation.hack",
            ContentType = ContentType.GameInstallation,
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
    public async Task StoreContentAsync_WithValidExternalSourcePath_ShouldSucceedAsync()
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

    /// <summary>
    /// Tests that content storage for an Addon physically stores files into CAS,
    /// populates file hashes, updates source types, and preserves the manifest file list.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_WithAddonContentType_PhysicallyStoresFilesInCasAsync()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempRoot, "AddonSource");
        Directory.CreateDirectory(sourceDir);

        var addonFile = Path.Combine(sourceDir, "addon.big");
        await File.WriteAllTextAsync(addonFile, "sample-addon-bytes");

        _casServiceMock
            .Setup(c => c.StoreContentAsync(addonFile, ContentType.Addon, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess("cas_hash_addon_123"));

        var manifest = new ContentManifest
        {
            Id = "1.0.local.addon.sample-hotkeys",
            ContentType = ContentType.Addon,
            Files =
            [
                new()
                {
                    RelativePath = "addon.big",
                    SourcePath = addonFile,
                    SourceType = ContentSourceType.LocalFile,
                },
            ],
        };

        // Act
        var result = await _service.StoreContentAsync(manifest, sourceDir);

        // Assert
        Assert.True(result.Success, $"Operation failed with: {result.FirstError}");
        _casServiceMock.Verify(c => c.StoreContentAsync(addonFile, ContentType.Addon, null, It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data.Files);
        Assert.Equal("cas_hash_addon_123", result.Data.Files[0].Hash);
        Assert.Equal(ContentSourceType.ContentAddressable, result.Data.Files[0].SourceType);
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
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
                // Ignore cleanup errors
            }
        }
    }
}
