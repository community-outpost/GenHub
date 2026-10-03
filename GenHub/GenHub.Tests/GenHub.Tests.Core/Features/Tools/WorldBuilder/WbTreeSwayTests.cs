// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Features.Tools.WorldBuilder.Rendering;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for the tree sway cycle.
/// </summary>
public sealed class WbTreeSwayTests
{
    /// <summary>
    /// Verifies phase zero is the rest pose.
    /// </summary>
    [Fact]
    public void CycleAt_Zero_IsRestPose()
    {
        var cycle = WbTreeSway.CycleAt(0);

        Assert.Equal(0.0f, cycle.X);
        Assert.Equal(0.0f, cycle.Y);
    }

    /// <summary>
    /// Verifies the quarter cycle peaks the lateral amplitude.
    /// </summary>
    [Fact]
    public void CycleAt_QuarterCycle_PeaksAmplitude()
    {
        var cycle = WbTreeSway.CycleAt(WbTreeSway.EntryCount / 4);

        Assert.Equal(1.0f, cycle.X, 5);
        Assert.Equal(-1.0f, cycle.Y, 5);
    }

    /// <summary>
    /// Verifies phases wrap around the table.
    /// </summary>
    [Fact]
    public void CycleAt_FullCycle_MatchesZero()
    {
        var wrapped = WbTreeSway.CycleAt(WbTreeSway.EntryCount + 3);
        var direct = WbTreeSway.CycleAt(3);

        Assert.Equal(direct.X, wrapped.X);
        Assert.Equal(direct.Y, wrapped.Y);
    }

    /// <summary>
    /// Verifies displacement follows the wind direction.
    /// </summary>
    [Fact]
    public void Displace_QuarterCycle_ScalesWithDirection()
    {
        var offset = WbTreeSway.Displace(WbTreeSway.EntryCount / 4, 0.0f, 1.0f, 2.0f);

        Assert.Equal(0.0f, offset.X);
        Assert.Equal(2.0f, offset.Y);
        Assert.Equal(-2.0f, offset.Z);
    }
}
