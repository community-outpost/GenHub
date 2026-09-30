using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// One level of detail.
/// </summary>
/// <param name="MaxScreenSize">The screen size switching to a higher level.</param>
/// <param name="SubObjects">The sub-objects.</param>
public sealed record W3dLevelOfDetail(float MaxScreenSize, IReadOnlyList<W3dSubObject> SubObjects);
