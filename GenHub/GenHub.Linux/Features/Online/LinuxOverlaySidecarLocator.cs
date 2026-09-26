using GenHub.Core.Constants;
using GenHub.Core.Services.Online;
using System;
using System.IO;
using System.Runtime.Versioning;

namespace GenHub.Linux.Features.Online;

/// <summary>
/// Locates the Linux overlay sidecar binary. Until the Phase 0 overlay
/// selection ships an installer, only the override is honored.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxOverlaySidecarLocator : OverlaySidecarLocatorBase
{
    /// <inheritdoc/>
    protected override Environment.SpecialFolder BaseFolder => Environment.SpecialFolder.UserProfile;

    /// <inheritdoc/>
    protected override string[] CandidateSegments =>
        [".local", "share", OnlineConstants.OverlayInstallDir, OnlineConstants.OverlaySubDir, OnlineConstants.OverlayUnixBinary];

    /// <inheritdoc/>
    protected override bool IsValidCandidate(string path)
    {
        return File.Exists(path) && HasExecutePermission(path);
    }
}
