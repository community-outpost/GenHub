using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Storage;

/// <summary>
/// Verifies that CAS statistics and integrity validation span every configured pool,
/// so the Settings Danger Zone reports the real pool footprint instead of one pool.
/// </summary>
public class CasServicePoolAwarenessTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"CasPoolAwareness_{Guid.NewGuid():N}");
    private bool _disposed;

    /// <summary>
    /// Verifies that stats aggregate unique objects across all pools while sizes reflect on-disk bytes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task GetStatsAsync_AggregatesObjectsAcrossAllPoolsAsync()
    {
        var primaryFile = CreateSizedFile("primary-a.bin", 100);
        var primarySharedFile = CreateSizedFile("primary-shared.bin", 200);
        var installationSharedFile = CreateSizedFile("installation-shared.bin", 200);
        var installationFile = CreateSizedFile("installation-b.bin", 300);
        var hashA = new string('a', 64);
        var sharedHash = new string('b', 64);
        var hashB = new string('c', 64);

        var primary = CreatePoolStorage(new Dictionary<string, string>
        {
            [hashA] = primaryFile,
            [sharedHash] = primarySharedFile,
        });
        var installation = CreatePoolStorage(new Dictionary<string, string>
        {
            [sharedHash] = installationSharedFile,
            [hashB] = installationFile,
        });
        var service = CreateService(primary.Object, installation.Object);

        var stats = await service.GetStatsAsync();

        Assert.Equal(3, stats.ObjectCount);
        Assert.Equal(800, stats.TotalSize);
    }

    /// <summary>
    /// Verifies that stats fall back to the default storage when no pool manager is configured.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task GetStatsAsync_WithoutPoolManager_UsesDefaultStorageAsync()
    {
        var file = CreateSizedFile("default.bin", 128);
        var hash = new string('d', 64);
        var storage = CreatePoolStorage(new Dictionary<string, string> { [hash] = file });
        var service = new CasService(
            storage.Object,
            NullLogger<CasService>.Instance,
            Mock.Of<IFileHashProvider>(),
            Mock.Of<IStreamHashProvider>());

        var stats = await service.GetStatsAsync();

        Assert.Equal(1, stats.ObjectCount);
        Assert.Equal(128, stats.TotalSize);
    }

    /// <summary>
    /// Verifies that integrity validation covers objects in every pool.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateIntegrityAsync_CoversObjectsInEveryPoolAsync()
    {
        var hashA = new string('a', 64);
        var hashB = new string('b', 64);
        var fileA = CreateSizedFile("primary-a.bin", 64);
        var fileB = CreateSizedFile("installation-b.bin", 64);
        var primary = CreatePoolStorage(new Dictionary<string, string> { [hashA] = fileA });
        var installation = CreatePoolStorage(new Dictionary<string, string> { [hashB] = fileB });
        var hashProvider = new Mock<IFileHashProvider>();
        hashProvider.Setup(provider => provider.ComputeFileHashAsync(fileA, It.IsAny<CancellationToken>())).ReturnsAsync(hashA);
        hashProvider.Setup(provider => provider.ComputeFileHashAsync(fileB, It.IsAny<CancellationToken>())).ReturnsAsync(hashB);
        var service = CreateService(primary.Object, installation.Object, hashProvider.Object);

        var result = await service.ValidateIntegrityAsync();

        Assert.True(result.Success);
        Assert.Equal(2, result.ObjectsValidated);
        Assert.Empty(result.Issues);
    }

    /// <summary>
    /// Verifies that a hash mismatch in a non-primary pool is reported.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateIntegrityAsync_ReportsMismatchInSecondaryPoolAsync()
    {
        var hashA = new string('a', 64);
        var hashB = new string('b', 64);
        var fileA = CreateSizedFile("primary-a.bin", 64);
        var fileB = CreateSizedFile("installation-b.bin", 64);
        var primary = CreatePoolStorage(new Dictionary<string, string> { [hashA] = fileA });
        var installation = CreatePoolStorage(new Dictionary<string, string> { [hashB] = fileB });
        var hashProvider = new Mock<IFileHashProvider>();
        hashProvider.Setup(provider => provider.ComputeFileHashAsync(fileA, It.IsAny<CancellationToken>())).ReturnsAsync(hashA);
        hashProvider.Setup(provider => provider.ComputeFileHashAsync(fileB, It.IsAny<CancellationToken>())).ReturnsAsync(new string('e', 64));
        var service = CreateService(primary.Object, installation.Object, hashProvider.Object);

        var result = await service.ValidateIntegrityAsync();

        Assert.False(result.Success);
        Assert.Equal(2, result.ObjectsValidated);
        Assert.Single(result.Issues);
        Assert.Equal(hashB, result.Issues[0].ExpectedHash);
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

    private static Mock<ICasStorage> CreatePoolStorage(Dictionary<string, string> hashToPath)
    {
        var storage = new Mock<ICasStorage>();
        storage.Setup(pool => pool.GetAllObjectHashesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([.. hashToPath.Keys]);
        foreach (var (hash, path) in hashToPath)
        {
            storage.Setup(pool => pool.GetObjectPath(hash)).Returns(path);
        }

        return storage;
    }

    private CasService CreateService(
        ICasStorage primary,
        ICasStorage installation,
        IFileHashProvider? hashProvider = null)
    {
        var poolManager = new Mock<ICasPoolManager>();
        poolManager.Setup(manager => manager.GetAllStorages()).Returns([primary, installation]);

        return new CasService(
            primary,
            NullLogger<CasService>.Instance,
            hashProvider ?? Mock.Of<IFileHashProvider>(),
            Mock.Of<IStreamHashProvider>(),
            poolManager.Object);
    }

    private string CreateSizedFile(string name, int sizeBytes)
    {
        Directory.CreateDirectory(_tempPath);
        var path = Path.Combine(_tempPath, name);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }
}
