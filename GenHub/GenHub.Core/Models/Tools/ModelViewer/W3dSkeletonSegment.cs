namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Skeleton line segment between a pivot and its parent in bind pose.
/// </summary>
/// <param name="Start">The parent end point.</param>
/// <param name="End">The pivot end point.</param>
/// <param name="PivotIndex">The pivot index.</param>
/// <param name="PivotName">The pivot name.</param>
/// <param name="ParentIndex">The parent pivot index, or -1 for the root.</param>
public sealed record W3dSkeletonSegment(W3dVector3 Start, W3dVector3 End, int PivotIndex, string PivotName, int ParentIndex);
