using GenHub.Core.Services.Online;
using System;
using System.Runtime.Versioning;

namespace GenHub.MacOS.Features.Online;

/// <summary>
/// Locates the macOS overlay sidecar binary. Until the Phase 0 overlay
/// selection ships an installer, only the override is honored.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacOSOverlaySidecarLocator : OverlaySidecarLocatorBase
{
    /// <inheritdoc/>
    protected override Environment.SpecialFolder BaseFolder => Environment.SpecialFolder.UserProfile;

    /// <inheritdoc/>
    protected override string[] CandidateSegments => ["Library", "Application Support", "GenHub", "overlay", "genhub-overlay"];
}
