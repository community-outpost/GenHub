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
    [Fact]
    public void HasActiveWrites_ReflectsHeldLeases()
    {
        // Arrange
        var fence = new CasWriteFence();

        // Act and assert
        Assert.False(fence.HasActiveWrites);

        using (var lease = fence.TrackWrite())
        {
            Assert.True(fence.HasActiveWrites);
        }

        Assert.False(fence.HasActiveWrites);
    }

    /// <summary>
    /// Verifies that overlapping leases keep the fence held until the last one is released.
    /// </summary>
    [Fact]
    public void TrackWrite_SupportsOverlappingLeases()
    {
        // Arrange
        var fence = new CasWriteFence();
        using var first = fence.TrackWrite();
        using var second = fence.TrackWrite();

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
    [Fact]
    public void Lease_DisposeIsIdempotent()
    {
        // Arrange
        var fence = new CasWriteFence();
        var lease = fence.TrackWrite();

        // Act
        lease.Dispose();
        lease.Dispose();

        // Assert
        Assert.False(fence.HasActiveWrites);
    }
}
