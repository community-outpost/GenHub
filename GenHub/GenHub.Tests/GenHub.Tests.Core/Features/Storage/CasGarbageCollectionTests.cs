using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace GenHub.Tests.Core.Features.Storage;

/// <summary>
/// Unit tests verifying CAS garbage collection safety, manifest protection,
/// multi-pool awareness, and grace period handling.
/// </summary>
public class CasGarbageCollectionTests
{
    private readonly Mock<ICasReferenceTracker> _referenceTrackerMock;
    private readonly Mock<IContentManifestPool> _manifestPoolMock;
    private readonly Mock<ICasStorage> _primaryStorageMock;
    private readonly IOptions<CasConfiguration> _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="CasGarbageCollectionTests"/> class.
    /// </summary>
    public CasGarbageCollectionTests()
    {
        _referenceTrackerMock = new Mock<ICasReferenceTracker>();
        _manifestPoolMock = new Mock<IContentManifestPool>();
        _primaryStorageMock = new Mock<ICasStorage>();
        _config = Options.Create(new CasConfiguration
        {
            GcGracePeriod = TimeSpan.FromDays(1),
            GcLockTimeout = TimeSpan.FromSeconds(5),
        });

        // Default empty reference tracker
        _referenceTrackerMock
            .Setup(t => t.GetAllReferencedHashesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        // Default empty manifest pool
        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));
    }

    /// <summary>
    /// Verifies that blobs linked by manifest files are never deleted by garbage collection, even when forced.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task RunGarbageCollectionAsync_WhenBlobLinkedByManifestFiles_NeverDeletesEvenWhenForced()
    {
        // Arrange
        const string manifestHash = "a1b2c3d4e5f60123456789abcdef0123456789abcdef0123456789abcdef0123";
        var manifest = new ContentManifest
        {
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "game.exe",
                    Hash = manifestHash,
                },
            ],
        };

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([manifest]));

        _primaryStorageMock
            .Setup(s => s.GetAllObjectHashesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([manifestHash]);

        using var manager = CreateLifecycleManager();

        // Act
        var result = await manager.RunGarbageCollectionAsync(force: true);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(0, result.Data.ObjectsDeleted);
        Assert.Equal(1, result.Data.ObjectsReferenced);
        _primaryStorageMock.Verify(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that blobs linked by manifest variants are never deleted by garbage collection, even when forced.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task RunGarbageCollectionAsync_WhenBlobLinkedByManifestVariants_NeverDeletesEvenWhenForced()
    {
        // Arrange
        const string variantHash = "b2c3d4e5f60123456789abcdef0123456789abcdef0123456789abcdef0123a1";
        var manifest = new ContentManifest
        {
            Files = [],
            Variants =
            [
                new ArtifactVariant
                {
                    RuntimeIdentifiers = ["win-x64"],
                    Files =
                    [
                        new ManifestFile
                        {
                            RelativePath = "textures/hd.tga",
                            Hash = variantHash,
                        },
                    ],
                },
            ],
        };

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([manifest]));

        _primaryStorageMock
            .Setup(s => s.GetAllObjectHashesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([variantHash]);

        using var manager = CreateLifecycleManager();

        // Act
        var result = await manager.RunGarbageCollectionAsync(force: true);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(0, result.Data.ObjectsDeleted);
        Assert.Equal(1, result.Data.ObjectsReferenced);
        _primaryStorageMock.Verify(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that garbage collection uses case-insensitive hash comparison so uppercase manifest hashes protect lowercase CAS objects.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task RunGarbageCollectionAsync_WithUppercaseManifestHashAndLowercaseStorageHash_ProtectsBlob()
    {
        // Arrange
        const string uppercaseHash = "C3D4E5F60123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123A1B2";
        var lowercaseHash = uppercaseHash.ToLowerInvariant();

        var manifest = new ContentManifest
        {
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "data.big",
                    Hash = uppercaseHash,
                },
            ],
        };

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([manifest]));

        _primaryStorageMock
            .Setup(s => s.GetAllObjectHashesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([lowercaseHash]);

        using var manager = CreateLifecycleManager();

        // Act
        var result = await manager.RunGarbageCollectionAsync(force: true);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(0, result.Data.ObjectsDeleted);
        Assert.Equal(1, result.Data.ObjectsReferenced);
        _primaryStorageMock.Verify(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that unreferenced blobs older than the grace period are deleted by normal garbage collection.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task RunGarbageCollectionAsync_WhenUnreferencedAndPastGracePeriod_DeletesBlob()
    {
        // Arrange
        const string unreferencedHash = "deadbeef0123456789abcdef0123456789abcdef0123456789abcdef01234567";

        _primaryStorageMock
            .Setup(s => s.GetAllObjectHashesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([unreferencedHash]);
        _primaryStorageMock
            .Setup(s => s.GetObjectCreationTimeAsync(unreferencedHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTime.UtcNow.AddDays(-3)); // 3 days old, past 1-day grace period
        _primaryStorageMock
            .Setup(s => s.DeleteObjectAsync(unreferencedHash, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var manager = CreateLifecycleManager();

        // Act
        var result = await manager.RunGarbageCollectionAsync(force: false);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Data.ObjectsDeleted);
        Assert.Equal(0, result.Data.ObjectsReferenced);
        _primaryStorageMock.Verify(s => s.DeleteObjectAsync(unreferencedHash, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that unreferenced blobs within the grace period are retained during normal GC, but removed when forced.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task RunGarbageCollectionAsync_WhenUnreferencedWithinGracePeriod_DoesNotDeleteUnlessForced()
    {
        // Arrange
        const string youngHash = "feedface0123456789abcdef0123456789abcdef0123456789abcdef01234567";

        _primaryStorageMock
            .Setup(s => s.GetAllObjectHashesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([youngHash]);
        _primaryStorageMock
            .Setup(s => s.GetObjectCreationTimeAsync(youngHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTime.UtcNow.AddHours(-2)); // 2 hours old, within 1-day grace period

        using var manager = CreateLifecycleManager();

        // Act - not forced
        var normalResult = await manager.RunGarbageCollectionAsync(force: false);

        // Assert - kept due to grace period
        Assert.True(normalResult.Success);
        Assert.NotNull(normalResult.Data);
        Assert.Equal(0, normalResult.Data.ObjectsDeleted);
        Assert.Equal(1, normalResult.Data.ObjectsReferenced);
        _primaryStorageMock.Verify(s => s.DeleteObjectAsync(youngHash, It.IsAny<CancellationToken>()), Times.Never);

        // Setup deletion for forced pass
        _primaryStorageMock
            .Setup(s => s.DeleteObjectAsync(youngHash, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act - forced
        var forceResult = await manager.RunGarbageCollectionAsync(force: true);

        // Assert - deleted
        Assert.True(forceResult.Success);
        Assert.NotNull(forceResult.Data);
        Assert.Equal(1, forceResult.Data.ObjectsDeleted);
        _primaryStorageMock.Verify(s => s.DeleteObjectAsync(youngHash, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that if the manifest pool query fails, garbage collection fails closed and deletes nothing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task RunGarbageCollectionAsync_WhenManifestPoolFails_FailsClosedAndDeletesNothing()
    {
        // Arrange
        const string hash = "11223344556677889900aabbccddeeff11223344556677889900aabbccddeeff";

        _primaryStorageMock
            .Setup(s => s.GetAllObjectHashesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([hash]);

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateFailure("Database offline"));

        using var manager = CreateLifecycleManager();

        // Act
        var result = await manager.RunGarbageCollectionAsync(force: true);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Database offline", result.FirstError);
        _primaryStorageMock.Verify(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that if the reference tracker throws, garbage collection fails closed and deletes nothing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task RunGarbageCollectionAsync_WhenReferenceTrackerThrows_FailsClosedAndDeletesNothing()
    {
        // Arrange
        const string hash = "11223344556677889900aabbccddeeff11223344556677889900aabbccddeeff";

        _primaryStorageMock
            .Setup(s => s.GetAllObjectHashesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([hash]);

        _referenceTrackerMock
            .Setup(t => t.GetAllReferencedHashesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk read error"));

        using var manager = CreateLifecycleManager();

        // Act
        var result = await manager.RunGarbageCollectionAsync(force: true);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Disk read error", result.FirstError);
        _primaryStorageMock.Verify(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that garbage collection scans and cleans unreferenced objects across all configured CAS pools.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task RunGarbageCollectionAsync_SpansMultipleCasPools()
    {
        // Arrange
        const string primaryUnreferenced = "1111111111111111111111111111111111111111111111111111111111111111";
        const string installationUnreferenced = "2222222222222222222222222222222222222222222222222222222222222222";

        _primaryStorageMock
            .Setup(s => s.GetAllObjectHashesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([primaryUnreferenced]);
        _primaryStorageMock
            .Setup(s => s.DeleteObjectAsync(primaryUnreferenced, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var installationStorageMock = new Mock<ICasStorage>();
        installationStorageMock
            .Setup(s => s.GetAllObjectHashesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([installationUnreferenced]);
        installationStorageMock
            .Setup(s => s.DeleteObjectAsync(installationUnreferenced, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var poolManagerMock = new Mock<ICasPoolManager>();
        poolManagerMock
            .Setup(p => p.GetAllStorages())
            .Returns([_primaryStorageMock.Object, installationStorageMock.Object]);

        using var manager = new CasLifecycleManager(
            _referenceTrackerMock.Object,
            _manifestPoolMock.Object,
            _primaryStorageMock.Object,
            _config,
            NullLogger<CasLifecycleManager>.Instance,
            poolManagerMock.Object);

        // Act
        var result = await manager.RunGarbageCollectionAsync(force: true);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.ObjectsDeleted);
        Assert.Equal(0, result.Data.ObjectsReferenced);
        _primaryStorageMock.Verify(s => s.DeleteObjectAsync(primaryUnreferenced, It.IsAny<CancellationToken>()), Times.Once);
        installationStorageMock.Verify(s => s.DeleteObjectAsync(installationUnreferenced, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that when garbage collection is already in progress, concurrent invocations return a skipped result.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task RunGarbageCollectionAsync_WhenAlreadyInProgress_ReturnsSkippedResult()
    {
        // Arrange
        using var manager = CreateLifecycleManager();

        var releaseSignal = new TaskCompletionSource<bool>();
        _referenceTrackerMock
            .Setup(t => t.GetAllReferencedHashesAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await releaseSignal.Task;
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            });

        var firstGcTask = Task.Run(() => manager.RunGarbageCollectionAsync(force: false));

        // Wait a short moment to ensure the first GC acquired the lock
        await Task.Delay(50);

        // Act - run second GC with 1ms timeout
        var secondResult = await manager.RunGarbageCollectionAsync(force: false, lockTimeout: TimeSpan.FromMilliseconds(1));

        // Release first GC
        releaseSignal.SetResult(true);
        await firstGcTask;

        // Assert
        Assert.True(secondResult.Success);
        Assert.NotNull(secondResult.Data);
        Assert.True(secondResult.Data.Skipped);
        Assert.True(secondResult.Data.InProgress);
    }

    private CasLifecycleManager CreateLifecycleManager(ICasPoolManager? poolManager = null)
    {
        return new CasLifecycleManager(
            _referenceTrackerMock.Object,
            _manifestPoolMock.Object,
            _primaryStorageMock.Object,
            _config,
            NullLogger<CasLifecycleManager>.Instance,
            poolManager);
    }
}
