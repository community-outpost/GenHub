using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Online;
using System;
using System.IO;

namespace GenHub.Core.Services.Online;

/// <summary>
/// Base sidecar locator honoring the binary override and a single platform candidate path.
/// </summary>
public abstract class OverlaySidecarLocatorBase : IOverlaySidecarLocator
{
    /// <summary>
    /// Gets the base special folder for the platform candidate path.
    /// </summary>
    protected abstract Environment.SpecialFolder BaseFolder { get; }

    /// <summary>
    /// Gets the path segments below <see cref="BaseFolder"/> locating the platform candidate binary.
    /// </summary>
    protected abstract string[] CandidateSegments { get; }

    /// <inheritdoc/>
    public string? LocateBinary()
    {
        var overridePath = Environment.GetEnvironmentVariable(OnlineConstants.OverlayBinaryEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var basePath = Environment.GetFolderPath(BaseFolder);
        var candidate = Path.Combine([basePath, .. CandidateSegments]);
        return File.Exists(candidate) ? candidate : null;
    }

    /// <inheritdoc/>
    public string BuildArguments(string configPath)
    {
        return $"--config \"{configPath}\"";
    }
}
