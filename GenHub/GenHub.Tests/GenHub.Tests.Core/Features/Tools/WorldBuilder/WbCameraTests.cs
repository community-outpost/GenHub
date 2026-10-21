// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using System;
using System.Numerics;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for <see cref="WbCamera"/> against the WbView3d setupCamera math.
/// </summary>
public sealed class WbCameraTests
{
    /// <summary>
    /// Verifies reset derives the offset from height, pitch, and yaw.
    /// </summary>
    [Fact]
    public void Reset_FramingTunables_DerivesOffset()
    {
        var camera = new WbCamera();

        camera.Reset(new Vector2(50, 50), 0.0f, 300.0f, 45.0f, 0.0f);

        Assert.Equal(0.0f, camera.Offset.X, 3);
        Assert.Equal(-300.0f, camera.Offset.Y, 3);
        Assert.Equal(300.0f, camera.Offset.Z, 3);
        Assert.Equal(0.0f, camera.WheelOffset);
    }

    /// <summary>
    /// Verifies the default eye and target placement on flat ground.
    /// </summary>
    [Fact]
    public void ComputeEyeTarget_FlatGround_PlacesEyeAndTarget()
    {
        var camera = new WbCamera();
        camera.Reset(new Vector2(50, 50), 0.0f, 300.0f, 45.0f, 0.0f);

        var (eye, target) = camera.ComputeEyeTarget();

        Assert.Equal(500.0f, eye.X, 2);
        Assert.Equal(200.0f, eye.Y, 2);
        Assert.Equal(300.0f, eye.Z, 2);
        Assert.Equal(500.0f, target.X, 2);
        Assert.Equal(500.0f, target.Y, 2);
        Assert.Equal(0.0f, target.Z, 2);
    }

    /// <summary>
    /// Verifies the triple-speed zoom curve: no wheel input is unity, and a
    /// full positive detent run moves the zoom.
    /// </summary>
    [Fact]
    public void ComputeZoom_WheelInput_MapsCurve()
    {
        var offset = new Vector3(0, -300, 300).Length();

        Assert.Equal(1.0f, WbCamera.ComputeZoom(0.0f, offset, 0.0f));
        Assert.Equal(2.0f, WbCamera.ComputeZoom(-1200.0f, offset, 0.0f), 3);
        Assert.True(WbCamera.ComputeZoom(1200.0f, offset, 0.0f) < 1.0f);
    }

    /// <summary>
    /// Verifies clip planes track the look distance.
    /// </summary>
    [Fact]
    public void ClipPlanes_FlatGround_TracksLookDistance()
    {
        var camera = new WbCamera();
        camera.Reset(new Vector2(50, 50), 0.0f, 300.0f, 45.0f, 0.0f);

        var (near, far) = camera.ClipPlanes();

        var expected = MathF.Sqrt(300.0f * 300.0f * 2.0f);
        Assert.Equal(expected / 200.0f, near, 3);
        Assert.Equal(expected * 3.0f, far, 2);
    }

    /// <summary>
    /// Verifies yaw ratchets to 45-degree steps under Angle Snap Lock.
    /// </summary>
    [Fact]
    public void Orbit_Snap45_Ratchets()
    {
        var camera = new WbCamera { Snap45 = true };

        camera.Orbit(0.5f);

        Assert.Equal(MathF.PI / 4.0f, camera.YawRadians, 4);
        Assert.Equal(0.5f, camera.YawRawRadians, 4);
    }

    /// <summary>
    /// Verifies the top-down projection parks the eye over the target.
    /// </summary>
    [Fact]
    public void ComputeEyeTarget_TopDown_ParksEyeOverhead()
    {
        var camera = new WbCamera { TopDown = true };
        camera.Reset(new Vector2(50, 50), 0.0f, 300.0f, 45.0f, 0.0f);

        var (eye, target) = camera.ComputeEyeTarget();

        Assert.Equal(target.X, eye.X);
        Assert.Equal(target.Y, eye.Y);
        Assert.Equal(0.0f, target.Z);
        Assert.True(eye.Z >= WbCamera.MinLookDistance);
    }

    /// <summary>
    /// Verifies the view center ray passes through the target.
    /// </summary>
    [Fact]
    public void ViewMatrix_CenterPixel_RayHitsTarget()
    {
        var camera = new WbCamera();
        camera.Reset(new Vector2(50, 50), 0.0f, 300.0f, 45.0f, 0.0f);
        var view = camera.ViewMatrix();
        var projection = camera.ProjectionMatrix(16.0f / 9.0f);

        var (origin, direction) = WbPicking.ScreenPointToRay(new Vector2(960, 540), new Vector2(1920, 1080), view, projection);
        var (_, target) = camera.ComputeEyeTarget();
        var expected = Vector3.Normalize(target - origin);

        Assert.Equal(expected.X, direction.X, 3);
        Assert.Equal(expected.Y, direction.Y, 3);
        Assert.Equal(expected.Z, direction.Z, 3);
    }
}
