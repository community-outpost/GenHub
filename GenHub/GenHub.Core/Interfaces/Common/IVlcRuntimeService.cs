using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Common;

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

/// <summary>
/// Service managing discovery and on-demand installation of the native LibVLC runtime.
/// </summary>
public interface IVlcRuntimeService
{
    /// <summary>
    /// Gets the current status of the native LibVLC runtime.
    /// </summary>
    VlcRuntimeStatus Status { get; }

    /// <summary>
    /// Gets the directory containing the native libvlc binaries, or null if relying on system paths.
    /// </summary>
    string? RuntimeDirectory { get; }

    /// <summary>
    /// Checks if the native runtime is present and ready for initialization.
    /// </summary>
    /// <returns>True if the runtime is available; otherwise false.</returns>
    bool IsAvailable();

    /// <summary>
    /// Downloads and installs the native LibVLC runtime on platforms that support on-demand acquisition.
    /// </summary>
    /// <param name="progress">Optional progress reporter emitting values between 0.0 and 1.0.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if installation succeeded; otherwise false.</returns>
    Task<bool> InstallRuntimeAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
