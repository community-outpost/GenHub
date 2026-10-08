using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Animation clip targeting a hierarchy.
/// </summary>
/// <param name="Name">The clip name.</param>
/// <param name="HierarchyName">The targeted hierarchy name.</param>
/// <param name="FrameCount">The frame count.</param>
/// <param name="FrameRate">The playback rate in frames per second.</param>
/// <param name="IsCompressed">Whether the clip uses compressed channels.</param>
/// <param name="Flavor">The compression flavor (time-coded or adaptive delta).</param>
/// <param name="Channels">The channels.</param>
public sealed record W3dAnimationClip(
    string Name,
    string HierarchyName,
    uint FrameCount,
    uint FrameRate,
    bool IsCompressed,
    int Flavor,
    IReadOnlyList<W3dAnimationChannel> Channels)
{
    /// <summary>
    /// Gets a value indicating whether the clip can be sampled by the viewer.
    /// </summary>
    public bool IsSamplable
    {
        get
        {
            foreach (var channel in Channels)
            {
                if (!channel.HasRawPayload && channel.Keys.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
