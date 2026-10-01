namespace GenHub.Features.Tools.WorldBuilder.Controls;

/// <summary>
/// Pointer data translated to map cell coordinates.
/// </summary>
/// <param name="CellX">Cell X.</param>
/// <param name="CellY">Cell Y.</param>
/// <param name="IsLeftButton">Left button state.</param>
/// <param name="IsMiddleButton">Middle button state.</param>
/// <param name="IsRightButton">Right button state.</param>
/// <param name="HasShift">Shift key state.</param>
public sealed record CellPointerEventArgs(int CellX, int CellY, bool IsLeftButton, bool IsMiddleButton, bool IsRightButton, bool HasShift = false);
