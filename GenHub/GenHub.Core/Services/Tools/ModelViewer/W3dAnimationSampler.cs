using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.ModelViewer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GenHub.Core.Services.Tools.ModelViewer;

/// <summary>
/// Evaluates hierarchy bind poses and samples animation clips.
/// Channels compose as deltas over the base pose: translation deltas apply in
/// the base orientation frame and rotation deltas premultiply the base rotation.
/// </summary>
public static class W3dAnimationSampler
{
    /// <summary>
    /// Computes bind-pose world transforms, one per pivot.
    /// </summary>
    /// <param name="hierarchy">The hierarchy.</param>
    /// <returns>The world transforms in pivot order.</returns>
    public static IReadOnlyList<Matrix4x4> BindPoseWorlds(W3dHierarchy hierarchy)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        var worlds = new Matrix4x4[hierarchy.Pivots.Count];
        for (int i = 0; i < hierarchy.Pivots.Count; i++)
        {
            worlds[i] = WorldTransform(hierarchy, i, null, 0);
        }

        return worlds;
    }

    /// <summary>
    /// Builds bind-pose skeleton segments.
    /// </summary>
    /// <param name="hierarchy">The hierarchy.</param>
    /// <returns>The segments from each pivot to its parent.</returns>
    public static IReadOnlyList<W3dSkeletonSegment> BindPoseSegments(W3dHierarchy hierarchy)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        var worlds = BindPoseWorlds(hierarchy);
        var segments = new List<W3dSkeletonSegment>();
        for (int i = 0; i < hierarchy.Pivots.Count; i++)
        {
            var pivot = hierarchy.Pivots[i];
            var end = new W3dVector3(worlds[i].Translation.X, worlds[i].Translation.Y, worlds[i].Translation.Z);
            if (pivot.ParentIndex < 0 || pivot.ParentIndex >= hierarchy.Pivots.Count)
            {
                segments.Add(new W3dSkeletonSegment(end, end, i, pivot.Name, -1));
            }
            else
            {
                var parent = worlds[pivot.ParentIndex].Translation;
                segments.Add(new W3dSkeletonSegment(new W3dVector3(parent.X, parent.Y, parent.Z), end, i, pivot.Name, pivot.ParentIndex));
            }
        }

        return segments;
    }

    /// <summary>
    /// Samples a clip at a frame, returning world transforms in pivot order.
    /// Values interpolate linearly between bracketing keys; frames outside a
    /// channel range hold the nearest key and quaternions use spherical interpolation.
    /// </summary>
    /// <param name="hierarchy">The hierarchy.</param>
    /// <param name="clip">The clip to sample.</param>
    /// <param name="frame">The frame number.</param>
    /// <returns>The world transforms in pivot order.</returns>
    public static IReadOnlyList<Matrix4x4> SampleFrame(W3dHierarchy hierarchy, W3dAnimationClip clip, int frame)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        ArgumentNullException.ThrowIfNull(clip);
        var channelsByPivot = clip.Channels
            .Where(channel => !channel.HasRawPayload && channel.Keys.Count > 0)
            .GroupBy(channel => channel.Pivot)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<W3dAnimationChannel>)group.ToList());

        var worlds = new Matrix4x4[hierarchy.Pivots.Count];
        for (int i = 0; i < hierarchy.Pivots.Count; i++)
        {
            worlds[i] = WorldTransform(hierarchy, i, channelsByPivot, frame);
        }

        return worlds;
    }

    private static Matrix4x4 WorldTransform(
        W3dHierarchy hierarchy,
        int pivotIndex,
        IReadOnlyDictionary<int, IReadOnlyList<W3dAnimationChannel>>? channelsByPivot,
        int frame)
    {
        var chain = new Stack<int>();
        int current = pivotIndex;
        int guard = 0;
        while (current >= 0 && current < hierarchy.Pivots.Count && guard <= hierarchy.Pivots.Count)
        {
            chain.Push(current);
            current = hierarchy.Pivots[current].ParentIndex;
            guard++;
        }

        var world = Matrix4x4.Identity;
        while (chain.Count > 0)
        {
            int index = chain.Pop();
            world = LocalTransform(hierarchy.Pivots[index], index, channelsByPivot, frame) * world;
        }

        return world;
    }

    private static Matrix4x4 LocalTransform(
        W3dPivot pivot,
        int pivotIndex,
        IReadOnlyDictionary<int, IReadOnlyList<W3dAnimationChannel>>? channelsByPivot,
        int frame)
    {
        var baseTranslation = new Vector3(pivot.Translation.X, pivot.Translation.Y, pivot.Translation.Z);
        var baseRotation = ToNumerics(Normalize(pivot.Rotation));
        var rotation = baseRotation;
        var deltaTranslation = Vector3.Zero;

        if (channelsByPivot?.TryGetValue(pivotIndex, out var channels) == true)
        {
            ApplyChannels(channels, frame, ref deltaTranslation, ref rotation);
        }

        var translation = Vector3.Transform(deltaTranslation, baseRotation) + baseTranslation;
        return Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
    }

    private static void ApplyChannels(IReadOnlyList<W3dAnimationChannel> channels, int frame, ref Vector3 translation, ref Quaternion rotation)
    {
        float eulerX = 0;
        float eulerY = 0;
        float eulerZ = 0;
        bool hasEuler = false;

        foreach (var channel in channels)
        {
            if (channel.Keys.Count == 0)
            {
                continue;
            }

            switch (channel.ChannelType)
            {
                case W3dConstants.AnimationChannels.X:
                    translation.X += SampleScalar(channel, frame);
                    break;
                case W3dConstants.AnimationChannels.Y:
                    translation.Y += SampleScalar(channel, frame);
                    break;
                case W3dConstants.AnimationChannels.Z:
                    translation.Z += SampleScalar(channel, frame);
                    break;
                case W3dConstants.AnimationChannels.Xr:
                    eulerX += SampleScalar(channel, frame);
                    hasEuler = true;
                    break;
                case W3dConstants.AnimationChannels.Yr:
                    eulerY += SampleScalar(channel, frame);
                    hasEuler = true;
                    break;
                case W3dConstants.AnimationChannels.Zr:
                    eulerZ += SampleScalar(channel, frame);
                    hasEuler = true;
                    break;
                case W3dConstants.AnimationChannels.Q:
                    var delta = SampleQuaternion(channel, frame);
                    if (delta != null)
                    {
                        rotation = Quaternion.Multiply(delta.Value, rotation);
                    }

                    break;
                default:
                    // Unknown or unsupported animation channel type; ignore.
                    break;
            }
        }

        if (hasEuler)
        {
            var eulerDelta = Quaternion.CreateFromYawPitchRoll(eulerY, eulerX, eulerZ);
            rotation = Quaternion.Multiply(eulerDelta, rotation);
        }
    }

    private static float SampleScalar(W3dAnimationChannel channel, int frame)
    {
        BracketKeys(channel, frame, out var lower, out var upper);
        float first = lower.Values.Count > 0 ? lower.Values[0] : 0;
        if (ReferenceEquals(lower, upper) || lower.Frame == upper.Frame || upper.Values.Count == 0)
        {
            return first;
        }

        float t = (float)(frame - lower.Frame) / (upper.Frame - lower.Frame);
        return first + ((upper.Values[0] - first) * t);
    }

    private static Quaternion? SampleQuaternion(W3dAnimationChannel channel, int frame)
    {
        BracketKeys(channel, frame, out var lower, out var upper);
        if (lower.Values.Count < 4)
        {
            return null;
        }

        var first = ToNumerics(Normalize(new W3dQuaternion(lower.Values[0], lower.Values[1], lower.Values[2], lower.Values[3])));
        if (ReferenceEquals(lower, upper) || lower.Frame == upper.Frame || upper.Values.Count < 4)
        {
            return first;
        }

        var second = ToNumerics(Normalize(new W3dQuaternion(upper.Values[0], upper.Values[1], upper.Values[2], upper.Values[3])));
        float t = (float)(frame - lower.Frame) / (upper.Frame - lower.Frame);
        return Quaternion.Slerp(first, second, t);
    }

    private static void BracketKeys(W3dAnimationChannel channel, int frame, out W3dAnimationKey lower, out W3dAnimationKey upper)
    {
        lower = channel.Keys[0];
        upper = channel.Keys[^1];
        foreach (var key in channel.Keys)
        {
            if (key.Frame <= frame && key.Frame >= lower.Frame)
            {
                lower = key;
            }

            if (key.Frame >= frame && key.Frame <= upper.Frame)
            {
                upper = key;
            }
        }
    }

    private static W3dQuaternion Normalize(W3dQuaternion rotation)
    {
        float length = (float)Math.Sqrt(
            (rotation.X * rotation.X) + (rotation.Y * rotation.Y) + (rotation.Z * rotation.Z) + (rotation.W * rotation.W));
        if (length < 1e-6f)
        {
            return new W3dQuaternion(0, 0, 0, 1);
        }

        return new W3dQuaternion(rotation.X / length, rotation.Y / length, rotation.Z / length, rotation.W / length);
    }

    private static Quaternion ToNumerics(W3dQuaternion rotation)
    {
        return new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
    }
}
