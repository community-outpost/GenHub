using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Online;
using System;
using System.IO;

namespace GenHub.Windows.Features.Online;

/// <summary>
/// Locates the Windows overlay sidecar binary. Until the Phase 0 overlay
/// selection ships an installer, only the override is honored.
/// </summary>
public sealed class WindowsOverlaySidecarLocator : IOverlaySidecarLocator
{
    /// <inheritdoc/>
    public string? LocateBinary()
    {
        var overridePath = Environment.GetEnvironmentVariable(OnlineConstants.OverlayBinaryEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidate = Path.Combine(localAppData, "GenHub", "overlay", "genhub-overlay.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    /// <inheritdoc/>
    public string BuildArguments(string configPath)
    {
        return $"--config \"{configPath}\"";
    }
}
