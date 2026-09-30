using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.ModelViewer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GenHub.Core.Services.Tools.ModelViewer;

/// <summary>
/// Evaluates hierarchy bind poses and samples animation clips.
/// Channels compose as deltas over the base pose: translations add, rotations premultiply.
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
                segments.Add(new W3dSkeletonSegment(end, end, i, pivot.Name));
            }
            else
            {
                var parent = worlds[pivot.ParentIndex].Translation;
                segments.Add(new W3dSkeletonSegment(new W3dVector3(parent.X, parent.Y, parent.Z), end, i, pivot.Name));
            }
        }

        return segments;
    }

    /// <summary>
    /// Samples a clip at a frame, returning world transforms in pivot order.
    /// Frames outside a channel range hold the nearest key.
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
        var translation = new Vector3(pivot.Translation.X, pivot.Translation.Y, pivot.Translation.Z);
        var rotation = ToNumerics(Normalize(pivot.Rotation));

        if (channelsByPivot != null && channelsByPivot.TryGetValue(pivotIndex, out var channels))
        {
            ApplyChannels(channels, frame, ref translation, ref rotation);
        }

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
            var key = NearestKey(channel, frame);
            if (key == null || key.Values.Count == 0)
            {
                continue;
            }

            switch (channel.ChannelType)
            {
                case W3dConstants.AnimationChannels.X:
                    translation.X += key.Values[0];
                    break;
                case W3dConstants.AnimationChannels.Y:
                    translation.Y += key.Values[0];
                    break;
                case W3dConstants.AnimationChannels.Z:
                    translation.Z += key.Values[0];
                    break;
                case W3dConstants.AnimationChannels.Xr:
                    eulerX += key.Values[0];
                    hasEuler = true;
                    break;
                case W3dConstants.AnimationChannels.Yr:
                    eulerY += key.Values[0];
                    hasEuler = true;
                    break;
                case W3dConstants.AnimationChannels.Zr:
                    eulerZ += key.Values[0];
                    hasEuler = true;
                    break;
                case W3dConstants.AnimationChannels.Q:
                    if (key.Values.Count >= 4)
                    {
                        var delta = Normalize(new W3dQuaternion(key.Values[0], key.Values[1], key.Values[2], key.Values[3]));
                        rotation = Quaternion.Multiply(ToNumerics(delta), rotation);
                    }

                    break;
                default:
                    break;
            }
        }

        if (hasEuler)
        {
            var eulerDelta = Quaternion.CreateFromYawPitchRoll(eulerY, eulerX, eulerZ);
            rotation = Quaternion.Multiply(eulerDelta, rotation);
        }
    }

    private static W3dAnimationKey? NearestKey(W3dAnimationChannel channel, int frame)
    {
        W3dAnimationKey? best = null;
        int bestDistance = int.MaxValue;
        foreach (var key in channel.Keys)
        {
            int distance = Math.Abs(key.Frame - frame);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = key;
            }
        }

        return best;
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
