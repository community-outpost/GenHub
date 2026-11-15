using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Single animation channel driving one pivot.
/// Classic channels store one float vector per frame; time-coded channels store
/// frame-stamped keys; adaptive-delta channels keep opaque payload bytes.
/// </summary>
/// <param name="Pivot">The affected pivot index.</param>
/// <param name="ChannelType">The channel type (translation, rotation, or time-coded variant).</param>
/// <param name="FirstFrame">The first driven frame.</param>
/// <param name="LastFrame">The last driven frame.</param>
/// <param name="VectorLength">The floats per key.</param>
/// <param name="Keys">The decoded keys in frame order.</param>
/// <param name="HasRawPayload">Whether the payload uses an encoding the sampler does not evaluate.</param>
public sealed record W3dAnimationChannel(
    int Pivot,
    int ChannelType,
    int FirstFrame,
    int LastFrame,
    int VectorLength,
    IReadOnlyList<W3dAnimationKey> Keys,
    bool HasRawPayload);
