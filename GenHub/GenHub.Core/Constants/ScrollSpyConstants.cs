namespace GenHub.Core.Constants;

/// <summary>
/// Constants for sidebar section scroll-spy behavior shared by settings-style pages.
/// </summary>
public static class ScrollSpyConstants
{
    /// <summary>
    /// Duration of the animated scroll to a section in milliseconds.
    /// </summary>
    public const int AnimationDurationMs = 350;

    /// <summary>
    /// Interval between animated scroll position updates in milliseconds.
    /// </summary>
    public const int AnimationFrameIntervalMs = 16;

    /// <summary>
    /// Minimum distance in pixels from the top of the viewport used to detect the active section.
    /// </summary>
    public const double MinActiveThreshold = 60.0;

    /// <summary>
    /// Fraction of the viewport height used to detect the active section.
    /// </summary>
    public const double ViewportThresholdRatio = 0.35;

    /// <summary>
    /// Distance in pixels from the maximum scroll offset that counts as scrolled to bottom.
    /// </summary>
    public const double BottomSnapTolerance = 25.0;

    /// <summary>
    /// Distance in pixels below which a programmatic scroll snaps directly instead of animating.
    /// </summary>
    public const double ScrollSnapEpsilon = 1.0;
}
