using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Results.CAS;
using GenHub.Core.Models.Storage;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace GenHub.Tests.Core.Features.Storage;

/// <summary>
/// Verifies that every programmatic CAS garbage-collection layer fails closed.
/// </summary>
public class CasGarbageCollectionDisabledTests
{
    /// <summary>
    /// Verifies that direct service calls cannot scan or delete CAS blobs, including forced calls.
    /// </summary>
    /// <param name="force">Whether the caller requests forced collection.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CasService_RunGarbageCollectionAsync_IsDisabledWithoutStorageAccess(bool force)
    {
        var storage = new Mock<ICasStorage>(MockBehavior.Strict);
        var referenceTracker = new Mock<ICasReferenceTracker>(MockBehavior.Strict);
        var service = new CasService(
            storage.Object,
            referenceTracker.Object,
            NullLogger<CasService>.Instance,
            Options.Create(new CasConfiguration()),
            Mock.Of<IFileHashProvider>(),
            Mock.Of<IStreamHashProvider>());

        var result = await service.RunGarbageCollectionAsync(force);

        Assert.False(result.Success);
        Assert.True(result.Disabled);
        Assert.Equal(CasDefaults.GarbageCollectionDisabledMessage, result.FirstError);
        Assert.Equal(0, result.ObjectsDeleted);
        Assert.Equal(0, result.BytesFreed);
        storage.VerifyNoOtherCalls();
        referenceTracker.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that the lifecycle API preserves the disabled result and reports no deletion.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task CasLifecycleManager_RunGarbageCollectionAsync_ReportsDisabled()
    {
        var casService = new Mock<ICasService>();
        casService
            .Setup(service => service.RunGarbageCollectionAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CasGarbageCollectionResult.CreateDisabled());

        using var lifecycleManager = new CasLifecycleManager(
            Mock.Of<ICasReferenceTracker>(),
            casService.Object,
            Mock.Of<ICasStorage>(),
            Options.Create(new CasConfiguration()),
            NullLogger<CasLifecycleManager>.Instance);

        var result = await lifecycleManager.RunGarbageCollectionAsync(force: true);

        Assert.False(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.Disabled);
        Assert.Equal(CasDefaults.GarbageCollectionDisabledMessage, result.FirstError);
        Assert.Equal(0, result.Data.ObjectsDeleted);
        Assert.Equal(0, result.Data.BytesFreed);
    }
}
