using GenHub.Core.Models.Storage;

namespace GenHub.Tests.Core.Features.Storage;

/// <summary>
/// Unit tests for <see cref="CasWriteFence"/>.
/// </summary>
public class CasWriteFenceTests
{
    /// <summary>
    /// Verifies that the fence reports active writes only while a lease is held.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task HasActiveWrites_ReflectsHeldLeasesAsync()
    {
        // Arrange
        var fence = new CasWriteFence();

        // Act and assert
        Assert.False(fence.HasActiveWrites);

        using (var lease = await fence.TrackWriteAsync())
        {
            Assert.True(fence.HasActiveWrites);
        }

        Assert.False(fence.HasActiveWrites);
    }

    /// <summary>
    /// Verifies that overlapping leases keep the fence held until the last one is released.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task TrackWriteAsync_SupportsOverlappingLeasesAsync()
    {
        // Arrange
        var fence = new CasWriteFence();
        using var first = await fence.TrackWriteAsync();
        using var second = await fence.TrackWriteAsync();

        // Act and assert
        Assert.True(fence.HasActiveWrites);
        first.Dispose();
        Assert.True(fence.HasActiveWrites);
        second.Dispose();
        Assert.False(fence.HasActiveWrites);
    }

    /// <summary>
    /// Verifies that disposing a lease twice releases the fence only once.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task Lease_DisposeIsIdempotentAsync()
    {
        // Arrange
        var fence = new CasWriteFence();
        var lease = await fence.TrackWriteAsync();

        // Act
        lease.Dispose();
        lease.Dispose();

        // Assert
        Assert.False(fence.HasActiveWrites);
    }

    /// <summary>
    /// Verifies that the collection lease is refused while a write lease is held.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task TryAcquireCollectionLease_WhenWriteActive_ReturnsFalseAsync()
    {
        // Arrange
        var fence = new CasWriteFence();
        using var write = await fence.TrackWriteAsync();

        // Act
        var acquired = fence.TryAcquireCollectionLease(TimeSpan.Zero, out var lease);

        // Assert
        Assert.False(acquired);
        Assert.Null(lease);
    }

    /// <summary>
    /// Verifies that a write lease waits while the collection lease is held, then proceeds after release.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task TrackWriteAsync_WhenCollectionLeaseHeld_WaitsForReleaseAsync()
    {
        // Arrange
        var fence = new CasWriteFence();
        Assert.True(fence.TryAcquireCollectionLease(TimeSpan.FromSeconds(5), out var collection));
        Assert.NotNull(collection);

        // Act
        var writeTask = fence.TrackWriteAsync();
        await Task.Delay(50);

        // Assert - still waiting while the collection lease is held
        Assert.False(writeTask.IsCompleted);
        collection.Dispose();

        // Act - released, the waiting write proceeds
        using var write = await writeTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(fence.HasActiveWrites);
    }
}
