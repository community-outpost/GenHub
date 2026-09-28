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
    /// Maximum recursion depth when building file explorer trees.
    /// </summary>
    public const int FileExplorerMaxDepth = 20;
}
