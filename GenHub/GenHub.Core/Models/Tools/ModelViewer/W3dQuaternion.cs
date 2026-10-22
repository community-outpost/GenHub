namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Pivot orientation quaternion.
/// </summary>
/// <param name="X">The X component.</param>
/// <param name="Y">The Y component.</param>
/// <param name="Z">The Z component.</param>
/// <param name="W">The W component.</param>
public readonly record struct W3dQuaternion(float X, float Y, float Z, float W);
