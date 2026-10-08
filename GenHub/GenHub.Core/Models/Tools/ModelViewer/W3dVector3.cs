namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Three-component position, normal, or euler angle vector.
/// </summary>
/// <param name="X">The X component.</param>
/// <param name="Y">The Y component.</param>
/// <param name="Z">The Z component.</param>
public readonly record struct W3dVector3(float X, float Y, float Z);
