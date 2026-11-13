namespace GenHub.Core.Models.Common;

/// <summary>
/// Status of the native LibVLC media player runtime.
/// </summary>
public enum VlcRuntimeStatus
{
    /// <summary>
    /// LibVLC native libraries are installed and ready for playback.
    /// </summary>
    Available,

    /// <summary>
    /// LibVLC native libraries are not installed on this machine.
    /// </summary>
    NotInstalled,

    /// <summary>
    /// LibVLC native libraries are currently being downloaded and installed.
    /// </summary>
    Downloading,

    /// <summary>
    /// On-demand installation failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The current platform does not support on-demand download.
    /// </summary>
    UnsupportedPlatform,
}
