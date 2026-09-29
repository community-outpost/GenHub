namespace GenHub.Core.Constants;

/// <summary>
/// Shared constants for the GenHub editor shell used by the WND, Texture, and future INI editors.
/// </summary>
public static class EditorConstants
{
    /// <summary>
    /// Default canvas zoom factor.
    /// </summary>
    public const double ZoomDefault = 1.0;

    /// <summary>
    /// Minimum canvas zoom factor.
    /// </summary>
    public const double ZoomMin = 0.25;

    /// <summary>
    /// Maximum canvas zoom factor.
    /// </summary>
    public const double ZoomMax = 8.0;

    /// <summary>
    /// Additive zoom step applied by zoom in and zoom out.
    /// </summary>
    public const double ZoomStep = 0.25;

    /// <summary>
    /// Multiplicative zoom factor applied per Ctrl+mouse wheel notch on the shared canvas.
    /// </summary>
    public const double ZoomWheelFactor = 1.2;

    /// <summary>
    /// Maximum recursion depth when building file explorer trees.
    /// </summary>
    public const int FileExplorerMaxDepth = 20;

    /// <summary>
    /// Edge length of the square canvas resize handles in device-independent pixels.
    /// </summary>
    public const double ResizeHandleSize = 10.0;

    /// <summary>
    /// Half the resize handle edge length, used to center handles on rectangle corners and edges.
    /// </summary>
    public const double ResizeHandleHalfSize = 5.0;

    /// <summary>
    /// Negative half resize handle edge length, used for leading-edge canvas placement.
    /// </summary>
    public const double ResizeHandleNegativeOffset = -ResizeHandleHalfSize;

    /// <summary>
    /// Default keyboard nudge step for selected slices or controls in pixels.
    /// </summary>
    public const int KeyboardNudgeStep = 1;

    /// <summary>
    /// Large keyboard nudge step (Shift+Arrow) for selected slices or controls in pixels.
    /// </summary>
    public const int KeyboardNudgeStepLarge = 10;
}
