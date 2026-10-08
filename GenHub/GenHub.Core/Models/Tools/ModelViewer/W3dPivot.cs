namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Single hierarchy pivot (bone).
/// </summary>
/// <param name="Name">The pivot name.</param>
/// <param name="ParentIndex">The parent pivot index, or -1 for the root.</param>
/// <param name="Translation">The pivot translation.</param>
/// <param name="EulerAngles">The pivot orientation as euler angles.</param>
/// <param name="Rotation">The pivot orientation quaternion.</param>
public sealed record W3dPivot(string Name, int ParentIndex, W3dVector3 Translation, W3dVector3 EulerAngles, W3dQuaternion Rotation);
