// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using System;
using System.Numerics;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// WorldBuilder orbit camera. Ports WbView3d setupCamera/getCurrentZoom/
/// rotateCamera/resetCamera: the eye rides on a yaw-rotated offset scaled by
/// the triple-speed wheel-zoom curve, the target sits on the smoothed ground
/// under the center, and the clip planes track the look distance.
/// One deliberate reconciliation: the C++ camera lives in absolute cell-index
/// world while terrain vertices are border-relative; here the center is stored
/// in map cells and the border is subtracted once when computing, so camera
/// and mesh share one border-relative world.
/// </summary>
public sealed class WbCamera
{
    /// <summary>
    /// Minimum look distance in feet before the clip planes are derived.
    /// </summary>
    public const float MinLookDistance = 300.0f;

    /// <summary>
    /// Near-plane divisor of the look distance.
    /// </summary>
    public const float NearPlaneDivisor = 200.0f;

    /// <summary>
    /// Far-plane multiplier of the look distance.
    /// </summary>
    public const float FarPlaneMultiplier = 3.0f;

    /// <summary>
    /// Vertical field of view in degrees for the 3D view.
    /// </summary>
    public const float DefaultFieldOfViewDegrees = 50.0f;

    /// <summary>
    /// Wheel-delta divisor feeding the triple-speed zoom curve.
    /// </summary>
    public const float WheelCurveDivisor = 1200.0f;

    /// <summary>
    /// Gets or sets the view center in map cells.
    /// </summary>
    public Vector2 CenterCells { get; set; }

    /// <summary>
    /// Gets or sets the camera yaw in radians (snapped when Angle Snap Lock is on).
    /// </summary>
    public float YawRadians { get; set; }

    /// <summary>
    /// Gets or sets the raw accumulated yaw so continuous drags keep building
    /// while the displayed angle ratchets between 45-degree steps.
    /// </summary>
    public float YawRawRadians { get; set; }

    /// <summary>
    /// Gets or sets the accumulated wheel delta driving the zoom curve.
    /// </summary>
    public float WheelOffset { get; set; }

    /// <summary>
    /// Gets or sets the focus height in feet (m_groundLevel).
    /// </summary>
    public float GroundLevel { get; set; }

    /// <summary>
    /// Gets or sets the smoothed terrain height under the center in feet.
    /// </summary>
    public float TerrainGroundZ { get; set; }

    /// <summary>
    /// Gets or sets the eye offset vector in feet (m_cameraOffset).
    /// </summary>
    public Vector3 Offset { get; set; } = new(1.0f, 1.0f, 1.0f);

    /// <summary>
    /// Gets or sets the FX pitch multiplier scaling eye height above target.
    /// </summary>
    public float FxPitch { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets a value indicating whether yaw ratchets to 45-degree steps.
    /// </summary>
    public bool Snap45 { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the top-down projection is active.
    /// </summary>
    public bool TopDown { get; set; }

    /// <summary>
    /// Gets or sets the map border size in cells.
    /// </summary>
    public int BorderSize { get; set; }

    /// <summary>
    /// Gets or sets the vertical field of view in degrees.
    /// </summary>
    public float FieldOfViewDegrees { get; set; } = DefaultFieldOfViewDegrees;

    /// <summary>
    /// Computes the triple-speed zoom multiplier from accumulated wheel input.
    /// </summary>
    /// <param name="wheelOffset">The accumulated wheel delta.</param>
    /// <param name="offsetLength">The eye offset length in feet.</param>
    /// <param name="groundLevel">The focus height in feet.</param>
    /// <returns>The zoom multiplier.</returns>
    public static float ComputeZoom(float wheelOffset, float offsetLength, float groundLevel)
    {
        var zOffset = -wheelOffset / WheelCurveDivisor;
        if (zOffset == 0.0f)
        {
            return 1.0f;
        }

        var zPos = offsetLength <= 0.0f ? 0.0f : (offsetLength - groundLevel) / offsetLength;
        var zAbs = Math.Abs(zOffset + zPos);
        if (zAbs < 0.01f)
        {
            zAbs = 0.01f;
        }

        return zAbs;
    }

    /// <summary>
    /// Resets the camera from GameData-style framing tunables, mirroring
    /// resetCamera: offset height above the ground, pitch setting the
    /// horizontal pullback, yaw setting the lateral skew.
    /// </summary>
    /// <param name="centerCells">The view center in map cells.</param>
    /// <param name="groundLevelFeet">The terrain height under the center.</param>
    /// <param name="maxCameraHeight">The eye height above ground in feet.</param>
    /// <param name="pitchDegrees">The camera pitch in degrees.</param>
    /// <param name="yawDegrees">The camera yaw in degrees.</param>
    public void Reset(Vector2 centerCells, float groundLevelFeet, float maxCameraHeight, float pitchDegrees, float yawDegrees)
    {
        CenterCells = centerCells;
        GroundLevel = groundLevelFeet;
        TerrainGroundZ = groundLevelFeet;
        WheelOffset = 0.0f;
        var pitch = Math.Clamp(pitchDegrees, 5.0f, 80.0f) * MathF.PI / 180.0f;
        var yaw = yawDegrees * MathF.PI / 180.0f;
        var height = groundLevelFeet + maxCameraHeight;
        var offsetY = -(height / MathF.Tan(pitch));
        var offsetX = -(offsetY * MathF.Tan(yaw));
        Offset = new Vector3(offsetX, offsetY, height);
    }

    /// <summary>
    /// Rotates the camera, ratcheting to 45-degree steps under Angle Snap Lock.
    /// </summary>
    /// <param name="deltaRadians">The yaw delta.</param>
    public void Orbit(float deltaRadians)
    {
        if (TopDown)
        {
            return;
        }

        YawRawRadians += deltaRadians;
        YawRadians = Snap45 ? SnapAngleTo45(YawRawRadians) : YawRawRadians;
    }

    /// <summary>
    /// Dollies the camera along the zoom curve (positive rolls in).
    /// </summary>
    /// <param name="wheelDelta">The raw wheel delta (120 per notch).</param>
    public void Dolly(float wheelDelta)
    {
        WheelOffset += wheelDelta;
    }

    /// <summary>
    /// Pans the view center by a cell delta.
    /// </summary>
    /// <param name="deltaCells">The pan delta in cells.</param>
    public void Pan(Vector2 deltaCells)
    {
        CenterCells += deltaCells;
    }

    /// <summary>
    /// Computes the eye and target in border-relative world feet.
    /// </summary>
    /// <returns>The eye and target positions.</returns>
    public (Vector3 Eye, Vector3 Target) ComputeEyeTarget()
    {
        var zoom = ComputeZoom(WheelOffset, Offset.Length(), GroundLevel);
        var pos = new Vector3(
            (CenterCells.X - BorderSize) * WorldBuilderConstants.Terrain.CellSize,
            (CenterCells.Y - BorderSize) * WorldBuilderConstants.Terrain.CellSize,
            0.0f);
        var source = Offset * zoom;
        source = Vector3.Transform(source, Matrix4x4.CreateRotationZ(YawRadians));
        var factor = source.Z != 0.0f ? 1.0f - (TerrainGroundZ / source.Z) : 1.0f;
        source *= factor;
        var eye = new Vector3(pos.X + source.X, pos.Y + source.Y, pos.Z + source.Z + TerrainGroundZ);
        var target = new Vector3(pos.X, pos.Y, pos.Z + TerrainGroundZ);
        var height = (eye.Z - target.Z) * FxPitch;
        target = new Vector3(target.X, target.Y, eye.Z - height);
        if (factor < 0.0f)
        {
            target = eye + (eye - target);
        }

        var lookDistance = LookDistance(eye, target);
        if (TopDown)
        {
            target = new Vector3(target.X, target.Y, 0.0f);
            eye = new Vector3(target.X, target.Y, lookDistance);
        }

        return (eye, target);
    }

    /// <summary>
    /// Computes the view matrix (Z-up look-at).
    /// </summary>
    /// <returns>The view matrix.</returns>
    public Matrix4x4 ViewMatrix()
    {
        var (eye, target) = ComputeEyeTarget();
        return Matrix4x4.CreateLookAt(eye, target, Vector3.UnitZ);
    }

    /// <summary>
    /// Computes the perspective projection matrix.
    /// </summary>
    /// <param name="aspect">The viewport width over height.</param>
    /// <returns>The projection matrix.</returns>
    public Matrix4x4 ProjectionMatrix(float aspect)
    {
        var (near, far) = ClipPlanes();
        return Matrix4x4.CreatePerspectiveFieldOfView(
            FieldOfViewDegrees * MathF.PI / 180.0f,
            Math.Max(0.01f, aspect),
            near,
            far);
    }

    /// <summary>
    /// Computes the near and far clip planes from the look distance.
    /// </summary>
    /// <returns>The near and far distances.</returns>
    public (float Near, float Far) ClipPlanes()
    {
        var (eye, target) = ComputeEyeTarget();
        var lookDistance = LookDistance(eye, target);
        return (lookDistance / NearPlaneDivisor, lookDistance * FarPlaneMultiplier);
    }

    private static float SnapAngleTo45(float angle)
    {
        const float step = MathF.PI / 4.0f;
        return step * MathF.Floor((angle / step) + 0.5f);
    }

    private static float LookDistance(Vector3 eye, Vector3 target)
    {
        var flat = new Vector3(target.X, target.Y, 0.0f);
        var distance = (flat - eye).Length();
        return Math.Max(MinLookDistance, distance);
    }
}
