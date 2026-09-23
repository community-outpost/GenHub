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
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
            referenceTracker,
            new CasWriteFence());
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
        // File SourcePath: /Temp/SecretDir/secret.txt (Absolute path outside sourceDir)
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
    /// Tests that content storage for an external Addon physically stores files into CAS
    /// to ensure durability even when source folders change.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_WithExternalAddon_PhysicallyStoresFilesInCasAsync()
    {
        // Arrange
        var currentDir = AppContext.BaseDirectory;
        var sourceDir = Path.Combine(currentDir, "ExternalAddon_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);

        try
        {
            var addonFile = Path.Combine(sourceDir, "external.big");
            await File.WriteAllTextAsync(addonFile, "sample-external-addon");

            _casServiceMock
                .Setup(c => c.StoreContentAsync(addonFile, ContentType.Addon, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess("cas_hash_external_addon_456"));

            var manifest = new ContentManifest
            {
                Id = "1.0.local.addon.external-hotkeys",
                ContentType = ContentType.Addon,
                SourcePath = sourceDir,
                Files =
                [
                    new()
                    {
                        RelativePath = "external.big",
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
            Assert.Equal("cas_hash_external_addon_456", result.Data.Files[0].Hash);
            Assert.Equal(ContentSourceType.ContentAddressable, result.Data.Files[0].SourceType);
        }
        finally
        {
            if (Directory.Exists(sourceDir))
            {
                Directory.Delete(sourceDir, true);
            }
        }
    }

    /// <summary>
    /// Tests that IsContentStoredAsync returns false when a required CAS object does not exist in CAS.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsContentStoredAsync_WhenCasObjectMissing_ReturnsFalseAsync()
    {
        // Arrange
        var manifestId = ManifestId.Create("1.0.local.addon.missing-cas");
        var manifestPath = _service.GetManifestStoragePath(manifestId);
        var manifestDir = Path.GetDirectoryName(manifestPath)!;
        Directory.CreateDirectory(manifestDir);

        var manifest = new ContentManifest
        {
            Id = manifestId,
            ContentType = ContentType.Addon,
            Files =
            [
                new()
                {
                    RelativePath = "Generals.exe",
                    Hash = "missing_hash_123",
                    SourceType = ContentSourceType.ContentAddressable,
                    IsRequired = true,
                },
            ],
        };

        await File.WriteAllTextAsync(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest));

        _casServiceMock
            .Setup(c => c.ExistsAsync("missing_hash_123", ContentType.Addon, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        _casServiceMock
            .Setup(c => c.ExistsAsync("missing_hash_123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        // Act
        var result = await _service.IsContentStoredAsync(manifestId);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Data);
    }

    /// <summary>
    /// Tests that IsContentStoredAsync returns true when all required CAS objects exist in CAS.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsContentStoredAsync_WhenCasObjectExists_ReturnsTrueAsync()
    {
        // Arrange
        var manifestId = ManifestId.Create("1.0.local.addon.present-cas");
        var manifestPath = _service.GetManifestStoragePath(manifestId);
        var manifestDir = Path.GetDirectoryName(manifestPath)!;
        Directory.CreateDirectory(manifestDir);

        var manifest = new ContentManifest
        {
            Id = manifestId,
            ContentType = ContentType.Addon,
            Files =
            [
                new()
                {
                    RelativePath = "Generals.exe",
                    Hash = "present_hash_456",
                    SourceType = ContentSourceType.ContentAddressable,
                    IsRequired = true,
                },
            ],
        };

        await File.WriteAllTextAsync(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest));

        _casServiceMock
            .Setup(c => c.ExistsAsync("present_hash_456", ContentType.Addon, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        _casServiceMock
            .Setup(c => c.ExistsAsync("present_hash_456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _service.IsContentStoredAsync(manifestId);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Data);
        _casServiceMock.Verify(c => c.ExistsAsync("present_hash_456", ContentType.Addon, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that IsContentStoredAsync correctly reads manifests serialized with string enums (e.g. from ContentManifestPool).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsContentStoredAsync_WhenManifestSerializedWithStringEnums_ReadsSuccessfullyAndChecksCasAsync()
    {
        // Arrange
        var manifestId = ManifestId.Create("1.0.local.addon.string-enums");
        var manifestPath = _service.GetManifestStoragePath(manifestId);
        var manifestDir = Path.GetDirectoryName(manifestPath)!;
        Directory.CreateDirectory(manifestDir);

        var manifest = new ContentManifest
        {
            Id = manifestId,
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Files =
            [
                new()
                {
                    RelativePath = "Generals.exe",
                    Hash = "string_enum_hash_999",
                    SourceType = ContentSourceType.ContentAddressable,
                    IsRequired = true,
                },
            ],
        };

        var poolSerializerOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };
        var serializedWithStrings = System.Text.Json.JsonSerializer.Serialize(manifest, poolSerializerOptions);
        Assert.Contains("\"ContentType\":\"Addon\"", serializedWithStrings);

        await File.WriteAllTextAsync(manifestPath, serializedWithStrings);

        _casServiceMock
            .Setup(c => c.ExistsAsync("string_enum_hash_999", ContentType.Addon, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        _casServiceMock
            .Setup(c => c.ExistsAsync("string_enum_hash_999", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _service.IsContentStoredAsync(manifestId);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Data);
        _casServiceMock.Verify(c => c.ExistsAsync("string_enum_hash_999", ContentType.Addon, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that StoreContentAsync fails when source directory does not exist and required CAS objects are missing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_WhenSourceDoesNotExistAndCasMissing_FailsAsync()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_tempRoot, "NonExistent_" + Guid.NewGuid().ToString("N"));
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.local.addon.missing-source-and-cas"),
            ContentType = ContentType.Addon,
            Files =
            [
                new()
                {
                    RelativePath = "addon.big",
                    Hash = "missing_hash_789",
                    SourceType = ContentSourceType.ContentAddressable,
                    IsRequired = true,
                },
            ],
        };

        _casServiceMock
            .Setup(c => c.ExistsAsync("missing_hash_789", ContentType.Addon, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        _casServiceMock
            .Setup(c => c.ExistsAsync("missing_hash_789", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        // Act
        var result = await _service.StoreContentAsync(manifest, nonExistentDir);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("missing from CAS", result.FirstError);
    }

    /// <summary>
    /// Tests that invalid-drive storage fails when physical storage is required and required CAS objects are missing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task HandleInvalidDriveAsync_WhenCasMissing_FailsAsync()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempRoot, "InvalidDriveSource");
        Directory.CreateDirectory(sourceDir);
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.local.addon.invalid-drive-missing-cas"),
            ContentType = ContentType.Addon,
            Files =
            [
                new()
                {
                    RelativePath = "addon.big",
                    Hash = "invalid_drive_missing_hash",
                    SourceType = ContentSourceType.ContentAddressable,
                    IsRequired = true,
                },
            ],
        };

        _casServiceMock
            .Setup(c => c.ExistsAsync("invalid_drive_missing_hash", ContentType.Addon, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        _casServiceMock
            .Setup(c => c.ExistsAsync("invalid_drive_missing_hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        // Act
        var result = await _service.HandleInvalidDriveAsync(manifest, sourceDir, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("missing from CAS", result.FirstError);
    }

    /// <summary>
    /// Tests that invalid-drive storage succeeds metadata-only when all required CAS objects exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task HandleInvalidDriveAsync_WhenCasPresent_StoresMetadataOnlyAsync()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempRoot, "InvalidDrivePresentSource");
        Directory.CreateDirectory(sourceDir);
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.local.addon.invalid-drive-cas-present"),
            ContentType = ContentType.Addon,
            Files =
            [
                new()
                {
                    RelativePath = "addon.big",
                    Hash = "invalid_drive_present_hash",
                    SourceType = ContentSourceType.ContentAddressable,
                    IsRequired = true,
                },
            ],
        };

        _casServiceMock
            .Setup(c => c.ExistsAsync("invalid_drive_present_hash", ContentType.Addon, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        _casServiceMock
            .Setup(c => c.ExistsAsync("invalid_drive_present_hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _service.HandleInvalidDriveAsync(manifest, sourceDir, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.True(File.Exists(_service.GetManifestStoragePath(manifest.Id)));
        _casServiceMock.Verify(c => c.ExistsAsync("invalid_drive_present_hash", ContentType.Addon, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that IsContentStoredAsync returns false when a required CAS file has no hash.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsContentStoredAsync_WhenRequiredCasFileHasEmptyHash_ReturnsFalseAsync()
    {
        // Arrange
        var manifestId = ManifestId.Create("1.0.local.addon.empty-hash");
        var manifestPath = _service.GetManifestStoragePath(manifestId);
        var manifestDir = Path.GetDirectoryName(manifestPath)!;
        Directory.CreateDirectory(manifestDir);

        var manifest = new ContentManifest
        {
            Id = manifestId,
            ContentType = ContentType.Addon,
            Files =
            [
                new()
                {
                    RelativePath = "Generals.exe",
                    Hash = string.Empty,
                    SourceType = ContentSourceType.ContentAddressable,
                    IsRequired = true,
                },
            ],
        };

        await File.WriteAllTextAsync(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest));

        // Act
        var result = await _service.IsContentStoredAsync(manifestId);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Data);
    }

    /// <summary>
    /// Tests that StoreContentAsync fails when a required file is missing from an existing source directory.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_WhenRequiredFileMissingFromSource_FailsAsync()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempRoot, "MissingFileSource");
        Directory.CreateDirectory(sourceDir);
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.local.addon.missing-required-file"),
            ContentType = ContentType.Addon,
            Files =
            [
                new()
                {
                    RelativePath = "missing.big",
                    Hash = string.Empty,
                    SourceType = ContentSourceType.ExtractedPackage,
                    IsRequired = true,
                },
            ],
        };

        // Act
        var result = await _service.StoreContentAsync(manifest, sourceDir);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("missing.big", result.FirstError);
    }

    /// <summary>
    /// Tests that StoreContentAsync preserves cooperative cancellation instead of converting it into a storage failure.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_WhenCancelled_ThrowsOperationCanceledExceptionAsync()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempRoot, "CancelledSource");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "addon.big"), "mock content");

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.local.addon.cancelled-store"),
            ContentType = ContentType.Addon,
            Files =
            [
                new()
                {
                    RelativePath = "addon.big",
                    Hash = string.Empty,
                    SourceType = ContentSourceType.ExtractedPackage,
                    IsRequired = true,
                },
            ],
        };

        _casServiceMock
            .Setup(c => c.StoreContentAsync(It.IsAny<string>(), ContentType.Addon, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess("cancelled_store_hash"));
        _casServiceMock
            .Setup(c => c.GetContentPathAsync(It.IsAny<string>(), ContentType.Addon, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateFailure("not in CAS"));
        _casServiceMock
            .Setup(c => c.GetContentPathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateFailure("not in CAS"));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.StoreContentAsync(manifest, sourceDir, null, cts.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    /// <summary>
    /// Tests that files carrying a manifest hash are stored through the known-hash path
    /// instead of being hashed a second time.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_WithManifestHash_UsesKnownHashStoreAsync()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempRoot, "KnownHashSource");
        Directory.CreateDirectory(sourceDir);

        var addonFile = Path.Combine(sourceDir, "addon.big");
        await File.WriteAllTextAsync(addonFile, "sample-addon-bytes");

        _casServiceMock
            .Setup(c => c.GetContentPathAsync(It.IsAny<string>(), ContentType.Addon, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateFailure("not in CAS"));
        _casServiceMock
            .Setup(c => c.GetContentPathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateFailure("not in CAS"));
        _casServiceMock
            .Setup(c => c.StoreContentWithKnownHashAsync(addonFile, "known_hash_addon_123", ContentType.Addon, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess("known_hash_addon_123"));

        var manifest = new ContentManifest
        {
            Id = "1.0.local.addon.known-hash",
            ContentType = ContentType.Addon,
            Files =
            [
                new()
                {
                    RelativePath = "addon.big",
                    SourcePath = addonFile,
                    SourceType = ContentSourceType.ContentAddressable,
                    Hash = "known_hash_addon_123",
                },
            ],
        };

        // Act
        var result = await _service.StoreContentAsync(manifest, sourceDir);

        // Assert
        Assert.True(result.Success, $"Operation failed with: {result.FirstError}");
        _casServiceMock.Verify(
            c => c.StoreContentWithKnownHashAsync(addonFile, "known_hash_addon_123", ContentType.Addon, It.IsAny<CancellationToken>()),
            Times.Once);
        _casServiceMock.Verify(
            c => c.StoreContentAsync(addonFile, ContentType.Addon, null, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Tests that parallel CAS storage preserves manifest file order.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_WithMultipleFiles_PreservesManifestOrderAsync()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempRoot, "OrderSource");
        Directory.CreateDirectory(sourceDir);

        var files = new List<ManifestFile>();
        for (var i = 0; i < 10; i++)
        {
            var fileName = $"file{i:00}.txt";
            var filePath = Path.Combine(sourceDir, fileName);
            await File.WriteAllTextAsync(filePath, $"content-{i}");
            files.Add(new ManifestFile
            {
                RelativePath = fileName,
                SourcePath = filePath,
                SourceType = ContentSourceType.ContentAddressable,
                Hash = $"known_hash_{i:00}",
            });
        }

        _casServiceMock
            .Setup(c => c.GetContentPathAsync(It.IsAny<string>(), ContentType.Addon, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateFailure("not in CAS"));
        _casServiceMock
            .Setup(c => c.GetContentPathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateFailure("not in CAS"));
        _casServiceMock
            .Setup(c => c.StoreContentWithKnownHashAsync(It.IsAny<string>(), It.IsAny<string>(), ContentType.Addon, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string source, string hash, ContentType contentType, CancellationToken token) =>
                OperationResult<string>.CreateSuccess(hash));

        var manifest = new ContentManifest
        {
            Id = "1.0.local.addon.ordered",
            ContentType = ContentType.Addon,
            Files = files,
        };

        // Act
        var result = await _service.StoreContentAsync(manifest, sourceDir);

        // Assert
        Assert.True(result.Success, $"Operation failed with: {result.FirstError}");
        Assert.NotNull(result.Data);
        Assert.Equal(
            files.Select(f => f.RelativePath).ToList(),
            result.Data.Files.Select(f => f.RelativePath).ToList());
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore cleanup errors
            }
        }
    }
}
