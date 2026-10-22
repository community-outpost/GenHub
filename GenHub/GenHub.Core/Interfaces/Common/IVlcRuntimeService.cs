using GenHub.Core.Models.Common;
using GenHub.Core.Models.Results;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Common;

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
    /// Gets a value indicating whether on-demand installation of the native LibVLC runtime is supported on the current platform.
    /// </summary>
    bool IsInstallSupported { get; }

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
    /// <returns>An <see cref="OperationResult{T}"/> indicating whether installation succeeded.</returns>
    Task<OperationResult<bool>> InstallRuntimeAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
