using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Online;
using System;
using System.IO;
using System.Runtime.Versioning;

namespace GenHub.Linux.Features.Online;

/// <summary>
/// Locates the Linux overlay sidecar binary. Until the Phase 0 overlay
/// selection ships an installer, only the override is honored.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxOverlaySidecarLocator : IOverlaySidecarLocator
{
    /// <inheritdoc/>
    public string? LocateBinary()
    {
        var overridePath = Environment.GetEnvironmentVariable(OnlineConstants.OverlayBinaryEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidate = Path.Combine(home, ".local", "share", "GenHub", "overlay", "genhub-overlay");
        return File.Exists(candidate) ? candidate : null;
    }

    /// <inheritdoc/>
    public string BuildArguments(string configPath)
    {
        return $"--config \"{configPath}\"";
    }
}
