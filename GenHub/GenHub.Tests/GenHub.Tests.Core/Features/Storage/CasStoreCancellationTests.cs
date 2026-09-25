using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Storage;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Storage;

/// <summary>
/// Verifies that cancelling a CAS write propagates as cancellation and leaves no partial files.
/// </summary>
public class CasStoreCancellationTests : IDisposable
{
    private const int ContentLength = 256 * 1024;

    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"CasStoreCancel_{Guid.NewGuid():N}");
    private readonly byte[] _content;
    private readonly string _hash;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CasStoreCancellationTests"/> class.
    /// </summary>
    public CasStoreCancellationTests()
    {
        _content = new byte[ContentLength];
        new Random(1234).NextBytes(_content);
        _hash = Convert.ToHexString(SHA256.HashData(_content)).ToLowerInvariant();
    }

    /// <summary>
    /// Gets the CasService store entry points that take a file path.
    /// </summary>
    public static IEnumerable<object[]> FilePathStoreOperations =>
    [
        ["StoreContentAsync(path)"],
        ["StoreContentAsync(path, contentType)"],
        ["StoreContentWithKnownHashAsync"],
    ];

    /// <summary>
    /// Verifies that cancelling mid-copy throws and leaves no temp file, object, or lock behind.
    /// </summary>
    /// <param name="verifyIntegrity">Whether the store verifies the hash while copying.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StoreObjectAsync_CancelledDuringCopy_ThrowsAndLeavesNoPartialFilesAsync(bool verifyIntegrity)
    {
        // Arrange
        var storage = CreateStorage(verifyIntegrity);
        using var cts = new CancellationTokenSource();
        await using var content = new CancellingStream(_content, cts);

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => storage.StoreObjectAsync(content, _hash, cts.Token));

        // Assert
        Assert.True(content.CancelledDuringRead);
        AssertNoPartialFiles(storage);
    }

    /// <summary>
    /// Verifies that a store cancelled before the lock is written releases the lock for later writes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task StoreObjectAsync_CancelledBeforeWrite_ReleasesLockForLaterStoreAsync()
    {
        // Arrange
        var storage = CreateStorage(verifyIntegrity: true);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await using var cancelledContent = new MemoryStream(_content);

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => storage.StoreObjectAsync(cancelledContent, _hash, cts.Token));
        await using var content = new MemoryStream(_content);
        var storedPath = await storage.StoreObjectAsync(content, _hash);

        // Assert
        Assert.Equal(storage.GetObjectPath(_hash), storedPath);
        Assert.Equal(_content, await File.ReadAllBytesAsync(storedPath!));
    }

    /// <summary>
    /// Verifies that CasService stream stores propagate a mid-copy cancellation without partial files.
    /// </summary>
    /// <param name="usePool">Whether the pool-aware overload is used.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StoreContentAsync_StreamCancelledDuringCopy_ThrowsAndLeavesNoPartialFilesAsync(bool usePool)
    {
        // Arrange
        var storage = CreateStorage(verifyIntegrity: true);
        var streamHashProvider = new Mock<IStreamHashProvider>();
        streamHashProvider
            .Setup(provider => provider.ComputeStreamHashAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_hash);
        var service = CreateService(storage, Mock.Of<IFileHashProvider>(), streamHashProvider.Object);
        using var cts = new CancellationTokenSource();
        await using var content = new CancellingStream(_content, cts);

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => usePool
                ? service.StoreContentAsync(content, ContentType.Patch, null, cts.Token)
                : service.StoreContentAsync(content, null, cts.Token));

        // Assert
        Assert.True(content.CancelledDuringRead);
        AssertNoPartialFiles(storage);
    }

    /// <summary>
    /// Verifies that CasService file stores propagate a storage cancellation instead of returning a failure.
    /// </summary>
    /// <param name="operation">The store entry point under test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Theory]
    [MemberData(nameof(FilePathStoreOperations))]
    public async Task StoreContentAsync_FileStoreCancelled_ThrowsAndDoesNotRetryAsync(string operation)
    {
        // Arrange
        Directory.CreateDirectory(_tempPath);
        var sourcePath = Path.Combine(_tempPath, "source.bin");
        await File.WriteAllBytesAsync(sourcePath, _content);
        using var cts = new CancellationTokenSource();
        var storage = new Mock<ICasStorage>();
        storage.Setup(s => s.ObjectExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        storage
            .Setup(s => s.StoreObjectAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await cts.CancelAsync();
                cts.Token.ThrowIfCancellationRequested();
                return null;
            });
        var fileHashProvider = new Mock<IFileHashProvider>();
        fileHashProvider
            .Setup(provider => provider.ComputeFileHashAsync(sourcePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_hash);
        var service = CreateService(storage.Object, fileHashProvider.Object, Mock.Of<IStreamHashProvider>());

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation switch
        {
            "StoreContentAsync(path)" => service.StoreContentAsync(sourcePath, null, cts.Token),
            "StoreContentAsync(path, contentType)" => service.StoreContentAsync(sourcePath, ContentType.Patch, null, cts.Token),
            _ => service.StoreContentWithKnownHashAsync(sourcePath, _hash, ContentType.Patch, cts.Token),
        });

        // Assert
        storage.Verify(
            s => s.StoreObjectAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases test filesystem resources.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing && Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, recursive: true);
        }

        _disposed = true;
    }

    private static CasService CreateService(ICasStorage storage, IFileHashProvider fileHashProvider, IStreamHashProvider streamHashProvider)
    {
        var poolManager = new Mock<ICasPoolManager>();
        poolManager.Setup(manager => manager.GetStorage(It.IsAny<ContentType>())).Returns(storage);

        return new CasService(
            storage,
            NullLogger<CasService>.Instance,
            fileHashProvider,
            streamHashProvider,
            poolManager.Object);
    }

    private CasStorage CreateStorage(bool verifyIntegrity)
    {
        return new CasStorage(
            Options.Create(new CasConfiguration { CasRootPath = Path.Combine(_tempPath, "cas"), VerifyIntegrity = verifyIntegrity }),
            NullLogger<CasStorage>.Instance);
    }

    private void AssertNoPartialFiles(CasStorage storage)
    {
        var casRoot = Path.Combine(_tempPath, "cas");
        Assert.False(File.Exists(storage.GetObjectPath(_hash)));
        Assert.True(Directory.Exists(casRoot));
        Assert.Empty(Directory.GetFiles(casRoot, "*", SearchOption.AllDirectories));
    }

    private sealed class CancellingStream(byte[] content, CancellationTokenSource cts) : MemoryStream(content)
    {
        public bool CancelledDuringRead { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            if (!CancelledDuringRead && read > 0)
            {
                CancelledDuringRead = true;
                await cts.CancelAsync();
            }

            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }
    }
}
