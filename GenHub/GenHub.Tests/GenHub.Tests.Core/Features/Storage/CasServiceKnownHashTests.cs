using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Results;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Storage;

/// <summary>
/// Verifies that known-hash CAS stores skip source re-hashing and fall back safely.
/// </summary>
public class CasServiceKnownHashTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"CasKnownHash_{Guid.NewGuid():N}");
    private bool _disposed;

    /// <summary>
    /// Verifies that storing with a known hash never re-hashes the source file.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task StoreContentWithKnownHashAsync_SkipsSourceHashingAsync()
    {
        // Arrange
        var sourceFile = CreateSourceFile("known.bin", "known-bytes");
        var knownHash = new string('a', 64);
        var storage = new Mock<ICasStorage>();
        storage.Setup(pool => pool.ObjectExistsAsync(knownHash, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        storage.Setup(pool => pool.StoreObjectAsync(It.IsAny<Stream>(), knownHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Path.Combine("cas", knownHash));
        var hashProvider = new Mock<IFileHashProvider>(MockBehavior.Strict);
        var service = CreateService(storage.Object, hashProvider.Object);

        // Act
        var result = await service.StoreContentWithKnownHashAsync(sourceFile, knownHash, ContentType.Patch);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(knownHash, result.Data);
        hashProvider.Verify(
            provider => provider.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that a failed known-hash store falls back to the re-hashing path.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task StoreContentWithKnownHashAsync_WhenStoreFails_FallsBackToFreshHashAsync()
    {
        // Arrange
        var sourceFile = CreateSourceFile("changed.bin", "changed-bytes");
        var staleHash = new string('a', 64);
        var freshHash = new string('b', 64);
        var storage = new Mock<ICasStorage>();
        storage.Setup(pool => pool.ObjectExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        storage.SetupSequence(pool => pool.StoreObjectAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null)
            .ReturnsAsync(Path.Combine("cas", freshHash));
        var hashProvider = new Mock<IFileHashProvider>();
        hashProvider.Setup(provider => provider.ComputeFileHashAsync(sourceFile, It.IsAny<CancellationToken>())).ReturnsAsync(freshHash);
        var service = CreateService(storage.Object, hashProvider.Object);

        // Act
        var result = await service.StoreContentWithKnownHashAsync(sourceFile, staleHash, ContentType.Patch);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(freshHash, result.Data);
        hashProvider.Verify(
            provider => provider.ComputeFileHashAsync(sourceFile, It.IsAny<CancellationToken>()),
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

    private CasService CreateService(ICasStorage storage, IFileHashProvider hashProvider)
    {
        var poolManager = new Mock<ICasPoolManager>();
        poolManager.Setup(manager => manager.GetStorage(It.IsAny<ContentType>())).Returns(storage);

        return new CasService(
            storage,
            NullLogger<CasService>.Instance,
            hashProvider,
            Mock.Of<IStreamHashProvider>(),
            poolManager.Object);
    }

    private string CreateSourceFile(string fileName, string content)
    {
        Directory.CreateDirectory(_tempPath);
        var path = Path.Combine(_tempPath, fileName);
        File.WriteAllText(path, content);
        return path;
    }
}
