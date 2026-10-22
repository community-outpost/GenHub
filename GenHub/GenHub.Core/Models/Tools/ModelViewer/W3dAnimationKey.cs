using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Single animation key.
/// </summary>
/// <param name="Frame">The frame number.</param>
/// <param name="Values">The channel values.</param>
public sealed record W3dAnimationKey(int Frame, IReadOnlyList<float> Values);
