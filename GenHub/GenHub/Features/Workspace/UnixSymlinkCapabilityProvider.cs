using GenHub.Core.Interfaces.Workspace;

namespace GenHub.Features.Workspace;

/// <summary>
/// Symlink capability on Linux and macOS, where <c>symlink(2)</c> requires no privilege.
/// </summary>
public sealed class UnixSymlinkCapabilityProvider : ISymlinkCapabilityProvider
{
    /// <inheritdoc/>
    public bool CanCreateSymlinks => true;
}
