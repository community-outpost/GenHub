namespace GenHub.Core.Interfaces.Workspace;

/// <summary>
/// Reports whether this process can create symbolic links.
/// <para>
/// Previously the launcher asked "is this process an administrator", computed it only on
/// Windows, and left it <c>false</c> everywhere else. It then downgraded the
/// <c>SymlinkOnly</c> and <c>HybridCopySymlink</c> strategies to <c>HardLink</c> whenever
/// the answer was false, which made both strategies permanently unreachable on Linux and
/// macOS — where <c>symlink(2)</c> needs no privilege at all. Users could select a
/// strategy in Settings that silently never applied.
/// </para>
/// <para>
/// Naming the capability rather than the privilege makes the platform answer obvious:
/// Windows genuinely gates symlink creation behind <c>SeCreateSymbolicLinkPrivilege</c>
/// (or Developer Mode); Unix does not gate it at all.
/// </para>
/// </summary>
public interface ISymlinkCapabilityProvider
{
    /// <summary>
    /// Gets a value indicating whether this process can create symbolic links.
    /// </summary>
    bool CanCreateSymlinks { get; }
}
