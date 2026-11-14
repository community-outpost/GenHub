using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Utilities;
using System;
using System.Runtime.Versioning;

namespace GenHub.Windows.Features.Workspace;

/// <summary>
/// Symlink capability on Windows.
/// </summary>
/// <remarks>
/// Capability is determined by a real, cached creation probe rather than Administrator
/// membership. Developer Mode can permit unelevated symlink creation, while policy can
/// deny it to an otherwise elevated process.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsSymlinkCapabilityProvider : ISymlinkCapabilityProvider
{
    private static readonly Lazy<bool> _cachedCapability = new(ProbeCapability);

    /// <inheritdoc/>
    public bool CanCreateSymlinks => _cachedCapability.Value;

    private static bool ProbeCapability() => SymbolicLinkCreator.CanCreateFileLink();
}
