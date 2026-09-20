using GenHub.Core.Services.Online;
using System;

namespace GenHub.Windows.Features.Online;

/// <summary>
/// Locates the Windows overlay sidecar binary. Until the Phase 0 overlay
/// selection ships an installer, only the override is honored.
/// </summary>
public sealed class WindowsOverlaySidecarLocator : OverlaySidecarLocatorBase
{
    /// <inheritdoc/>
    protected override Environment.SpecialFolder BaseFolder => Environment.SpecialFolder.LocalApplicationData;

    /// <inheritdoc/>
    protected override string[] CandidateSegments => ["GenHub", "overlay", "genhub-overlay.exe"];
}
