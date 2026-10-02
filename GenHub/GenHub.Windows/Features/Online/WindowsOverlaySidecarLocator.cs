using GenHub.Core.Constants;
using GenHub.Core.Services.Online;
using System;
using System.Collections.Generic;

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
    protected override IReadOnlyList<string> CandidateSegments =>
        [OnlineConstants.OverlayInstallDir, OnlineConstants.OverlaySubDir, OnlineConstants.OverlayWindowsBinary];
}
