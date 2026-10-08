namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the sidebar layout and pane dimensions.
/// </summary>
public static class SidebarConstants
{
    /// <summary>
    /// Default open width of the sidebar pane in pixels.
    /// </summary>
    public const double DefaultOpenPaneLength = 220.0;

    /// <summary>
    /// Minimum allowed width of the sidebar pane in pixels when resizing.
    /// </summary>
    public const double MinPaneLength = 140.0;

    /// <summary>
    /// Maximum allowed width of the sidebar pane in pixels when resizing.
    /// </summary>
    public const double MaxPaneLength = 360.0;

    /// <summary>
    /// Width of the splitter divider column in pixels.
    /// </summary>
    public const double SplitterWidth = 4.0;

    /// <summary>
    /// Font size of publisher names in the downloads sidebar in pixels. Mirrors the publisher
    /// item template so auto-fit measurements match the rendered text.
    /// </summary>
    public const double DownloadsPublisherNameFontSize = 13.5;

    /// <summary>
    /// Fixed horizontal chrome around a downloads publisher name in pixels: pane content
    /// margins (28), list item padding (20), logo width and gaps (40), plus a buffer for
    /// rounding, font fallback metrics, and a possible vertical scrollbar (12).
    /// </summary>
    public const double DownloadsPaneChromeWidth = 100.0;

    /// <summary>
    /// Extra horizontal allowance in pixels for the content count badge (margins, padding,
    /// border, and up to four digits) when at least one publisher shows a badge.
    /// </summary>
    public const double DownloadsPaneBadgeAllowance = 56.0;
}
